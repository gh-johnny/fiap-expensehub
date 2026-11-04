using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ExpenseHub.Api.Controllers;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Reports;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Rubric;

/// <summary>
/// One test per minimum scenario of the CP3 test contract (CP3-I04, section 8), named after it. Each one drives the
/// real request service, worker processor and Excel export over in-memory doubles (no database, network or broker)
/// and asserts results and effects: what was persisted, published, calculated or returned.
/// </summary>
[TestClass]
public sealed class Cp3ScenarioTests
{
    private static readonly string[] _headers = { "Status", "Count", "Amount" };
    private static readonly string[] _states = { "Draft", "Submitted", "Approved", "Rejected", "Paid" };
    private static readonly DateOnly _from = new(2026, 9, 1);
    private static readonly DateOnly _to = new(2026, 9, 30);
    private readonly ReportStoreFake _store = new();
    private readonly ReportPublisherFake _publisher = new();
    private readonly CurrentActor _employee = new("employee", new List<string> { RoleNames.Employee });

    /// <summary>Known data and inclusive period limits: count and exact sum per state and in total.</summary>
    [TestMethod]
    public async Task KnownDataAndInclusiveLimits()
    {
        AddExpense("employee", _from, ExpenseStatus.Approved, 100.00m);
        AddExpense("employee", _to, ExpenseStatus.Approved, 50.00m);
        AddExpense("employee", new DateOnly(2026, 9, 15), ExpenseStatus.Paid, 25.00m);
        AddExpense("employee", new DateOnly(2026, 9, 16), ExpenseStatus.Draft, 0.10m);
        AddExpense("employee", new DateOnly(2026, 9, 17), ExpenseStatus.Draft, 0.20m);
        AddExpense("other", new DateOnly(2026, 9, 12), ExpenseStatus.Approved, 200.00m);
        AddExpense("employee", _from.AddDays(-1), ExpenseStatus.Paid, 700.00m);
        AddExpense("employee", _to.AddDays(1), ExpenseStatus.Paid, 500.00m);

        ReportResult result = await CompletedResultAsync(await RequestAsync("Mine"));

        Assert.AreEqual(5, result.TotalCount);
        Assert.AreEqual(175.30m, result.TotalAmount);
        AssertTotals(result, ("Draft", 2, 0.30m), ("Submitted", 0, 0m), ("Approved", 2, 150.00m), ("Rejected", 0, 0m), ("Paid", 1, 25.00m));
    }

