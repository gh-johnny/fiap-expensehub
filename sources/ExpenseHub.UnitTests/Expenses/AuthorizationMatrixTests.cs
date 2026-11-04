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

/// <summary>Exhausts role combinations and lifecycle states against an explicit expected matrix.</summary>
[TestClass]
public sealed class AuthorizationMatrixTests
{
    /// <summary>All 32 role combinations preserve union semantics for owned and foreign records.</summary>
    /// <param name="roles">A bit set: Employee, Approver, Finance, Auditor, Admin.</param>
    /// <param name="owned">Visible own states: Draft, Submitted, Approved, Rejected, Paid.</param>
    /// <param name="foreign">Visible foreign states using the same state bit set.</param>
    [TestMethod]
    [DataRow(0, 0, 0)]
    [DataRow(1, 31, 0)]
    [DataRow(2, 2, 2)]
    [DataRow(3, 31, 2)]
    [DataRow(4, 20, 20)]
    [DataRow(5, 31, 20)]
    [DataRow(6, 22, 22)]
    [DataRow(7, 31, 22)]
    [DataRow(8, 31, 31)]
    [DataRow(9, 31, 31)]
    [DataRow(10, 31, 31)]
    [DataRow(11, 31, 31)]
    [DataRow(12, 31, 31)]
    [DataRow(13, 31, 31)]
    [DataRow(14, 31, 31)]
    [DataRow(15, 31, 31)]
    [DataRow(16, 0, 0)]
    [DataRow(17, 31, 0)]
    [DataRow(18, 2, 2)]
    [DataRow(19, 31, 2)]
    [DataRow(20, 20, 20)]
    [DataRow(21, 31, 20)]
    [DataRow(22, 22, 22)]
    [DataRow(23, 31, 22)]
    [DataRow(24, 31, 31)]
    [DataRow(25, 31, 31)]
    [DataRow(26, 31, 31)]
    [DataRow(27, 31, 31)]
    [DataRow(28, 31, 31)]
    [DataRow(29, 31, 31)]
    [DataRow(30, 31, 31)]
    [DataRow(31, 31, 31)]
    public async Task EveryRoleCombinationHasExpectedVisibility(int roles, int owned, int foreign)
    {
        var names = new List<string> { RoleNames.Employee, RoleNames.Approver, RoleNames.Finance, RoleNames.Auditor, RoleNames.Admin };
        var granted = new List<string>();
        for (int index = 0; index < names.Count; index++)
        {
            if ((roles & (1 << index)) != 0)
            {
                granted.Add(names[index]);
            }
        }

        var actor = new CurrentActor("owner", granted);
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        var states = new List<ExpenseStatus> { ExpenseStatus.Draft, ExpenseStatus.Submitted, ExpenseStatus.Approved, ExpenseStatus.Rejected, ExpenseStatus.Paid };
        var expected = new List<Guid>();
        for (int index = 0; index < states.Count; index++)
        {
            foreach ((string owner, int mask) in new List<(string, int)> { ("owner", owned), ("other", foreign) })
            {
                var expense = new Expense { OwnerId = owner, Status = states[index] };
                store.AddExpense(expense);
                if ((mask & (1 << index)) != 0)
                {
                    expected.Add(expense.Id);
                }
            }
        }

        if (owned == 0 && foreign == 0)
        {
            ApiProblemException denied = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                () => service.ListAsync(actor, CancellationToken.None));
            Assert.AreEqual(403, denied.StatusCode);
            ApiProblemException deniedHistory = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                () => service.HistoryAsync(actor, Guid.NewGuid(), CancellationToken.None));
            Assert.AreEqual(403, deniedHistory.StatusCode);
            return;
        }

        IReadOnlyList<ExpenseResponse> listed = await service.ListAsync(actor, CancellationToken.None);
        CollectionAssert.AreEquivalent(expected, listed.Select(expense => expense.Id).ToList());
        foreach (Expense expense in store.Expenses.Values)
        {
            if (expected.Contains(expense.Id))
            {
                ExpenseResponse found = await service.FindAsync(actor, expense.Id, CancellationToken.None);
                Assert.AreEqual(expense.Id, found.Id);
                Assert.IsEmpty(await service.HistoryAsync(actor, expense.Id, CancellationToken.None));
            }
            else
            {
                ApiProblemException invisible = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                    () => service.FindAsync(actor, expense.Id, CancellationToken.None));
                Assert.AreEqual(404, invisible.StatusCode);
                ApiProblemException hiddenHistory = await Assert.ThrowsExactlyAsync<ApiProblemException>(
                    () => service.HistoryAsync(actor, expense.Id, CancellationToken.None));
                Assert.AreEqual(404, hiddenHistory.StatusCode);
            }
        }
    }

    /// <summary>Auditor and Admin add read/admin rights without revoking Employee writes.</summary>
    /// <param name="extraRole">The role combined with Employee.</param>
    [TestMethod]
    [DataRow(RoleNames.Auditor)]
    [DataRow(RoleNames.Admin)]
    public async Task AccumulatedRolesPreserveEmployeeWrites(string extraRole)
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        var actor = new CurrentActor("owner", new List<string> { RoleNames.Employee, extraRole });
        var request = new ExpenseDraftRequest { Description = "Metro ticket expense", Amount = 6.25m, ExpenseDate = new DateOnly(2026, 10, 3) };
        ExpenseResponse draft = await service.CreateAsync(actor, request, CancellationToken.None);
        ExpenseResponse submitted = await service.SubmitAsync(actor, draft.Id, CancellationToken.None);
        Assert.AreEqual("Submitted", submitted.Status);
        Assert.HasCount(2, store.History);
    }

    /// <summary>Read-only roles cannot change an owned draft even if several accumulate.</summary>
    [TestMethod]
    public async Task ReadPermissionsCannotGrantEmployeeMutations()
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        var expense = new Expense { OwnerId = "owner", Description = "Metro ticket expense", Amount = 6.25m, ExpenseDate = new DateOnly(2026, 10, 3) };
        store.AddExpense(expense);
        var actor = new CurrentActor("owner", new List<string> { RoleNames.Admin, RoleNames.Auditor, RoleNames.Approver, RoleNames.Finance });
        var request = new ExpenseDraftRequest { Description = "Another train expense", Amount = 10m, ExpenseDate = expense.ExpenseDate };
        ApiProblemException edit = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.UpdateAsync(actor, expense.Id, request, CancellationToken.None));
        ApiProblemException submit = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => service.SubmitAsync(actor, expense.Id, CancellationToken.None));
        Assert.AreEqual(403, edit.StatusCode);
        Assert.AreEqual(403, submit.StatusCode);
        Assert.AreEqual(0, store.Saves);
        Assert.IsEmpty(store.History);
        Assert.AreEqual("Metro ticket expense", expense.Description);
    }
}
