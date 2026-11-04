using System;

namespace ExpenseHub.Api.Contracts.Responses;

/// <summary>A read-only reimbursement view without persistence entities.</summary>
/// <param name="Id">The server-generated identifier.</param>
/// <param name="OwnerId">The authenticated creator.</param>
/// <param name="Description">The trimmed description.</param>
/// <param name="Amount">The exact amount.</param>
/// <param name="ExpenseDate">The expense date.</param>
/// <param name="Status">The lifecycle state name.</param>
/// <param name="CategoryId">The server-assigned category.</param>
/// <param name="CreatedAtUtc">The creation instant in UTC.</param>
/// <param name="Revision">The current concurrency revision.</param>
public sealed record ExpenseResponse(Guid Id, string OwnerId, string Description, decimal Amount,
    DateOnly ExpenseDate, string Status, Guid CategoryId, DateTime CreatedAtUtc, long Revision);
