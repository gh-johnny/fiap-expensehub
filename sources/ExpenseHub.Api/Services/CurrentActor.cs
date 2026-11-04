using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using ExpenseHub.Api.Infrastructure;

namespace ExpenseHub.Api.Services;

/// <summary>An immutable snapshot of an authenticated account and its known roles.</summary>
public sealed class CurrentActor
{
    /// <summary>Copies the authenticated identity into an immutable snapshot.</summary>
    /// <param name="userId">The server-authenticated account identifier.</param>
    /// <param name="roles">The roles granted by authentication.</param>
    public CurrentActor(string userId, IEnumerable<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(roles);
        UserId = userId;
        Roles = roles.Where(RoleNames.All.Contains).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>Gets the authenticated account identifier.</summary>
    public string UserId { get; }

    /// <summary>Gets the immutable set of known roles.</summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>Builds an actor from a principal validated by authentication.</summary>
    /// <param name="principal">The request principal.</param>
    /// <returns>The immutable actor snapshot.</returns>
    public static CurrentActor FromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        string? userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            throw new ApiProblemException(401, "auth.required", "Authentication is required.");
        }

        return new CurrentActor(userId, principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value));
    }
}
