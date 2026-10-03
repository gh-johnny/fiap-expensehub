using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>Checks draft rules independently of relational persistence or network.</summary>
[TestClass]
public sealed class DraftTests
{
    /// <summary>Creation derives identity, state and time and commits the first event together.</summary>
    [TestMethod]
    public async Task CreateDerivesServerFieldsAndHistory()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseDraftRequest request = Valid();
        request.Description = "  Team lunch expense  ";
        ExpenseResponse result = await service.CreateAsync(Employee("owner"), request, CancellationToken.None);
        Assert.AreEqual("owner", result.OwnerId);
        Assert.AreEqual("Draft", result.Status);
        Assert.AreEqual("Team lunch expense", result.Description);
        Assert.AreEqual(ExpenseCategory.GeneralId, result.CategoryId);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(new FixedTimeProvider().GetUtcNow().UtcDateTime, result.CreatedAtUtc);
        ExpenseHistory history = store.History.Single();
        Assert.AreEqual(result.Id, history.ExpenseId);
        Assert.AreEqual("Created", history.Action);
        Assert.IsNull(history.PreviousStatus);
        Assert.AreEqual("owner", history.ActorId);
        Assert.AreEqual(DateTimeKind.Utc, history.OccurredAtUtc.Kind);
        Assert.AreEqual(1, store.Saves);
    }

    /// <summary>The exact monetary boundaries are accepted without double conversion.</summary>
    /// <param name="amount">A valid decimal representation.</param>
    [TestMethod]
    [DataRow("0.01")]
    [DataRow("2147483647")]
    public async Task ValidAmountBoundaries(string amount)
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseDraftRequest request = Valid();
        request.Amount = decimal.Parse(amount, CultureInfo.InvariantCulture);
        ExpenseResponse response = await service.CreateAsync(Employee("owner"), request, CancellationToken.None);
        Assert.AreEqual(request.Amount, response.Amount);
    }

    /// <summary>Invalid amounts cause no persistence or history.</summary>
    /// <param name="amount">An invalid decimal representation.</param>
    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("2147483647.01")]
    public async Task InvalidAmountHasNoSideEffects(string amount)
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseDraftRequest request = Valid();
        request.Amount = decimal.Parse(amount, CultureInfo.InvariantCulture);
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.CreateAsync(Employee("owner"), request, CancellationToken.None));
        Assert.AreEqual(400, error.StatusCode);
        Assert.IsEmpty(store.Expenses);
        Assert.IsEmpty(store.History);
        Assert.AreEqual(0, store.Saves);
    }

    /// <summary>Description limits apply after trimming.</summary>
    /// <param name="length">The trimmed character count.</param>
    /// <param name="accepted">Whether the boundary is valid.</param>
    [TestMethod]
    [DataRow(9, false)]
    [DataRow(10, true)]
    [DataRow(500, true)]
    [DataRow(501, false)]
    public async Task DescriptionBoundaries(int length, bool accepted)
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseDraftRequest request = Valid();
        request.Description = "  " + new string('x', length) + "  ";
        if (accepted)
        {
            ExpenseResponse response = await service.CreateAsync(Employee("owner"), request, CancellationToken.None);
            Assert.HasCount(length, response.Description);
        }
        else
        {
            ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                () => service.CreateAsync(Employee("owner"), request, CancellationToken.None));
            Assert.AreEqual(400, error.StatusCode);
            Assert.IsEmpty(store.History);
        }
    }

    /// <summary>Missing and future dates cannot create a draft.</summary>
    [TestMethod]
    public async Task MissingAndFutureDatesAreRejected()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        foreach (DateOnly? date in new List<DateOnly?> { null, new DateOnly(2026, 10, 4) })
        {
            ExpenseDraftRequest request = Valid();
            request.ExpenseDate = date;
            ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                () => service.CreateAsync(Employee("owner"), request, CancellationToken.None));
            Assert.AreEqual(400, error.StatusCode);
        }

        Assert.IsEmpty(store.Expenses);
    }

    /// <summary>Roles other than Employee cannot create even when accumulated.</summary>
    [TestMethod]
    public async Task NonEmployeeCannotCreate()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        var actor = new CurrentActor("actor", new List<string> { RoleNames.Admin, RoleNames.Auditor, RoleNames.Approver, RoleNames.Finance });
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.CreateAsync(actor, Valid(), CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
        Assert.IsEmpty(store.Expenses);
    }

    /// <summary>Only effective changes increment revision and record before/after values.</summary>
    [TestMethod]
    public async Task UpdateAuditsChangesButIdenticalInputIsANoOp()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseResponse created = await service.CreateAsync(Employee("owner"), Valid(), CancellationToken.None);
        ExpenseDraftRequest request = Valid();
        request.Amount = 120.25m;
        request.Description = "Updated lunch expense";
        request.ExpenseDate = new DateOnly(2026, 10, 2);
        ExpenseResponse updated = await service.UpdateAsync(Employee("owner"), created.Id, request, CancellationToken.None);
        ExpenseHistory history = store.History.Last();
        Assert.AreEqual(2L, updated.Revision);
        Assert.AreEqual(created.Amount, history.PreviousAmount);
        Assert.AreEqual(updated.Amount, history.NewAmount);
        Assert.AreEqual(created.Description, history.PreviousDescription);
        Assert.AreEqual(updated.Description, history.NewDescription);
        Assert.AreEqual(created.ExpenseDate, history.PreviousExpenseDate);
        Assert.AreEqual(updated.ExpenseDate, history.NewExpenseDate);
        ExpenseResponse repeated = await service.UpdateAsync(Employee("owner"), created.Id, request, CancellationToken.None);
        Assert.AreEqual(2L, repeated.Revision);
        Assert.HasCount(2, store.History);
        Assert.AreEqual(2, store.Saves);
    }

    /// <summary>Ownership is checked before edits, with no history changes.</summary>
    [TestMethod]
    public async Task AnotherEmployeeCannotEdit()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseResponse created = await service.CreateAsync(Employee("owner"), Valid(), CancellationToken.None);
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.UpdateAsync(Employee("other"), created.Id, Valid(), CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
        Assert.HasCount(1, store.History);
        Assert.AreEqual(1, store.Saves);
    }

    /// <summary>Every state outside Draft rejects edits as a conflict.</summary>
    /// <param name="state">An ineligible state.</param>
    [TestMethod]
    [DataRow(ExpenseStatus.Submitted)]
    [DataRow(ExpenseStatus.Approved)]
    [DataRow(ExpenseStatus.Rejected)]
    [DataRow(ExpenseStatus.Paid)]
    public async Task NonDraftCannotBeEdited(ExpenseStatus state)
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseResponse created = await service.CreateAsync(Employee("owner"), Valid(), CancellationToken.None);
        store.Expenses[created.Id].Status = state;
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.UpdateAsync(Employee("owner"), created.Id, Valid(), CancellationToken.None));
        Assert.AreEqual(409, error.StatusCode);
        Assert.HasCount(1, store.History);
    }

    /// <summary>An absent resource is reported without staging any event.</summary>
    [TestMethod]
    public async Task MissingExpenseReturnsNotFound()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.UpdateAsync(Employee("owner"), Guid.NewGuid(), Valid(), CancellationToken.None));
        Assert.AreEqual(404, error.StatusCode);
        Assert.IsEmpty(store.History);
    }

    private static CurrentActor Employee(string id) => new(id, new List<string> { RoleNames.Employee });

    private static ExpenseDraftRequest Valid() => new()
    {
        Description = "Team lunch expense",
        Amount = 100.01m,
        ExpenseDate = new DateOnly(2026, 10, 3),
    };
}
