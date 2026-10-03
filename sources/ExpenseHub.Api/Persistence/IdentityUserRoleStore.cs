using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExpenseHub.Api.Persistence;

/// <summary>Adapts Identity operations to an atomic administrative persistence boundary.</summary>
public sealed class IdentityUserRoleStore : IUserRoleStore
{
    private readonly UserManager<IdentityUser> _users;
    private readonly ExpenseHubDbContext _context;

    /// <summary>Uses the Identity manager and the same scoped relational context.</summary>
    /// <param name="users">The account manager.</param>
    /// <param name="context">The shared relational context.</param>
    public IdentityUserRoleStore(UserManager<IdentityUser> users, ExpenseHubDbContext context)
    {
        _users = users;
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken)
    {
        List<IdentityUser> users = await _users.Users.AsNoTracking().OrderBy(user => user.Id).ToListAsync(cancellationToken);
        var result = new List<UserResponse>();
        foreach (IdentityUser user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IList<string> roles = await _users.GetRolesAsync(user);
            result.Add(new UserResponse(user.Id, user.Email, roles.OrderBy(role => role, StringComparer.Ordinal).ToList()));
        }

        return result;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken)
        => _users.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public async Task ReplaceAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        IdentityUser? user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            throw new ApiProblemException(404, "admin.user_not_found", "The account was not found.");
        }

        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            IList<string> current = await _users.GetRolesAsync(user);
            if (current.Count > 0)
            {
                EnsureSuccess(await _users.RemoveFromRolesAsync(user, current));
            }

            if (roles.Count > 0)
            {
                EnsureSuccess(await _users.AddToRolesAsync(user, roles));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ApiProblemException(409, "admin.roles_update_failed", "Roles could not be updated.");
        }
    }
}
