namespace CodeIsland.Hooks;

public sealed class ToolDetector
{
    private readonly string _userHome;
    private readonly string[] _pathEntries;
    private readonly HookFileStore _store;

    public ToolDetector(string? userHome = null, string? path = null, HookFileStore? store = null)
    {
        _userHome = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _pathEntries = (path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _store = store ?? new HookFileStore();
    }

    public ToolInstallation Detect(HookTool tool)
    {
        var executable = FindExecutable(tool.ExecutableNames);
        var candidates = tool.ConfigPaths
            .Select(path => Path.Combine(_userHome, path))
            .ToArray();
        var config = candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault();
        var configExists = config is not null && File.Exists(config);
        var registration = configExists ? _store.Read(config!, tool.HookMarker) : null;
        var nativeHooksCurrent = configExists && HasCurrentNativeHooks(config!, tool);
        var markerPresent = registration is not null || nativeHooksCurrent;
        var currentVersion = nativeHooksCurrent || registration?.ProtocolVersion == HookRegistration.CurrentProtocolVersion
            && registration.InstallerVersion == HookRegistration.CurrentInstallerVersion
            && registration.Events.SequenceEqual(tool.Events);
        var problem = executable is null
            ? "Executable not found on PATH."
            : !configExists
                ? "No supported user configuration file found."
                : !markerPresent ? "Hook registration is not installed."
                : !currentVersion ? "Hook protocol version is outdated." : null;
        return new ToolInstallation(tool, executable, config, markerPresent,
            executable is not null && configExists && markerPresent && currentVersion, problem);
    }

    public IReadOnlyList<ToolInstallation> DetectAll() => KnownTools.All.Select(Detect).ToArray();

    private static bool HasCurrentNativeHooks(string configPath, HookTool tool)
    {
        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath));
            return tool.Events.All(eventName =>
                (root?["hooks"]?[eventName]?.ToJsonString() ?? string.Empty)
                    .Contains(tool.HookMarker, StringComparison.Ordinal));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private string? FindExecutable(IEnumerable<string> names)
    {
        foreach (var directory in _pathEntries)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
