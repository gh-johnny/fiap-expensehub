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

/// <summary>Checks submission and read isolation without persistence infrastructure.</summary>
[TestClass]
public sealed class SubmissionTests
{
    /// <summary>Submission adds exactly one event and cannot be repeated.</summary>
    [TestMethod]
    public async Task SubmitRecordsTransitionOnce()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        CurrentActor owner = Actor("owner", RoleNames.Employee);
        ExpenseResponse draft = await service.CreateAsync(owner, Valid(), CancellationToken.None);
        ExpenseResponse submitted = await service.SubmitAsync(owner, draft.Id, CancellationToken.None);
        Assert.AreEqual("Submitted", submitted.Status);
        Assert.AreEqual(2L, submitted.Revision);
        ExpenseHistory history = store.History.Last();
        Assert.AreEqual(ExpenseStatus.Draft, history.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Submitted, history.NewStatus);
        Assert.AreEqual("owner", history.ActorId);
        ApiProblemException repeated = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.SubmitAsync(owner, draft.Id, CancellationToken.None));
        Assert.AreEqual(409, repeated.StatusCode);
        Assert.HasCount(2, store.History);
        Assert.AreEqual(2, store.Saves);
    }

    /// <summary>Another Employee cannot submit a known identifier.</summary>
    [TestMethod]
    public async Task ForeignEmployeeCannotSubmit()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        ExpenseResponse draft = await service.CreateAsync(Actor("owner", RoleNames.Employee), Valid(), CancellationToken.None);
        ApiProblemException denied = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.SubmitAsync(Actor("other", RoleNames.Employee), draft.Id, CancellationToken.None));
        Assert.AreEqual(403, denied.StatusCode);
        Assert.AreEqual(ExpenseStatus.Draft, store.Expenses[draft.Id].Status);
        Assert.HasCount(1, store.History);
    }

    /// <summary>Invisible and missing resources share 404 while lists contain only owned records.</summary>
    [TestMethod]
    public async Task EmployeeReadsOnlyOwnExpenses()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        CurrentActor owner = Actor("owner", RoleNames.Employee);
        ExpenseResponse owned = await service.CreateAsync(owner, Valid(), CancellationToken.None);
        ExpenseResponse foreign = await service.CreateAsync(Actor("other", RoleNames.Employee), Valid(), CancellationToken.None);
        IReadOnlyList<ExpenseResponse> listed = await service.ListAsync(owner, CancellationToken.None);
        Assert.HasCount(1, listed);
        Assert.AreEqual(owned.Id, listed[0].Id);
        foreach (Guid id in new List<Guid> { foreign.Id, Guid.NewGuid() })
        {
            ApiProblemException absent = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                () => service.FindAsync(owner, id, CancellationToken.None));
            Assert.AreEqual(404, absent.StatusCode);
            Assert.AreEqual("expense.not_found", absent.Code);
        }
    }

    /// <summary>Approver visibility begins with submission; Finance does not see that queue.</summary>
    [TestMethod]
    public async Task SubmissionChangesApproverVisibility()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        CurrentActor owner = Actor("owner", RoleNames.Employee);
        CurrentActor approver = Actor("approver", RoleNames.Approver);
        ExpenseResponse draft = await service.CreateAsync(owner, Valid(), CancellationToken.None);
        Assert.IsEmpty(await service.ListAsync(approver, CancellationToken.None));
        await service.SubmitAsync(owner, draft.Id, CancellationToken.None);
        Assert.HasCount(1, await service.ListAsync(approver, CancellationToken.None));
        Assert.IsEmpty(await service.ListAsync(Actor("finance", RoleNames.Finance), CancellationToken.None));
    }

    /// <summary>Administration and missing roles grant no expense read access.</summary>
    /// <param name="role">A role that grants no expense permission.</param>
    [TestMethod]
    [DataRow(RoleNames.Admin)]
    [DataRow("Unknown")]
    public async Task NonReaderCannotList(string role)
    {
        var service = new ExpenseService(new ExpenseStoreFake(), new FixedTimeProvider());
        ApiProblemException denied = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.ListAsync(Actor("actor", role), CancellationToken.None));
        Assert.AreEqual(403, denied.StatusCode);
    }

    private static CurrentActor Actor(string id, string role) => new(id, new List<string> { role });

    private static ExpenseDraftRequest Valid() => new()
    {
        Description = "Train ticket expense",
        Amount = 15.25m,
        ExpenseDate = new DateOnly(2026, 10, 3),
    };
}
