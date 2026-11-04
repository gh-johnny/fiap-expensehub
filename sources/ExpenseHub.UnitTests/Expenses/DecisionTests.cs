using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

/// <summary>Checks decisions, justifications and prohibition of self-action without EF or HTTP.</summary>
[TestClass]
public sealed class DecisionTests
{
    /// <summary>Approval returns its result while later read visibility disappears and repeat remains 409.</summary>
    [TestMethod]
    public async Task ApprovalDoesNotReuseReadVisibility()
    {
        (ExpenseStoreFake store, Expense expense) = Arrange(ExpenseStatus.Submitted);
        var service = new ExpenseService(store, new FixedTimeProvider());
        CurrentActor approver = Actor("approver", RoleNames.Approver);
        ExpenseResponse approved = await service.ApproveAsync(approver, expense.Id, CancellationToken.None);
        Assert.AreEqual("Approved", approved.Status);
        Assert.AreEqual(2L, approved.Revision);
        Assert.AreEqual("approver", store.History[0].ActorId);
        Assert.AreEqual(ExpenseStatus.Submitted, store.History[0].PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Approved, store.History[0].NewStatus);
        Assert.IsNull(store.History[0].Reason);
        ApiProblemException invisible = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.FindAsync(approver, expense.Id, CancellationToken.None));
        ApiProblemException repeated = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ApproveAsync(approver, expense.Id, CancellationToken.None));
        Assert.AreEqual(404, invisible.StatusCode);
        Assert.AreEqual(409, repeated.StatusCode);
        Assert.HasCount(1, store.History);
        Assert.AreEqual(1, store.Saves);
    }

    /// <summary>A rejected expense is final and retains its trimmed justification.</summary>
    [TestMethod]
    public async Task RejectionRecordsReasonAndRemainsTerminal()
    {
        (ExpenseStoreFake store, Expense expense) = Arrange(ExpenseStatus.Submitted);
        var service = new ExpenseService(store, new FixedTimeProvider());
        CurrentActor approver = Actor("approver", RoleNames.Approver);
        var request = new RejectExpenseRequest { Reason = "  Receipt is missing  " };
        ExpenseResponse rejected = await service.RejectAsync(approver, expense.Id, request, CancellationToken.None);
        Assert.AreEqual("Rejected", rejected.Status);
        Assert.AreEqual("Receipt is missing", store.History[0].Reason);
        ApiProblemException repeated = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.RejectAsync(approver, expense.Id, request, CancellationToken.None));
        ApiProblemException resubmit = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.SubmitAsync(Actor("owner", RoleNames.Employee), expense.Id, CancellationToken.None));
        Assert.AreEqual(409, repeated.StatusCode);
        Assert.AreEqual(409, resubmit.StatusCode);
        Assert.HasCount(1, store.History);
    }

    /// <summary>Rejection validates trimmed boundaries before staging any change.</summary>
    /// <param name="length">The justification length after trimming.</param>
    /// <param name="accepted">Whether the boundary is valid.</param>
    [TestMethod]
    [DataRow(9, false)]
    [DataRow(10, true)]
    [DataRow(500, true)]
    [DataRow(501, false)]
    public async Task RejectionReasonBoundaries(int length, bool accepted)
    {
        (ExpenseStoreFake store, Expense expense) = Arrange(ExpenseStatus.Submitted);
        var service = new ExpenseService(store, new FixedTimeProvider());
        CurrentActor approver = Actor("approver", RoleNames.Approver);
        var request = new RejectExpenseRequest { Reason = "  " + new string('x', length) + "  " };
        if (accepted)
        {
            await service.RejectAsync(approver, expense.Id, request, CancellationToken.None);
            Assert.AreEqual(new string('x', length), store.History[0].Reason);
        }
        else
        {
            ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                () => service.RejectAsync(approver, expense.Id, request, CancellationToken.None));
            Assert.AreEqual(400, error.StatusCode);
            Assert.AreEqual(ExpenseStatus.Submitted, expense.Status);
            Assert.IsEmpty(store.History);
        }
    }

    /// <summary>Self-decision is forbidden in every state even with all roles.</summary>
    /// <param name="state">The current lifecycle state.</param>
    [TestMethod]
    [DataRow(ExpenseStatus.Draft)]
    [DataRow(ExpenseStatus.Submitted)]
    [DataRow(ExpenseStatus.Approved)]
    [DataRow(ExpenseStatus.Rejected)]
    [DataRow(ExpenseStatus.Paid)]
    public async Task OwnerCannotDecideEvenWithAllRoles(ExpenseStatus state)
    {
        (ExpenseStoreFake store, Expense expense) = Arrange(state);
        var service = new ExpenseService(store, new FixedTimeProvider());
        var owner = new CurrentActor("owner", RoleNames.All);
        ApiProblemException approve = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ApproveAsync(owner, expense.Id, CancellationToken.None));
        ApiProblemException reject = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.RejectAsync(owner, expense.Id, new RejectExpenseRequest { Reason = "Receipt is missing" }, CancellationToken.None));
        Assert.AreEqual(403, approve.StatusCode);
        Assert.AreEqual(403, reject.StatusCode);
        Assert.AreEqual(state, expense.Status);
        Assert.AreEqual(1L, expense.Revision);
        Assert.IsEmpty(store.History);
        Assert.AreEqual(0, store.Saves);
    }

    /// <summary>Every non-Submitted state returns conflict for decisions by a foreign Approver.</summary>
    /// <param name="state">An ineligible state.</param>
    [TestMethod]
    [DataRow(ExpenseStatus.Draft)]
    [DataRow(ExpenseStatus.Approved)]
    [DataRow(ExpenseStatus.Rejected)]
    [DataRow(ExpenseStatus.Paid)]
    public async Task WrongDecisionStateHasNoSideEffects(ExpenseStatus state)
    {
        (ExpenseStoreFake store, Expense expense) = Arrange(state);
        var service = new ExpenseService(store, new FixedTimeProvider());
        CurrentActor actor = Actor("approver", RoleNames.Approver);
        ApiProblemException approve = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ApproveAsync(actor, expense.Id, CancellationToken.None));
        ApiProblemException reject = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.RejectAsync(actor, expense.Id, new RejectExpenseRequest { Reason = "Receipt is missing" }, CancellationToken.None));
        Assert.AreEqual(409, approve.StatusCode);
        Assert.AreEqual(409, reject.StatusCode);
        Assert.IsEmpty(store.History);
    }

    /// <summary>The action role is required before revealing whether the identifier exists.</summary>
    /// <param name="role">A role without decision privileges.</param>
    [TestMethod]
    [DataRow(RoleNames.Employee)]
    [DataRow(RoleNames.Finance)]
    [DataRow(RoleNames.Auditor)]
    [DataRow(RoleNames.Admin)]
    public async Task OtherRolesCannotDecideMissingResource(string role)
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ApproveAsync(Actor("actor", role), Guid.NewGuid(), CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
        Assert.AreEqual(0, store.Saves);
    }

    private static (ExpenseStoreFake Store, Expense Expense) Arrange(ExpenseStatus state)
    {
        var store = new ExpenseStoreFake();
        var expense = new Expense { OwnerId = "owner", Status = state, Amount = 25.50m, Description = "Meal expense receipt", ExpenseDate = new DateOnly(2026, 10, 3) };
        store.AddExpense(expense);
        return (store, expense);
    }

    private static CurrentActor Actor(string id, string role) => new(id, new List<string> { role });
}
