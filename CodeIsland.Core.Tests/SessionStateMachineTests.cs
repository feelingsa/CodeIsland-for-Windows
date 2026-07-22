using CodeIsland.Core;

namespace CodeIsland.Core.Tests;

public sealed class SessionStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AppliesFullSessionLifecycle()
    {
        var machine = new SessionStateMachine();

        Assert.True(machine.Apply(Event("1", AgentEventType.SessionStart)));
        Assert.True(machine.Apply(Event("2", AgentEventType.ToolStart, tool: "shell")));
        Assert.True(machine.Apply(Event("3", AgentEventType.PermissionRequest, text: "Run command?")));
        Assert.True(machine.TryGet("session-1", out var waiting));
        Assert.Equal(SessionState.WaitingForPermission, waiting!.State);
        Assert.Equal("3", waiting.PendingEventId);
        Assert.Equal("shell", waiting.ActiveTool);

        Assert.True(machine.Apply(Event("4", AgentEventType.ToolEnd, tool: "shell")));
        Assert.True(machine.Apply(Event("5", AgentEventType.SessionEnd)));
        Assert.True(machine.TryGet("session-1", out var completed));
        Assert.Equal(SessionState.Completed, completed!.State);
        Assert.Null(completed.ActiveTool);
    }

    [Fact]
    public void DuplicateEventIsIgnored()
    {
        var machine = new SessionStateMachine();
        var value = Event("same", AgentEventType.SessionStart);

        Assert.True(machine.Apply(value));
        Assert.False(machine.Apply(value));
        Assert.Single(machine.Sessions);
    }

    [Fact]
    public void ErrorMarksSessionFailed()
    {
        var machine = new SessionStateMachine();
        machine.Apply(Event("1", AgentEventType.SessionStart));
        machine.Apply(Event("2", AgentEventType.Error, text: "process exited"));

        Assert.True(machine.TryGet("session-1", out var snapshot));
        Assert.Equal(SessionState.Failed, snapshot!.State);
        Assert.Equal("process exited", snapshot.Error);
    }

    [Fact]
    public void ApprovalActivityDoesNotOverwritePendingPermission()
    {
        var machine = new SessionStateMachine();
        machine.Apply(Event("1", AgentEventType.SessionStart));
        machine.Apply(Event("2", AgentEventType.PermissionRequest, text: "Allow command?"));
        machine.Apply(Event("3", AgentEventType.ToolStart, text: "VS Code requires approval.", tool: "approval terminal"));

        Assert.True(machine.TryGet("session-1", out var waiting));
        Assert.Equal(SessionState.WaitingForPermission, waiting!.State);
        Assert.Equal("2", waiting.PendingEventId);
        Assert.False(waiting.IsExecutingTool);
        Assert.Equal("VS Code requires approval.", waiting.LastMessage);

        Assert.True(machine.ResolvePending("session-1", "2", Now.AddSeconds(4)));
        Assert.Equal(SessionState.Running, machine.Sessions.Single().State);
    }

    [Fact]
    public void LateOlderTranscriptEventCannotOverwriteLiveState()
    {
        var machine = new SessionStateMachine();
        machine.Apply(Event("1", AgentEventType.SessionStart));
        machine.Apply(Event("3", AgentEventType.PermissionRequest, text: "Current approval"));
        machine.Apply(Event("2", AgentEventType.SessionStart, text: "Old session metadata"));

        Assert.True(machine.TryGet("session-1", out var snapshot));
        Assert.Equal(SessionState.WaitingForPermission, snapshot!.State);
        Assert.Equal("3", snapshot.PendingEventId);
        Assert.Equal("Current approval", snapshot.LastMessage);
    }

    [Fact]
    public void NewActivityRecoversFailedSessionAndClearsStaleError()
    {
        var machine = new SessionStateMachine();
        machine.Apply(Event("1", AgentEventType.SessionStart));
        machine.Apply(Event("2", AgentEventType.Error, text: "Codex task failed"));
        machine.Apply(Event("3", AgentEventType.SessionStart, text: "Codex task restarted"));

        Assert.True(machine.TryGet("session-1", out var restarted));
        Assert.Equal(SessionState.Running, restarted!.State);
        Assert.Null(restarted.Error);
        Assert.Equal("Codex task restarted", restarted.LastMessage);

        machine.Apply(Event("4", AgentEventType.Error, text: "interrupted again"));
        machine.Apply(Event("5", AgentEventType.Message, text: "Continuing with live output"));

        Assert.True(machine.TryGet("session-1", out var continued));
        Assert.Equal(SessionState.Running, continued!.State);
        Assert.Null(continued.Error);
        Assert.Equal("Continuing with live output", continued.LastMessage);
    }

    private static AgentEvent Event(string id, AgentEventType type, string? text = null, string? tool = null) =>
        new(id, "session-1", AgentKind.Codex, type, Now.AddSeconds(int.Parse(id == "same" ? "0" : id)),
            @"E:\repo", "Task", text, tool);
}
