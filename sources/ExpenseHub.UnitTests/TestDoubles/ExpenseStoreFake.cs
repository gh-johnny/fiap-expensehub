using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Services;

namespace ExpenseHub.UnitTests.TestDoubles;

internal sealed class ExpenseStoreFake : IExpenseStore
{
    public Dictionary<Guid, Expense> Expenses { get; } = new();

    public List<ExpenseHistory> History { get; } = new();

    public List<PaymentRecord> Payments { get; } = new();

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

    public Task<IReadOnlyList<ExpenseHistoryResponse>?> HistoryVisibleAsync(Guid id, ExpenseReadScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Expenses.TryGetValue(id, out Expense? expense) || !scope.Matches(expense))
        {
            return Task.FromResult<IReadOnlyList<ExpenseHistoryResponse>?>(null);
        }

        IReadOnlyList<ExpenseHistoryResponse> result = History.Where(history => history.ExpenseId == id)
            .OrderBy(history => history.Revision).Select(history => new ExpenseHistoryResponse(history.Id, history.ExpenseId,
                history.Revision, history.Action, history.ActorId, history.OccurredAtUtc, history.PreviousStatus?.ToString(),
                history.NewStatus.ToString(), history.Reason, history.PreviousDescription, history.NewDescription,
                history.PreviousAmount, history.NewAmount, history.PreviousExpenseDate, history.NewExpenseDate)).ToList();
        return Task.FromResult<IReadOnlyList<ExpenseHistoryResponse>?>(result);
    }

    public void AddPayment(PaymentRecord payment) => Payments.Add(payment);

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
