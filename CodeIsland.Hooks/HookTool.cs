using CodeIsland.Core;

namespace CodeIsland.Hooks;

public sealed record HookTool(
    AgentKind Agent,
    string DisplayName,
    IReadOnlyList<string> ExecutableNames,
    IReadOnlyList<string> ConfigPaths,
    string HookMarker);

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
            [@".claude.json", @".claude\settings.json"], "codeisland-claude"),
        new(AgentKind.Codex, "Codex", ["codex.exe", "codex.cmd", "codex"],
            [@".codex\config.json", @".codex\hooks.json"], "codeisland-codex"),
        new(AgentKind.Gemini, "Gemini CLI", ["gemini.exe", "gemini.cmd", "gemini"],
            [@".gemini\settings.json", @".gemini\hooks.json"], "codeisland-gemini")
    ];
}
