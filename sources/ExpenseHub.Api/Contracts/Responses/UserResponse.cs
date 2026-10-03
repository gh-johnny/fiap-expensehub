using System.Collections.Generic;

namespace ExpenseHub.Api.Contracts.Responses;

/// <summary>An administrative account view without password hashes or credentials.</summary>
/// <param name="Id">The account identifier.</param>
/// <param name="Email">The registered email address.</param>
/// <param name="Roles">The current role names.</param>
public sealed record UserResponse(string Id, string? Email, IReadOnlyList<string> Roles);
