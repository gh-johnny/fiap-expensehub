using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Services;

/// <summary>Coordinates validated reimbursement rules with one atomic persistence boundary.</summary>
public sealed class ExpenseService
{
    private readonly IExpenseStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Initializes the service with replaceable persistence and server time.</summary>
    /// <param name="store">The atomic persistence boundary.</param>
    /// <param name="clock">The server clock.</param>
    public ExpenseService(IExpenseStore store, TimeProvider clock)
    {
        _store = store;
        _clock = clock;
    }

    /// <summary>Creates an Employee-owned draft and its first audit event.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="request">Only the editable fields.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The persisted draft view.</returns>
    public async Task<ExpenseResponse> CreateAsync(CurrentActor actor, ExpenseDraftRequest request, CancellationToken cancellationToken)
    {
        ExpenseAuthorization.RequireRole(actor, RoleNames.Employee);
        ValidateDraft(request);
        var expense = new Expense
        {
            OwnerId = actor.UserId,
            Description = request.Description,
            Amount = request.Amount,
            ExpenseDate = request.ExpenseDate!.Value,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
        };
        _store.AddExpense(expense);
        _store.AddHistory(NewHistory(expense, actor, "Created", null));
        await _store.SaveAsync(cancellationToken);
        return View(expense);
    }

    /// <summary>Edits a Draft owned by Employee, auditing only effective changes.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="id">The expense identifier.</param>
    /// <param name="request">The complete new editable fields.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The updated expense view.</returns>
    public async Task<ExpenseResponse> UpdateAsync(CurrentActor actor, Guid id, ExpenseDraftRequest request, CancellationToken cancellationToken)
    {
        ExpenseAuthorization.RequireRole(actor, RoleNames.Employee);
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseAuthorization.RequireOwnDraft(actor, expense);
        ValidateDraft(request);
        if (expense.Description == request.Description && expense.Amount == request.Amount && expense.ExpenseDate == request.ExpenseDate)
        {
            return View(expense);
        }

        string oldDescription = expense.Description;
        decimal oldAmount = expense.Amount;
        DateOnly oldDate = expense.ExpenseDate;
        expense.Description = request.Description;
        expense.Amount = request.Amount;
        expense.ExpenseDate = request.ExpenseDate!.Value;
        expense.Revision++;
        ExpenseHistory history = NewHistory(expense, actor, "Updated", ExpenseStatus.Draft);
        history.PreviousDescription = oldDescription;
        history.NewDescription = expense.Description;
        history.PreviousAmount = oldAmount;
        history.NewAmount = expense.Amount;
        history.PreviousExpenseDate = oldDate;
        history.NewExpenseDate = expense.ExpenseDate;
        _store.AddHistory(history);
        await _store.SaveAsync(cancellationToken);
        return View(expense);
    }

    private static ExpenseResponse View(Expense expense)
        => new(expense.Id, expense.OwnerId, expense.Description, expense.Amount, expense.ExpenseDate,
            expense.Status.ToString(), expense.CategoryId, expense.CreatedAtUtc, expense.Revision);

    private void ValidateDraft(ExpenseDraftRequest request)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), validateAllProperties: true))
        {
            throw new ApiProblemException(400, "request.invalid", "The draft fields are invalid.");
        }

        if (request.ExpenseDate > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime))
        {
            throw new ApiProblemException(400, "expense.invalid_date", "The expense date cannot be in the future.");
        }
    }

    private async Task<Expense> LoadAsync(Guid id, CancellationToken cancellationToken)
        => await _store.LoadForUpdateAsync(id, cancellationToken)
            ?? throw new ApiProblemException(404, "expense.not_found", "The expense was not found.");

    private ExpenseHistory NewHistory(Expense expense, CurrentActor actor, string action, ExpenseStatus? previous)
        => new()
        {
            ExpenseId = expense.Id,
            Revision = expense.Revision,
            ActorId = actor.UserId,
            Action = action,
            OccurredAtUtc = _clock.GetUtcNow().UtcDateTime,
            PreviousStatus = previous,
            NewStatus = expense.Status,
        };
}
