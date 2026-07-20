using CodeIsland.Core;
using CodeIsland.Ipc;
using CodeIsland.Windows;
using CodeIsland.Bluetooth;
using System.IO.Compression;

var appIconPath = Path.Combine(AppContext.BaseDirectory, "source", "codeisland.png");
using (var appIcon = new System.Drawing.Bitmap(appIconPath))
{
    Require(appIcon.Width == 256 && appIcon.Height == 256 && appIcon.GetPixel(0, 0).A == 0,
        "Application pixel icon must be 256x256 with a transparent background.");
}
var appIcoPath = Path.Combine(AppContext.BaseDirectory, "source", "codeisland.ico");
using (var appIco = new System.Drawing.Icon(appIcoPath))
    Require(appIco.Width == 256 && appIco.Height == 256, "Application ICO must contain the 256px pixel icon.");
using (var trayMenu = TrayMenuFactory.Create())
{
    trayMenu.Items.Add("Open panel");
    Require(trayMenu.BackColor == System.Drawing.Color.FromArgb(8, 8, 9)
            && trayMenu.ForeColor == System.Drawing.Color.FromArgb(232, 232, 235),
        "Tray menu must use the black CodeIsland palette.");
    Require(trayMenu.Items[0].Height == 34 && !trayMenu.ShowImageMargin,
        "Tray menu items must use the compact pixel-theme layout.");
}
Console.WriteLine("SMOKE PASS: transparent app icon and pixel-theme tray menu verified.");

var statusConverter = new SessionStatusTextConverter();
Require((string)statusConverter.Convert(null, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture)
        == "CODEISLAND 0",
    "Collapsed idle panel must show CODEISLAND 0 instead of an active-session status.");
Console.WriteLine("SMOKE PASS: collapsed idle status text verified.");

var codexGifPath = Path.Combine(AppContext.BaseDirectory, "source", "codex.gif");
using (var codexGif = System.Drawing.Image.FromFile(codexGifPath))
{
    Require(codexGif.Width == 32 && codexGif.Height == 32,
        "Codex pet GIF must remain at its native 32x32 size.");
    Require(codexGif.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time) == 6,
        "Codex pet GIF must contain six animation frames.");
}
var codexExpandedGifPath = Path.Combine(AppContext.BaseDirectory, "source", "codex-expanded.gif");
using (var codexExpandedGif = new System.Drawing.Bitmap(codexExpandedGifPath))
{
    Require(codexExpandedGif.Width == 32 && codexExpandedGif.Height == 32,
        "Expanded Codex pet GIF must remain at its native 32x32 size.");
    Require(codexExpandedGif.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time) == 6,
        "Expanded Codex pet GIF must contain six animation frames.");
    var background = codexExpandedGif.GetPixel(0, 0);
    Require(background.R == 13 && background.G == 13 && background.B == 14,
        "Expanded Codex pet GIF must match the #0D0D0E session-card background.");
}
Console.WriteLine("SMOKE PASS: opaque 32x32 six-frame Codex pet GIF verified.");

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
Require(store.CurrentSession?.SessionId == request.SessionId,
    "Collapsed panel must expose the current active session.");

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
    var exported = Path.Combine(settingsRoot, "exported.json");
    settingsStore.Export(exported, loaded);
    Require(SettingsStore.Import(exported) == loaded, "Exported settings must import without data loss.");
    File.WriteAllText(settingsStore.FilePath, "{broken");
    Require(settingsStore.Load() == new AppSettings(), "Malformed settings must fall back to defaults.");
    var invalidImportRejected = false;
    try { SettingsStore.Import(settingsStore.FilePath); }
    catch (InvalidDataException) { invalidImportRejected = true; }
    Require(invalidImportRejected, "Malformed imported settings must be rejected.");
    Console.WriteLine("SMOKE PASS: settings round-trip, validation, fallback and localization verified.");
}
finally
{
    if (Directory.Exists(settingsRoot)) Directory.Delete(settingsRoot, true);
}

