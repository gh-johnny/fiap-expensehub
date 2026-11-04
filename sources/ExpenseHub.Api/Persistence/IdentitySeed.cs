using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Api.Persistence;

/// <summary>Creates only the initial Admin account and the supported roles.</summary>
public static class IdentitySeed
{
    /// <summary>The stable identity of the sole bootstrap account.</summary>
    public const string AdminUserId = "7bdf434c-fbc4-48a1-b62b-6060efb7ef40";

    /// <summary>Creates missing bootstrap data without duplicating users or roles.</summary>
    /// <param name="services">The root service provider.</param>
    /// <param name="configuration">External bootstrap credentials.</param>
    /// <param name="cancellationToken">Cancellation requested by the host.</param>
    /// <returns>A task representing bootstrap completion.</returns>
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        foreach (string role in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roles.RoleExistsAsync(role))
            {
                EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)), "Role bootstrap failed.");
            }
        }

        IdentityUser? admin = await users.FindByIdAsync(AdminUserId);
        if (admin is null)
        {
            string? email = configuration["Seed:AdminEmail"];
            string? credential = configuration["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(credential))
            {
                throw new InvalidOperationException("Set Seed__AdminEmail and Seed__AdminPassword before initial startup.");
            }

            admin = new IdentityUser
            {
                Id = AdminUserId,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            EnsureSuccess(await users.CreateAsync(admin, credential), "Initial Admin configuration is invalid.");
        }

        if (!await users.IsInRoleAsync(admin, RoleNames.Admin))
        {
            EnsureSuccess(await users.AddToRoleAsync(admin, RoleNames.Admin), "Admin role assignment failed.");
        }
    }

    private static void EnsureSuccess(IdentityResult result, string message)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(message);
        }
    }
}
