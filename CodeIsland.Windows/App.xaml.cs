using System.Configuration;
using System.Data;
using System.Windows;

using System.Threading;
using System.Windows.Forms;
using CodeIsland.Ipc;
using CodeIsland.Core;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace CodeIsland.Windows;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private NotifyIcon? _tray;
    private MainWindow? _window;
    private PipeServer? _pipeServer;
    private CancellationTokenSource? _pipeStop;
    private Task? _pipeTask;
    private DispatcherTimer? _cleanupTimer;
    private readonly NotificationSoundManager _sounds = new();
    public DesktopSessionStore Sessions { get; private set; } = null!;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, "CodeIsland.Windows.SingleInstance", out var created);
        if (!created)
        {
            Shutdown(2);
            return;
        }

        _settings = new SettingsStore().Load();
        L10n.Apply(Resources, _settings.Language);
        Sessions = new DesktopSessionStore(_settings.MaxVisibleSessions, _settings.EventHistoryLimit);
        _sounds.Enabled = _settings.SoundEnabled;
        StartPipeServer();
        Sessions.EventApplied += (_, agentEvent) => _sounds.Play(agentEvent);
        _window = new MainWindow(Sessions);
        _window.Show();
        _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _cleanupTimer.Tick += (_, _) => Sessions.RemoveExpired(DateTimeOffset.UtcNow.AddMinutes(-_settings.SessionCleanupMinutes));
        _cleanupTimer.Start();
        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "CodeIsland"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开面板", null, (_, _) => ShowPanel());
        menu.Items.Add("收起面板", null, (_, _) => _window.Hide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowPanel();
        base.OnStartup(e);
    }

    private void StartPipeServer()
    {
        _pipeStop = new CancellationTokenSource();
        _pipeServer = new PipeServer(async (message, cancellationToken) =>
        {
            if (message.Type == PipeMessageType.Event && message.Event is not null)
            {
                if (message.Event.Type is AgentEventType.PermissionRequest or AgentEventType.Question)
                {
                    Task<PipeMessage>? responseTask = null;
                    Dispatcher.Invoke(() => { responseTask = Sessions.WaitForResponseAsync(message.Event, cancellationToken); });
                    return await responseTask!;
                }
                Dispatcher.Invoke(() => Sessions.Apply(message.Event));
                return new PipeMessage(PipeMessageType.Ack, Guid.NewGuid().ToString("N"), AckFor: message.MessageId);
            }
            if (message.Type is PipeMessageType.Hello or PipeMessageType.Heartbeat)
                return new PipeMessage(PipeMessageType.Ack, Guid.NewGuid().ToString("N"), AckFor: message.MessageId);
            return new PipeMessage(PipeMessageType.Error, Guid.NewGuid().ToString("N"), Error: "Unsupported message.");
        });
        _pipeTask = _pipeServer.RunAsync(_pipeStop.Token);
    }

    private void ShowPanel()
    {
        if (_window is null) return;
        _window.Show();
        _window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeStop?.Cancel();
        _cleanupTimer?.Stop();
        if (_pipeTask is not null)
        {
            try { _pipeTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
        _pipeServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _pipeStop?.Dispose();
        _tray?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

