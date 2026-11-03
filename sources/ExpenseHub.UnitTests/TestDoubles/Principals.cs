using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseHub.UnitTests.TestDoubles;

/// <summary>Builds authenticated principals and controller contexts without an authentication server.</summary>
internal static class Principals
{
    public static ClaimsPrincipal Authenticated(string userId, params string[] roles)
    {
        IEnumerable<Claim> claims = roles.Select(role => new Claim(ClaimTypes.Role, role))
            .Prepend(new Claim(ClaimTypes.NameIdentifier, userId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "fixture"));
    }

    public static ControllerContext Context(string userId, params string[] roles)
        => new() { HttpContext = new DefaultHttpContext { User = Authenticated(userId, roles) } };
}
