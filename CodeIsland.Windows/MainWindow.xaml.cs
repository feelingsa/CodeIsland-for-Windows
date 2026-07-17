using System.Text;
using System.Windows;
using CodeIsland.Ipc;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using CodeIsland.Core;

namespace CodeIsland.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _expanded = true;
    private const double ExpandedWidth = 560;
    private const double ExpandedHeight = 160;
    private const double CollapsedWidth = 260;
    private const double CollapsedHeight = 64;
    private readonly DesktopSessionStore _sessions;
    private readonly Dictionary<string, string> _answerDrafts = new(StringComparer.Ordinal);
    private GlobalHotKeyManager? _hotKeys;
    private HwndSource? _source;
    private AppSettings _settings;
    private readonly TerminalActivator _terminalActivator = new();
    private readonly WorkspaceLauncher _workspaceLauncher = new();
    public string HotKeyStatus => _hotKeys?.RegistrationSummary ?? "Shortcuts are not registered yet.";

    public MainWindow(DesktopSessionStore sessions, AppSettings settings)
    {
        InitializeComponent();
        _sessions = sessions;
        _settings = settings;
        _sessions.EventApplied += OnEventApplied;
        DataContext = sessions;
        Loaded += (_, _) => PositionPanel();
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                _source = source;
                RegisterHotKeys();
            }
        };
        Closed += (_, _) =>
        {
            _hotKeys?.Dispose();
            _sessions.EventApplied -= OnEventApplied;
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        MouseLeftButtonDown += (_, _) => DragMove();
        MouseDoubleClick += (_, _) => TogglePanel();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        if (_source is not null) RegisterHotKeys();
    }

    private void RegisterHotKeys()
    {
        _hotKeys?.Dispose();
        _hotKeys = new GlobalHotKeyManager(_source!, _settings, TogglePanel,
            () => _sessions.ResolveCurrent(UserAction.Approve),
            () => _sessions.ResolveCurrent(UserAction.Deny));
    }

    private void OnApproveClick(object sender, RoutedEventArgs e) => Resolve(sender, UserAction.Approve);
    private void OnDenyClick(object sender, RoutedEventArgs e) => Resolve(sender, UserAction.Deny);
    private void OnAlwaysAllowClick(object sender, RoutedEventArgs e) => Resolve(sender, UserAction.AlwaysAllow);

    private void Resolve(object sender, UserAction action)
    {
        if (sender is WpfButton { Tag: string eventId }) _sessions.Resolve(eventId, action);
    }

    private void OnAnswerChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is WpfTextBox { Tag: string eventId } textBox) _answerDrafts[eventId] = textBox.Text;
    }

    private void OnAnswerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string eventId }) return;
        _answerDrafts.TryGetValue(eventId, out var answer);
        if (string.IsNullOrWhiteSpace(answer)) return;
        if (_sessions.Resolve(eventId, UserAction.Answer, answer)) _answerDrafts.Remove(eventId);
    }

    private void OnCloseSessionClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string sessionId }) _sessions.RemoveSession(sessionId);
    }

    private void OnJumpClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: SessionSnapshot snapshot }) return;
        var activated = _terminalActivator.TryActivate(snapshot.ProcessId, snapshot.WorkingDirectory);
        if (!activated) activated = _workspaceLauncher.TryLaunch(snapshot.Agent, snapshot.TerminalKind, snapshot.WorkingDirectory);
        if (!activated) ToolTip = "No matching terminal or IDE was found, and no fallback launcher is available.";
    }

    private void PositionPanel()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + 14;
    }

    public void TogglePanel()
    {
        _expanded = !_expanded;
        Width = _expanded ? ExpandedWidth : CollapsedWidth;
        Height = _expanded ? ExpandedHeight : CollapsedHeight;
        PositionPanel();
    }

    private void OnEventApplied(object? sender, AgentEvent agentEvent)
    {
        if (!_expanded && agentEvent.Type is AgentEventType.PermissionRequest or AgentEventType.Question or AgentEventType.Error)
            TogglePanel();
        if (!SystemParameters.ClientAreaAnimation) return;
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        PanelBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
    }
}
