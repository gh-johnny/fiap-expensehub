using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Reports;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Checks idempotent processing, resumability and failure classification without a broker or database.</summary>
[TestClass]
public sealed class ReportProcessorTests
{
    private readonly ReportStoreFake _store = new();
    private readonly ListLogger<ReportProcessor> _logger = new();

    /// <summary>A Pending request is started, calculated on current data and persisted Completed with its result.</summary>
    [TestMethod]
    public async Task PendingRequestCompletes()
    {
        ReportRequest report = Stored(ReportStatus.Pending);
        _store.Expenses.Add(Expense("requester", ExpenseStatus.Approved, 100m));
        _store.Expenses.Add(Expense("someone-else", ExpenseStatus.Approved, 200m));

        Assert.AreEqual(ReportProcessingOutcome.Completed, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(ReportStatus.Completed, report.Status);
        Assert.AreEqual(1, report.Attempts);
        Assert.AreEqual(Now, report.StartedAtUtc);
        Assert.AreEqual(Now, report.CompletedAtUtc);
        ReportResult result = ReportResultJson.Deserialize(report.ResultJson)!;
        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(100m, result.TotalAmount);
        Assert.AreEqual(Now, result.GeneratedAt);
        Assert.AreEqual(2, _store.Saves);
        Assert.IsNull(report.ErrorCode);
    }

    /// <summary>A redelivered terminal request produces no new result, save or calculation.</summary>
    /// <param name="status">The persisted terminal state.</param>
    [TestMethod]
    [DataRow(ReportStatus.Completed)]
    [DataRow(ReportStatus.Failed)]
    public async Task TerminalRequestIsADuplicate(ReportStatus status)
    {
        ReportRequest report = Stored(status);
        report.ResultJson = status == ReportStatus.Completed ? "{\"persisted\":true}" : null;
        long revision = report.Revision;

        Assert.AreEqual(ReportProcessingOutcome.Duplicate, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(status, report.Status);
        Assert.AreEqual(revision, report.Revision);
        Assert.AreEqual(0, _store.Saves);
        Assert.AreEqual(0, _store.RowLoads);
        Assert.IsTrue(_logger.Entries.Exists(entry => entry.Message.Contains("duplicate", StringComparison.Ordinal)));
    }

    /// <summary>Protection is persistent: a fresh processor instance (a restarted worker) still sees the Completed state.</summary>
    [TestMethod]
    public async Task ProtectionSurvivesANewProcessorInstance()
    {
        ReportRequest report = Stored(ReportStatus.Pending);
        await Processor().ProcessAsync(report.Id, CancellationToken.None);
        string? firstResult = report.ResultJson;

        Assert.AreEqual(ReportProcessingOutcome.Duplicate, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(firstResult, report.ResultJson);
        Assert.AreEqual(1, _store.RowLoads);
    }

    /// <summary>A request left in Processing by a crashed attempt is resumed, keeping its first start instant.</summary>
    [TestMethod]
    public async Task ProcessingRequestIsResumed()
    {
        ReportRequest report = Stored(ReportStatus.Processing);
        DateTime firstStart = Now.AddMinutes(-3);
        report.StartedAtUtc = firstStart;
        report.Attempts = 1;

        Assert.AreEqual(ReportProcessingOutcome.Completed, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(2, report.Attempts);
        Assert.AreEqual(firstStart, report.StartedAtUtc);
    }

    /// <summary>A request that already consumed every attempt is failed instead of redelivered forever.</summary>
    [TestMethod]
    public async Task ExhaustedAttemptsFailDefinitively()
    {
        ReportRequest report = Stored(ReportStatus.Processing);
        report.Attempts = 3;

        Assert.AreEqual(ReportProcessingOutcome.Failed, await Processor(maxAttempts: 3).ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(ReportStatus.Failed, report.Status);
        Assert.AreEqual("report.attempts_exhausted", report.ErrorCode);
        Assert.AreEqual(0, _store.RowLoads);
        Assert.IsNull(report.ResultJson);
    }

    /// <summary>An unknown identifier is reported so the consumer can discard the message.</summary>
    [TestMethod]
    public async Task UnknownRequestIsNotFound()
        => Assert.AreEqual(ReportProcessingOutcome.NotFound, await Processor().ProcessAsync(Guid.NewGuid(), CancellationToken.None));

    /// <summary>Database or I/O unavailability while loading or reading rows is retried, never persisted as Failed.</summary>
    /// <param name="stage">Where the transient failure happens.</param>
    [TestMethod]
    [DataRow("load")]
    [DataRow("rows-db")]
    [DataRow("rows-timeout")]
    [DataRow("rows-io")]
    [DataRow("concurrency")]
    public async Task TransientFailuresAreRetried(string stage)
    {
        ReportRequest report = Stored(ReportStatus.Pending);
        switch (stage)
        {
            case "load":
                _store.LoadFailure = new TransientDbException();
                break;
            case "rows-db":
                _store.RowsFailure = new TransientDbException();
                break;
            case "rows-timeout":
                _store.RowsFailure = new TimeoutException();
                break;
            case "rows-io":
                _store.RowsFailure = new IOException();
                break;
            default:
                _store.SaveFailure = save => save == 2 ? new DbUpdateConcurrencyException() : null;
                break;
        }

        Assert.AreEqual(ReportProcessingOutcome.Retry, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreNotEqual(ReportStatus.Failed, report.Status);
        Assert.IsNull(report.ErrorCode);
    }

    /// <summary>A definitive calculation error persists Failed with a sanitized message and no result.</summary>
    [TestMethod]
    public async Task DefinitiveFailureIsPersistedSanitized()
    {
        ReportRequest report = Stored(ReportStatus.Pending);
        _store.RowsFailure = new InvalidOperationException("SELECT * FROM secret_table");

        Assert.AreEqual(ReportProcessingOutcome.Failed, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(ReportStatus.Failed, report.Status);
        Assert.AreEqual("report.calculation_failed", report.ErrorCode);
        Assert.DoesNotContain("secret", report.ErrorMessage!, StringComparison.Ordinal);
        Assert.IsNull(report.ResultJson);
        Assert.AreEqual(Now, report.CompletedAtUtc);
    }

    /// <summary>If storing the Failed state fails, the exception escapes so the delivery is not acknowledged.</summary>
    [TestMethod]
    public async Task FailureToStoreFailedStatePropagates()
    {
        ReportRequest report = Stored(ReportStatus.Pending);
        _store.RowsFailure = new InvalidOperationException("definitive");
        _store.SaveFailure = save => save == 2 ? new InvalidOperationException("disk full") : null;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Processor().ProcessAsync(report.Id, CancellationToken.None));
    }

    /// <summary>Shutdown cancellation is not misclassified as a definitive failure.</summary>
    [TestMethod]
    public async Task CancellationIsNotAFailure()
    {
        ReportRequest report = Stored(ReportStatus.Pending);
        _store.RowsFailure = new OperationCanceledException();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreNotEqual(ReportStatus.Failed, report.Status);
    }

    /// <summary>The scope and requester come from the persisted request; Mine never counts another owner.</summary>
    [TestMethod]
    public async Task PersistedScopeDrivesTheCalculation()
    {
        ReportRequest global = Stored(ReportStatus.Pending, ReportScope.Global);
        _store.Expenses.Add(Expense("requester", ExpenseStatus.Paid, 1m));
        _store.Expenses.Add(Expense("someone-else", ExpenseStatus.Paid, 2m));

        await Processor().ProcessAsync(global.Id, CancellationToken.None);
        Assert.AreEqual(3m, ReportResultJson.Deserialize(global.ResultJson)!.TotalAmount);
    }

    /// <summary>Every log entry of a processing run carries the report id.</summary>
    [TestMethod]
    public async Task LogsAreCorrelatedByReportId()
    {
        ReportRequest report = Stored(ReportStatus.Pending);
        await Processor().ProcessAsync(report.Id, CancellationToken.None);
        Assert.IsGreaterThanOrEqualTo(2, _logger.Entries.Count);
        Assert.IsTrue(_logger.Entries.All(entry => Equals(entry.Values["ReportId"], report.Id)));
    }

    /// <summary>Disabled logging changes nothing in the outcome.</summary>
    [TestMethod]
    public async Task WorksWithLoggingDisabled()
    {
        _logger.Enabled = false;
        ReportRequest report = Stored(ReportStatus.Pending);
        Assert.AreEqual(ReportProcessingOutcome.Completed, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(ReportProcessingOutcome.Duplicate, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(ReportProcessingOutcome.NotFound, await Processor().ProcessAsync(Guid.NewGuid(), CancellationToken.None));
        _store.LoadFailure = new TransientDbException();
        Assert.AreEqual(ReportProcessingOutcome.Retry, await Processor().ProcessAsync(report.Id, CancellationToken.None));
        _store.LoadFailure = null;
        ReportRequest failing = Stored(ReportStatus.Pending);
        _store.RowsFailure = new InvalidOperationException();
        Assert.AreEqual(ReportProcessingOutcome.Failed, await Processor().ProcessAsync(failing.Id, CancellationToken.None));
        _store.RowsFailure = new TimeoutException();
        ReportRequest retrying = Stored(ReportStatus.Pending);
        Assert.AreEqual(ReportProcessingOutcome.Retry, await Processor().ProcessAsync(retrying.Id, CancellationToken.None));
        Assert.IsEmpty(_logger.Entries);
    }

    private static DateTime Now => new FixedTimeProvider().GetUtcNow().UtcDateTime;

    private static Expense Expense(string owner, ExpenseStatus status, decimal amount) => new()
    {
        OwnerId = owner,
        ExpenseDate = new DateOnly(2026, 9, 10),
        Status = status,
        Amount = amount,
    };

    private ReportProcessor Processor(int maxAttempts = 5)
        => new(_store, new FixedTimeProvider(), Options.Create(new ReportProcessingOptions { MaxAttempts = maxAttempts }), _logger);

    private ReportRequest Stored(ReportStatus status, ReportScope scope = ReportScope.Mine)
    {
        var report = new ReportRequest
        {
            RequesterId = "requester",
            Scope = scope,
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            Status = status,
            CreatedAtUtc = Now.AddMinutes(-5),
        };
        _store.Reports[report.Id] = report;
        return report;
    }
}
