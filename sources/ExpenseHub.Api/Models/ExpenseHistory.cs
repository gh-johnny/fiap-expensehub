using System;

namespace ExpenseHub.Api.Models;

/// <summary>
/// An immutable persisted event for one expense revision.
/// </summary>
public sealed class ExpenseHistory
{
    /// <summary>Gets the server-generated event identifier.</summary>
    public Guid Id { get; internal set; } = Guid.NewGuid();

    /// <summary>Gets the related expense identifier.</summary>
    public Guid ExpenseId { get; internal set; }

    /// <summary>Gets the revision recorded by this event.</summary>
    public long Revision { get; internal set; }

    /// <summary>Gets the recorded action name.</summary>
    public string Action { get; internal set; } = string.Empty;

    /// <summary>Gets the authenticated actor identifier.</summary>
    public string ActorId { get; internal set; } = string.Empty;

    /// <summary>Gets the server event instant in UTC.</summary>
    public DateTime OccurredAtUtc { get; internal set; }

    /// <summary>Gets the preceding state, absent for creation.</summary>
    public ExpenseStatus? PreviousStatus { get; internal set; }

    /// <summary>Gets the resulting state.</summary>
    public ExpenseStatus NewStatus { get; internal set; }

    /// <summary>Gets the rejection reason when applicable.</summary>
    public string? Reason { get; internal set; }

    /// <summary>Gets the description before a draft change.</summary>
    public string? PreviousDescription { get; internal set; }

    /// <summary>Gets the description after a draft change.</summary>
    public string? NewDescription { get; internal set; }

    /// <summary>Gets the amount before a draft change.</summary>
    public decimal? PreviousAmount { get; internal set; }

    /// <summary>Gets the amount after a draft change.</summary>
    public decimal? NewAmount { get; internal set; }

    /// <summary>Gets the date before a draft change.</summary>
    public DateOnly? PreviousExpenseDate { get; internal set; }

    /// <summary>Gets the date after a draft change.</summary>
    public DateOnly? NewExpenseDate { get; internal set; }
}
