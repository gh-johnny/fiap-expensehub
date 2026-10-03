using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseHub.Api.Controllers;

/// <summary>Exposes account administration guarded by the Admin role.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly UserRoleService _service;

    /// <summary>Initializes the controller with its validated application service.</summary>
    /// <param name="service">The account administration service.</param>
    public AdminUsersController(UserRoleService service)
    {
        _service = service;
    }

    /// <summary>Lists accounts without sensitive Identity fields.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The authorized account views.</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(200)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _service.ListAsync(CurrentActor.FromPrincipal(User), cancellationToken));

    /// <summary>Replaces the target account roles after contextual validation.</summary>
    /// <param name="id">The target account identifier.</param>
    /// <param name="request">The complete desired role set.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>No content after the atomic update.</returns>
    [HttpPut("{id}/roles")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Replace(string id, UpdateUserRolesRequest request, CancellationToken cancellationToken)
    {
        await _service.ReplaceAsync(CurrentActor.FromPrincipal(User), id, request, cancellationToken);
        return NoContent();
    }
}