    /// <summary>No eligible reimbursement: the five states and both totals are zero.</summary>
    [TestMethod]
    public async Task NoEligibleExpense()
    {
        AddExpense("other", new DateOnly(2026, 9, 10), ExpenseStatus.Paid, 80.00m);
        AddExpense("employee", new DateOnly(2026, 10, 1), ExpenseStatus.Paid, 80.00m);

        ReportResult result = await CompletedResultAsync(await RequestAsync("Mine"));

        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0m, result.TotalAmount);
        AssertTotals(result, _states.Select(state => (state, 0, 0m)).ToArray());
    }

    /// <summary>A start date after the end date is rejected with 400; nothing is persisted or published.</summary>
    [TestMethod]
    public async Task StartAfterEnd()
    {
        var request = new CreateReportRequest { FromDate = _to, ToDate = _from, Scope = "Mine" };

        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().RequestAsync(_employee, request, CancellationToken.None));

        Assert.AreEqual(400, error.StatusCode);
        Assert.AreEqual("report.invalid_period", error.Code);
        Assert.IsEmpty(_store.Reports);
        Assert.AreEqual(0, _store.Saves);
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>An Employee asking for Global is denied with 403 and no processing is authorized: no record, no message.</summary>
    [TestMethod]
    public async Task EmployeeRequestsGlobal()
    {
        AddExpense("other", new DateOnly(2026, 9, 10), ExpenseStatus.Paid, 80.00m);

        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().RequestAsync(_employee, Body("Global"), CancellationToken.None));

        Assert.AreEqual(403, error.StatusCode);
        Assert.AreEqual("report.scope_forbidden", error.Code);
        Assert.IsEmpty(_store.Reports);
        Assert.IsEmpty(_publisher.Published);
        Assert.AreEqual(0, _store.RowLoads);
    }

    /// <summary>Another user's request exposes neither its detail nor its result: 404 like an absent id, and absent from the list.</summary>
    [TestMethod]
    public async Task ForeignRequest()
    {
        AddExpense("owner", new DateOnly(2026, 9, 10), ExpenseStatus.Paid, 80.00m);
        var owner = new CurrentActor("owner", new List<string> { RoleNames.Employee });
        Guid foreign = await RequestAsync("Mine", owner);
        await CompletedResultAsync(foreign);

        ApiProblemException detail = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().FindAsync(_employee, foreign, CancellationToken.None));
        ApiProblemException absent = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().FindAsync(_employee, Guid.NewGuid(), CancellationToken.None));

        Assert.AreEqual(404, detail.StatusCode);
        Assert.AreEqual((absent.StatusCode, absent.Code, absent.Message), (detail.StatusCode, detail.Code, detail.Message));
        Assert.IsEmpty(detail.Extensions);
        Assert.DoesNotContain(foreign.ToString(), detail.Message);
        Assert.IsEmpty(await Service().ListAsync(_employee, CancellationToken.None));
    }

    /// <summary>The same message again produces no new result and no new completion effect, even on a fresh processor.</summary>
    [TestMethod]
    public async Task SameMessageAgain()
    {
        AddExpense("employee", new DateOnly(2026, 9, 10), ExpenseStatus.Paid, 80.00m);
        Guid id = await RequestAsync("Mine");
        Assert.AreEqual(ReportProcessingOutcome.Completed, await Processor().ProcessAsync(id, CancellationToken.None));
        ReportRequest report = _store.Reports[id];
        (string? json, DateTime? completed, DateTime? started, int attempts, long revision) =
            (report.ResultJson, report.CompletedAtUtc, report.StartedAtUtc, report.Attempts, report.Revision);
        (int saves, int rowLoads) = (_store.Saves, _store.RowLoads);
        AddExpense("employee", new DateOnly(2026, 9, 11), ExpenseStatus.Paid, 999.00m);

        ReportProcessingOutcome again = await Processor().ProcessAsync(id, CancellationToken.None);

        Assert.AreEqual(ReportProcessingOutcome.Duplicate, again);
        Assert.AreEqual(json, report.ResultJson);
        Assert.AreEqual(completed, report.CompletedAtUtc);
        Assert.AreEqual(started, report.StartedAtUtc);
        Assert.AreEqual(attempts, report.Attempts);
        Assert.AreEqual(revision, report.Revision);
        Assert.AreEqual(saves, _store.Saves);
        Assert.AreEqual(rowLoads, _store.RowLoads);
        Assert.HasCount(1, _store.Reports);
    }

    /// <summary>Querying after a reimbursement changes returns the persisted result, without recalculating.</summary>
    [TestMethod]
    public async Task QueryAfterExpenseChanges()
    {
        Expense expense = AddExpense("employee", new DateOnly(2026, 9, 10), ExpenseStatus.Approved, 80.00m);
        Guid id = await RequestAsync("Mine");
        ReportResult before = await CompletedResultAsync(id);
        int rowLoads = _store.RowLoads;
        expense.Status = ExpenseStatus.Paid;
        expense.Amount = 1.00m;
        AddExpense("employee", new DateOnly(2026, 9, 11), ExpenseStatus.Paid, 500.00m);

        ReportResponse detail = await Service().FindAsync(_employee, id, CancellationToken.None);
        ReportResponse export = await Service().FindCompletedAsync(_employee, id, CancellationToken.None);

        Assert.AreEqual(rowLoads, _store.RowLoads);
        foreach (ReportResult after in new[] { detail.Result!, export.Result! })
        {
            Assert.AreEqual(before.GeneratedAt, after.GeneratedAt);
            Assert.AreEqual(1, after.TotalCount);
            Assert.AreEqual(80.00m, after.TotalAmount);
            AssertTotals(after, ("Draft", 0, 0m), ("Submitted", 0, 0m), ("Approved", 1, 80.00m), ("Rejected", 0, 0m), ("Paid", 0, 0m));
        }
    }

    /// <summary>Downloading a completed report: a real workbook, correct headers and numbers equal to the persisted result.</summary>
    [TestMethod]
    public async Task CompletedDownload()
    {
        AddExpense("employee", _from, ExpenseStatus.Approved, 100.00m);
        AddExpense("employee", _to, ExpenseStatus.Approved, 50.00m);
        AddExpense("employee", new DateOnly(2026, 9, 15), ExpenseStatus.Paid, 25.00m);
        Guid id = await RequestAsync("Mine");
        ReportResult saved = await CompletedResultAsync(id);

        var file = (FileContentResult)await Controller("employee").Excel(id, CancellationToken.None);

        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.AreEqual($"report-{id}.xlsx", file.FileDownloadName);
        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        IXLWorksheet sheet = workbook.Worksheet("Report");
        Assert.AreEqual(id.ToString(), sheet.Cell(2, 2).GetString());
        Assert.AreEqual("Mine", sheet.Cell(3, 2).GetString());
        Assert.AreEqual(_from.ToDateTime(TimeOnly.MinValue), sheet.Cell(4, 2).GetDateTime());
        Assert.AreEqual(_to.ToDateTime(TimeOnly.MinValue), sheet.Cell(5, 2).GetDateTime());
        Assert.AreEqual(saved.GeneratedAt, DateTime.SpecifyKind(sheet.Cell(6, 2).GetDateTime(), DateTimeKind.Utc));
        CollectionAssert.AreEqual(_headers,
            new[] { sheet.Cell(8, 1).GetString(), sheet.Cell(8, 2).GetString(), sheet.Cell(8, 3).GetString() });
        for (int index = 0; index < saved.ByStatus.Count; index++)
        {
            ReportStatusTotal expected = saved.ByStatus[index];
            int row = 9 + index;
            Assert.AreEqual(expected.Status, sheet.Cell(row, 1).GetString());
            Assert.AreEqual(XLDataType.Number, sheet.Cell(row, 2).DataType);
            Assert.AreEqual(XLDataType.Number, sheet.Cell(row, 3).DataType);
            Assert.AreEqual(expected.Count, sheet.Cell(row, 2).GetValue<int>());
            Assert.AreEqual(expected.Amount, sheet.Cell(row, 3).GetValue<decimal>());
        }

        Assert.AreEqual("Total", sheet.Cell(14, 1).GetString());
        Assert.AreEqual(saved.TotalCount, sheet.Cell(14, 2).GetValue<int>());
        Assert.AreEqual(saved.TotalAmount, sheet.Cell(14, 3).GetValue<decimal>());
        Assert.AreEqual(175.00m, sheet.Cell(14, 3).GetValue<decimal>());
    }

    /// <summary>Download of a Pending, Processing or Failed own report is 409; a foreign Completed one is 404. No file either way.</summary>
    [TestMethod]
    public async Task PendingFailedOrForeignDownload()
    {
        Guid pending = await RequestAsync("Mine");
        Guid processing = await RequestAsync("Mine");
        _store.Reports[processing].Status = ReportStatus.Processing;
        Guid failed = await RequestAsync("Mine");
        _store.RowsFailure = new InvalidOperationException("Simulated calculation defect.");
        Assert.AreEqual(ReportProcessingOutcome.Failed, await Processor().ProcessAsync(failed, CancellationToken.None));
        _store.RowsFailure = null;
        Guid foreign = await RequestAsync("Mine", new CurrentActor("owner", new List<string> { RoleNames.Employee }));
        await CompletedResultAsync(foreign);
        ReportsController controller = Controller("employee");

        foreach (Guid own in new[] { pending, processing, failed })
        {
            ApiProblemException conflict = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                () => controller.Excel(own, CancellationToken.None));
            Assert.AreEqual(409, conflict.StatusCode);
            Assert.AreEqual("report.not_completed", conflict.Code);
        }

        ApiProblemException hidden = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => controller.Excel(foreign, CancellationToken.None));
        Assert.AreEqual(404, hidden.StatusCode);
        Assert.AreEqual("report.not_found", hidden.Code);
        Assert.IsEmpty(hidden.Extensions);
    }

    private static CreateReportRequest Body(string scope) => new() { FromDate = _from, ToDate = _to, Scope = scope };

    private static void AssertTotals(ReportResult result, params (string Status, int Count, decimal Amount)[] expected)
    {
        CollectionAssert.AreEquivalent(_states, result.ByStatus.Select(total => total.Status).ToArray());
        foreach ((string status, int count, decimal amount) in expected)
        {
            ReportStatusTotal total = result.ByStatus.Single(item => item.Status == status);
            Assert.AreEqual(count, total.Count, status);
            Assert.AreEqual(amount, total.Amount, status);
        }

        Assert.AreEqual(result.TotalCount, result.ByStatus.Sum(total => total.Count));
        Assert.AreEqual(result.TotalAmount, result.ByStatus.Sum(total => total.Amount));
    }

    private Expense AddExpense(string owner, DateOnly date, ExpenseStatus status, decimal amount)
    {
        var expense = new Expense { OwnerId = owner, ExpenseDate = date, Status = status, Amount = amount };
        _store.Expenses.Add(expense);
        return expense;
    }

    private async Task<Guid> RequestAsync(string scope, CurrentActor? actor = null)
        => (await Service().RequestAsync(actor ?? _employee, Body(scope), CancellationToken.None)).Id;

    private async Task<ReportResult> CompletedResultAsync(Guid id)
    {
        Assert.AreEqual(ReportProcessingOutcome.Completed, await Processor().ProcessAsync(id, CancellationToken.None));
        ReportRequest report = _store.Reports[id];
        Assert.AreEqual(ReportStatus.Completed, report.Status);
        Assert.IsNull(report.ErrorCode);
        return ReportResultJson.Deserialize(report.ResultJson)!;
    }

    private ReportService Service() => new(_store, _publisher, new FixedTimeProvider(), new ListLogger<ReportService>());

    private ReportProcessor Processor()
        => new(_store, new FixedTimeProvider(), Options.Create(new ReportProcessingOptions()), new ListLogger<ReportProcessor>());

    private ReportsController Controller(string userId) => new(Service())
    {
        ControllerContext = Principals.Context(userId, RoleNames.Employee),
    };
}
