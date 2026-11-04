using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Messaging;
using ExpenseHub.Core.Messaging.RabbitMq;
using ExpenseHub.Core.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace ExpenseHub.Worker.Messaging;

/// <summary>
/// Consumes report requests with manual acknowledgements. A delivery is acknowledged only after
/// <see cref="ReportProcessor"/> persisted its outcome; transient failures are returned to the queue after a pause, and
/// a crash before the acknowledgement simply leads to a redelivery that the persisted state makes harmless.
/// </summary>
internal sealed partial class ReportConsumer : BackgroundService
{
    private static readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
    private readonly RabbitMqConnectionProvider _connections;
    private readonly IServiceScopeFactory _scopes;
    private readonly RabbitMqOptions _broker;
    private readonly ReportProcessingOptions _processing;
    private readonly ILogger<ReportConsumer> _logger;

    public ReportConsumer(RabbitMqConnectionProvider connections, IServiceScopeFactory scopes, IOptions<RabbitMqOptions> broker,
        IOptions<ReportProcessingOptions> processing, ILogger<ReportConsumer> logger)
    {
        _connections = connections;
        _scopes = scopes;
        _broker = broker.Value;
        _processing = processing.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IChannel? channel = null;
        while (channel is null && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                IConnection connection = await _connections.GetConnectionAsync(stoppingToken);
                channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            }
            catch (Exception exception) when (exception is BrokerUnreachableException or ConnectFailureException
                or OperationInterruptedException or AlreadyClosedException)
            {
                LogBrokerUnavailable(_reconnectDelay);
                await Task.Delay(_reconnectDelay, stoppingToken);
            }
        }

        if (channel is null)
        {
            return;
        }

        await using (channel)
        {
            await _connections.DeclareReportQueueAsync(channel, stoppingToken);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _processing.Prefetch, global: false, stoppingToken);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);
            await channel.BasicConsumeAsync(_broker.ReportQueue, autoAck: false, consumer, stoppingToken);
            LogConsuming(_broker.ReportQueue, _processing.Prefetch);
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                LogStopping();
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        if (!ReportRequestedMessage.TryParse(delivery.Body.Span, out ReportRequestedMessage? message))
        {
            LogInvalidMessage(delivery.BasicProperties.MessageId);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        Guid reportId = message!.ReportId;
        LogReceived(reportId, delivery.Redelivered);
        ReportProcessingOutcome outcome;
        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            outcome = await scope.ServiceProvider.GetRequiredService<ReportProcessor>().ProcessAsync(reportId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Not acknowledged: the broker redelivers it after the channel closes.
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Persisting the outcome itself failed: never acknowledge as if it had been stored.
            LogOutcomeNotPersisted(exception, reportId);
            outcome = ReportProcessingOutcome.Retry;
        }

        if (outcome == ReportProcessingOutcome.Retry)
        {
            await Task.Delay(_processing.RetryDelay, stoppingToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
            LogRequeued(reportId);
            return;
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        LogAcknowledged(reportId, outcome);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broker unavailable; retrying in {Delay}")]
    private partial void LogBrokerUnavailable(TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Queue} with prefetch {Prefetch}")]
    private partial void LogConsuming(string queue, ushort prefetch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker stopping; unacknowledged deliveries return to the queue")]
    private partial void LogStopping();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid message {MessageId} discarded")]
    private partial void LogInvalidMessage(string? messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Report {ReportId} received (redelivered: {Redelivered})")]
    private partial void LogReceived(Guid reportId, bool redelivered);

    [LoggerMessage(Level = LogLevel.Error, Message = "Report {ReportId} outcome could not be persisted; it will be retried")]
    private partial void LogOutcomeNotPersisted(Exception exception, Guid reportId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Report {ReportId} returned to the queue")]
    private partial void LogRequeued(Guid reportId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Report {ReportId} acknowledged as {Outcome}")]
    private partial void LogAcknowledged(Guid reportId, ReportProcessingOutcome outcome);
}
