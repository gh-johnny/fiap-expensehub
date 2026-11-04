using System;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Services;

/// <summary>Represents the union of role-based read permissions applied before materialization.</summary>
/// <param name="OwnerId">The authenticated account identifier.</param>
/// <param name="Own">Whether all states owned by this account are visible.</param>
/// <param name="Submitted">Whether submitted expenses are visible.</param>
/// <param name="Finance">Whether approved and paid expenses are visible.</param>
/// <param name="All">Whether every expense is visible for an Auditor.</param>
public sealed record ExpenseReadScope(string OwnerId, bool Own, bool Submitted, bool Finance, bool All)
{
    /// <summary>Evaluates the same visibility union independently of a database.</summary>
    /// <param name="expense">The expense being checked.</param>
    /// <returns>Whether any granted permission makes it visible.</returns>
    public bool Matches(Expense expense)
        => All || (Own && string.Equals(OwnerId, expense.OwnerId, StringComparison.Ordinal))
            || (Submitted && expense.Status == ExpenseStatus.Submitted)
            || (Finance && (expense.Status == ExpenseStatus.Approved || expense.Status == ExpenseStatus.Paid));
}
