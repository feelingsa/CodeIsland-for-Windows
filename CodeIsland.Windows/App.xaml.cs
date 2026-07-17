using System.Configuration;
using System.Data;
using System.Windows;

using System.Threading;
using System.Windows.Forms;
using CodeIsland.Ipc;
using CodeIsland.Core;
using System.Windows.Threading;
using System.IO;
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
    private DispatcherTimer? _fullscreenTimer;
    private bool _fullscreenSuppressed;
    private readonly NotificationSoundManager _sounds = new();
    private readonly SettingsStore _settingsStore = new();
    private SettingsWindow? _settingsWindow;
    private readonly AppLogger _logger = new();
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

        _settings = _settingsStore.Load();
        _logger.Info("Application startup.");
        L10n.Apply(Resources, _settings.Language);
        Sessions = new DesktopSessionStore(_settings.MaxVisibleSessions, _settings.EventHistoryLimit);
        _sounds.Enabled = _settings.SoundEnabled;
        StartPipeServer();
        Sessions.EventApplied += (_, agentEvent) =>
        {
            _sounds.Play(agentEvent);
            _logger.Info($"Event received: agent={agentEvent.Agent} type={agentEvent.Type} session={agentEvent.SessionId}");
        };
        _window = new MainWindow(Sessions, _settings);
        _window.Show();
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreenVisibility();
        _fullscreenTimer.Start();
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
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("导出诊断", null, (_, _) => ExportDiagnostics());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowPanel();
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings();
        base.OnStartup(e);
    }

    private void ExportDiagnostics()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CodeIsland diagnostics (*.zip)|*.zip",
            FileName = $"codeisland-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            new DiagnosticsExporter().Export(dialog.FileName, _settings, Sessions, _logger.LogDirectory);
            _logger.Info("Diagnostics exported.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Diagnostics export failed", ex);
            System.Windows.MessageBox.Show(ex.Message, "CodeIsland", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settingsStore, _settings, ApplySettings,
            () => _window?.HotKeyStatus ?? "Shortcuts are unavailable.");
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _sounds.Enabled = settings.SoundEnabled;
        L10n.Apply(Resources, settings.Language);
        _window?.ApplySettings(settings);
        if (!settings.HideInFullscreen && _fullscreenSuppressed && _window is not null)
        {
            _window.Show();
            _fullscreenSuppressed = false;
        }
    }

    private void UpdateFullscreenVisibility()
    {
        if (_window is null || !_settings.HideInFullscreen) return;
        var fullscreen = FullscreenDetector.IsFullscreenForeground(_window.NativeHandle);
        if (fullscreen && _window.IsVisible)
        {
            _window.Hide();
            _fullscreenSuppressed = true;
        }
        else if (!fullscreen && _fullscreenSuppressed)
        {
            _window.Show();
            _fullscreenSuppressed = false;
        }
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
        _logger.Info("Application exit.");
        _cleanupTimer?.Stop();
        _fullscreenTimer?.Stop();
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

