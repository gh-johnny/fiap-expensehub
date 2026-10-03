using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Persistence;
using ExpenseHub.Api.Services;
using ExpenseHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.IntegrationTests;

/// <summary>Checks migrations, bootstrap cardinality and transactional Identity role updates.</summary>
[TestClass]
public sealed class IdentityPersistenceTests
{
    /// <summary>Migrations are repeatable and seed creates exactly one Admin account and five roles.</summary>
    [TestMethod]
    public async Task MigrationsAndSeedAreIdempotent()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await using (ExpenseHubDbContext initial = fixture.Context())
        {
            Assert.AreEqual(0, await initial.Users.CountAsync());
            Assert.AreEqual(1, await initial.ExpenseCategories.CountAsync());
            await initial.Database.MigrateAsync();
        }

        await fixture.SeedAsync();
        string? hash;
        await using (ExpenseHubDbContext context = fixture.Context())
        {
            hash = (await context.Users.SingleAsync()).PasswordHash;
        }

        await fixture.SeedAsync();
        await using ExpenseHubDbContext proof = fixture.Context();
        Assert.AreEqual(1, await proof.Users.CountAsync());
        IdentityUser admin = await proof.Users.SingleAsync();
        Assert.AreEqual(IdentitySeed.AdminUserId, admin.Id);
        Assert.AreEqual(hash, admin.PasswordHash);
        CollectionAssert.AreEquivalent(RoleNames.All.ToList(), await proof.Roles.Select(role => role.Name!).ToListAsync());
        string role = await (from assignment in proof.UserRoles
                             join definition in proof.Roles on assignment.RoleId equals definition.Id
                             select definition.Name!).SingleAsync();
        Assert.AreEqual(RoleNames.Admin, role);
    }

    /// <summary>A failure after role removal rolls back both the role set and concurrency stamp.</summary>
    [TestMethod]
    public async Task FailedRoleInsertRestoresOriginalAccount()
    {
        await using SqliteFixture fixture = await SqliteFixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.CreateAccountAsync("target", RoleNames.Employee);
        string? stamp;
        await using (ExpenseHubDbContext initial = fixture.Context())
        {
            stamp = (await initial.Users.SingleAsync(user => user.Id == "target")).ConcurrencyStamp;
            await initial.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_role_insert BEFORE INSERT ON AspNetUserRoles WHEN NEW.UserId='target' BEGIN SELECT RAISE(ABORT, 'fixture role failure'); END");
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExpenseHubDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var service = new UserRoleService(new IdentityUserRoleStore(users, context));
            var admin = new CurrentActor(IdentitySeed.AdminUserId, new List<string> { RoleNames.Admin });
            var request = new UpdateUserRolesRequest { Roles = new List<string> { RoleNames.Finance } };
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => service.ReplaceAsync(admin, "target", request, CancellationToken.None));
            Assert.IsEmpty(context.ChangeTracker.Entries());
        }

        await using ExpenseHubDbContext proof = fixture.Context();
        List<string> roles = await (from assignment in proof.UserRoles
                                    join definition in proof.Roles on assignment.RoleId equals definition.Id
                                    where assignment.UserId == "target"
                                    select definition.Name!).ToListAsync();
        CollectionAssert.AreEqual(new List<string> { RoleNames.Employee }, roles);
        Assert.AreEqual(stamp, (await proof.Users.SingleAsync(user => user.Id == "target")).ConcurrencyStamp);
    }
}
