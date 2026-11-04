using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Controllers;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>Checks the administration adapter statuses and that the actor comes from the principal.</summary>
[TestClass]
public sealed class AdminUsersControllerTests
{
    /// <summary>An Admin lists accounts without credential fields.</summary>
    [TestMethod]
    public async Task ListReturnsAccounts()
    {
        var store = new UserRoleStoreFake();
        AdminUsersController controller = Controller(store, "admin", RoleNames.Admin);
        var users = (IReadOnlyList<UserResponse>)((OkObjectResult)await controller.List(CancellationToken.None)).Value!;
        Assert.HasCount(2, users);
        Assert.AreEqual("admin@example.test", users[0].Email);
    }

    /// <summary>A valid replacement answers 204 and persists once.</summary>
    [TestMethod]
    public async Task ReplaceReturnsNoContent()
    {
        var store = new UserRoleStoreFake();
        AdminUsersController controller = Controller(store, "admin", RoleNames.Admin);
        var request = new UpdateUserRolesRequest { Roles = new List<string> { RoleNames.Auditor } };
        IActionResult result = await controller.Replace("target", request, CancellationToken.None);
        Assert.IsInstanceOfType<NoContentResult>(result);
        CollectionAssert.AreEqual(new[] { RoleNames.Auditor }, store.Accounts["target"]);
    }

    /// <summary>A non-Admin principal is rejected by the service even if routing were bypassed.</summary>
    [TestMethod]
    public async Task NonAdminCannotList()
    {
        AdminUsersController controller = Controller(new UserRoleStoreFake(), "employee", RoleNames.Employee);
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(() => controller.List(CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
    }

    private static AdminUsersController Controller(UserRoleStoreFake store, string userId, params string[] roles)
        => new(new UserRoleService(store)) { ControllerContext = Principals.Context(userId, roles) };
}
