using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ExpenseHub.Core.Messaging.RabbitMq;

/// <summary>
/// Owns one long-lived, automatically recovering AMQP connection per process, created on first use so the host
/// starts even while the broker is still booting.
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly string _clientName;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    /// <summary>Initializes the provider.</summary>
    /// <param name="options">The broker settings.</param>
    /// <param name="clientName">The name shown in the broker management UI.</param>
    public RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options, string clientName)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _clientName = clientName;
    }

    /// <summary>Gets the open connection, connecting on first use.</summary>
    /// <param name="cancellationToken">Cancellation of the connection attempt.</param>
    /// <returns>The shared connection.</returns>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                // Bounds a request when the broker is down; the default would hold the HTTP call for 30 s.
                RequestedConnectionTimeout = _options.PublishTimeout,
                ClientProvidedName = _clientName,
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Declares the durable quorum queue; idempotent, so both hosts can call it safely.</summary>
    /// <param name="channel">An open channel.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A task representing the declaration.</returns>
    public Task DeclareReportQueueAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.QueueDeclareAsync(_options.ReportQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