var locatorRoot = Path.Combine(Path.GetTempPath(), $"codeisland-locator-{Guid.NewGuid():N}");
try
{
    var appDirectory = Path.Combine(locatorRoot, "app");
    Directory.CreateDirectory(appDirectory);
    var packagedBridge = Path.Combine(appDirectory, "CodeIsland.Bridge.exe");
    File.WriteAllText(packagedBridge, "bridge");
    Require(BridgeLocator.Find(appDirectory) == packagedBridge, "Packaged Bridge must be located beside the app.");
    File.Delete(packagedBridge);
    var developmentBridge = Path.Combine(locatorRoot, "CodeIsland.Bridge", "bin", "Debug", "net8.0", "CodeIsland.Bridge.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(developmentBridge)!);
    File.WriteAllText(developmentBridge, "bridge");
    Require(BridgeLocator.Find(appDirectory, locatorRoot) == developmentBridge,
        "Development Bridge must be found from the repository root.");
    Console.WriteLine("SMOKE PASS: packaged and development Bridge location verified.");
}
finally
{
    if (Directory.Exists(locatorRoot)) Directory.Delete(locatorRoot, true);
}
var startupExecutable = Path.Combine(Path.GetTempPath(), "CodeIsland", "CodeIsland.Windows.exe");
Require(StartupRegistration.BuildCommand(startupExecutable) == $"\"{Path.GetFullPath(startupExecutable)}\"",
    "Startup command must quote and normalize the executable path.");
Console.WriteLine("SMOKE PASS: startup command generation verified without registry mutation.");
Require(HotKeyBinding.TryParse("ctrl+shift+i", out var parsedHotKey), "Valid shortcut must parse.");
Require(parsedHotKey.ToString() == "Ctrl+Shift+I", "Shortcut must normalize modifier and key casing.");
Require(!HotKeyBinding.TryParse("Ctrl+Shift", out _), "Shortcut without a key must be rejected.");
var normalizedSettings = new AppSettings { ToggleShortcut = "Ctrl+Shift+I", ApproveShortcut = "Ctrl+Shift+I" }.Validate();
Require(normalizedSettings.ApproveShortcut == "Ctrl+Shift+I", "Settings validation must preserve valid shortcut syntax.");
Console.WriteLine("SMOKE PASS: shortcut parsing and normalization verified.");
Require(WindowMatcher.TitleMatchesWorkingDirectory("PowerShell - CodexStatus", @"E:\Demo\CodexStatus"),
    "Window title must match the working-directory leaf name.");
Require(!WindowMatcher.TitleMatchesWorkingDirectory("PowerShell - Other", @"E:\Demo\CodexStatus"),
    "Unrelated window title must not match.");
var processStore = new DesktopSessionStore();
processStore.Apply(new AgentEvent("process-1", "process-session", AgentKind.Codex,
    AgentEventType.SessionStart, DateTimeOffset.UtcNow, @"E:\Demo\CodexStatus", ProcessId: 4321,
    TerminalKind: "windows-terminal"));
Require(processStore.Sessions.Single().ProcessId == 4321
        && processStore.Sessions.Single().TerminalKind == "windows-terminal",
    "Process and terminal metadata must propagate to the session snapshot.");
Console.WriteLine("SMOKE PASS: window matching and process metadata propagation verified.");
var launcherRoot = Path.Combine(Path.GetTempPath(), $"codeisland-launcher-{Guid.NewGuid():N}");
try
{
    var launcherBin = Path.Combine(launcherRoot, "bin");
    var workspace = Path.Combine(launcherRoot, "workspace");
    Directory.CreateDirectory(launcherBin);
    Directory.CreateDirectory(workspace);
    File.WriteAllText(Path.Combine(launcherBin, "cursor.cmd"), "@echo off");
    File.WriteAllText(Path.Combine(launcherBin, "wt.exe"), "binary");
    var launcher = new WorkspaceLauncher();
    var cursorTarget = launcher.Resolve(AgentKind.Cursor, null, workspace, launcherBin);
    Require(cursorTarget?.Executable.EndsWith("cursor.cmd", StringComparison.OrdinalIgnoreCase) == true,
        "Cursor sessions must prefer the Cursor launcher.");
    var terminalTarget = launcher.Resolve(AgentKind.Codex, "windows-terminal", workspace, launcherBin);
    Require(terminalTarget?.Executable.EndsWith("wt.exe", StringComparison.OrdinalIgnoreCase) == true
            && terminalTarget.Arguments.StartsWith("-d ", StringComparison.Ordinal),
        "Terminal sessions must prefer Windows Terminal with a working-directory argument.");
    var codexDesktopTarget = launcher.ResolveConversation(AgentKind.Codex, "codex-desktop",
        "00000000-0000-4000-8000-000000000001");
    Require(codexDesktopTarget?.Executable ==
            "codex://threads/00000000-0000-4000-8000-000000000001",
        "Codex Desktop sessions must resolve to the exact thread deep link.");
Console.WriteLine("SMOKE PASS: Cursor, terminal and Codex Desktop thread launch resolution verified.");
}
finally
{
    if (Directory.Exists(launcherRoot)) Directory.Delete(launcherRoot, true);
}
var agentFrame = BuddyProtocol.EncodeAgent(AgentKind.Codex, SessionState.WaitingForPermission, "shell");
Require(agentFrame.SequenceEqual(new byte[] { 1, 3, 5, (byte)'s', (byte)'h', (byte)'e', (byte)'l', (byte)'l' }),
    "Buddy agent frame must match the upstream byte layout.");
var pairFrame = BuddyProtocol.EncodePairRequest(new byte[] { 1, 2, 3, 4, 5, 6 });
Require(pairFrame.SequenceEqual(new byte[] { 0xE0, 1, 2, 3, 4, 5, 6 }),
    "Buddy pair request must contain marker and six-byte host id.");
Require(BuddyProtocol.EncodeBrightness(500).SequenceEqual(new byte[] { 0xFE, 100 }),
    "Buddy brightness must clamp to 100 percent.");
Require(BuddyProtocol.DecodeUplink(new byte[] { 0xF0 }) is { Kind: BuddyUplinkKind.ControlCommand, Value: 0xF0 },
    "Buddy approve opcode must decode as a control command.");
Require(BuddyProtocol.DecodeUplink(new byte[] { 0xE2 }) is { Kind: BuddyUplinkKind.PairResponse },
    "Buddy pending opcode must decode as a pair response.");
Console.WriteLine("SMOKE PASS: Buddy agent, pairing, brightness and uplink frames verified.");
var diagnosticsRoot = Path.Combine(Path.GetTempPath(), $"codeisland-diagnostics-{Guid.NewGuid():N}");
try
{
    var blockedLogRoot = Path.Combine(diagnosticsRoot, "blocked-log-root");
    Directory.CreateDirectory(diagnosticsRoot);
    File.WriteAllText(blockedLogRoot, "not a directory");
    var fallbackLogger = new AppLogger(blockedLogRoot);
    fallbackLogger.Info("fallback logger must not fail application startup");
    Require(!string.Equals(fallbackLogger.LogDirectory, blockedLogRoot, StringComparison.OrdinalIgnoreCase),
        "Logger must fall back when the configured log directory is unavailable.");
    var logRoot = Path.Combine(diagnosticsRoot, "logs");
    var logger = new AppLogger(logRoot, maxBytes: 80);
    logger.Info("Bearer abcdefghijklmnop sk-abcdefghijklmnop "
                + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    logger.Info(new string('x', 100));
    logger.Info("rotation trigger");
    Require(Directory.GetFiles(logRoot, "codeisland.log*").Length >= 2, "Logger must rotate oversized files.");
    var diagnosticsZip = Path.Combine(diagnosticsRoot, "diagnostics.zip");
    new DiagnosticsExporter().Export(diagnosticsZip, new AppSettings(), store, logRoot);
    using var diagnosticsArchive = ZipFile.OpenRead(diagnosticsZip);
    Require(diagnosticsArchive.GetEntry("diagnostics.json") is not null, "Diagnostics archive must contain a summary.");
    Require(diagnosticsArchive.Entries.Any(entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal)),
        "Diagnostics archive must contain logs.");
    var diagnosticText = string.Join('\n', diagnosticsArchive.Entries.Select(entry =>
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }));
    Require(!diagnosticText.Contains("abcdefghijklmnop", StringComparison.Ordinal),
        "Diagnostics must redact API keys and bearer tokens.");
Console.WriteLine("SMOKE PASS: rolling logs, diagnostics ZIP and secret redaction verified.");
}
finally
{
    if (Directory.Exists(diagnosticsRoot)) Directory.Delete(diagnosticsRoot, true);
}
var displayPosition = DisplayPositioner.TopCenter(new System.Drawing.Rectangle(1920, 0, 1920, 1080),
    dpiScaleX: 1.5, dpiScaleY: 1.5, widthDip: 560);
