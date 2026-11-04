using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Controllers;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>Checks that the HTTP adapter maps every route to the right status and derives the actor from the principal.</summary>
[TestClass]
public sealed class ExpensesControllerTests
{
    private static readonly string[] _paidTimeline = { "Created", "Updated", "Submitted", "Approved", "Paid" };
    private readonly ExpenseStoreFake _store = new();

    /// <summary>Creation answers 201 with a Location pointing to the new resource and the token owner.</summary>
    [TestMethod]
    public async Task CreateReturnsCreatedWithLocation()
    {
        ExpensesController controller = Controller("owner", RoleNames.Employee);
        IActionResult result = await controller.Create(Draft(), CancellationToken.None);
        var created = (CreatedResult)result;
        var body = (ExpenseResponse)created.Value!;
        Assert.AreEqual($"/api/expenses/{body.Id}", created.Location);
        Assert.AreEqual("owner", body.OwnerId);
        Assert.AreEqual("Draft", body.Status);
    }

    /// <summary>The whole lifecycle answers 200 through the adapter, each step under the right actor.</summary>
    [TestMethod]
    public async Task LifecycleRoutesReturnOkViews()
    {
        Guid paid = await CreateAsync();
        Assert.AreEqual(2L, Body<ExpenseResponse>(await Controller("owner", RoleNames.Employee)
            .Update(paid, Draft("Updated client dinner"), CancellationToken.None)).Revision);
        Assert.AreEqual("Submitted", Body<ExpenseResponse>(await Controller("owner", RoleNames.Employee)
            .Submit(paid, CancellationToken.None)).Status);
        Assert.AreEqual("Approved", Body<ExpenseResponse>(await Controller("approver", RoleNames.Approver)
            .Approve(paid, CancellationToken.None)).Status);
        Assert.AreEqual("Paid", Body<ExpenseResponse>(await Controller("finance", RoleNames.Finance)
            .Pay(paid, CancellationToken.None)).Status);

        Guid rejected = await CreateAsync();
        await Controller("owner", RoleNames.Employee).Submit(rejected, CancellationToken.None);
        ExpenseResponse rejectedView = Body<ExpenseResponse>(await Controller("approver", RoleNames.Approver)
            .Reject(rejected, new RejectExpenseRequest { Reason = "Receipt is missing" }, CancellationToken.None));
        Assert.AreEqual("Rejected", rejectedView.Status);

        ExpensesController auditor = Controller("auditor", RoleNames.Auditor);
        Assert.HasCount(2, Body<IReadOnlyList<ExpenseResponse>>(await auditor.List(CancellationToken.None)));
        Assert.AreEqual(paid, Body<ExpenseResponse>(await auditor.Find(paid, CancellationToken.None)).Id);
        IReadOnlyList<ExpenseHistoryResponse> history = Body<IReadOnlyList<ExpenseHistoryResponse>>(
            await auditor.History(paid, CancellationToken.None));
        CollectionAssert.AreEqual(_paidTimeline, history.Select(item => item.Action).ToArray());
    }

    private static T Body<T>(IActionResult result) => (T)((OkObjectResult)result).Value!;

    private static ExpenseDraftRequest Draft(string description = "Client dinner expense") => new()
    {
        Description = description,
        Amount = 42.50m,
        ExpenseDate = new DateOnly(2026, 10, 1),
    };

    private ExpensesController Controller(string userId, params string[] roles)
        => new(new ExpenseService(_store, new FixedTimeProvider())) { ControllerContext = Principals.Context(userId, roles) };

    private async Task<Guid> CreateAsync()
    {
        IActionResult result = await Controller("owner", RoleNames.Employee).Create(Draft(), CancellationToken.None);
        return ((ExpenseResponse)((CreatedResult)result).Value!).Id;
    }
}
