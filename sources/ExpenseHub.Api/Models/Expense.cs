using System;

namespace ExpenseHub.Api.Models;

/// <summary>
/// A persisted reimbursement request with server-managed ownership and state.
/// </summary>
public sealed class Expense
{
    /// <summary>Gets the server-generated identifier.</summary>
    public Guid Id { get; internal set; } = Guid.NewGuid();

    /// <summary>Gets the authenticated owner identifier.</summary>
    public string OwnerId { get; internal set; } = string.Empty;

    /// <summary>Gets the validated expense description.</summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>Gets the exact expense amount.</summary>
    public decimal Amount { get; internal set; }

    /// <summary>Gets the expense date.</summary>
    public DateOnly ExpenseDate { get; internal set; }

    /// <summary>Gets the current lifecycle state.</summary>
    public ExpenseStatus Status { get; internal set; } = ExpenseStatus.Draft;

    /// <summary>Gets the server-assigned category.</summary>
    public Guid CategoryId { get; internal set; } = ExpenseCategory.GeneralId;

    /// <summary>Gets the server creation instant in UTC.</summary>
    public DateTime CreatedAtUtc { get; internal set; }

    /// <summary>Gets the application-managed concurrency revision.</summary>
    public long Revision { get; internal set; } = 1;
}
