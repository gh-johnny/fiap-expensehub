using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

/// <summary>Covers input normalization and partial-change edges not exercised by the lifecycle tests.</summary>
[TestClass]
public sealed class ContractEdgeTests
{
    /// <summary>Null text normalizes to empty, so validation (not a NullReferenceException) rejects it.</summary>
    [TestMethod]
    public void NullTextNormalizesToEmpty()
    {
        Assert.AreEqual(string.Empty, new ExpenseDraftRequest { Description = null! }.Description);
        Assert.AreEqual(string.Empty, new RejectExpenseRequest { Reason = null! }.Reason);
    }

    /// <summary>Changing a single field still records an update.</summary>
    /// <param name="field">The field that changes.</param>
    [TestMethod]
    [DataRow("amount")]
    [DataRow("date")]
    public async Task SingleFieldChangeIsAnUpdate(string field)
    {
        var store = new ExpenseStoreFake();
        var service = new ExpenseService(store, new FixedTimeProvider());
        var actor = new CurrentActor("owner", new List<string> { RoleNames.Employee });
        ExpenseResponse created = await service.CreateAsync(actor, Draft(), CancellationToken.None);
        ExpenseDraftRequest change = Draft();
        if (field == "amount")
        {
            change.Amount = 11m;
        }
        else
        {
            change.ExpenseDate = new DateOnly(2026, 9, 30);
        }

        ExpenseResponse updated = await service.UpdateAsync(actor, created.Id, change, CancellationToken.None);
        Assert.AreEqual(2L, updated.Revision);
        Assert.HasCount(2, store.History);
    }

    /// <summary>A principal without an identity object is treated as anonymous.</summary>
    [TestMethod]
    public void PrincipalWithoutIdentityIsAnonymous()
    {
        ApiProblemException error = Assert.ThrowsExactly<ApiProblemException>(() => CurrentActor.FromPrincipal(new ClaimsPrincipal()));
        Assert.AreEqual(401, error.StatusCode);
    }

    /// <summary>The server-owned category defaults to an empty name until mapped.</summary>
    [TestMethod]
    public void CategoryDefaults()
    {
        var category = new ExpenseCategory { Id = ExpenseCategory.GeneralId };
        Assert.AreEqual(ExpenseCategory.GeneralId, category.Id);
        Assert.AreEqual(string.Empty, category.Name);
    }

    private static ExpenseDraftRequest Draft() => new()
    {
        Description = "Taxi to the client office",
        Amount = 10m,
        ExpenseDate = new DateOnly(2026, 10, 1),
    };
}
