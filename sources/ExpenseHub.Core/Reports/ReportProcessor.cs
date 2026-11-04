using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExpenseHub.Core.Reports;

/// <summary>What the consumer must do with the delivery after <see cref="ReportProcessor.ProcessAsync"/>.</summary>
public enum ReportProcessingOutcome
{
    /// <summary>The result was persisted; acknowledge.</summary>
    Completed,
    /// <summary>The request was already terminal; acknowledge without any new effect.</summary>
    Duplicate,
    /// <summary>A definitive failure was persisted; acknowledge so it is not redelivered forever.</summary>
    Failed,
    /// <summary>No such request exists; acknowledge and log.</summary>
    NotFound,
    /// <summary>A transient failure (database busy or unavailable); return the message to the queue.</summary>
    Retry,
}

/// <summary>
/// Processes one report request idempotently. The persisted state is the protection: a Completed or Failed request
/// never produces a second effect, even after a worker restart, while Pending and Processing are always resumable.
/// </summary>
public sealed class ReportProcessor
{
    private readonly IReportStore _store;
    private readonly TimeProvider _clock;
    private readonly ReportProcessingOptions _options;
    private readonly ILogger<ReportProcessor> _logger;

    /// <summary>Initializes the processor.</summary>
    /// <param name="store">The report persistence boundary.</param>
    /// <param name="clock">The server clock.</param>
    /// <param name="options">The attempt limit.</param>
    /// <param name="logger">The correlated logger.</param>
    public ReportProcessor(IReportStore store, TimeProvider clock, IOptions<ReportProcessingOptions> options, ILogger<ReportProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _store = store;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Loads the persisted request (never trusting the message beyond its id), calculates and persists the outcome.</summary>
    /// <param name="reportId">The identifier carried by the message.</param>
    /// <param name="cancellationToken">Worker shutdown.</param>
    /// <returns>How the delivery must be settled; it is only returned after the state was persisted.</returns>
    /// <exception cref="Exception">Persisting a definitive failure itself failed; the delivery must not be acknowledged.</exception>
    public async Task<ReportProcessingOutcome> ProcessAsync(Guid reportId, CancellationToken cancellationToken)
    {
        using IDisposable? scope = _logger.BeginScope("ReportId:{ReportId}", reportId);
        ReportRequest? report;
        try
        {
            report = await _store.LoadForProcessingAsync(reportId, cancellationToken);
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Report {ReportId} could not be loaded; it will be retried", reportId);
            }

            return ReportProcessingOutcome.Retry;
        }

        if (report is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Report {ReportId} does not exist; message discarded", reportId);
            }

            return ReportProcessingOutcome.NotFound;
        }

        if (report.Status is ReportStatus.Completed or ReportStatus.Failed)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Report {ReportId} is already {Status}; duplicate delivery ignored", reportId, report.Status);
            }

            return ReportProcessingOutcome.Duplicate;
        }

        if (report.Attempts >= _options.MaxAttempts)
        {
            await FailAsync(report, "report.attempts_exhausted", "The report could not be processed after several attempts.", cancellationToken);
            return ReportProcessingOutcome.Failed;
        }

        try
        {
            await StartAsync(report, cancellationToken);
            IReadOnlyList<ReportExpenseRow> rows = await _store.LoadRowsAsync(report, cancellationToken);
            DateTime now = _clock.GetUtcNow().UtcDateTime;
            report.ResultJson = ReportResultJson.Serialize(ReportCalculator.Summarize(rows, now));
            report.Status = ReportStatus.Completed;
            report.CompletedAtUtc = now;
            report.Revision++;
            await _store.SaveAsync(cancellationToken);
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Report {ReportId} hit a transient failure; it will be retried", reportId);
            }

            return ReportProcessingOutcome.Retry;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(exception, "Report {ReportId} failed definitively", reportId);
            }

            await FailAsync(report, "report.calculation_failed", "The report could not be calculated.", cancellationToken);
            return ReportProcessingOutcome.Failed;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Report {ReportId} completed on attempt {Attempt}", reportId, report.Attempts);
        }

        return ReportProcessingOutcome.Completed;
    }

    /// <summary>
    /// Database and I/O availability problems, including optimistic-concurrency conflicts (another delivery won the
    /// race and the redelivery will see the terminal state), are worth retrying; everything else is definitive.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>Whether the delivery should be retried.</returns>
    public static bool IsTransient(Exception exception)
        => exception is DbException or DbUpdateException or TimeoutException or IOException;

    private async Task StartAsync(ReportRequest report, CancellationToken cancellationToken)
    {
        report.Status = ReportStatus.Processing;
        report.StartedAtUtc ??= _clock.GetUtcNow().UtcDateTime;
        report.Attempts++;
        report.Revision++;
        await _store.SaveAsync(cancellationToken);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Report {ReportId} started attempt {Attempt}", report.Id, report.Attempts);
        }
    }

    private async Task FailAsync(ReportRequest report, string code, string message, CancellationToken cancellationToken)
    {
        report.Status = ReportStatus.Failed;
        report.ErrorCode = code;
        report.ErrorMessage = message;
        report.ResultJson = null;
        report.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
        report.Revision++;
        // If this save fails the exception propagates: the delivery is not acknowledged as if the failure had been stored.
        await _store.SaveAsync(cancellationToken);
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("Report {ReportId} marked Failed with {ErrorCode}", report.Id, code);
        }
    }
}
