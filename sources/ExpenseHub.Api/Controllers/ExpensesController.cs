using System;
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
    public async Task<IActionResult> Update(Guid id, ExpenseDraftRequest request, CancellationToken cancellationToken)
        => Ok(await _service.UpdateAsync(CurrentActor.FromPrincipal(User), id, request, cancellationToken));
}
