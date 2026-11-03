using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Core.Persistence;

/// <summary>Applies pending migrations for hosts that own the schema (the web host in containers).</summary>
public static class DatabaseInitializer
{
    /// <summary>Migrates the database and enables WAL so the web host and the worker can share the SQLite file.</summary>
    /// <param name="services">The root service provider.</param>
    /// <param name="cancellationToken">Cancellation requested by the host.</param>
    /// <returns>A task representing the schema update.</returns>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExpenseHubDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
