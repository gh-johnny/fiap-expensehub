using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Administration;

/// <summary>Verifies administration guards independently of Identity, database and HTTP.</summary>
[TestClass]
public sealed class UserRoleServiceTests
{
    /// <summary>Only Admin grants access even when other roles exist.</summary>
    /// <param name="role">A role that does not grant administration.</param>
    [TestMethod]
    [DataRow(RoleNames.Employee)]
    [DataRow(RoleNames.Approver)]
    [DataRow(RoleNames.Finance)]
    [DataRow(RoleNames.Auditor)]
    [DataRow("Unknown")]
    public async Task NonAdminCannotReplaceRoles(string role)
    {
        var store = new UserRoleStoreFake();
        var service = new UserRoleService(store);
        var actor = new CurrentActor("reader", new List<string> { role });
        var request = new UpdateUserRolesRequest { Roles = new List<string> { RoleNames.Admin } };
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ReplaceAsync(actor, "target", request, CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
        Assert.AreEqual(0, store.Replacements);
        CollectionAssert.AreEqual(new List<string> { RoleNames.Employee }, store.Accounts["target"]);
    }

    /// <summary>Listing also enforces the service guard without a controller.</summary>
    [TestMethod]
    public async Task NonAdminCannotListAccounts()
    {
        var service = new UserRoleService(new UserRoleStoreFake());
        var actor = new CurrentActor("reader", new List<string> { RoleNames.Auditor });
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ListAsync(actor, CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
    }

    /// <summary>An invalid role anywhere in the request prevents every change.</summary>
    [TestMethod]
    public async Task UnknownRoleLeavesOriginalSetUntouched()
    {
        var store = new UserRoleStoreFake();
        var service = new UserRoleService(store);
        var request = new UpdateUserRolesRequest
        {
            Roles = new List<string> { RoleNames.Finance, "Root" },
        };
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ReplaceAsync(Admin(), "target", request, CancellationToken.None));
        Assert.AreEqual(400, error.StatusCode);
        Assert.AreEqual("admin.invalid_role", error.Code);
        Assert.AreEqual(0, store.Replacements);
        CollectionAssert.AreEqual(new List<string> { RoleNames.Employee }, store.Accounts["target"]);
    }

    /// <summary>A missing role collection is rejected before persistence.</summary>
    [TestMethod]
    public async Task NullRoleSetIsRejected()
    {
        var store = new UserRoleStoreFake();
        var service = new UserRoleService(store);
        var request = new UpdateUserRolesRequest { Roles = null! };
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ReplaceAsync(Admin(), "target", request, CancellationToken.None));
        Assert.AreEqual(400, error.StatusCode);
        Assert.AreEqual(0, store.Replacements);
    }

    /// <summary>Admin cannot remove their own role, including with an empty set.</summary>
    [TestMethod]
    public async Task OwnAdminRoleCannotBeRemoved()
    {
        var store = new UserRoleStoreFake();
        var service = new UserRoleService(store);
        var request = new UpdateUserRolesRequest { Roles = new List<string>() };
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ReplaceAsync(Admin(), "admin", request, CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
        Assert.AreEqual("admin.self_demotion", error.Code);
        Assert.AreEqual(0, store.Replacements);
        CollectionAssert.AreEqual(new List<string> { RoleNames.Admin }, store.Accounts["admin"]);
    }

    /// <summary>Another account may intentionally be left with no roles.</summary>
    [TestMethod]
    public async Task EmptySetRemovesAllRolesFromAnotherAccount()
    {
        var store = new UserRoleStoreFake();
        var service = new UserRoleService(store);
        await service.ReplaceAsync(Admin(), "target", new UpdateUserRolesRequest(), CancellationToken.None);
        Assert.IsEmpty(store.Accounts["target"]);
        Assert.AreEqual(1, store.Replacements);
    }

    /// <summary>Replacement discards old roles and de-duplicates the requested set.</summary>
    [TestMethod]
    public async Task ReplacementUsesTheCompleteKnownSet()
    {
        var store = new UserRoleStoreFake();
        var service = new UserRoleService(store);
        var actor = new CurrentActor("admin", new List<string> { RoleNames.Admin, RoleNames.Employee });
        var request = new UpdateUserRolesRequest
        {
            Roles = new List<string> { RoleNames.Finance, RoleNames.Approver, RoleNames.Finance },
        };
        await service.ReplaceAsync(actor, "target", request, CancellationToken.None);
        CollectionAssert.AreEqual(new List<string> { RoleNames.Approver, RoleNames.Finance }, store.Accounts["target"]);
        Assert.AreEqual(1, store.Replacements);
    }

    /// <summary>Unknown accounts do not create new role assignments.</summary>
    [TestMethod]
    public async Task MissingAccountReturnsNotFound()
    {
        var store = new UserRoleStoreFake();
        var service = new UserRoleService(store);
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ReplaceAsync(Admin(), "absent", new UpdateUserRolesRequest(), CancellationToken.None));
        Assert.AreEqual(404, error.StatusCode);
        Assert.AreEqual(0, store.Replacements);
    }

    /// <summary>Changes to the input role list cannot escalate an existing actor snapshot.</summary>
    [TestMethod]
    public async Task ActorRoleSnapshotCannotBeEscalatedByInputMutation()
    {
        var roles = new List<string> { RoleNames.Employee };
        var actor = new CurrentActor("employee", roles);
        roles.Add(RoleNames.Admin);
        var service = new UserRoleService(new UserRoleStoreFake());
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ListAsync(actor, CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
    }

    /// <summary>An unauthenticated principal is rejected even if it contains an identifier claim.</summary>
    [TestMethod]
    public void AnonymousPrincipalCannotProduceAnActor()
    {
        var identity = new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "admin") });
        ApiProblemException error = Assert.ThrowsExactly<ApiProblemException>(
            () => CurrentActor.FromPrincipal(new ClaimsPrincipal(identity)));
        Assert.AreEqual(401, error.StatusCode);
    }

    private static CurrentActor Admin() => new CurrentActor("admin", new List<string> { RoleNames.Admin });
}
