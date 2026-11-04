using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Contracts.Requests;

/// <summary>The complete set of roles requested for an existing account.</summary>
public sealed class UpdateUserRolesRequest
{
    /// <summary>Gets or sets the supported role names; an empty list removes all roles.</summary>
    [Required]
    public List<string> Roles { get; set; } = new List<string>();
}
