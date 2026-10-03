using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Persistence;

/// <summary>Persists reimbursement changes and audit events in one relational commit.</summary>
public sealed class EfExpenseStore : IExpenseStore
{
    private readonly ExpenseHubDbContext _context;

    /// <summary>Uses the scoped context shared by all staged changes.</summary>
    /// <param name="context">The relational context.</param>
    public EfExpenseStore(ExpenseHubDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Expense?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken)
        => _context.Expenses.SingleOrDefaultAsync(expense => expense.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpenseResponse>> ListVisibleAsync(ExpenseReadScope scope, CancellationToken cancellationToken)
        => await Visible(scope).OrderByDescending(expense => expense.ExpenseDate).ThenBy(expense => expense.Id)
            .Select(Projection()).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ExpenseResponse?> FindVisibleAsync(Guid id, ExpenseReadScope scope, CancellationToken cancellationToken)
        => Visible(scope).Where(expense => expense.Id == id).Select(Projection()).SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void AddExpense(Expense expense) => _context.Expenses.Add(expense);

    /// <inheritdoc />
    public void AddHistory(ExpenseHistory history) => _context.ExpenseHistories.Add(history);

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            throw new ApiProblemException(409, "expense.concurrent_update", "The expense changed in another request.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException sqlite
            && sqlite.SqliteExtendedErrorCode == 2067
            && sqlite.Message.Contains("ExpenseHistories.ExpenseId, ExpenseHistories.Revision", StringComparison.Ordinal))
        {
            _context.ChangeTracker.Clear();
            throw new ApiProblemException(409, "expense.concurrent_update", "Another request already recorded this revision.");
        }
    }
    private IQueryable<Expense> Visible(ExpenseReadScope scope)
        => _context.Expenses.AsNoTracking().Where(expense => scope.All
            || (scope.Own && expense.OwnerId == scope.OwnerId)
            || (scope.Submitted && expense.Status == ExpenseStatus.Submitted)
            || (scope.Finance && (expense.Status == ExpenseStatus.Approved || expense.Status == ExpenseStatus.Paid)));

    private static Expression<Func<Expense, ExpenseResponse>> Projection()
        => expense => new ExpenseResponse(expense.Id, expense.OwnerId, expense.Description, expense.Amount,
            expense.ExpenseDate, expense.Status.ToString(), expense.CategoryId, expense.CreatedAtUtc, expense.Revision);

}
