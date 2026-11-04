using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Reports;

namespace ExpenseHub.UnitTests.TestDoubles;

/// <summary>Records confirmed publications or simulates an unconfirmed broker.</summary>
internal sealed class ReportPublisherFake : IReportPublisher
{
    public List<Guid> Published { get; } = new();

    public bool Unconfirmed { get; set; }

    public Task PublishAsync(Guid reportId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Unconfirmed)
        {
            throw new ReportPublishException();
        }

        Published.Add(reportId);
        return Task.CompletedTask;
    }
}
