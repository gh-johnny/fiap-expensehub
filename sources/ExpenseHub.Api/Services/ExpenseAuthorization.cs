using System;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Services;

/// <summary>Enforces action permissions, ownership and lifecycle before any mutation.</summary>
public static class ExpenseAuthorization
{
    /// <summary>Requires the role that grants a particular action.</summary>
    /// <param name="actor">The authenticated immutable actor.</param>
    /// <param name="role">The required role.</param>
    public static void RequireRole(CurrentActor actor, string role)
    {
        if (!actor.Roles.Contains(role))
        {
            throw new ApiProblemException(403, "auth.forbidden", "The action is not permitted.");
        }
    }

    /// <summary>Allows only an Employee editing or submitting their own draft.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="expense">The loaded expense.</param>
    public static void RequireOwnDraft(CurrentActor actor, Expense expense)
    {
        RequireRole(actor, RoleNames.Employee);
        if (!string.Equals(actor.UserId, expense.OwnerId, StringComparison.Ordinal))
        {
            throw new ApiProblemException(403, "expense.not_owner", "Only the owner can change this draft.");
        }

        RequireState(expense, ExpenseStatus.Draft);
    }

    /// <summary>Rejects invalid and repeated transitions with a conflict.</summary>
    /// <param name="expense">The loaded expense.</param>
    /// <param name="expected">The state required by the action.</param>
    public static void RequireState(Expense expense, ExpenseStatus expected)
    {
        if (expense.Status != expected)
        {
            throw new ApiProblemException(409, "expense.invalid_state", "The action is incompatible with the current state.");
        }
    }
}
