using System.Windows;
using System.Windows.Controls;
using CodeIsland.Hooks;
using System.IO;
using WpfButton = System.Windows.Controls.Button;

namespace CodeIsland.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly Action<AppSettings> _applied;
    private AppSettings _settings;
    private readonly StartupRegistration _startup = new();

    public SettingsWindow(SettingsStore store, AppSettings settings, Action<AppSettings> applied)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _applied = applied;
        SelectLanguage(settings.Language);
        LaunchAtLoginBox.IsChecked = settings.LaunchAtLogin || _startup.IsEnabled();
        SoundEnabledBox.IsChecked = settings.SoundEnabled;
        CleanupMinutesBox.Text = settings.SessionCleanupMinutes.ToString();
        MaxSessionsBox.Text = settings.MaxVisibleSessions.ToString();
        HistoryLimitBox.Text = settings.EventHistoryLimit.ToString();
        RefreshHooks();
    }

    private void SelectLanguage(string language)
    {
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>()
            .First(item => Equals(item.Tag, language));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings = _settings with
        {
            Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system",
            LaunchAtLogin = LaunchAtLoginBox.IsChecked == true,
            SoundEnabled = SoundEnabledBox.IsChecked == true,
            SessionCleanupMinutes = Parse(CleanupMinutesBox.Text, _settings.SessionCleanupMinutes),
            MaxVisibleSessions = Parse(MaxSessionsBox.Text, _settings.MaxVisibleSessions),
            EventHistoryLimit = Parse(HistoryLimitBox.Text, _settings.EventHistoryLimit)
        };
        _settings = _settings.Validate();
        _store.Save(_settings);
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "CodeIsland.Windows.exe");
            _startup.SetEnabled(_settings.LaunchAtLogin, executable);
        }
        catch (UnauthorizedAccessException ex)
        {
            GeneralStatus.Text = ex.Message;
        }
        _applied(_settings);
        DialogResult = true;
    }

    private static int Parse(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnRefreshHooks(object sender, RoutedEventArgs e) => RefreshHooks();
    private void RefreshHooks() => HooksGrid.ItemsSource = new ToolDetector().DetectAll()
        .Select(value => new HookStatusRow(value.Tool, value.ExecutablePath is not null,
            value.HookInstalled, value.IsHealthy, value.Problem)).ToArray();

    private sealed record HookStatusRow(HookTool Tool, bool HasExecutable, bool HookInstalled, bool IsHealthy, string? Problem);

    private void OnInstallHook(object sender, RoutedEventArgs e) => RunHookAction(sender, HookOperation.Install);
    private void OnRepairHook(object sender, RoutedEventArgs e) => RunHookAction(sender, HookOperation.Repair);
    private void OnUninstallHook(object sender, RoutedEventArgs e) => RunHookAction(sender, HookOperation.Uninstall);

    private void RunHookAction(object sender, HookOperation operation)
    {
        if (sender is not WpfButton { Tag: HookTool tool }) return;
        try
        {
            var bridge = BridgeLocator.FindCurrent();
            if (operation != HookOperation.Uninstall && bridge is null)
                throw new FileNotFoundException("CodeIsland.Bridge.exe was not found. Build or reinstall CodeIsland.");
            var fileStore = new HookFileStore();
            var manager = new HookManager(new ToolDetector(store: fileStore), fileStore);
            var status = operation switch
            {
                HookOperation.Install => manager.Install(tool, bridge!),
                HookOperation.Repair => manager.Repair(tool, bridge!),
                HookOperation.Uninstall => manager.Uninstall(tool),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            HookOperationStatus.Text = $"{tool.DisplayName}: {operation} completed. Healthy={status.IsHealthy}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            HookOperationStatus.Text = $"{tool.DisplayName}: {ex.Message}";
        }
        RefreshHooks();
    }

    private enum HookOperation { Install, Repair, Uninstall }
}
