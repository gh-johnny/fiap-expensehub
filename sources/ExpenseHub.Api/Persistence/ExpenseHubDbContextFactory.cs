using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExpenseHub.Api.Persistence;

/// <summary>Allows migration tooling to run without bootstrap credentials or an HTTP host.</summary>
public sealed class ExpenseHubDbContextFactory : IDesignTimeDbContextFactory<ExpenseHubDbContext>
{
    /// <inheritdoc />
    public ExpenseHubDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExpenseHubDbContext>();
        options.UseSqlite(Environment.GetEnvironmentVariable("ConnectionStrings__ExpenseHub")
            ?? "Data Source=expensehub.db");
        return new ExpenseHubDbContext(options.Options);
    }
}
