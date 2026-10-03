using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Persistence;
using ExpenseHub.Api.Services;
using ExpenseHub.IntegrationTests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.IntegrationTests;

/// <summary>Verifies real SQLite transactions, constraints, concurrency and query projections.</summary>
[TestClass]
public sealed class RelationalExpenseTests
{
    /// <summary>Money, DateOnly and UTC survive materialization without tracking on read paths.</summary>
    [TestMethod]
    public async Task RoundTripAndReadQueriesUseRealSqlite()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        Guid owned = await fixture.CreateExpenseAsync();
        Guid foreign = await fixture.CreateExpenseAsync("other", submit: true);
        await using ExpenseHubDbContext context = fixture.Context();
        var store = new EfExpenseStore(context);
        var service = new ExpenseService(store, TimeProvider.System);
        var actor = new CurrentActor("owner", new List<string> { RoleNames.Employee, RoleNames.Approver });
        IReadOnlyList<ExpenseResponse> result = await service.ListAsync(actor, CancellationToken.None);
        CollectionAssert.AreEquivalent(new List<Guid> { owned, foreign }, result.Select(expense => expense.Id).ToList());
        ExpenseResponse expense = await service.FindAsync(actor, owned, CancellationToken.None);
        Assert.AreEqual(2147483647m, expense.Amount);
        Assert.AreEqual(DateOnly.FromDateTime(DateTime.UtcNow), expense.ExpenseDate);
        Assert.AreEqual(DateTimeKind.Utc, expense.CreatedAtUtc.Kind);
        IReadOnlyList<ExpenseHistoryResponse> history = await service.HistoryAsync(actor, foreign, CancellationToken.None);
        CollectionAssert.AreEqual(new List<long> { 1, 2 }, history.Select(item => item.Revision).ToList());
        Assert.IsTrue(history.All(item => item.OccurredAtUtc.Kind == DateTimeKind.Utc));
        Assert.IsEmpty(context.ChangeTracker.Entries());
        var employee = new CurrentActor("owner", new List<string> { RoleNames.Employee });
        ApiProblemException invisible = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.HistoryAsync(employee, foreign, CancellationToken.None));
        Assert.AreEqual(404, invisible.StatusCode);
    }

    /// <summary>A failed audit insert rolls back the new expense in the same SaveChanges transaction.</summary>
    [TestMethod]
    public async Task HistoryFailureRollsBackCreation()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        await using (ExpenseHubDbContext context = fixture.Context())
        {
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_history BEFORE INSERT ON ExpenseHistories BEGIN SELECT RAISE(ABORT, 'fixture history failure'); END");
            var service = new ExpenseService(new EfExpenseStore(context), TimeProvider.System);
            var request = new ExpenseDraftRequest { Description = "Receipt for failed creation", Amount = 25.50m, ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow) };
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => service.CreateAsync(Actor("owner", RoleNames.Employee), request, CancellationToken.None));
            Assert.IsEmpty(context.ChangeTracker.Entries());
        }

        await using ExpenseHubDbContext proof = fixture.Context();
        Assert.AreEqual(0, await proof.Expenses.CountAsync());
        Assert.AreEqual(0, await proof.ExpenseHistories.CountAsync());
    }

    /// <summary>A payment insert failure leaves Approved, its revision and previous events intact.</summary>
    [TestMethod]
    public async Task PaymentFailureRollsBackStateHistoryAndPayment()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        Guid id = await fixture.CreateExpenseAsync(submit: true, approve: true);
        await using (ExpenseHubDbContext context = fixture.Context())
        {
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_payment BEFORE INSERT ON PaymentRecords BEGIN SELECT RAISE(ABORT, 'fixture payment failure'); END");
            var service = new ExpenseService(new EfExpenseStore(context), TimeProvider.System);
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => service.PayAsync(Actor("finance", RoleNames.Finance), id, CancellationToken.None));
        }

        await using ExpenseHubDbContext proof = fixture.Context();
        Expense expense = await proof.Expenses.SingleAsync(item => item.Id == id);
        Assert.AreEqual(ExpenseStatus.Approved, expense.Status);
        Assert.AreEqual(3L, expense.Revision);
        Assert.AreEqual(3, await proof.ExpenseHistories.CountAsync(item => item.ExpenseId == id));
        Assert.AreEqual(0, await proof.PaymentRecords.CountAsync());
    }

    /// <summary>A stale decision cannot overwrite the winner or add another event for its revision.</summary>
    [TestMethod]
    public async Task TwoContextsRejectAStaleDecision()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        Guid id = await fixture.CreateExpenseAsync(submit: true);
        await using ExpenseHubDbContext first = fixture.Context();
        await using ExpenseHubDbContext second = fixture.Context();
        await first.Expenses.SingleAsync(expense => expense.Id == id);
        await second.Expenses.SingleAsync(expense => expense.Id == id);
        var winner = new ExpenseService(new EfExpenseStore(first), TimeProvider.System);
        var stale = new ExpenseService(new EfExpenseStore(second), TimeProvider.System);
        CurrentActor actor = Actor("approver", RoleNames.Approver);
        await winner.ApproveAsync(actor, id, CancellationToken.None);
        ApiProblemException conflict = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => stale.RejectAsync(actor, id, new RejectExpenseRequest { Reason = "Receipt is missing" }, CancellationToken.None));
        Assert.AreEqual(409, conflict.StatusCode);
        Assert.AreEqual("expense.concurrent_update", conflict.Code);
        Assert.IsEmpty(second.ChangeTracker.Entries());
        await using ExpenseHubDbContext proof = fixture.Context();
        Expense persisted = await proof.Expenses.SingleAsync(expense => expense.Id == id);
        Assert.AreEqual(ExpenseStatus.Approved, persisted.Status);
        Assert.AreEqual(3L, persisted.Revision);
        Assert.AreEqual(3, await proof.ExpenseHistories.CountAsync(history => history.ExpenseId == id));
        Assert.AreEqual(0, await proof.ExpenseHistories.CountAsync(history => history.Action == "Rejected"));
    }

    /// <summary>The unique payment constraint also protects writes made directly through the adapter.</summary>
    [TestMethod]
    public async Task DuplicatePaymentIsAConflictWithoutAnotherRecord()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        Guid id = await fixture.CreateExpenseAsync(submit: true, approve: true);
        await using (ExpenseHubDbContext context = fixture.Context())
        {
            var service = new ExpenseService(new EfExpenseStore(context), TimeProvider.System);
            await service.PayAsync(Actor("finance", RoleNames.Finance), id, CancellationToken.None);
        }

        await using (ExpenseHubDbContext context = fixture.Context())
        {
            var store = new EfExpenseStore(context);
            store.AddPayment(new PaymentRecord { ExpenseId = id, ActorId = "finance", Amount = 1m, PaidAtUtc = DateTime.UtcNow });
            ApiProblemException conflict = await Assert.ThrowsExactlyAsync<ApiProblemException>(() => store.SaveAsync(CancellationToken.None));
            Assert.AreEqual(409, conflict.StatusCode);
        }

        await using ExpenseHubDbContext proof = fixture.Context();
        Assert.AreEqual(1, await proof.PaymentRecords.CountAsync(payment => payment.ExpenseId == id));
        Assert.AreEqual(4, await proof.ExpenseHistories.CountAsync(history => history.ExpenseId == id));
        Assert.AreEqual(2147483647m, (await proof.PaymentRecords.SingleAsync()).Amount);
    }

    /// <summary>An unrelated FK violation remains a provider failure instead of becoming a false 409.</summary>
    [TestMethod]
    public async Task ForeignKeyFailuresAreNotTranslatedAsConcurrency()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await using (ExpenseHubDbContext context = fixture.Context())
        {
            var store = new EfExpenseStore(context);
            store.AddExpense(new Expense { OwnerId = "missing", Description = "Unknown account expense", Amount = 1m });
            DbUpdateException error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => store.SaveAsync(CancellationToken.None));
            Assert.IsInstanceOfType<SqliteException>(error.InnerException);
            Assert.AreEqual(787, ((SqliteException)error.InnerException!).SqliteExtendedErrorCode);
        }

        await using ExpenseHubDbContext proof = fixture.Context();
        Assert.AreEqual(0, await proof.Expenses.CountAsync());
    }

    private static CurrentActor Actor(string id, string role) => new(id, new List<string> { role });
}
