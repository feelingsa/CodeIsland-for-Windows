using System.Text.Json;
using System.Text.Json.Serialization;
using CodeIsland.Core;
using CodeIsland.Ipc;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "serve";
if (mode == "serve")
{
    var machine = new SessionStateMachine();
    await using var server = CreateServer(machine);
    Console.WriteLine($"CodeIsland Bridge listening on {PipeEndpoint.Name()}");
    await server.RunAsync();
}
else if (mode == "send" && args.Length >= 2)
{
    var input = args[1] == "--stdin" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(args[1]);
    var agentEvent = ParseEvent(input);
    await using var client = new PipeClient();
    await client.ConnectWithRetryAsync(3, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(150));
    var response = await client.SendAsync(
        new PipeMessage(PipeMessageType.Event, Guid.NewGuid().ToString("N"), Event: agentEvent),
        TimeSpan.FromSeconds(3));
    Console.WriteLine(PipeJson.Serialize(response));
}
else if (mode == "self-test")
{
    using var stop = new CancellationTokenSource();
    var machine = new SessionStateMachine();
    await using var server = CreateServer(machine);
    var serverTask = server.RunAsync(stop.Token);
    await using var client = new PipeClient();
    await client.ConnectWithRetryAsync(10, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(50));

    await ExpectAck(client, new PipeMessage(PipeMessageType.Hello, "hello-1"));
    var testEvent = new AgentEvent("event-1", "session-1", AgentKind.Codex,
        AgentEventType.SessionStart, DateTimeOffset.UtcNow, Environment.CurrentDirectory, "IPC self-test");
    var serializedEvent = JsonSerializer.Serialize(testEvent, CreateEventJsonOptions());
    testEvent = ParseEvent(serializedEvent);
    await ExpectAck(client, new PipeMessage(PipeMessageType.Event, "message-1", Event: testEvent));
    await ExpectAck(client, new PipeMessage(PipeMessageType.Heartbeat, "heartbeat-1"));

    if (!machine.TryGet("session-1", out var snapshot) || snapshot?.State != SessionState.Running)
        throw new InvalidOperationException("Event did not reach the session state machine.");
    Console.WriteLine("SELF-TEST PASS: handshake, event acknowledgement, heartbeat and state update verified.");
    await stop.CancelAsync();
    await serverTask;
}
else
{
    Console.Error.WriteLine("Usage: codeisland-bridge [serve | send <event.json> | self-test]");
    return 2;
}

return 0;

static PipeServer CreateServer(SessionStateMachine machine) => new((message, _) =>
{
    var response = message.Type switch
    {
        PipeMessageType.Hello or PipeMessageType.Heartbeat =>
            new PipeMessage(PipeMessageType.Ack, Guid.NewGuid().ToString("N"), AckFor: message.MessageId),
        PipeMessageType.Event when message.Event is not null =>
            new PipeMessage(PipeMessageType.Ack, Guid.NewGuid().ToString("N"), AckFor: message.MessageId),
        _ => new PipeMessage(PipeMessageType.Error, Guid.NewGuid().ToString("N"), Error: "Unsupported message.")
    };
    if (message.Type == PipeMessageType.Event && message.Event is not null) machine.Apply(message.Event);
    return ValueTask.FromResult<PipeMessage?>(response);
});

static async Task ExpectAck(PipeClient client, PipeMessage message)
{
    var response = await client.SendAsync(message, TimeSpan.FromSeconds(2));
    if (response.Type != PipeMessageType.Ack || response.AckFor != message.MessageId)
        throw new InvalidOperationException($"Expected ACK for {message.MessageId}.");
}

static AgentEvent ParseEvent(string json) =>
    JsonSerializer.Deserialize<AgentEvent>(json, CreateEventJsonOptions())
    ?? throw new InvalidOperationException("Input event JSON is empty.");

static JsonSerializerOptions CreateEventJsonOptions() => new(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
};
