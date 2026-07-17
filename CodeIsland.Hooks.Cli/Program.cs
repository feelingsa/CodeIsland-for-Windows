using CodeIsland.Hooks;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
if (command == "status")
{
    foreach (var status in new ToolDetector().DetectAll())
    {
        Console.WriteLine($"{status.Tool.DisplayName,-14} executable={status.ExecutablePath ?? "not found"}");
        Console.WriteLine($"{new string(' ', 15)}config={status.ConfigPath ?? "not found"} hook={status.HookInstalled} healthy={status.IsHealthy}");
    }
    return 0;
}

if (command == "self-test")
{
    var root = Path.Combine(Path.GetTempPath(), $"codeisland-hooks-{Guid.NewGuid():N}");
    var home = Path.Combine(root, "home");
    var bin = Path.Combine(root, "bin");
    var backups = Path.Combine(root, "backups");
    Directory.CreateDirectory(home);
    Directory.CreateDirectory(bin);
    try
    {
        var tool = KnownTools.All.Single(value => value.DisplayName == "Codex");
        File.WriteAllText(Path.Combine(bin, "codex.cmd"), "@echo off");
        var configPath = Path.Combine(home, tool.ConfigPaths[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, "{\"model\":\"gpt-5\"}");

        var manager = new HookManager(new ToolDetector(home, bin), new HookFileStore(backups));
        Require(!manager.GetStatus(tool).HookInstalled, "Hook must initially be absent.");
        Require(manager.Install(tool).IsHealthy, "Install must produce a healthy status.");
        Require(manager.Install(tool).IsHealthy, "Repeated install must be idempotent.");
        Require(Directory.GetFiles(backups).Length == 1, "Idempotent install must create one backup only.");
        Require(!manager.Uninstall(tool).HookInstalled, "Uninstall must remove the marker.");
        Require(File.ReadAllText(configPath).Contains("gpt-5", StringComparison.Ordinal), "User configuration must be preserved.");
        Console.WriteLine("SELF-TEST PASS: detect, backup, install, idempotency, health and uninstall verified.");
        return 0;
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

Console.Error.WriteLine("Usage: codeisland-hooks [status | self-test]");
return 2;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
