using System;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Services;

/// <summary>Enforces action permissions, ownership and lifecycle before any mutation.</summary>
public static class ExpenseAuthorization
{
    /// <summary>Builds the union of expense read permissions and rejects roleless or Admin-only actors.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <returns>The scope to apply in the persistence query.</returns>
    public static ExpenseReadScope ReadScope(CurrentActor actor)
    {
        var scope = new ExpenseReadScope(actor.UserId, actor.Roles.Contains(RoleNames.Employee),
            actor.Roles.Contains(RoleNames.Approver), actor.Roles.Contains(RoleNames.Finance), actor.Roles.Contains(RoleNames.Auditor));
        if (!scope.Own && !scope.Submitted && !scope.Finance && !scope.All)
        {
            throw new ApiProblemException(403, "auth.forbidden", "Expense reading is not permitted.");
        }

        return scope;
    }

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
