using System;

namespace ExpenseHub.Api.Models;

/// <summary>
/// A unique payment recorded for an approved expense.
/// </summary>
public sealed class PaymentRecord
{
    /// <summary>Gets the server-generated payment identifier.</summary>
    public Guid Id { get; internal set; } = Guid.NewGuid();

    /// <summary>Gets the paid expense identifier.</summary>
    public Guid ExpenseId { get; internal set; }

    /// <summary>Gets the authenticated finance actor identifier.</summary>
    public string ActorId { get; internal set; } = string.Empty;

    /// <summary>Gets the server payment instant in UTC.</summary>
    public DateTime PaidAtUtc { get; internal set; }

    /// <summary>Gets the amount derived from the expense.</summary>
    public decimal Amount { get; internal set; }
}
