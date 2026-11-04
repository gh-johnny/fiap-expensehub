using System.Text.Json;
using ExpenseHub.Core.Contracts.Responses;

namespace ExpenseHub.Core.Reports;

/// <summary>Serializes the persisted result and maps requests to their read-only envelope.</summary>
public static class ReportResultJson
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a result for persistence; decimals keep their exact value.</summary>
    /// <param name="result">The calculated result.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(ReportResult result) => JsonSerializer.Serialize(result, _options);

    /// <summary>Reads a persisted result without recalculating it.</summary>
    /// <param name="json">The persisted JSON, or null.</param>
    /// <returns>The result, or null when none was persisted.</returns>
    public static ReportResult? Deserialize(string? json)
        => json is null ? null : JsonSerializer.Deserialize<ReportResult>(json, _options);

    /// <summary>Maps a persisted request to the public envelope.</summary>
    /// <param name="report">The persisted request.</param>
    /// <returns>The envelope with the stored result or error.</returns>
    public static ReportResponse View(ReportRequest report)
    {
        ReportError? error = report.ErrorCode is null ? null : new ReportError(report.ErrorCode, report.ErrorMessage ?? string.Empty);
        return new ReportResponse(report.Id, report.Scope.ToString(), report.FromDate, report.ToDate, report.Status.ToString(),
            report.CreatedAtUtc, report.StartedAtUtc, report.CompletedAtUtc, Deserialize(report.ResultJson), error);
    }
}
