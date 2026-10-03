using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace ExpenseHub.Api.Services;

/// <summary>Defines the only roles accepted by the application.</summary>
public static class RoleNames
{
    /// <summary>The user administration role.</summary>
    public const string Admin = "Admin";
    /// <summary>The role for owned expense requests.</summary>
    public const string Employee = "Employee";
    /// <summary>The role for expense decisions.</summary>
    public const string Approver = "Approver";
    /// <summary>The role for payment recording.</summary>
    public const string Finance = "Finance";
    /// <summary>The role for unrestricted expense reads.</summary>
    public const string Auditor = "Auditor";

    /// <summary>Gets the immutable set of supported role names.</summary>
    public static IReadOnlySet<string> All { get; } = new List<string>
    {
        Admin, Employee, Approver, Finance, Auditor,
    }.ToFrozenSet(StringComparer.Ordinal);
}
