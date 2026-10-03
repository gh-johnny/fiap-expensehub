using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Contracts.Requests;

/// <summary>Accepts only the justification for rejecting a submitted expense.</summary>
public sealed class RejectExpenseRequest
{
    private string _reason = string.Empty;

    /// <summary>Gets or sets the trimmed justification, from 10 to 500 characters.</summary>
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Reason
    {
        get => _reason;
        set => _reason = value?.Trim() ?? string.Empty;
    }
}
