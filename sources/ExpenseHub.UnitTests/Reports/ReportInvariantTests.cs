using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Reports;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>
/// Invariants that survived mutation testing (make mutation) until these tests existed: every persisted transition
/// advances the concurrency revision, failure paths leave correlated logs and sanitized messages, problems always carry
/// a human message, and the public boundaries reject null arguments.
/// </summary>
[TestClass]
public sealed class ReportInvariantTests
{
    private readonly ReportStoreFake _store = new();
    private readonly ReportPublisherFake _publisher = new();
    private readonly ListLogger<ReportProcessor> _processorLog = new();
    private readonly ListLogger<ReportService> _serviceLog = new();

    /// <summary>Each save of a processing run sees the revision advanced by exactly one, so stale writers conflict.</summary>
    /// <param name="path">The processing path.</param>
    /// <param name="expected">The revisions observed at each save.</param>
    [TestMethod]
    [DataRow("completed", new long[] { 2, 3 })]
    [DataRow("definitive", new long[] { 2, 3 })]
    [DataRow("exhausted", new long[] { 2 })]
    public async Task EveryPersistedTransitionAdvancesTheRevision(string path, long[] expected)
    {
        ReportRequest report = Stored(attempts: path == "exhausted" ? 5 : 0);
        if (path == "definitive")
        {
            _store.RowsFailure = new InvalidOperationException("Simulated calculation defect.");
        }

        var observed = new List<long>();
        _store.SaveFailure = _ =>
        {
            observed.Add(report.Revision);
            return null;
        };

        await Processor().ProcessAsync(report.Id, CancellationToken.None);

        CollectionAssert.AreEqual(expected, observed);
    }

    /// <summary>A whole processing run happens inside a logging scope that carries the report id.</summary>
    [TestMethod]
    public async Task ProcessingRunsInsideAReportIdScope()
    {
        ReportRequest report = Stored();

        await Processor().ProcessAsync(report.Id, CancellationToken.None);

        var scope = (IReadOnlyList<KeyValuePair<string, object?>>)_processorLog.Scopes.Single();
        Assert.AreEqual($"ReportId:{report.Id}", scope.ToString());
        Assert.AreEqual(report.Id, scope.Single(pair => pair.Key == "ReportId").Value);
    }

    /// <summary>Every failure path leaves a correlated entry at the right level with a meaningful message.</summary>
    /// <param name="path">The processing path.</param>
    /// <param name="level">The expected level.</param>
    /// <param name="phrase">A phrase the message must contain.</param>
    [TestMethod]
    [DataRow("missing", LogLevel.Warning, "does not exist")]
    [DataRow("transient-load", LogLevel.Warning, "could not be loaded")]
    [DataRow("transient-rows", LogLevel.Warning, "transient failure")]
    [DataRow("definitive", LogLevel.Error, "failed definitively")]
    [DataRow("definitive", LogLevel.Warning, "marked Failed")]
    [DataRow("exhausted", LogLevel.Warning, "marked Failed")]
    public async Task FailurePathsLeaveCorrelatedLogs(string path, LogLevel level, string phrase)
    {
        ReportRequest report = Stored(attempts: path == "exhausted" ? 5 : 0);
        Guid id = path == "missing" ? Guid.NewGuid() : report.Id;
        _store.LoadFailure = path == "transient-load" ? new TimeoutException() : null;
        _store.RowsFailure = path switch
        {
            "transient-rows" => new TimeoutException(),
            "definitive" => new InvalidOperationException("Simulated calculation defect."),
            _ => null,
        };

        await Processor().ProcessAsync(id, CancellationToken.None);

        LogEntry entry = _processorLog.Entries.Single(item => item.Level == level && item.Message.Contains(phrase, StringComparison.Ordinal));
        Assert.AreEqual(id, entry.Values["ReportId"]);
    }

    /// <summary>A definitive failure stores a stable code and a non-empty message that leaks nothing from the exception.</summary>
    /// <param name="path">The failure path.</param>
    /// <param name="code">The expected stable code.</param>
    [TestMethod]
    [DataRow("definitive", "report.calculation_failed")]
    [DataRow("exhausted", "report.attempts_exhausted")]
    public async Task DefinitiveFailuresStoreASanitizedMessage(string path, string code)
    {
        ReportRequest report = Stored(attempts: path == "exhausted" ? 5 : 0);
        _store.RowsFailure = path == "definitive" ? new InvalidOperationException("SQLite secret detail at /data/expensehub.db") : null;

        await Processor().ProcessAsync(report.Id, CancellationToken.None);

        Assert.AreEqual(code, report.ErrorCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(report.ErrorMessage));
        Assert.DoesNotContain("secret", report.ErrorMessage!);
        Assert.DoesNotContain("/data", report.ErrorMessage!);
        Assert.IsNull(report.ResultJson);
    }

