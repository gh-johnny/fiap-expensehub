using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Contracts.Responses;
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

    public Task<IReadOnlyList<ExpenseResponse>> ListVisibleAsync(ExpenseReadScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ExpenseResponse> result = Expenses.Values.Where(scope.Matches)
            .OrderByDescending(expense => expense.ExpenseDate).ThenBy(expense => expense.Id).Select(View).ToList();
        return Task.FromResult(result);
    }

    public Task<ExpenseResponse?> FindVisibleAsync(Guid id, ExpenseReadScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Expenses.TryGetValue(id, out Expense? expense);
        return Task.FromResult(expense is not null && scope.Matches(expense) ? View(expense) : null);
    }

    private static ExpenseResponse View(Expense expense)
        => new(expense.Id, expense.OwnerId, expense.Description, expense.Amount, expense.ExpenseDate,
            expense.Status.ToString(), expense.CategoryId, expense.CreatedAtUtc, expense.Revision);

    public void AddExpense(Expense expense) => Expenses.Add(expense.Id, expense);

    public void AddHistory(ExpenseHistory history) => History.Add(history);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Saves++;
        return Task.CompletedTask;
    }
}
