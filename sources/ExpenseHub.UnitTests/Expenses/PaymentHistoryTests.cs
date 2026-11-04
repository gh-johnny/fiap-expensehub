using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>Checks payment guards, server fields and the shared history read scope without a database.</summary>
[TestClass]
public sealed class PaymentHistoryTests
{
    /// <summary>Payment derives its amount, actor and time and cannot be repeated.</summary>
    [TestMethod]
    public async Task PaymentIsDerivedAndRecordedOnce()
    {
        var store = new ExpenseStoreFake();
        var expense = new Expense { OwnerId = "owner", Status = ExpenseStatus.Approved, Amount = 2147483647m };
        store.AddExpense(expense);
        var service = new ExpenseService(store, new FixedTimeProvider());
        var finance = new CurrentActor("finance", new List<string> { RoleNames.Finance });
        ExpenseResponse paid = await service.PayAsync(finance, expense.Id, CancellationToken.None);
        Assert.AreEqual("Paid", paid.Status);
        PaymentRecord payment = store.Payments.Single();
        Assert.AreEqual(expense.Id, payment.ExpenseId);
        Assert.AreEqual(expense.Amount, payment.Amount);
        Assert.AreEqual("finance", payment.ActorId);
        Assert.AreEqual(new FixedTimeProvider().GetUtcNow().UtcDateTime, payment.PaidAtUtc);
        Assert.AreEqual(ExpenseStatus.Approved, store.History[0].PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Paid, store.History[0].NewStatus);
        ApiProblemException repeated = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.PayAsync(finance, expense.Id, CancellationToken.None));
        Assert.AreEqual(409, repeated.StatusCode);
        Assert.HasCount(1, store.Payments);
        Assert.HasCount(1, store.History);
        Assert.AreEqual(1, store.Saves);
    }

    /// <summary>The owner cannot pay in any state, even with every role.</summary>
    /// <param name="state">The lifecycle state.</param>
    [TestMethod]
    [DataRow(ExpenseStatus.Draft)]
    [DataRow(ExpenseStatus.Submitted)]
    [DataRow(ExpenseStatus.Approved)]
    [DataRow(ExpenseStatus.Rejected)]
    [DataRow(ExpenseStatus.Paid)]
    public async Task OwnerCannotPayWithAccumulatedRoles(ExpenseStatus state)
    {
        var store = new ExpenseStoreFake();
        var expense = new Expense { OwnerId = "owner", Status = state };
        store.AddExpense(expense);
        var service = new ExpenseService(store, new FixedTimeProvider());
        var actor = new CurrentActor("owner", RoleNames.All);
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.PayAsync(actor, expense.Id, CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
        Assert.AreEqual(state, expense.Status);
        Assert.IsEmpty(store.Payments);
        Assert.IsEmpty(store.History);
    }

    /// <summary>Finance may pay only Approved, without events for invalid states.</summary>
    /// <param name="state">An ineligible state.</param>
    [TestMethod]
    [DataRow(ExpenseStatus.Draft)]
    [DataRow(ExpenseStatus.Submitted)]
    [DataRow(ExpenseStatus.Rejected)]
    [DataRow(ExpenseStatus.Paid)]
    public async Task FinanceCannotPayWrongState(ExpenseStatus state)
    {
        var store = new ExpenseStoreFake();
        var expense = new Expense { OwnerId = "owner", Status = state };
        store.AddExpense(expense);
        var service = new ExpenseService(store, new FixedTimeProvider());
        var actor = new CurrentActor("finance", new List<string> { RoleNames.Finance });
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.PayAsync(actor, expense.Id, CancellationToken.None));
        Assert.AreEqual(409, error.StatusCode);
        Assert.IsEmpty(store.Payments);
        Assert.IsEmpty(store.History);
    }

    /// <summary>Roles without Finance cannot pay even a missing identifier.</summary>
    /// <param name="role">A role without payment rights.</param>
    [TestMethod]
    [DataRow(RoleNames.Employee)]
    [DataRow(RoleNames.Approver)]
    [DataRow(RoleNames.Auditor)]
    [DataRow(RoleNames.Admin)]
    public async Task PaymentRequiresFinance(string role)
    {
        var service = new ExpenseService(new ExpenseStoreFake(), new FixedTimeProvider());
        var actor = new CurrentActor("actor", new List<string> { role });
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.PayAsync(actor, Guid.NewGuid(), CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
    }

    /// <summary>History is ordered by revision when timestamps coincide and storage order differs.</summary>
    [TestMethod]
    public async Task FullLifecycleHistoryUsesRevisionAndVisibility()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        var owner = new CurrentActor("owner", new List<string> { RoleNames.Employee });
        var approver = new CurrentActor("approver", new List<string> { RoleNames.Approver });
        var finance = new CurrentActor("finance", new List<string> { RoleNames.Finance });
        var request = new ExpenseDraftRequest { Description = "Client train expense", Amount = 15.25m, ExpenseDate = new DateOnly(2026, 10, 3) };
        ExpenseResponse created = await service.CreateAsync(owner, request, CancellationToken.None);
        request.Amount = 20.75m;
        await service.UpdateAsync(owner, created.Id, request, CancellationToken.None);
        await service.SubmitAsync(owner, created.Id, CancellationToken.None);
        await service.ApproveAsync(approver, created.Id, CancellationToken.None);
        await service.PayAsync(finance, created.Id, CancellationToken.None);
        store.History.Reverse();
        IReadOnlyList<ExpenseHistoryResponse> history = await service.HistoryAsync(owner, created.Id, CancellationToken.None);
        CollectionAssert.AreEqual(new List<long> { 1, 2, 3, 4, 5 }, history.Select(item => item.Revision).ToList());
        CollectionAssert.AreEqual(new List<string> { "Created", "Updated", "Submitted", "Approved", "Paid" }, history.Select(item => item.Action).ToList());
        Assert.IsTrue(history.All(item => item.OccurredAtUtc == history[0].OccurredAtUtc));
        Assert.HasCount(5, await service.HistoryAsync(finance, created.Id, CancellationToken.None));
        ApiProblemException invisible = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.HistoryAsync(approver, created.Id, CancellationToken.None));
        var other = new CurrentActor("other", new List<string> { RoleNames.Employee });
        ApiProblemException foreign = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.HistoryAsync(other, created.Id, CancellationToken.None));
        Assert.AreEqual(404, invisible.StatusCode);
        Assert.AreEqual(404, foreign.StatusCode);
    }
}
