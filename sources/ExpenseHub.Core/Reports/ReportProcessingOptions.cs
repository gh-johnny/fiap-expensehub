using System;

namespace ExpenseHub.Core.Reports;

/// <summary>Worker settings bound from the <c>ReportProcessing</c> configuration section.</summary>
public sealed class ReportProcessingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ReportProcessing";

    /// <summary>Gets or sets how many processing starts a request may consume before it is marked Failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Gets or sets the pause before a transiently failed message is returned to the queue.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how many unacknowledged messages the worker holds at once.</summary>
    public ushort Prefetch { get; set; } = 1;
}
