using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeIsland.Core;
using CodeIsland.Ipc;

var realCodex = FindRealCodex();
if (realCodex is null)
{
    Console.Error.WriteLine("CodeIsland could not find the Codex executable bundled with the VS Code extension.");
    return 127;
}

var startInfo = new ProcessStartInfo(realCodex)
{
    UseShellExecute = false,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    StandardInputEncoding = new UTF8Encoding(false),
    StandardOutputEncoding = new UTF8Encoding(false),
    StandardErrorEncoding = new UTF8Encoding(false)
};
foreach (var argument in args) startInfo.ArgumentList.Add(argument);

using var codex = Process.Start(startInfo)
    ?? throw new InvalidOperationException("Could not start the Codex executable.");
using var stop = new CancellationTokenSource();
var stdinLock = new SemaphoreSlim(1, 1);

var parentInput = ForwardParentInputAsync(Console.In, codex.StandardInput, stdinLock, stop.Token);
var childError = ForwardLinesAsync(codex.StandardError, Console.Error, stop.Token);
var childOutput = ForwardChildOutputAsync(codex.StandardOutput, Console.Out, codex.StandardInput, stdinLock, stop.Token);

await codex.WaitForExitAsync(stop.Token);
stop.Cancel();
await IgnoreCancellation(parentInput, childError, childOutput);
return codex.ExitCode;

static async Task ForwardParentInputAsync(TextReader input, StreamWriter output, SemaphoreSlim stdinLock,
    CancellationToken cancellationToken)
{
    try
    {
        string? line;
        while ((line = await input.ReadLineAsync(cancellationToken)) is not null)
        {
            await stdinLock.WaitAsync(cancellationToken);
            try { await output.WriteLineAsync(line); }
            finally { stdinLock.Release(); }
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        try { await output.FlushAsync(cancellationToken); }
        catch (OperationCanceledException) { }
    }
}

static async Task ForwardLinesAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
{
    try
    {
        string? line;
        while ((line = await input.ReadLineAsync(cancellationToken)) is not null)
            await output.WriteLineAsync(line);
    }
    catch (OperationCanceledException) { }
}

static async Task ForwardChildOutputAsync(TextReader input, TextWriter parentOutput, StreamWriter codexInput,
    SemaphoreSlim stdinLock, CancellationToken cancellationToken)
{
    try
    {
        string? line;
        while ((line = await input.ReadLineAsync(cancellationToken)) is not null)
        {
            var approval = TryParseCommandApproval(line);
            if (approval is null)
            {
                await parentOutput.WriteLineAsync(line);
                continue;
            }

            var response = await TryGetCodeIslandResponseAsync(approval.Event, cancellationToken);
            if (response is null)
            {
                // Preserve the normal VS Code UI when CodeIsland is unavailable.
                await parentOutput.WriteLineAsync(line);
                continue;
            }

            var result = approval.IsModern
                ? response.Action switch
                {
                    UserAction.Approve => "accept",
                    UserAction.AlwaysAllow => "acceptForSession",
                    _ => "decline"
                }
                : response.Action switch
                {
                    UserAction.Approve => "approved",
                    UserAction.AlwaysAllow => "approved_for_session",
                    _ => "denied"
                };
            var json = $"{{\"id\":{approval.JsonRpcId},\"result\":{{\"decision\":\"{result}\"}}}}";
            await stdinLock.WaitAsync(cancellationToken);
            try { await codexInput.WriteLineAsync(json); }
            finally { stdinLock.Release(); }
        }
    }
    catch (OperationCanceledException) { }
}

static CommandApproval? TryParseCommandApproval(string line)
{
    try
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("id", out var id)
            || !root.TryGetProperty("params", out var parameters)) return null;

        var method = methodElement.GetString();
        var modern = string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal);
        if (!modern && !string.Equals(method, "execCommandApproval", StringComparison.Ordinal)) return null;

        var sessionId = FirstString(parameters, modern ? "threadId" : "conversationId") ?? "vscode-codex";
        var command = modern ? FirstString(parameters, "command") : CommandArray(parameters);
        var reason = FirstString(parameters, "reason");
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { reason, command }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var eventId = $"vscode-codex-{id.GetRawText()}";
        var agentEvent = new AgentEvent(eventId, sessionId, AgentKind.Codex, AgentEventType.PermissionRequest,
            DateTimeOffset.UtcNow, FirstString(parameters, "cwd"), "VS Code Codex approval", text,
            "approval terminal", root.Clone());
        return new CommandApproval(id.GetRawText(), modern, agentEvent);
    }
    catch (JsonException)
    {
        return null;
    }
}

static async Task<PipeMessage?> TryGetCodeIslandResponseAsync(AgentEvent agentEvent, CancellationToken cancellationToken)
{
    try
    {
        await using var client = new PipeClient();
        await client.ConnectWithRetryAsync(3, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(150), cancellationToken);
        var response = await client.SendAsync(
            new PipeMessage(PipeMessageType.Event, Guid.NewGuid().ToString("N"), Event: agentEvent),
            TimeSpan.FromHours(8), cancellationToken);
        return response.Type == PipeMessageType.ActionResponse ? response : null;
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
    {
        return null;
    }
}

static string? FirstString(JsonElement value, string name) =>
    value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;

static string? CommandArray(JsonElement value)
{
    if (!value.TryGetProperty("command", out var command)) return null;
    return command.ValueKind switch
    {
        JsonValueKind.String => command.GetString(),
        JsonValueKind.Array => string.Join(" ", command.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString())),
        _ => null
    };
}

static string? FindRealCodex()
{
    var configured = Environment.GetEnvironmentVariable("CODEISLAND_REAL_CODEX_EXE");
    if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

    var userProfile = Environment.GetEnvironmentVariable("USERPROFILE")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var extensionRoot = Path.Combine(userProfile, ".vscode", "extensions");
    if (!Directory.Exists(extensionRoot)) return null;
    return Directory.EnumerateDirectories(extensionRoot, "openai.chatgpt-*")
        .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
        .Select(path => Path.Combine(path, "bin", "windows-x86_64", "codex.exe"))
        .FirstOrDefault(File.Exists);
}

static async Task IgnoreCancellation(params Task[] tasks)
{
    try { await Task.WhenAll(tasks); }
    catch (OperationCanceledException) { }
}

file sealed record CommandApproval(string JsonRpcId, bool IsModern, AgentEvent Event);
