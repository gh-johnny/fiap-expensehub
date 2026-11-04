using System;
using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Contracts.Requests;

/// <summary>Accepts only editable draft fields; ownership and lifecycle stay server-managed.</summary>
public sealed class ExpenseDraftRequest
{
    private string _description = string.Empty;

    /// <summary>Gets or sets the trimmed description, from 10 to 500 characters.</summary>
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Description
    {
        get => _description;
        set => _description = value?.Trim() ?? string.Empty;
    }

    /// <summary>Gets or sets the single exact amount within the required bounds.</summary>
    [Required]
    [Range(typeof(decimal), "0.01", "2147483647", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the required expense date, validated against the server UTC date.</summary>
    [Required]
    public DateOnly? ExpenseDate { get; set; }
}
