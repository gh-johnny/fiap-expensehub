using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Services;
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
    }
}
