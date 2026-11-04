using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Reports;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Checks the request contract, publication semantics and owner-only reads, without broker or database.</summary>
[TestClass]
public sealed class ReportServiceTests
{
    private readonly ReportStoreFake _store = new();
    private readonly ReportPublisherFake _publisher = new();
    private readonly ListLogger<ReportService> _logger = new();

    /// <summary>A valid request is persisted as Pending for the token owner, published once, then returned.</summary>
    [TestMethod]
    public async Task ValidRequestIsPersistedThenPublished()
    {
        ReportResponse response = await Service().RequestAsync(Actor("requester"), Valid(), CancellationToken.None);
        ReportRequest stored = _store.Reports[response.Id];
        Assert.AreEqual("requester", stored.RequesterId);
        Assert.AreEqual(ReportScope.Mine, stored.Scope);
        Assert.AreEqual(new DateOnly(2026, 9, 1), stored.FromDate);
        Assert.AreEqual(new DateOnly(2026, 9, 30), stored.ToDate);
        Assert.AreEqual(1, _store.Saves);
        CollectionAssert.AreEqual(new[] { response.Id }, _publisher.Published);
        Assert.AreEqual("Pending", response.Status);
        Assert.AreEqual(new FixedTimeProvider().GetUtcNow().UtcDateTime, response.CreatedAt);
        Assert.IsNull(response.StartedAt);
        Assert.IsNull(response.CompletedAt);
        Assert.IsNull(response.Result);
        Assert.IsNull(response.Error);
    }

    /// <summary>Each valid POST creates a new request; client deduplication is not implied.</summary>
    [TestMethod]
    public async Task EachRequestGetsANewIdentifier()
    {
        ReportService service = Service();
        ReportResponse first = await service.RequestAsync(Actor("requester"), Valid(), CancellationToken.None);
        ReportResponse second = await service.RequestAsync(Actor("requester"), Valid(), CancellationToken.None);
        Assert.AreNotEqual(first.Id, second.Id);
        Assert.HasCount(2, _publisher.Published);
    }

    /// <summary>Scope names are accepted case-insensitively.</summary>
    /// <param name="scope">The client value.</param>
    /// <param name="expected">The registered scope.</param>
    [TestMethod]
    [DataRow("Mine", "Mine")]
    [DataRow("global", "Global")]
    public async Task ScopeNamesAreParsed(string scope, string expected)
    {
        CreateReportRequest request = Valid();
        request.Scope = scope;
        var actor = new CurrentActor("requester", new List<string> { RoleNames.Employee, RoleNames.Auditor });
        ReportResponse response = await Service().RequestAsync(actor, request, CancellationToken.None);
        Assert.AreEqual(expected, response.Scope);
    }

    /// <summary>Invalid scopes, including numeric enum values, are rejected before any persistence or publication.</summary>
    /// <param name="scope">The client value.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("1")]
    [DataRow("Everyone")]
    public async Task InvalidScopeCreatesNoWork(string scope)
    {
        CreateReportRequest request = Valid();
        request.Scope = scope;
        await AssertRejectedAsync(request, "report.invalid_scope");
    }

    /// <summary>A start after the end, or a missing date, is rejected without persistence or publication.</summary>
    /// <param name="case">Which period defect to send.</param>
    [TestMethod]
    [DataRow("inverted")]
    [DataRow("missing-from")]
    [DataRow("missing-to")]
    public async Task InvalidPeriodCreatesNoWork(string @case)
    {
        CreateReportRequest request = Valid();
        if (@case == "inverted")
        {
            (request.FromDate, request.ToDate) = (request.ToDate, request.FromDate);
        }
        else if (@case == "missing-from")
        {
            request.FromDate = null;
        }
        else
        {
            request.ToDate = null;
        }

        await AssertRejectedAsync(request, "report.invalid_period");
    }

    /// <summary>A single-day period (from == to) is valid.</summary>
    [TestMethod]
    public async Task SingleDayPeriodIsValid()
    {
        CreateReportRequest request = Valid();
        request.ToDate = request.FromDate;
        ReportResponse response = await Service().RequestAsync(Actor("requester"), request, CancellationToken.None);
        Assert.AreEqual(response.FromDate, response.ToDate);
    }

