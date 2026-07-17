namespace CodeIsland.Hooks;

public sealed record HookRegistration(
    string Id,
    string Command,
    IReadOnlyList<string> Events,
    int ProtocolVersion,
    string InstallerVersion)
{
    public const int CurrentProtocolVersion = 1;
    public const string CurrentInstallerVersion = "0.1.0";

    public static HookRegistration Create(HookTool tool, string bridgePath) => new(
        tool.HookMarker,
        $"\"{Path.GetFullPath(bridgePath)}\" send --stdin",
        tool.Events,
        CurrentProtocolVersion,
        CurrentInstallerVersion);
}
