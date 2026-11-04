using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Persistence;
using ExpenseHub.Core.Reports;
using ExpenseHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.IntegrationTests;

/// <summary>Proves on real SQLite that processing is persistent, consistent and idempotent across restarts and races.</summary>
[TestClass]
public sealed class RelationalReportProcessingTests
{
    /// <summary>
    /// Completed state and result are committed together; a "restarted" worker (new context and processor) receiving the
    /// same message produces no second result, and later expense changes do not alter the stored report.
    /// </summary>
    [TestMethod]
    public async Task CompletionIsPersistentAndRedeliveryHasNoEffect()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        Guid expenseId = await SeedExpenseAsync(fixture, 40.00m);
        Guid reportId = await RegisterAsync(fixture);

        Assert.AreEqual(ReportProcessingOutcome.Completed, await ProcessAsync(fixture, reportId));
        ReportRequest first = await LoadAsync(fixture, reportId);
        Assert.AreEqual(ReportStatus.Completed, first.Status);
        Assert.AreEqual(40.00m, ReportResultJson.Deserialize(first.ResultJson)!.TotalAmount);

        await using (ExpenseHubDbContext change = fixture.Context())
        {
            Expense expense = await change.Expenses.SingleAsync(item => item.Id == expenseId);
            expense.Amount = 999.00m;
            await change.SaveChangesAsync();
        }

        Assert.AreEqual(ReportProcessingOutcome.Duplicate, await ProcessAsync(fixture, reportId));
        ReportRequest after = await LoadAsync(fixture, reportId);
        Assert.AreEqual(first.ResultJson, after.ResultJson);
        Assert.AreEqual(first.Revision, after.Revision);
        Assert.AreEqual(1, after.Attempts);
        Assert.AreEqual(DateTimeKind.Utc, after.CompletedAtUtc!.Value.Kind);
    }

    /// <summary>Two deliveries racing on the same request: the loser hits the revision token, retries and sees a duplicate.</summary>
    [TestMethod]
    public async Task ConcurrentDeliveryLosesOnTheRevisionToken()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.PrepareAccountsAsync();
        Guid reportId = await RegisterAsync(fixture);

        await using ExpenseHubDbContext slowContext = fixture.Context();
        var slowStore = new EfReportStore(slowContext);
        ReportRequest stale = (await slowStore.LoadForProcessingAsync(reportId, CancellationToken.None))!;
        Assert.AreEqual(ReportProcessingOutcome.Completed, await ProcessAsync(fixture, reportId));

        stale.Status = ReportStatus.Processing;
        stale.Revision++;
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => slowStore.SaveAsync(CancellationToken.None));
        Assert.IsTrue(ReportProcessor.IsTransient(new DbUpdateConcurrencyException()));
        Assert.AreEqual(ReportProcessingOutcome.Duplicate, await ProcessAsync(fixture, reportId));
    }

    private static async Task<ReportProcessingOutcome> ProcessAsync(SqliteFixture fixture, Guid reportId)
    {
        await using ExpenseHubDbContext context = fixture.Context();
        var processor = new ReportProcessor(new EfReportStore(context), TimeProvider.System,
            Options.Create(new ReportProcessingOptions()), NullLogger<ReportProcessor>.Instance);
        return await processor.ProcessAsync(reportId, CancellationToken.None);
    }

    private static async Task<ReportRequest> LoadAsync(SqliteFixture fixture, Guid reportId)
    {
        await using ExpenseHubDbContext context = fixture.Context();
        return await context.ReportRequests.AsNoTracking().SingleAsync(report => report.Id == reportId);
    }

    private static async Task<Guid> RegisterAsync(SqliteFixture fixture)
    {
        var report = new ReportRequest
        {
            RequesterId = "owner",
            Scope = ReportScope.Mine,
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            CreatedAtUtc = DateTime.UtcNow,
        };
        await using ExpenseHubDbContext context = fixture.Context();
        context.ReportRequests.Add(report);
        await context.SaveChangesAsync();
        return report.Id;
    }

    private static async Task<Guid> SeedExpenseAsync(SqliteFixture fixture, decimal amount)
    {
        var expense = new Expense
        {
            OwnerId = "owner",
            Description = "Processing fixture expense",
            ExpenseDate = new DateOnly(2026, 9, 15),
            Status = ExpenseStatus.Approved,
            Amount = amount,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await using ExpenseHubDbContext context = fixture.Context();
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
        return expense.Id;
    }
}
