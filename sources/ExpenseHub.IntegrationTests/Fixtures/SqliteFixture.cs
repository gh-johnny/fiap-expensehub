using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Requests;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Persistence;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.IntegrationTests.Fixtures;

internal sealed class SqliteFixture : IAsyncDisposable
{
    private readonly string _directory;
    private readonly string _connection;

    private SqliteFixture()
    {
        _directory = Path.Combine(Path.GetTempPath(), "expensehub-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _connection = "Data Source=" + Path.Combine(_directory, "fixture.db") + ";Pooling=False";
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:AdminEmail"] = "admin@example.test",
            ["Seed:AdminPassword"] = Guid.NewGuid().ToString("N") + "aA1!",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ExpenseHubDbContext>(options => options.UseSqlite(_connection));
        services.AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ExpenseHubDbContext>();
        Services = services.BuildServiceProvider(validateScopes: true);
    }

    public ServiceProvider Services { get; }

    public IConfiguration Configuration { get; }

    public static async Task<SqliteFixture> CreateAsync()
    {
        var fixture = new SqliteFixture();
        try
        {
            await using ExpenseHubDbContext context = fixture.Context();
            await context.Database.MigrateAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public ExpenseHubDbContext Context()
        => new(new DbContextOptionsBuilder<ExpenseHubDbContext>().UseSqlite(_connection).Options);

    public Task SeedAsync() => IdentitySeed.InitializeAsync(Services, Configuration, CancellationToken.None);

    public async Task PrepareAccountsAsync()
    {
        await SeedAsync();
        await CreateAccountAsync("owner", RoleNames.Employee);
        await CreateAccountAsync("other", RoleNames.Employee);
        await CreateAccountAsync("approver", RoleNames.Approver);
        await CreateAccountAsync("finance", RoleNames.Finance);
    }

    public async Task CreateAccountAsync(string id, string role)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { Id = id, UserName = id + "@example.test", Email = id + "@example.test" };
        IdentityResult created = await manager.CreateAsync(user);
        IdentityResult assigned = await manager.AddToRoleAsync(user, role);
        if (!created.Succeeded || !assigned.Succeeded)
        {
            throw new InvalidOperationException("Integration fixture account setup failed.");
        }
    }

    public async Task<Guid> CreateExpenseAsync(string owner = "owner", bool submit = false, bool approve = false)
    {
        await using ExpenseHubDbContext context = Context();
        var service = new ExpenseService(new EfExpenseStore(context), TimeProvider.System);
        var actor = new CurrentActor(owner, new List<string> { RoleNames.Employee });
        var request = new ExpenseDraftRequest
        {
            Description = "Relational receipt expense",
            Amount = 2147483647m,
            ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        ExpenseResponse expense = await service.CreateAsync(actor, request, CancellationToken.None);
        if (submit)
        {
            await service.SubmitAsync(actor, expense.Id, CancellationToken.None);
        }

        if (approve)
        {
            await service.ApproveAsync(new CurrentActor("approver", new List<string> { RoleNames.Approver }), expense.Id, CancellationToken.None);
        }

        return expense.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }
}
