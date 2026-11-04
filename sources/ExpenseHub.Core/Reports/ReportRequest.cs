using System;

namespace ExpenseHub.Core.Reports;

/// <summary>A persisted report request with server-derived requester, period, scope and processing state.</summary>
public sealed class ReportRequest
{
    /// <summary>Gets the server-generated identifier, also used as the message key.</summary>
    public Guid Id { get; internal set; } = Guid.NewGuid();

    /// <summary>Gets the authenticated requester; never supplied by the client or the message.</summary>
    public string RequesterId { get; internal set; } = string.Empty;

    /// <summary>Gets the scope registered at request time.</summary>
    public ReportScope Scope { get; internal set; }

    /// <summary>Gets the inclusive start of the expense-date period.</summary>
    public DateOnly FromDate { get; internal set; }

    /// <summary>Gets the inclusive end of the expense-date period.</summary>
    public DateOnly ToDate { get; internal set; }

    /// <summary>Gets the processing state.</summary>
    public ReportStatus Status { get; internal set; } = ReportStatus.Pending;

    /// <summary>Gets the registration instant in UTC.</summary>
    public DateTime CreatedAtUtc { get; internal set; }

    /// <summary>Gets the instant the worker first started processing, in UTC.</summary>
    public DateTime? StartedAtUtc { get; internal set; }

    /// <summary>Gets the instant the request reached a terminal state, in UTC.</summary>
    public DateTime? CompletedAtUtc { get; internal set; }

    /// <summary>Gets the persisted result serialized as JSON; filled only when Completed.</summary>
    public string? ResultJson { get; internal set; }

    /// <summary>Gets the stable error code; filled only when Failed.</summary>
    public string? ErrorCode { get; internal set; }

    /// <summary>Gets the sanitized error message; filled only when Failed.</summary>
    public string? ErrorMessage { get; internal set; }

    /// <summary>Gets how many times the worker started processing this request.</summary>
    public int Attempts { get; internal set; }

    /// <summary>Gets the application-managed concurrency revision.</summary>
    public long Revision { get; internal set; } = 1;
}
