using CodeIsland.Core;

namespace CodeIsland.Hooks;

public sealed record HookTool(
    AgentKind Agent,
    string DisplayName,
    IReadOnlyList<string> ExecutableNames,
    IReadOnlyList<string> ConfigPaths,
    string HookMarker,
    IReadOnlyList<string> Events,
    HookConfigurationFormat Format,
    int CommandTimeout);

public enum HookConfigurationFormat { Claude, EventMap }

public sealed record ToolInstallation(
    HookTool Tool,
    string? ExecutablePath,
    string? ConfigPath,
    bool HookInstalled,
    bool IsHealthy,
    string? Problem);

public static class KnownTools
{
    public static IReadOnlyList<HookTool> All { get; } =
    [
        new(AgentKind.Claude, "Claude Code", ["claude.exe", "claude.cmd", "claude"],
            [@".claude\settings.json", @".claude.json"], "codeisland-claude",
            ["SessionStart", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop"], HookConfigurationFormat.Claude, 5),
        new(AgentKind.Codex, "Codex", ["codex.exe", "codex.cmd", "codex"],
            [@".codex\hooks.json", @".codex\config.json"], "codeisland-codex",
            ["SessionStart", "PreToolUse", "PermissionRequest", "SessionEnd"], HookConfigurationFormat.EventMap, 5),
        new(AgentKind.Gemini, "Gemini CLI", ["gemini.exe", "gemini.cmd", "gemini"],
            [@".gemini\settings.json", @".gemini\hooks.json"], "codeisland-gemini",
            ["SessionStart", "BeforeTool", "AfterTool", "Notification", "SessionEnd"], HookConfigurationFormat.EventMap, 10000)
    ];

    public static int TimeoutFor(HookTool tool, string eventName) =>
        tool.Agent == AgentKind.Codex && eventName == "PermissionRequest" ? 86400 : tool.CommandTimeout;
}
