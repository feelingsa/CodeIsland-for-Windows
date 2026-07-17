namespace CodeIsland.Hooks;

public sealed class ToolDetector
{
    private readonly string _userHome;
    private readonly string[] _pathEntries;

    public ToolDetector(string? userHome = null, string? path = null)
    {
        _userHome = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _pathEntries = (path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public ToolInstallation Detect(HookTool tool)
    {
        var executable = FindExecutable(tool.ExecutableNames);
        var candidates = tool.ConfigPaths
            .Select(path => Path.Combine(_userHome, path))
            .ToArray();
        var config = candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault();
        var configExists = config is not null && File.Exists(config);
        var markerPresent = configExists && File.ReadAllText(config!).Contains(tool.HookMarker, StringComparison.Ordinal);
        var problem = executable is null
            ? "Executable not found on PATH."
            : !configExists
                ? "No supported user configuration file found."
                : markerPresent ? null : "Hook marker is not installed.";
        return new ToolInstallation(tool, executable, config, markerPresent, executable is not null && configExists && markerPresent, problem);
    }

    public IReadOnlyList<ToolInstallation> DetectAll() => KnownTools.All.Select(Detect).ToArray();

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