    /// <summary>A successful request logs its registration and its confirmed publication, both correlated by id.</summary>
    [TestMethod]
    public async Task RequestLogsRegistrationAndPublication()
    {
        ReportResponse response = await Service().RequestAsync(Employee(), Body("Mine"), CancellationToken.None);

        LogEntry registered = _serviceLog.Entries.Single(entry => entry.Message.Contains("registered", StringComparison.Ordinal));
        LogEntry published = _serviceLog.Entries.Single(entry => entry.Message.EndsWith("published", StringComparison.Ordinal));
        Assert.AreEqual(response.Id, registered.Values["ReportId"]);
        Assert.AreEqual(ReportScope.Mine, registered.Values["Scope"]);
        Assert.AreEqual(response.Id, published.Values["ReportId"]);
        Assert.IsTrue(_serviceLog.Entries.All(entry => entry.Level == LogLevel.Information));
    }

    /// <summary>With logging disabled, a request produces no entries and the same outcome.</summary>
    [TestMethod]
    public async Task RequestLogsNothingWhenDisabled()
    {
        _serviceLog.Enabled = false;

        ReportResponse response = await Service().RequestAsync(Employee(), Body("Mine"), CancellationToken.None);

        Assert.IsEmpty(_serviceLog.Entries);
        CollectionAssert.AreEqual(new[] { response.Id }, _publisher.Published);
    }

    /// <summary>Every report problem carries a non-empty human message next to its stable code.</summary>
    /// <param name="case">Which problem to provoke.</param>
    /// <param name="code">The expected code.</param>
    [TestMethod]
    [DataRow("forbidden", "report.scope_forbidden")]
    [DataRow("invalid-scope", "report.invalid_scope")]
    [DataRow("not-found", "report.not_found")]
    [DataRow("not-completed", "report.not_completed")]
    [DataRow("unconfirmed", "report.publish_unconfirmed")]
    public async Task ProblemsCarryAHumanMessage(string @case, string code)
    {
        ReportService service = Service();
        Func<Task> act = @case switch
        {
            "forbidden" => () => service.RequestAsync(Employee(), Body("Global"), CancellationToken.None),
            "invalid-scope" => () => service.RequestAsync(Employee(), Body("Everyone"), CancellationToken.None),
            "not-found" => () => service.FindAsync(Employee(), Guid.NewGuid(), CancellationToken.None),
            "not-completed" => () => service.FindCompletedAsync(Employee(), Stored(requester: "employee").Id, CancellationToken.None),
            _ => () => RequestUnconfirmedAsync(service),
        };

        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(act);

        Assert.AreEqual(code, error.Code);
        Assert.IsGreaterThan(10, error.Message.Length);
    }

    /// <summary>Public report boundaries reject null arguments instead of failing later or acting on nothing.</summary>
    [TestMethod]
    public async Task BoundariesRejectNullArguments()
    {
        ReportService service = Service();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.RequestAsync(null!, Body("Mine"), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.RequestAsync(Employee(), null!, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.ListAsync(null!, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.FindAsync(null!, Guid.NewGuid(), CancellationToken.None));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReportCalculator.Eligible(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReportCalculator.Summarize(null!, DateTime.UtcNow));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ReportProcessor(_store, new FixedTimeProvider(), null!, _processorLog));
        Assert.IsEmpty(_store.Reports);
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>A new request starts unowned, Pending, unattempted and at revision 1 until the server fills it.</summary>
    [TestMethod]
    public void NewRequestHasSafeDefaults()
    {
        var report = new ReportRequest();
        Assert.AreEqual(string.Empty, report.RequesterId);
        Assert.AreEqual(ReportStatus.Pending, report.Status);
        Assert.AreEqual(0, report.Attempts);
        Assert.AreEqual(1, report.Revision);
        Assert.AreNotEqual(Guid.Empty, report.Id);
    }

    private static CurrentActor Employee() => new("employee", new List<string> { RoleNames.Employee });

    private static CreateReportRequest Body(string scope) => new()
    {
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
        Scope = scope,
    };

    private ReportService Service() => new(_store, _publisher, new FixedTimeProvider(), _serviceLog);

    private Task<ReportResponse> RequestUnconfirmedAsync(ReportService service)
    {
        _publisher.Unconfirmed = true;
        return service.RequestAsync(Employee(), Body("Mine"), CancellationToken.None);
    }

    private ReportProcessor Processor()
        => new(_store, new FixedTimeProvider(), Options.Create(new ReportProcessingOptions { MaxAttempts = 5 }), _processorLog);

    private ReportRequest Stored(int attempts = 0, string requester = "requester")
    {
        var report = new ReportRequest
        {
            RequesterId = requester,
            Scope = ReportScope.Mine,
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            CreatedAtUtc = new FixedTimeProvider().GetUtcNow().UtcDateTime,
            Attempts = attempts,
        };
        _store.Reports[report.Id] = report;
        _store.Expenses.Add(new Expense { OwnerId = requester, ExpenseDate = new DateOnly(2026, 9, 10), Status = ExpenseStatus.Paid, Amount = 1m });
        return report;
    }
}
