using System;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Services;

namespace ExpenseHub.Core.Reports;

/// <summary>
/// The profile x scope matrix for report requests: Mine requires Employee and Global requires Auditor.
/// Admin, Approver and Finance grant neither. Reads are never widened by any role: they are owner-only.
/// </summary>
public static class ReportAuthorization
{
    /// <summary>Tells whether the actor may request a report with the given scope.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="scope">The requested scope.</param>
    /// <returns>Whether the request is permitted.</returns>
    public static bool CanRequest(CurrentActor actor, ReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return scope switch
        {
            ReportScope.Mine => actor.Roles.Contains(RoleNames.Employee),
            ReportScope.Global => actor.Roles.Contains(RoleNames.Auditor),
            _ => false,
        };
    }

    /// <summary>Rejects a scope the actor's profile does not grant, before anything is persisted or published.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="scope">The requested scope.</param>
    /// <exception cref="ApiProblemException">403 <c>report.scope_forbidden</c>.</exception>
    public static void EnsureCanRequest(CurrentActor actor, ReportScope scope)
    {
        if (!CanRequest(actor, scope))
        {
            throw new ApiProblemException(403, "report.scope_forbidden", "The report scope is not permitted for this profile.");
        }
    }
}
