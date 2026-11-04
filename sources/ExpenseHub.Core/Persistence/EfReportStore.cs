using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Reports;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Core.Persistence;

/// <summary>Persists report requests and reads eligible expenses with SQL-side filtering.</summary>
public sealed class EfReportStore : IReportStore
{
    private readonly ExpenseHubDbContext _context;

    /// <summary>Uses the scoped context shared by all staged changes.</summary>
    /// <param name="context">The relational context.</param>
    public EfReportStore(ExpenseHubDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public void Add(ReportRequest report) => _context.ReportRequests.Add(report);

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportRequest>> ListOwnedAsync(string requesterId, CancellationToken cancellationToken)
        => await _context.ReportRequests.AsNoTracking().Where(report => report.RequesterId == requesterId)
            .OrderByDescending(report => report.CreatedAtUtc).ThenBy(report => report.Id).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ReportRequest?> FindOwnedAsync(Guid id, string requesterId, CancellationToken cancellationToken)
        => _context.ReportRequests.AsNoTracking()
            .SingleOrDefaultAsync(report => report.Id == id && report.RequesterId == requesterId, cancellationToken);

    /// <inheritdoc />
    public Task<ReportRequest?> LoadForProcessingAsync(Guid id, CancellationToken cancellationToken)
        => _context.ReportRequests.SingleOrDefaultAsync(report => report.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportExpenseRow>> LoadRowsAsync(ReportRequest report, CancellationToken cancellationToken)
        => await _context.Expenses.AsNoTracking().Where(ReportCalculator.Eligible(report))
            .Select(expense => new ReportExpenseRow(expense.Status, expense.Amount)).ToListAsync(cancellationToken);
}
