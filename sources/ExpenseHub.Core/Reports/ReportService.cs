using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Services;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Core.Reports;

/// <summary>Registers, publishes and exposes report requests; the calculation runs in the worker.</summary>
public sealed class ReportService
{
    private readonly IReportStore _store;
    private readonly IReportPublisher _publisher;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReportService> _logger;

    /// <summary>Initializes the service with its persistence, broker and clock boundaries.</summary>
    /// <param name="store">The report persistence boundary.</param>
    /// <param name="publisher">The confirmed broker publisher.</param>
    /// <param name="clock">The server clock.</param>
    /// <param name="logger">The correlated logger.</param>
    public ReportService(IReportStore store, IReportPublisher publisher, TimeProvider clock, ILogger<ReportService> logger)
    {
        _store = store;
        _publisher = publisher;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Validates and persists a Pending request for the authenticated requester, then publishes it.
    /// Returns only after the broker confirms the message.
    /// </summary>
    /// <param name="actor">The authenticated actor; the only source of the requester.</param>
    /// <param name="request">The period and scope.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The Pending envelope.</returns>
    /// <exception cref="ApiProblemException">400 for invalid input; 503 with the persisted id when unconfirmed.</exception>
    public async Task<ReportResponse> RequestAsync(CurrentActor actor, CreateReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        (DateOnly from, DateOnly to) = ValidatePeriod(request);
        ReportScope scope = ParseScope(request.Scope);
        var report = new ReportRequest
        {
            RequesterId = actor.UserId,
            Scope = scope,
            FromDate = from,
            ToDate = to,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
        };
        _store.Add(report);
        await _store.SaveAsync(cancellationToken);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Report {ReportId} registered with scope {Scope}", report.Id, report.Scope);
        }

        try
        {
            await _publisher.PublishAsync(report.Id, cancellationToken);
        }
        catch (ReportPublishException exception)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Report {ReportId} persisted but its publication was not confirmed", report.Id);
            }

            throw new ApiProblemException(503, "report.publish_unconfirmed",
                "The report was registered but could not be queued. Query it by id; a new request creates another report.",
                new Dictionary<string, object?> { ["reportId"] = report.Id });
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Report {ReportId} published", report.Id);
        }

        return ReportResultJson.View(report);
    }

    /// <summary>Lists only the requests the actor created.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The actor's envelopes, newest first.</returns>
    public async Task<IReadOnlyList<ReportResponse>> ListAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        IReadOnlyList<ReportRequest> reports = await _store.ListOwnedAsync(actor.UserId, cancellationToken);
        return reports.Select(ReportResultJson.View).ToList();
    }

    /// <summary>Reads progress and the persisted result of an own request, without recalculating.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="id">The request identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="ApiProblemException">404 for absent or foreign requests.</exception>
    public async Task<ReportResponse> FindAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
        => ReportResultJson.View(await LoadOwnedAsync(actor, id, cancellationToken));

    /// <summary>Returns an own Completed request so its persisted result can be exported.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="id">The request identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The Completed envelope with its result.</returns>
    /// <exception cref="ApiProblemException">404 for absent or foreign requests; 409 when not Completed.</exception>
    public async Task<ReportResponse> FindCompletedAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ReportRequest report = await LoadOwnedAsync(actor, id, cancellationToken);
        if (report.Status != ReportStatus.Completed)
        {
            throw new ApiProblemException(409, "report.not_completed", "Only a Completed report can be downloaded.");
        }

        return ReportResultJson.View(report);
    }

    private static (DateOnly From, DateOnly To) ValidatePeriod(CreateReportRequest request)
    {
        if (request.FromDate is not DateOnly from || request.ToDate is not DateOnly to)
        {
            throw new ApiProblemException(400, "report.invalid_period", "fromDate and toDate are required (yyyy-MM-dd).");
        }

        if (from > to)
        {
            throw new ApiProblemException(400, "report.invalid_period", "fromDate must be on or before toDate.");
        }

        return (from, to);
    }

    private static ReportScope ParseScope(string? scope)
    {
        // Explicit names only: Enum.TryParse would also accept numeric strings such as "1".
        if (string.Equals(scope, nameof(ReportScope.Mine), StringComparison.OrdinalIgnoreCase))
        {
            return ReportScope.Mine;
        }

        if (string.Equals(scope, nameof(ReportScope.Global), StringComparison.OrdinalIgnoreCase))
        {
            return ReportScope.Global;
        }

        throw new ApiProblemException(400, "report.invalid_scope", "scope must be Mine or Global.");
    }

    private async Task<ReportRequest> LoadOwnedAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return await _store.FindOwnedAsync(id, actor.UserId, cancellationToken)
            ?? throw new ApiProblemException(404, "report.not_found", "The report was not found.");
    }
}