Require(Math.Abs(displayPosition.Left - 1640) < 0.01 && Math.Abs(displayPosition.Top - 14) < 0.01,
    "Panel positioning must convert the selected display from pixels to DIPs.");
Require(new AppSettings { DisplayMode = "invalid" }.Validate().DisplayMode == "primary",
    "Invalid display mode must fall back to primary.");
Console.WriteLine("SMOKE PASS: multi-display DPI positioning and settings validation verified.");
var workArea = new System.Windows.Rect(0, 0, 1920, 1080);
var topDock = PanelDocking.Resolve(workArea, new System.Windows.Size(780, 300),
    new System.Windows.Point(570, 7));
Require(topDock.Edges == DockEdges.Top && topDock.Top == 0,
    "Panel near the top edge must snap flush to the screen.");
Require(topDock.Corners.TopLeft == 0 && topDock.Corners.TopRight == 0,
    "Top-docked black panel must remain flat behind its curved shoulders.");
var bottomRightDock = PanelDocking.Resolve(workArea, new System.Windows.Size(400, 64),
    new System.Windows.Point(1518, 1018));
Require(bottomRightDock.Edges == (DockEdges.Right | DockEdges.Bottom)
        && bottomRightDock.Left == 1520 && bottomRightDock.Top == 1016,
    "Collapsed panel must snap exactly into a screen corner.");
