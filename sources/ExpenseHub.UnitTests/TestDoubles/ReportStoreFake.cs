using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Reports;

namespace ExpenseHub.UnitTests.TestDoubles;

/// <summary>
/// In-memory report store that applies the same eligibility predicate EF Core translates to SQL, with failure
/// injection for each boundary the worker crosses.
/// </summary>
internal sealed class ReportStoreFake : IReportStore
{
    private readonly List<ReportRequest> _staged = new();

    public Dictionary<Guid, ReportRequest> Reports { get; } = new();

    public List<Expense> Expenses { get; } = new();

    public int Saves { get; private set; }

    public int RowLoads { get; private set; }

    /// <summary>Gets or sets the failure for the n-th save call (1-based), or null to succeed.</summary>
    public Func<int, Exception?> SaveFailure { get; set; } = _ => null;

    public Exception? LoadFailure { get; set; }

    public Exception? RowsFailure { get; set; }

    public void Add(ReportRequest report) => _staged.Add(report);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Exception? failure = SaveFailure(Saves + 1);
        if (failure is not null)
        {
            _staged.Clear();
            throw failure;
        }

        foreach (ReportRequest report in _staged)
        {
            Reports[report.Id] = report;
        }

        _staged.Clear();
        Saves++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReportRequest>> ListOwnedAsync(string requesterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ReportRequest> result = Reports.Values.Where(report => report.RequesterId == requesterId)
            .OrderByDescending(report => report.CreatedAtUtc).ThenBy(report => report.Id).ToList();
        return Task.FromResult(result);
    }

    public Task<ReportRequest?> FindOwnedAsync(Guid id, string requesterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reports.TryGetValue(id, out ReportRequest? report);
        return Task.FromResult(report?.RequesterId == requesterId ? report : null);
    }

    public Task<ReportRequest?> LoadForProcessingAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LoadFailure is not null)
        {
            throw LoadFailure;
        }

        Reports.TryGetValue(id, out ReportRequest? report);
        return Task.FromResult(report);
    }

    public Task<IReadOnlyList<ReportExpenseRow>> LoadRowsAsync(ReportRequest report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RowLoads++;
        if (RowsFailure is not null)
        {
            throw RowsFailure;
        }

        Func<Expense, bool> eligible = ReportCalculator.Eligible(report).Compile();
        IReadOnlyList<ReportExpenseRow> rows = Expenses.Where(eligible)
            .Select(expense => new ReportExpenseRow(expense.Status, expense.Amount)).ToList();
        return Task.FromResult(rows);
    }
}
