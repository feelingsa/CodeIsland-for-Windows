using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeIsland.Hooks;

public sealed class HookFileStore
{
    private readonly string _backupDirectory;

    public HookFileStore(string? backupDirectory = null)
    {
        _backupDirectory = backupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeIsland", "hook-backups");
    }

    public string InstallMarker(string configPath, string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var original = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
        var root = JsonNode.Parse(original)?.AsObject()
            ?? throw new JsonException($"Configuration root in '{configPath}' must be a JSON object.");
        if (root[marker]?.GetValue<bool>() == true) return string.Empty;

        var backup = Path.Combine(_backupDirectory, $"{Path.GetFileName(configPath)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");
        Directory.CreateDirectory(_backupDirectory);
        File.WriteAllText(backup, original, new UTF8Encoding(false));
        root[marker] = true;
        File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return backup;
    }

    public bool RemoveMarker(string configPath, string marker)
    {
        if (!File.Exists(configPath)) return false;
        var root = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject()
            ?? throw new JsonException($"Configuration root in '{configPath}' must be a JSON object.");
        if (!root.Remove(marker)) return false;
        File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return true;
    }
}
