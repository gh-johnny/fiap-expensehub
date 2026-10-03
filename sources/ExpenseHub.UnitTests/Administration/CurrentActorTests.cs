using System.Collections.Generic;
using System.Security.Claims;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Administration;

/// <summary>Checks claims-to-actor contracts without an HTTP host or authentication provider.</summary>
[TestClass]
public sealed class CurrentActorTests
{
    /// <summary>Only authenticated identifier and exact known role claims become permissions.</summary>
    [TestMethod]
    public void AuthenticatedClaimsPreserveKnownRoleUnion()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "owner"),
            new(ClaimTypes.Role, RoleNames.Admin),
            new(ClaimTypes.Role, RoleNames.Employee),
            new(ClaimTypes.Role, RoleNames.Employee),
            new(ClaimTypes.Role, "employee"),
            new(ClaimTypes.Role, "Root"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "fixture"));
        CurrentActor actor = CurrentActor.FromPrincipal(principal);
        Assert.AreEqual("owner", actor.UserId);
        Assert.HasCount(2, actor.Roles);
        Assert.Contains(RoleNames.Admin, actor.Roles);
        Assert.Contains(RoleNames.Employee, actor.Roles);
        Assert.DoesNotContain("Root", actor.Roles);
    }

    /// <summary>Authentication without the account identifier is rejected.</summary>
    [TestMethod]
    public void AuthenticatedPrincipalRequiresIdentifier()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.Role, RoleNames.Employee) }, "fixture"));
        ApiProblemException error = Assert.ThrowsExactly<ApiProblemException>(() => CurrentActor.FromPrincipal(principal));
        Assert.AreEqual(401, error.StatusCode);
    }

    /// <summary>A display name cannot stand in for the authenticated account identifier.</summary>
    [TestMethod]
    public void NameClaimDoesNotGrantAnAccountIdentity()
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "owner"), new(ClaimTypes.Role, RoleNames.Employee) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "fixture"));
        ApiProblemException error = Assert.ThrowsExactly<ApiProblemException>(() => CurrentActor.FromPrincipal(principal));
        Assert.AreEqual(401, error.StatusCode);
    }
}
