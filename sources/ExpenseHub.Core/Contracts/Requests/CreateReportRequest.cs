using System;
using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Core.Contracts.Requests;

/// <summary>The only fields a client sends to request a report; requester and identifier come from the server.</summary>
public sealed class CreateReportRequest
{
    /// <summary>Gets or sets the inclusive start date (yyyy-MM-dd).</summary>
    [Required]
    public DateOnly? FromDate { get; set; }

    /// <summary>Gets or sets the inclusive end date (yyyy-MM-dd), not before <see cref="FromDate"/>.</summary>
    [Required]
    public DateOnly? ToDate { get; set; }

    /// <summary>Gets or sets the scope: <c>Mine</c> or <c>Global</c>.</summary>
    [Required]
    [StringLength(16)]
    public string Scope { get; set; } = string.Empty;
}
