using System;
using System.Text.Json;

namespace ExpenseHub.Core.Messaging;

/// <summary>
/// The minimal message contract: only the report identifier. Period, scope and requester are always
/// reloaded from the persisted request, never trusted from the message.
/// </summary>
/// <param name="ReportId">The persisted request identifier.</param>
public sealed record ReportRequestedMessage(Guid ReportId)
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    /// <summary>Gets the message type sent in the AMQP properties.</summary>
    public static string MessageType => "expensehub.report.requested";

    /// <summary>Serializes the message body as UTF-8 JSON, for example <c>{"reportId":"…"}</c>.</summary>
    /// <returns>The body bytes.</returns>
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, _options);

    /// <summary>Parses a body, rejecting malformed JSON and empty identifiers.</summary>
    /// <param name="body">The received body.</param>
    /// <param name="message">The parsed message.</param>
    /// <returns>Whether the body is a valid message.</returns>
    public static bool TryParse(ReadOnlySpan<byte> body, out ReportRequestedMessage? message)
    {
        message = null;
        try
        {
            message = JsonSerializer.Deserialize<ReportRequestedMessage>(body, _options);
        }
        catch (JsonException)
        {
            return false;
        }

        return message is not null && message.ReportId != Guid.Empty;
    }
}
