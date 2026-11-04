namespace ExpenseHub.Api.Models;

/// <summary>Defines the supported expense lifecycle states.</summary>
public enum ExpenseStatus
{
    /// <summary>The owner can edit or submit the expense.</summary>
    Draft,
    /// <summary>An approver can decide the submitted expense.</summary>
    Submitted,
    /// <summary>Finance can record payment of the approved expense.</summary>
    Approved,
    /// <summary>The request was rejected and is terminal.</summary>
    Rejected,
    /// <summary>The request was paid and is terminal.</summary>
    Paid,
}