Require(bottomRightDock.Corners.TopLeft > 0 && bottomRightDock.Corners.TopRight == 0
        && bottomRightDock.Corners.BottomLeft == 0 && bottomRightDock.Corners.BottomRight == 0,
    "Corner-docked black panel must leave its screen-facing corners flat.");
var shoulders = DockShoulderGeometry.Create(new System.Windows.Size(780, 160), DockEdges.Top);
Require(!shoulders.First.Bounds.IsEmpty && !shoulders.Second.Bounds.IsEmpty
        && shoulders.First.Bounds.Left == 0 && shoulders.Second.Bounds.Right == 780,
    "Top docking must create curved shoulders at both screen connections.");
var collapsedCenter = new System.Windows.Point(340 + 320d / 2, 0 + 40d / 2);
var expandedFromCenter = PanelDocking.Place(workArea, new System.Windows.Size(780, 300),
    new System.Windows.Point(collapsedCenter.X - 780d / 2, collapsedCenter.Y - 300d / 2), DockEdges.Top);
Require(Math.Abs((expandedFromCenter.Left + 780d / 2) - collapsedCenter.X) < .01,
    "Top-docked expand must preserve the collapsed panel's horizontal center.");
Console.WriteLine("SMOKE PASS: four-edge snapping and seamless dock corner geometry verified.");
var monitorBounds = new System.Drawing.Rectangle(0, 0, 1920, 1080);
Require(FullscreenDetector.IsSameBounds(new System.Drawing.Rectangle(0, 0, 1920, 1080), monitorBounds),
    "A window covering the monitor must be detected as full screen.");
Require(!FullscreenDetector.IsSameBounds(new System.Drawing.Rectangle(0, 0, 1920, 1040), monitorBounds),
    "A window leaving taskbar space must not be detected as full screen.");
Console.WriteLine("SMOKE PASS: full-screen monitor bounds detection verified.");
var filterSession = new SessionSnapshot("filter", AgentKind.Codex, SessionState.Running,
    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null);
Require(SessionFilter.IsVisible(filterSession, "all"), "All filter must include active CLI sessions.");
Require(SessionFilter.IsVisible(filterSession, "active"), "Active filter must include running sessions.");
Require(!SessionFilter.IsVisible(filterSession with { State = SessionState.Completed }, "active"),
    "Active filter must exclude completed sessions.");
Require(SessionFilter.IsVisible(filterSession, "cli"), "CLI filter must include known CLI agents.");
Require(!SessionFilter.IsVisible(filterSession with { Agent = AgentKind.Unknown }, "cli"),
    "CLI filter must exclude unknown sources.");
Console.WriteLine("SMOKE PASS: all, active and CLI session filters verified.");
var transcriptRoot = Path.Combine(Path.GetTempPath(), $"codeisland-transcript-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(transcriptRoot);
    var transcript = Path.Combine(transcriptRoot, "rollout-test.jsonl");
    File.WriteAllLines(transcript,
    [
        "{\"timestamp\":\"2026-07-17T08:00:00Z\",\"type\":\"session_meta\",\"payload\":{\"session_id\":\"live-session\",\"cwd\":\"E:\\\\Demo\\\\CodexStatus\"}}",
        "{\"timestamp\":\"2026-07-17T08:00:01Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}"
    ]);
    var transcriptEvents = new List<AgentEvent>();
    using var liveSignal = new ManualResetEventSlim();
    using var tailer = new CodexSessionTailer(transcriptRoot);
    tailer.EventReceived += (_, agentEvent) =>
    {
        lock (transcriptEvents) transcriptEvents.Add(agentEvent);
        if (agentEvent.ToolName == "shell") liveSignal.Set();
    };
    tailer.Start(TimeSpan.FromDays(1));
    lock (transcriptEvents)
        Require(transcriptEvents.Any(value => value.SessionId == "live-session"),
            "Tailer startup must recover an existing active Codex session.");
    File.AppendAllText(transcript, Environment.NewLine
        + "{\"timestamp\":\"2026-07-17T08:00:02Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"custom_tool_call\",\"call_id\":\"call-1\",\"name\":\"shell\"}}"
        + Environment.NewLine);
    Require(liveSignal.Wait(TimeSpan.FromSeconds(5)), "Tailer must emit events appended after startup.");
    Console.WriteLine("SMOKE PASS: Codex active-session recovery and live JSONL tailing verified.");
}
finally
{
    if (Directory.Exists(transcriptRoot)) Directory.Delete(transcriptRoot, true);
}
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
