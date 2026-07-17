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
Require(store.Sessions.Single().State == SessionState.Running,
    "Resolved permission request must return the session to Running.");

var denyRequest = request with { EventId = "permission-2" };
var denyPending = store.WaitForResponseAsync(denyRequest, stop.Token);
Require(store.ResolveCurrent(UserAction.Deny), "Current pending request must be selectable for deny.");
Require((await denyPending).Action == UserAction.Deny, "Current request deny action must be returned.");

Console.WriteLine("SMOKE PASS: permission request, pending state, approve action and response payload verified.");

var question = request with { EventId = "question-1", Type = AgentEventType.Question, Text = "Which environment?" };
var questionPending = store.WaitForResponseAsync(question, stop.Token);
Require(store.Sessions.Single().State == SessionState.WaitingForAnswer, "Session must enter WaitingForAnswer state.");
Require(store.Resolve(question.EventId, UserAction.Answer, "staging"), "Answer action must resolve the question.");
var answer = await questionPending;
Require(answer.Action == UserAction.Answer && answer.ResponseText == "staging",
    "Answer response must preserve user text.");
Console.WriteLine("SMOKE PASS: question request and text answer payload verified.");

var bounded = new DesktopSessionStore(maxVisibleSessions: 2, historyLimit: 2);
for (var i = 1; i <= 3; i++)
{
    bounded.Apply(new AgentEvent($"history-{i}", $"bounded-{i}", AgentKind.Claude,
        AgentEventType.SessionStart, DateTimeOffset.UtcNow.AddMinutes(-i)));
}
Require(bounded.Sessions.Count == 2, "Visible sessions must respect the configured maximum.");
Require(bounded.EventHistory.Count == 2, "Event history must respect the configured maximum.");
var removed = bounded.RemoveExpired(DateTimeOffset.UtcNow.AddMinutes(-1.5));
Require(removed == 2, "Expired session cleanup must remove sessions older than the cutoff.");
Require(bounded.RemoveSession("bounded-1"), "Visible session must support explicit removal.");
Require(bounded.SessionCount == 0, "Explicit removal must update the visible collection.");
Console.WriteLine("SMOKE PASS: state recovery, bounded history, visible limit and expiry cleanup verified.");

Require(NotificationSoundManager.CueFor(AgentEventType.SessionStart) == NotificationCue.Start,
    "Session start must map to the start cue.");
Require(NotificationSoundManager.CueFor(AgentEventType.PermissionRequest) == NotificationCue.Approval,
    "Permission request must map to the approval cue.");
Require(NotificationSoundManager.CueFor(AgentEventType.SessionEnd) == NotificationCue.Complete,
    "Session end must map to the completion cue.");
Require(NotificationSoundManager.CueFor(AgentEventType.Error) == NotificationCue.Error,
    "Error must map to the error cue.");
Console.WriteLine("SMOKE PASS: notification sound event mapping verified.");

var settingsRoot = Path.Combine(Path.GetTempPath(), $"codeisland-settings-{Guid.NewGuid():N}");
try
{
    var settingsStore = new SettingsStore(settingsRoot);
    settingsStore.Save(new AppSettings
    {
        Language = "zh-CN",
        SoundEnabled = false,
        MaxVisibleSessions = 99,
        SessionCleanupMinutes = 0
    });
    var loaded = settingsStore.Load();
    Require(loaded.Language == "zh-CN" && !loaded.SoundEnabled, "Settings must round-trip.");
    Require(loaded.MaxVisibleSessions == 20 && loaded.SessionCleanupMinutes == 1,
        "Numeric settings must be clamped to supported ranges.");
    Require(L10n.Get("ApproveText", loaded.Language) == "允许", "Chinese resources must resolve.");
    Require(L10n.Get("ApproveText", "en-US") == "Approve", "English resources must resolve.");
    File.WriteAllText(settingsStore.FilePath, "{broken");
    Require(settingsStore.Load() == new AppSettings(), "Malformed settings must fall back to defaults.");
    Console.WriteLine("SMOKE PASS: settings round-trip, validation, fallback and localization verified.");
}
finally
{
    if (Directory.Exists(settingsRoot)) Directory.Delete(settingsRoot, true);
}
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
