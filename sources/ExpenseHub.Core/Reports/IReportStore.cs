using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ExpenseHub.Core.Reports;

/// <summary>Separates report rules from relational persistence and commit semantics.</summary>
public interface IReportStore
{
    /// <summary>Stages a new request in the current unit of work.</summary>
    /// <param name="report">The server-created request.</param>
    void Add(ReportRequest report);

    /// <summary>Commits staged changes atomically.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A task representing the commit.</returns>
    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>Lists only the requests created by the requester, newest first.</summary>
    /// <param name="requesterId">The authenticated requester.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The requester's own requests.</returns>
    Task<IReadOnlyList<ReportRequest>> ListOwnedAsync(string requesterId, CancellationToken cancellationToken);

    /// <summary>Finds a request only if the requester created it.</summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="requesterId">The authenticated requester.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The request, or null for both absent and foreign requests.</returns>
    Task<ReportRequest?> FindOwnedAsync(Guid id, string requesterId, CancellationToken cancellationToken);

    /// <summary>Loads a tracked request for the worker, regardless of requester; the message only carries its id.</summary>
    /// <param name="id">The request identifier.</param>
    /// <param name="cancellationToken">Processing cancellation.</param>
    /// <returns>The request, or null when absent.</returns>
    Task<ReportRequest?> LoadForProcessingAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Reads, at processing time, the status and amount of the expenses eligible for the persisted request.</summary>
    /// <param name="report">The persisted request; its period, scope and requester drive the SQL filter.</param>
    /// <param name="cancellationToken">Processing cancellation.</param>
    /// <returns>The eligible rows.</returns>
    Task<IReadOnlyList<ReportExpenseRow>> LoadRowsAsync(ReportRequest report, CancellationToken cancellationToken);
}
