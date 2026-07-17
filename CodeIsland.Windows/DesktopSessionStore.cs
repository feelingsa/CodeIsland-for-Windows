using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodeIsland.Core;
using CodeIsland.Ipc;

namespace CodeIsland.Windows;

public sealed class DesktopSessionStore : INotifyPropertyChanged
{
    private readonly SessionStateMachine _machine = new();
    private readonly Dictionary<string, TaskCompletionSource<PipeMessage>> _pending = new(StringComparer.Ordinal);
    public ObservableCollection<SessionSnapshot> Sessions { get; } = [];
    public int SessionCount => Sessions.Count;
    public bool HasSessions => Sessions.Count > 0;
    public bool IsIdle => !HasSessions;

    public void Apply(AgentEvent agentEvent)
    {
        if (!_machine.Apply(agentEvent) || !_machine.TryGet(agentEvent.SessionId, out var snapshot) || snapshot is null)
            return;
        var existing = Sessions.Select((value, index) => (value, index))
            .FirstOrDefault(pair => pair.value.SessionId == snapshot.SessionId);
        if (existing.value is null) Sessions.Insert(0, snapshot);
        else Sessions[existing.index] = snapshot;
        OnPropertyChanged(nameof(SessionCount));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(IsIdle));
    }

    public Task<PipeMessage> WaitForResponseAsync(AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        Apply(agentEvent);
        var pending = new TaskCompletionSource<PipeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[agentEvent.EventId] = pending;
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
        return pending.Task;
    }

    public bool Resolve(string eventId, UserAction action, string? responseText = null)
    {
        if (!_pending.Remove(eventId, out var pending)) return false;
        return pending.TrySetResult(new PipeMessage(
            PipeMessageType.ActionResponse,
            Guid.NewGuid().ToString("N"),
            AckFor: eventId,
            Action: action,
            ResponseText: responseText));
    }

    public int PendingCount => _pending.Count;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