    /// <summary>An unconfirmed publication answers 503 with the persisted id and keeps the request Pending.</summary>
    [TestMethod]
    public async Task UnconfirmedPublicationReturns503WithPersistedId()
    {
        _publisher.Unconfirmed = true;
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().RequestAsync(Actor("requester"), Valid(), CancellationToken.None));
        Assert.AreEqual(503, error.StatusCode);
        Assert.AreEqual("report.publish_unconfirmed", error.Code);
        var id = (Guid)error.Extensions["reportId"]!;
        Assert.AreEqual(ReportStatus.Pending, _store.Reports[id].Status);
        Assert.IsTrue(_logger.Entries.Exists(entry => entry.Message.Contains("not confirmed", StringComparison.Ordinal)));
    }

    /// <summary>A database outage before registration surfaces as an error: no id, no publication.</summary>
    [TestMethod]
    public async Task StorageFailureBeforeRegistrationPublishesNothing()
    {
        _store.SaveFailure = _ => new InvalidOperationException("Simulated database outage.");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Service().RequestAsync(Actor("requester"), Valid(), CancellationToken.None));
        Assert.IsEmpty(_store.Reports);
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>Listing returns only the actor's own requests, newest first.</summary>
    [TestMethod]
    public async Task ListReturnsOnlyOwnRequests()
    {
        ReportRequest older = Stored("requester", ReportStatus.Completed, minutesAgo: 10);
        ReportRequest newer = Stored("requester", ReportStatus.Pending, minutesAgo: 1);
        Stored("someone-else", ReportStatus.Pending, minutesAgo: 0);
        IReadOnlyList<ReportResponse> list = await Service().ListAsync(Actor("requester"), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { newer.Id, older.Id }, new[] { list[0].Id, list[1].Id });
        Assert.HasCount(2, list);
    }

    /// <summary>The detail of a foreign request is indistinguishable from an absent one.</summary>
    [TestMethod]
    public async Task ForeignDetailIsNotFound()
    {
        ReportRequest foreign = Stored("someone-else", ReportStatus.Completed);
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().FindAsync(Actor("requester"), foreign.Id, CancellationToken.None));
        Assert.AreEqual(404, error.StatusCode);
        Assert.AreEqual("report.not_found", error.Code);
    }

    /// <summary>The detail returns the persisted result as stored, even if expenses changed afterwards.</summary>
    [TestMethod]
    public async Task DetailReturnsPersistedResultWithoutRecalculating()
    {
        ReportRequest completed = Stored("requester", ReportStatus.Completed);
        _store.Expenses.Add(new Core.Models.Expense { OwnerId = "requester", ExpenseDate = completed.FromDate, Amount = 999m });
        ReportResponse response = await Service().FindAsync(Actor("requester"), completed.Id, CancellationToken.None);
        Assert.AreEqual(42.50m, response.Result!.TotalAmount);
        Assert.AreEqual(1, response.Result.TotalCount);
    }

    /// <summary>A Failed request exposes its stable code and sanitized message, and no result.</summary>
    [TestMethod]
    public async Task FailedDetailExposesSanitizedError()
    {
        ReportRequest failed = Stored("requester", ReportStatus.Failed);
        ReportResponse response = await Service().FindAsync(Actor("requester"), failed.Id, CancellationToken.None);
        Assert.AreEqual("report.calculation_failed", response.Error!.Code);
        Assert.AreEqual("The report could not be calculated.", response.Error.Message);
        Assert.IsNull(response.Result);
    }

    /// <summary>Only a Completed own request can be exported; other states answer 409.</summary>
    /// <param name="status">The stored state.</param>
    /// <param name="expected">The expected HTTP status (0 for success).</param>
    [TestMethod]
    [DataRow(ReportStatus.Completed, 0)]
    [DataRow(ReportStatus.Pending, 409)]
    [DataRow(ReportStatus.Processing, 409)]
    [DataRow(ReportStatus.Failed, 409)]
    public async Task ExportRequiresCompleted(ReportStatus status, int expected)
    {
        ReportRequest report = Stored("requester", status);
        if (expected == 0)
        {
            ReportResponse response = await Service().FindCompletedAsync(Actor("requester"), report.Id, CancellationToken.None);
            Assert.IsNotNull(response.Result);
            return;
        }

        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().FindCompletedAsync(Actor("requester"), report.Id, CancellationToken.None));
        Assert.AreEqual(expected, error.StatusCode);
        Assert.AreEqual("report.not_completed", error.Code);
    }

    /// <summary>Exporting a foreign Completed request answers 404, never the file.</summary>
    [TestMethod]
    public async Task ForeignExportIsNotFound()
    {
        ReportRequest foreign = Stored("someone-else", ReportStatus.Completed);
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().FindCompletedAsync(Actor("requester"), foreign.Id, CancellationToken.None));
        Assert.AreEqual(404, error.StatusCode);
    }

    private static CurrentActor Actor(string id) => new(id, new List<string> { RoleNames.Employee });

    private static CreateReportRequest Valid() => new()
    {
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
        Scope = "Mine",
    };

    private ReportService Service() => new(_store, _publisher, new FixedTimeProvider(), _logger);

    private async Task AssertRejectedAsync(CreateReportRequest request, string code)
    {
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().RequestAsync(Actor("requester"), request, CancellationToken.None));
        Assert.AreEqual(400, error.StatusCode);
        Assert.AreEqual(code, error.Code);
        Assert.IsEmpty(_store.Reports);
        Assert.AreEqual(0, _store.Saves);
        Assert.IsEmpty(_publisher.Published);
    }

    private ReportRequest Stored(string requester, ReportStatus status, int minutesAgo = 0)
    {
        DateTime created = new FixedTimeProvider().GetUtcNow().UtcDateTime.AddMinutes(-minutesAgo);
        var report = new ReportRequest
        {
            RequesterId = requester,
            Scope = ReportScope.Mine,
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            Status = status,
            CreatedAtUtc = created,
        };
        if (status == ReportStatus.Completed)
        {
            var totals = new List<ReportStatusTotal> { new("Draft", 1, 42.50m) };
            report.ResultJson = ReportResultJson.Serialize(new ReportResult(created, 1, 42.50m, totals));
            report.CompletedAtUtc = created;
        }
        else if (status == ReportStatus.Failed)
        {
            report.ErrorCode = "report.calculation_failed";
            report.ErrorMessage = "The report could not be calculated.";
            report.CompletedAtUtc = created;
        }

        _store.Reports[report.Id] = report;
        return report;
    }
}
