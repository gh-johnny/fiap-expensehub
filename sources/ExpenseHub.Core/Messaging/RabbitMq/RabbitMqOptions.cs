using System;

namespace ExpenseHub.Core.Messaging.RabbitMq;

/// <summary>Broker settings bound from the <c>RabbitMq</c> configuration section; credentials come from the environment.</summary>
public sealed class RabbitMqOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "RabbitMq";

    /// <summary>Gets or sets the broker host name.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Gets or sets the AMQP port.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Gets or sets the virtual host.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Gets or sets the user name.</summary>
    public string UserName { get; set; } = "guest";

    /// <summary>Gets or sets the credential, supplied only through configuration outside the repository.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the durable queue that carries report requests.</summary>
    public string ReportQueue { get; set; } = "expensehub.reports.requested";

    /// <summary>Gets or sets how long a publication waits for the broker confirmation.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
