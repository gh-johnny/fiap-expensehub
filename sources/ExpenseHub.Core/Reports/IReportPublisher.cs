using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExpenseHub.Core.Reports;

/// <summary>Hands a persisted request to the broker and waits for its confirmation.</summary>
public interface IReportPublisher
{
    /// <summary>Publishes the request identifier as a persistent message and returns only after the broker confirms it.</summary>
    /// <param name="reportId">The persisted request identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A task completed after the broker confirmation.</returns>
    /// <exception cref="ReportPublishException">The broker did not confirm the message.</exception>
    Task PublishAsync(Guid reportId, CancellationToken cancellationToken);
}

/// <summary>The broker did not confirm a publication; the outcome of the message is unknown or negative.</summary>
public sealed class ReportPublishException : Exception
{
    /// <summary>Initializes the exception without exposing broker details.</summary>
    public ReportPublishException()
        : base("The report message was not confirmed by the broker.")
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">A sanitized message.</param>
    public ReportPublishException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception wrapping the broker failure for logs only.</summary>
    /// <param name="message">A sanitized message.</param>
    /// <param name="innerException">The broker failure.</param>
    public ReportPublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
