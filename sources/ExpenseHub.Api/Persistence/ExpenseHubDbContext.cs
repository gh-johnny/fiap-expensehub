using System;
using ExpenseHub.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Persistence;

/// <summary>Persists Identity accounts and the reimbursement domain in one database.</summary>
public sealed class ExpenseHubDbContext : IdentityDbContext<IdentityUser>
{
    /// <summary>Initializes the context with configured provider options.</summary>
    /// <param name="options">The provider and connection options.</param>
    public ExpenseHubDbContext(DbContextOptions<ExpenseHubDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the reimbursement requests.</summary>
    public DbSet<Expense> Expenses => Set<Expense>();

    /// <summary>Gets the server-managed categories.</summary>
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    /// <summary>Gets revision-ordered audit events.</summary>
    public DbSet<ExpenseHistory> ExpenseHistories => Set<ExpenseHistory>();

    /// <summary>Gets unique expense payments.</summary>
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Expense>(entity =>
        {
            entity.ToTable("Expenses", table => table.HasCheckConstraint(
                "CK_Expenses_Status", "Status IN ('Draft', 'Submitted', 'Approved', 'Rejected', 'Paid')"));
            entity.Property(expense => expense.Description).IsRequired().HasMaxLength(500);
            entity.Property(expense => expense.OwnerId).IsRequired();
            entity.Property(expense => expense.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(expense => expense.Revision).IsConcurrencyToken();
            entity.Property(expense => expense.CreatedAtUtc).HasConversion(
                instant => instant, instant => DateTime.SpecifyKind(instant, DateTimeKind.Utc));
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(expense => expense.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExpenseCategory>().WithMany().HasForeignKey(expense => expense.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(expense => new { expense.OwnerId, expense.ExpenseDate, expense.Id });
            entity.HasIndex(expense => new { expense.Status, expense.ExpenseDate, expense.Id });
        });

        builder.Entity<ExpenseCategory>(entity =>
        {
            entity.Property(category => category.Name).IsRequired().HasMaxLength(100);
            entity.HasData(new ExpenseCategory { Id = ExpenseCategory.GeneralId, Name = "General" });
        });

        builder.Entity<ExpenseHistory>(entity =>
        {
            entity.Property(history => history.Action).IsRequired().HasMaxLength(32);
            entity.Property(history => history.ActorId).IsRequired();
            entity.Property(history => history.Reason).HasMaxLength(500);
            entity.Property(history => history.PreviousDescription).HasMaxLength(500);
            entity.Property(history => history.NewDescription).HasMaxLength(500);
            entity.Property(history => history.PreviousStatus).HasConversion<string>();
            entity.Property(history => history.NewStatus).HasConversion<string>();
            entity.Property(history => history.OccurredAtUtc).HasConversion(
                instant => instant, instant => DateTime.SpecifyKind(instant, DateTimeKind.Utc));
            entity.HasOne<Expense>().WithMany().HasForeignKey(history => history.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(history => history.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(history => new { history.ExpenseId, history.Revision }).IsUnique();
        });

        builder.Entity<PaymentRecord>(entity =>
        {
            entity.Property(payment => payment.ActorId).IsRequired();
            entity.Property(payment => payment.PaidAtUtc).HasConversion(
                instant => instant, instant => DateTime.SpecifyKind(instant, DateTimeKind.Utc));
            entity.HasOne<Expense>().WithMany().HasForeignKey(payment => payment.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(payment => payment.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(payment => payment.ExpenseId).IsUnique();
        });
    }
}
