using System.Text.Json.Serialization;
using CodeIsland.Core;

namespace CodeIsland.Ipc;

public enum PipeMessageType
{
    Hello,
    Event,
    Ack,
    Heartbeat,
    Error
}

public sealed record PipeMessage(
    [property: JsonPropertyName("type")] PipeMessageType Type,
    [property: JsonPropertyName("messageId")] string MessageId,
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion = 1,
    [property: JsonPropertyName("event")] AgentEvent? Event = null,
    [property: JsonPropertyName("ackFor")] string? AckFor = null,
    [property: JsonPropertyName("error")] string? Error = null);
