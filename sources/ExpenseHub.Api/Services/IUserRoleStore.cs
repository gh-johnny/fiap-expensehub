using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Responses;

namespace ExpenseHub.Api.Services;

/// <summary>Isolates account administration from the Identity persistence implementation.</summary>
public interface IUserRoleStore
{
    /// <summary>Reads account views without credentials.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The account views.</returns>
    Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Checks whether the requested account exists.</summary>
    /// <param name="userId">The account identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Whether the account exists.</returns>
    Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Atomically replaces all roles for an account.</summary>
    /// <param name="userId">The account identifier.</param>
    /// <param name="roles">The validated target roles.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A task representing the complete replacement.</returns>
    Task ReplaceAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);
}
