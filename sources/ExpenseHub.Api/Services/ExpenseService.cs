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

    /// <summary>Lists the union of all granted read scopes.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Only authorized read-only views.</returns>
    public Task<IReadOnlyList<ExpenseResponse>> ListAsync(CurrentActor actor, CancellationToken cancellationToken)
        => _store.ListVisibleAsync(ExpenseAuthorization.ReadScope(actor), cancellationToken);

    /// <summary>Finds a visible expense without disclosing invisible identifiers.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="id">The requested identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The authorized view.</returns>
    public async Task<ExpenseResponse> FindAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
        => await _store.FindVisibleAsync(id, ExpenseAuthorization.ReadScope(actor), cancellationToken)
            ?? throw new ApiProblemException(404, "expense.not_found", "The expense was not found.");

    /// <summary>Submits an owned Draft once, recording its transition atomically.</summary>
    /// <param name="actor">The authenticated Employee.</param>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The submitted expense view.</returns>
    public async Task<ExpenseResponse> SubmitAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ExpenseAuthorization.RequireRole(actor, RoleNames.Employee);
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseAuthorization.RequireOwnDraft(actor, expense);
        RecordTransition(expense, actor, ExpenseStatus.Submitted, "Submitted", null);
        await _store.SaveAsync(cancellationToken);
        return View(expense);
    }

    /// <summary>Approves a submitted expense without using read visibility for the mutation.</summary>
    /// <param name="actor">The authenticated Approver.</param>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The approved view.</returns>
    public async Task<ExpenseResponse> ApproveAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ExpenseAuthorization.RequireRole(actor, RoleNames.Approver);
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseAuthorization.RequireExternalAction(actor, expense, RoleNames.Approver, ExpenseStatus.Submitted);
        RecordTransition(expense, actor, ExpenseStatus.Approved, "Approved", null);
        await _store.SaveAsync(cancellationToken);
        return View(expense);
    }

    /// <summary>Rejects a submitted expense with a validated justification.</summary>
    /// <param name="actor">The authenticated Approver.</param>
    /// <param name="id">The expense identifier.</param>
    /// <param name="request">The trimmed justification.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The rejected view.</returns>
    public async Task<ExpenseResponse> RejectAsync(CurrentActor actor, Guid id, RejectExpenseRequest request, CancellationToken cancellationToken)
    {
        ExpenseAuthorization.RequireRole(actor, RoleNames.Approver);
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseAuthorization.RequireExternalAction(actor, expense, RoleNames.Approver, ExpenseStatus.Submitted);
        if (!Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), validateAllProperties: true))
        {
            throw new ApiProblemException(400, "expense.invalid_reason", "The rejection justification is invalid.");
        }

        RecordTransition(expense, actor, ExpenseStatus.Rejected, "Rejected", request.Reason);
        await _store.SaveAsync(cancellationToken);
        return View(expense);
    }

    /// <summary>Pays a foreign Approved expense once using server-derived actor, time and amount.</summary>
    /// <param name="actor">The authenticated Finance actor.</param>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The paid view.</returns>
    public async Task<ExpenseResponse> PayAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ExpenseAuthorization.RequireRole(actor, RoleNames.Finance);
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseAuthorization.RequireExternalAction(actor, expense, RoleNames.Finance, ExpenseStatus.Approved);
        RecordTransition(expense, actor, ExpenseStatus.Paid, "Paid", null);
        _store.AddPayment(new PaymentRecord
        {
            ExpenseId = expense.Id,
            ActorId = actor.UserId,
            Amount = expense.Amount,
            PaidAtUtc = _clock.GetUtcNow().UtcDateTime,
        });
        await _store.SaveAsync(cancellationToken);
        return View(expense);
    }

    /// <summary>Reads the audit timeline under the same expense read scope.</summary>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="id">The requested expense.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The authorized revision-ordered timeline.</returns>
    public async Task<IReadOnlyList<ExpenseHistoryResponse>> HistoryAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
        => await _store.HistoryVisibleAsync(id, ExpenseAuthorization.ReadScope(actor), cancellationToken)
            ?? throw new ApiProblemException(404, "expense.not_found", "The expense was not found.");

    private void RecordTransition(Expense expense, CurrentActor actor, ExpenseStatus target, string action, string? reason)
    {
        ExpenseStatus previous = expense.Status;
        expense.Status = target;
        expense.Revision++;
        ExpenseHistory history = NewHistory(expense, actor, action, previous);
        history.Reason = reason;
        _store.AddHistory(history);
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
