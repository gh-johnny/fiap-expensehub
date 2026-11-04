using System;

namespace ExpenseHub.Api.Contracts.Responses;

/// <summary>A revision-ordered, server-authored reimbursement audit event.</summary>
/// <param name="Id">The event identifier.</param>
/// <param name="ExpenseId">The related expense.</param>
/// <param name="Revision">The expense revision.</param>
/// <param name="Action">The recorded action.</param>
/// <param name="ActorId">The authenticated actor.</param>
/// <param name="OccurredAtUtc">The server instant in UTC.</param>
/// <param name="PreviousStatus">The preceding state, absent for creation.</param>
/// <param name="NewStatus">The resulting state.</param>
/// <param name="Reason">The rejection justification when applicable.</param>
/// <param name="PreviousDescription">Description before a Draft edit.</param>
/// <param name="NewDescription">Description after a Draft edit.</param>
/// <param name="PreviousAmount">Amount before a Draft edit.</param>
/// <param name="NewAmount">Amount after a Draft edit.</param>
/// <param name="PreviousExpenseDate">Date before a Draft edit.</param>
/// <param name="NewExpenseDate">Date after a Draft edit.</param>
public sealed record ExpenseHistoryResponse(Guid Id, Guid ExpenseId, long Revision, string Action, string ActorId,
    DateTime OccurredAtUtc, string? PreviousStatus, string NewStatus, string? Reason, string? PreviousDescription,
    string? NewDescription, decimal? PreviousAmount, decimal? NewAmount, DateOnly? PreviousExpenseDate, DateOnly? NewExpenseDate);
