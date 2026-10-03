using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Infrastructure;

namespace ExpenseHub.Api.Services;

/// <summary>Enforces account administration permissions before changing persisted roles.</summary>
public sealed class UserRoleService
{
    private readonly IUserRoleStore _store;

    /// <summary>Initializes administration with its persistence boundary.</summary>
    /// <param name="store">The account persistence boundary.</param>
    public UserRoleService(IUserRoleStore store)
    {
        _store = store;
    }

    /// <summary>Lists accounts only for an Admin actor.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The authorized account views.</returns>
    public Task<IReadOnlyList<UserResponse>> ListAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        EnsureAdmin(actor);
        return _store.ListAsync(cancellationToken);
    }

    /// <summary>Validates and replaces the complete role set for one account.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="userId">The target account identifier.</param>
    /// <param name="request">The requested complete role set.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A task representing the completed update.</returns>
    public async Task ReplaceAsync(CurrentActor actor, string userId, UpdateUserRolesRequest request, CancellationToken cancellationToken)
    {
        EnsureAdmin(actor);
        if (request.Roles is null || request.Roles.Any(role => !RoleNames.All.Contains(role)))
        {
            throw new ApiProblemException(400, "admin.invalid_role", "Only known roles are accepted.");
        }

        List<string> roles = request.Roles.Distinct(StringComparer.Ordinal).OrderBy(role => role, StringComparer.Ordinal).ToList();
        if (string.Equals(actor.UserId, userId, StringComparison.Ordinal) && !roles.Contains(RoleNames.Admin, StringComparer.Ordinal))
        {
            throw new ApiProblemException(403, "admin.self_demotion", "An Admin cannot remove their own Admin role.");
        }

        if (!await _store.ExistsAsync(userId, cancellationToken))
        {
            throw new ApiProblemException(404, "admin.user_not_found", "The account was not found.");
        }

        await _store.ReplaceAsync(userId, roles, cancellationToken);
    }

    private static void EnsureAdmin(CurrentActor actor)
    {
        if (!actor.Roles.Contains(RoleNames.Admin))
        {
            throw new ApiProblemException(403, "auth.forbidden", "The action is not permitted.");
        }
    }
}
