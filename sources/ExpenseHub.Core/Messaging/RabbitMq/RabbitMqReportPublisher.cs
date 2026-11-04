using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Reports;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ExpenseHub.Core.Messaging.RabbitMq;

/// <summary>
/// Publishes persistent, mandatory messages on a channel with publisher confirmations: the call completes only after
/// the broker acknowledges the message, and fails when it is nacked, returned as unroutable or times out.
/// </summary>
public sealed class RabbitMqReportPublisher : IReportPublisher
{
    private readonly RabbitMqConnectionProvider _connections;
    private readonly RabbitMqOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Initializes the publisher.</summary>
    /// <param name="connections">The shared connection.</param>
    /// <param name="options">The broker settings.</param>
    /// <param name="clock">The server clock used for the message timestamp.</param>
    public RabbitMqReportPublisher(RabbitMqConnectionProvider connections, IOptions<RabbitMqOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connections = connections;
        _options = options.Value;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task PublishAsync(Guid reportId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PublishTimeout);
        try
        {
            // Name resolution of a stopped broker ignores cancellation; WaitAsync bounds the wait, and the cancelled
            // token stops every later step, so a late connection can never publish after a 503.
            IConnection connection = await _connections.GetConnectionAsync(timeout.Token).WaitAsync(timeout.Token);
            await using IChannel channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                timeout.Token);
            await _connections.DeclareReportQueueAsync(channel, timeout.Token);
            string id = reportId.ToString("D", CultureInfo.InvariantCulture);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = id,
                CorrelationId = id,
                Type = ReportRequestedMessage.MessageType,
                Timestamp = new AmqpTimestamp(_clock.GetUtcNow().ToUnixTimeSeconds()),
            };
            await channel.BasicPublishAsync(string.Empty, _options.ReportQueue, mandatory: true, properties,
                new ReportRequestedMessage(reportId).ToBytes(), timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReportPublishException("The broker did not confirm the report message in time.", exception);
        }
        catch (Exception exception) when (exception is PublishException or BrokerUnreachableException
            or OperationInterruptedException or AlreadyClosedException or ConnectFailureException)
        {
            throw new ReportPublishException("The broker did not confirm the report message.", exception);
        }
    }
}
