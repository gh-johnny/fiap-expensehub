using System;

namespace ExpenseHub.Api.Models;

/// <summary>
/// A relational category assigned by the server.
/// </summary>
public sealed class ExpenseCategory
{
    /// <summary>Gets the identifier of the initial General category.</summary>
    public static Guid GeneralId { get; } = new Guid("01ac82ee-28fb-43c5-beb2-7d812c6ea32a");

    /// <summary>Gets the category identifier.</summary>
    public Guid Id { get; internal set; }

    /// <summary>Gets the category name.</summary>
    public string Name { get; internal set; } = string.Empty;
}
