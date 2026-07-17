namespace CodeIsland.Hooks;

public interface IHookManager
{
    ToolInstallation GetStatus(HookTool tool);
    ToolInstallation Install(HookTool tool);
    ToolInstallation Repair(HookTool tool);
    ToolInstallation Uninstall(HookTool tool);
}

public sealed class HookManager : IHookManager
{
    private readonly ToolDetector _detector;
    private readonly HookFileStore _store;

    public HookManager(ToolDetector detector, HookFileStore store)
    {
        _detector = detector;
        _store = store;
    }

    public ToolInstallation GetStatus(HookTool tool) => _detector.Detect(tool);

    public ToolInstallation Install(HookTool tool)
    {
        var status = _detector.Detect(tool);
        if (status.ExecutablePath is null)
            throw new InvalidOperationException($"{tool.DisplayName} executable was not found.");
        if (status.ConfigPath is null)
            throw new InvalidOperationException($"No supported configuration path for {tool.DisplayName}.");
        _store.InstallMarker(status.ConfigPath, tool.HookMarker);
        return _detector.Detect(tool);
    }

    public ToolInstallation Repair(HookTool tool) => Install(tool);

    public ToolInstallation Uninstall(HookTool tool)
    {
        var status = _detector.Detect(tool);
        if (status.ConfigPath is not null) _store.RemoveMarker(status.ConfigPath, tool.HookMarker);
        return _detector.Detect(tool);
    }
}
