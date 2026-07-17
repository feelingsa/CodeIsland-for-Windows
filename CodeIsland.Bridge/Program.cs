using System.Text.Json;
using CodeIsland.Core;
using CodeIsland.Ipc;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "serve";
if (mode == "serve")
{
    var machine = new SessionStateMachine();
    await using var server = new PipeServer((message, _) =>
    {
        var response = message.Type switch
        {
            PipeMessageType.Hello or PipeMessageType.Heartbeat =>
                new PipeMessage(PipeMessageType.Ack, Guid.NewGuid().ToString("N"), AckFor: message.MessageId),
            PipeMessageType.Event when message.Event is not null =>
                new PipeMessage(PipeMessageType.Ack, Guid.NewGuid().ToString("N"), AckFor: message.MessageId),
            _ => new PipeMessage(PipeMessageType.Error, Guid.NewGuid().ToString("N"), Error: "Unsupported message.")
        };
        if (message.Type == PipeMessageType.Event && message.Event is not null)
            machine.Apply(message.Event);
        return ValueTask.FromResult<PipeMessage?>(response);
    });
    Console.WriteLine($"CodeIsland Bridge listening on {PipeEndpoint.Name()}");
    await server.RunAsync();
}
else if (mode == "send" && args.Length >= 2)
{
    var agentEvent = JsonSerializer.Deserialize<AgentEvent>(await File.ReadAllTextAsync(args[1]))
        ?? throw new InvalidOperationException("Input event JSON is empty.");
    await using var client = new PipeClient();
    await client.ConnectAsync(TimeSpan.FromSeconds(3));
    var response = await client.SendAsync(
        new PipeMessage(PipeMessageType.Event, Guid.NewGuid().ToString("N"), Event: agentEvent),
        TimeSpan.FromSeconds(3));
    Console.WriteLine(PipeJson.Serialize(response));
}
else
{
    Console.Error.WriteLine("Usage: codeisland-bridge [serve | send <event.json>]");
    return 2;
}

return 0;
