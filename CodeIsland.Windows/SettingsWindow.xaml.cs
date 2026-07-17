using System.Windows;
using System.Windows.Controls;
using CodeIsland.Hooks;

namespace CodeIsland.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly Action<AppSettings> _applied;
    private AppSettings _settings;

    public SettingsWindow(SettingsStore store, AppSettings settings, Action<AppSettings> applied)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _applied = applied;
        SelectLanguage(settings.Language);
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
            SoundEnabled = SoundEnabledBox.IsChecked == true,
            SessionCleanupMinutes = Parse(CleanupMinutesBox.Text, _settings.SessionCleanupMinutes),
            MaxVisibleSessions = Parse(MaxSessionsBox.Text, _settings.MaxVisibleSessions),
            EventHistoryLimit = Parse(HistoryLimitBox.Text, _settings.EventHistoryLimit)
        };
        _settings = _settings.Validate();
        _store.Save(_settings);
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
}
