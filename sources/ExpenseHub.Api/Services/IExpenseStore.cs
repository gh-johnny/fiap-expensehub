using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Services;

/// <summary>Separates application rules from relational persistence and commit semantics.</summary>
public interface IExpenseStore
{
    /// <summary>Loads a tracked expense for mutation, independently of read visibility.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The expense, or null when absent.</returns>
    Task<Expense?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stages a new expense in the current unit of work.</summary>
    /// <param name="expense">The server-created expense.</param>
    void AddExpense(Expense expense);

    /// <summary>Stages an immutable audit event in the same unit of work.</summary>
    /// <param name="history">The server-created event.</param>
    void AddHistory(ExpenseHistory history);

    /// <summary>Commits every staged change atomically.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A task representing the relational commit.</returns>
    Task SaveAsync(CancellationToken cancellationToken);
}
