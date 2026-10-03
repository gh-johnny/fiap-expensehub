using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using ExpenseHub.Api.Contracts.Responses;
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

    /// <summary>Queries only visible expenses and projects read-only views.</summary>
    /// <param name="scope">The authorized visibility union.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The visible ordered views.</returns>
    Task<IReadOnlyList<ExpenseResponse>> ListVisibleAsync(ExpenseReadScope scope, CancellationToken cancellationToken);

    /// <summary>Finds an expense only within the authorized read scope.</summary>
    /// <param name="id">The requested identifier.</param>
    /// <param name="scope">The authorized visibility union.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The view or null for both absent and invisible resources.</returns>
    Task<ExpenseResponse?> FindVisibleAsync(Guid id, ExpenseReadScope scope, CancellationToken cancellationToken);

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
