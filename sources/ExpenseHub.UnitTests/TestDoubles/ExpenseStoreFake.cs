using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Services;

namespace ExpenseHub.UnitTests.TestDoubles;

internal sealed class ExpenseStoreFake : IExpenseStore
{
    public Dictionary<Guid, Expense> Expenses { get; } = new();

    public List<ExpenseHistory> History { get; } = new();

    public int Saves { get; private set; }

    public Task<Expense?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Expenses.TryGetValue(id, out Expense? expense);
        return Task.FromResult(expense);
    }

    public void AddExpense(Expense expense) => Expenses.Add(expense.Id, expense);

    public void AddHistory(ExpenseHistory history) => History.Add(history);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Saves++;
        return Task.CompletedTask;
    }
}
