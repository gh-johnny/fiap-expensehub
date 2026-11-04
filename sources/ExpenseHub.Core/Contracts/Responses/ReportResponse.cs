using System;
using ExpenseHub.Core.Reports;

namespace ExpenseHub.Core.Contracts.Responses;

/// <summary>The report envelope returned by every report route; instants are UTC.</summary>
/// <param name="Id">The server-generated identifier.</param>
/// <param name="Scope">The registered scope name.</param>
/// <param name="FromDate">The inclusive start date.</param>
/// <param name="ToDate">The inclusive end date.</param>
/// <param name="Status">The processing state name.</param>
/// <param name="CreatedAt">The registration instant.</param>
/// <param name="StartedAt">The first processing instant, if any.</param>
/// <param name="CompletedAt">The terminal instant, if any.</param>
/// <param name="Result">The persisted result when Completed.</param>
/// <param name="Error">The sanitized error when Failed.</param>
public sealed record ReportResponse(Guid Id, string Scope, DateOnly FromDate, DateOnly ToDate, string Status,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, ReportResult? Result, ReportError? Error);
