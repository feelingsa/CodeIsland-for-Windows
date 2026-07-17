using CodeIsland.Core;
using CodeIsland.Ipc;
using CodeIsland.Windows;

var store = new DesktopSessionStore();
var request = new AgentEvent(
    "permission-1", "session-1", AgentKind.Codex, AgentEventType.PermissionRequest,
    DateTimeOffset.UtcNow, @"E:\repo", "Run tests", "Allow dotnet test?", "shell");
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var pending = store.WaitForResponseAsync(request, stop.Token);

Require(!pending.IsCompleted, "Permission request must wait for user action.");
Require(store.PendingCount == 1, "Pending request must be tracked.");
Require(store.Sessions.Single().State == SessionState.WaitingForPermission,
    "Session must enter WaitingForPermission state.");
Require(store.Resolve(request.EventId, UserAction.Approve), "Approve action must resolve the request.");

var response = await pending;
Require(response.Type == PipeMessageType.ActionResponse, "Response type must be ActionResponse.");
Require(response.Action == UserAction.Approve, "Response must preserve the user action.");
Require(response.AckFor == request.EventId, "Response must target the pending event.");
Require(store.PendingCount == 0, "Resolved request must be removed.");

Console.WriteLine("SMOKE PASS: permission request, pending state, approve action and response payload verified.");
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
