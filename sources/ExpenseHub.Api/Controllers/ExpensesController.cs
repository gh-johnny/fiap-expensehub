using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseHub.Api.Controllers;

/// <summary>Exposes reimbursement operations through validated input contracts.</summary>
[ApiController]
[Route("api/expenses")]
[Authorize]
public sealed class ExpensesController : ControllerBase
{
    private readonly ExpenseService _service;

    /// <summary>Initializes the thin HTTP adapter.</summary>
    /// <param name="service">The reimbursement service.</param>
    public ExpensesController(ExpenseService service)
    {
        _service = service;
    }

    /// <summary>Creates an authenticated Employee draft.</summary>
    /// <param name="request">Only the editable fields.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The created view and its resource location.</returns>
    [HttpPost]
    [Authorize(Roles = RoleNames.Employee)]
    [ProducesResponseType<ExpenseResponse>(201)]
    public async Task<IActionResult> Create(ExpenseDraftRequest request, CancellationToken cancellationToken)
    {
        ExpenseResponse response = await _service.CreateAsync(CurrentActor.FromPrincipal(User), request, cancellationToken);
        return Created($"/api/expenses/{response.Id}", response);
    }

    /// <summary>Replaces editable fields of an owned Draft.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="request">The editable fields.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The updated view.</returns>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Employee)]
    [ProducesResponseType<ExpenseResponse>(200)]
    public async Task<IActionResult> Update(Guid id, ExpenseDraftRequest request, CancellationToken cancellationToken)
        => Ok(await _service.UpdateAsync(CurrentActor.FromPrincipal(User), id, request, cancellationToken));
    /// <summary>Lists expenses visible under the union of the actor roles.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Authorized read-only views.</returns>
    [HttpGet]
    [Authorize(Roles = RoleNames.Employee + "," + RoleNames.Approver + "," + RoleNames.Finance + "," + RoleNames.Auditor)]
    [ProducesResponseType<IReadOnlyList<ExpenseResponse>>(200)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _service.ListAsync(CurrentActor.FromPrincipal(User), cancellationToken));

    /// <summary>Reads an expense only within the authorized visibility scope.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The authorized view.</returns>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = RoleNames.Employee + "," + RoleNames.Approver + "," + RoleNames.Finance + "," + RoleNames.Auditor)]
    [ProducesResponseType<ExpenseResponse>(200)]
    public async Task<IActionResult> Find(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.FindAsync(CurrentActor.FromPrincipal(User), id, cancellationToken));

    /// <summary>Submits the authenticated Employee's Draft.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The submitted view.</returns>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = RoleNames.Employee)]
    [ProducesResponseType<ExpenseResponse>(200)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.SubmitAsync(CurrentActor.FromPrincipal(User), id, cancellationToken));

    /// <summary>Approves a Submitted expense belonging to a different account.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The approved view.</returns>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = RoleNames.Approver)]
    [ProducesResponseType<ExpenseResponse>(200)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.ApproveAsync(CurrentActor.FromPrincipal(User), id, cancellationToken));

    /// <summary>Rejects a Submitted expense belonging to a different account.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="request">The required justification.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The rejected view.</returns>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = RoleNames.Approver)]
    [ProducesResponseType<ExpenseResponse>(200)]
    public async Task<IActionResult> Reject(Guid id, RejectExpenseRequest request, CancellationToken cancellationToken)
        => Ok(await _service.RejectAsync(CurrentActor.FromPrincipal(User), id, request, cancellationToken));
    /// <summary>Records payment for a foreign Approved expense.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The paid view.</returns>
    [HttpPost("{id:guid}/pay")]
    [Authorize(Roles = RoleNames.Finance)]
    [ProducesResponseType<ExpenseResponse>(200)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.PayAsync(CurrentActor.FromPrincipal(User), id, cancellationToken));

    /// <summary>Reads an expense timeline with the same visibility as its detail.</summary>
    /// <param name="id">The expense identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Authorized events ordered by revision.</returns>
    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = RoleNames.Employee + "," + RoleNames.Approver + "," + RoleNames.Finance + "," + RoleNames.Auditor)]
    [ProducesResponseType<IReadOnlyList<ExpenseHistoryResponse>>(200)]
    public async Task<IActionResult> History(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.HistoryAsync(CurrentActor.FromPrincipal(User), id, cancellationToken));

}
