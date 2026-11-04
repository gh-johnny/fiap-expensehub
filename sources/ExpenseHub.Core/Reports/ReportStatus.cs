namespace ExpenseHub.Core.Reports;

/// <summary>Defines the processing lifecycle of a report request.</summary>
public enum ReportStatus
{
    /// <summary>Registered and published; waiting for the worker.</summary>
    Pending,
    /// <summary>The worker started processing; no result yet.</summary>
    Processing,
    /// <summary>The result was persisted; terminal.</summary>
    Completed,
    /// <summary>A definitive failure was persisted with a sanitized error; terminal.</summary>
    Failed,
}
