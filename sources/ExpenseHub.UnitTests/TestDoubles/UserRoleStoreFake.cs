using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Services;

namespace ExpenseHub.UnitTests.TestDoubles;

internal sealed class UserRoleStoreFake : IUserRoleStore
{
    public Dictionary<string, List<string>> Accounts { get; } = new Dictionary<string, List<string>>
    {
        ["admin"] = new List<string> { RoleNames.Admin },
        ["target"] = new List<string> { RoleNames.Employee },
    };

    public int Replacements { get; private set; }

    public Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<UserResponse> result = Accounts.Select(account =>
            new UserResponse(account.Key, account.Key + "@example.test", account.Value.AsReadOnly())).ToList();
        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Accounts.ContainsKey(userId));
    }

    public Task ReplaceAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Accounts[userId] = roles.ToList();
        Replacements++;
        return Task.CompletedTask;
    }
}
