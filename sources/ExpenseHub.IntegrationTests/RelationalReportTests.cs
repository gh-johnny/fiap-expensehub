using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Persistence;
using ExpenseHub.Core.Reports;
using ExpenseHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.IntegrationTests;

/// <summary>Verifies report persistence and the SQL translation of the eligibility rules on real SQLite.</summary>
[TestClass]
public sealed class RelationalReportTests
{
    /// <summary>The SQL filter applies inclusive dates and, for Mine, the persisted requester only.</summary>
    /// <param name="scope">The registered scope.</param>
    /// <param name="count">The expected eligible rows.</param>
    /// <param name="amount">The expected exact sum.</param>
    [TestMethod]
    [DataRow(ReportScope.Mine, 3, "175.00")]
    [DataRow(ReportScope.Global, 4, "375.00")]
    public async Task SqlFilterMatchesTheReferenceExample(ReportScope scope, int count, string amount)
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        await using (ExpenseHubDbContext seed = fixture.Context())
        {
            seed.Expenses.AddRange(
                Expense("owner", new DateOnly(2026, 9, 1), ExpenseStatus.Approved, 100.00m),
                Expense("owner", new DateOnly(2026, 9, 30), ExpenseStatus.Approved, 50.00m),
                Expense("owner", new DateOnly(2026, 9, 15), ExpenseStatus.Paid, 25.00m),
                Expense("other", new DateOnly(2026, 9, 12), ExpenseStatus.Approved, 200.00m),
                Expense("owner", new DateOnly(2026, 10, 1), ExpenseStatus.Paid, 500.00m),
                Expense("owner", new DateOnly(2026, 8, 31), ExpenseStatus.Paid, 700.00m));
            await seed.SaveChangesAsync();
        }

        await using ExpenseHubDbContext context = fixture.Context();
        IReadOnlyList<ReportExpenseRow> rows = await new EfReportStore(context).LoadRowsAsync(Request("owner", scope), CancellationToken.None);
        ReportResult result = ReportCalculator.Summarize(rows, DateTime.UtcNow);
        Assert.AreEqual(count, result.TotalCount);
        Assert.AreEqual(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), result.TotalAmount);
        Assert.IsEmpty(context.ChangeTracker.Entries());
    }

    /// <summary>Requests round-trip with UTC instants and owner-only queries return nothing for other users.</summary>
    [TestMethod]
    public async Task OwnedQueriesIsolateRequesters()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        ReportRequest mine = Request("owner", ReportScope.Mine);
        ReportRequest foreign = Request("other", ReportScope.Mine);
        await using (ExpenseHubDbContext write = fixture.Context())
        {
            var store = new EfReportStore(write);
            store.Add(mine);
            store.Add(foreign);
            await store.SaveAsync(CancellationToken.None);
        }

        await using ExpenseHubDbContext read = fixture.Context();
        var reader = new EfReportStore(read);
        IReadOnlyList<ReportRequest> list = await reader.ListOwnedAsync("owner", CancellationToken.None);
        Assert.AreEqual(mine.Id, list.Single().Id);
        Assert.IsNull(await reader.FindOwnedAsync(foreign.Id, "owner", CancellationToken.None));
        ReportRequest found = (await reader.FindOwnedAsync(mine.Id, "owner", CancellationToken.None))!;
        Assert.AreEqual(DateTimeKind.Utc, found.CreatedAtUtc.Kind);
        Assert.AreEqual(ReportStatus.Pending, found.Status);
        Assert.AreEqual(new DateOnly(2026, 9, 30), found.ToDate);
    }

    /// <summary>The database itself rejects an inverted period and an unknown requester.</summary>
    [TestMethod]
    public async Task ConstraintsRejectInvalidRows()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        ReportRequest inverted = Request("owner", ReportScope.Mine);
        inverted.FromDate = new DateOnly(2026, 10, 1);
        await AssertRejectedAsync(fixture, inverted);
        await AssertRejectedAsync(fixture, Request("ghost", ReportScope.Mine));
    }

    private static async Task AssertRejectedAsync(SqliteFixture fixture, ReportRequest report)
    {
        await using ExpenseHubDbContext context = fixture.Context();
        context.ReportRequests.Add(report);
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static ReportRequest Request(string requester, ReportScope scope) => new()
    {
        RequesterId = requester,
        Scope = scope,
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static Expense Expense(string owner, DateOnly date, ExpenseStatus status, decimal amount) => new()
    {
        OwnerId = owner,
        Description = "Report fixture expense",
        ExpenseDate = date,
        Status = status,
        Amount = amount,
        CreatedAtUtc = DateTime.UtcNow,
    };
}
