namespace CodeIsland.Windows;

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public string Language { get; init; } = "system";
    public bool SoundEnabled { get; init; } = true;
    public int MaxVisibleSessions { get; init; } = 5;
    public int EventHistoryLimit { get; init; } = 200;
    public int SessionCleanupMinutes { get; init; } = 30;

    public AppSettings Validate() => this with
    {
        Language = Language is "zh-CN" or "en-US" or "system" ? Language : "system",
        MaxVisibleSessions = Math.Clamp(MaxVisibleSessions, 1, 20),
        EventHistoryLimit = Math.Clamp(EventHistoryLimit, 20, 2000),
        SessionCleanupMinutes = Math.Clamp(SessionCleanupMinutes, 1, 1440)
    };
}
