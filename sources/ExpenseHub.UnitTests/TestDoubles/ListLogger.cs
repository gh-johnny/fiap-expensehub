using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.UnitTests.TestDoubles;

/// <summary>Captures structured log entries so tests can assert correlation fields and secret hygiene.</summary>
/// <typeparam name="T">The logging category.</typeparam>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public bool Enabled { get; set; } = true;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => Enabled;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (KeyValuePair<string, object?> pair in pairs)
            {
                values[pair.Key] = pair.Value;
            }
        }

        Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, values));
    }
}

/// <summary>One captured log entry.</summary>
/// <param name="Level">The severity.</param>
/// <param name="Message">The rendered message.</param>
/// <param name="Exception">The attached exception, if any.</param>
/// <param name="Values">The structured template values.</param>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Values);
