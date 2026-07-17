using System.Collections.Concurrent;
using System.IO;
using CodeIsland.Core;
using CodeIsland.Protocol;

namespace CodeIsland.Windows;

public sealed class CodexSessionTailer : IDisposable
{
    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<string, long> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CodexTranscriptContext> _contexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    public event EventHandler<AgentEvent>? EventReceived;

    public CodexSessionTailer(string? rootDirectory = null)
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
            codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _rootDirectory = rootDirectory ?? Path.Combine(codexHome, "sessions");
    }

    public void Start(TimeSpan? activeWindow = null)
    {
        if (!Directory.Exists(_rootDirectory) || _watcher is not null) return;
        var cutoff = DateTime.UtcNow - (activeWindow ?? TimeSpan.FromMinutes(30));
        foreach (var file in Directory.GetFiles(_rootDirectory, "*.jsonl", SearchOption.AllDirectories)
                     .Select(path => new FileInfo(path)).Where(file => file.LastWriteTimeUtc >= cutoff)
                     .OrderByDescending(file => file.LastWriteTimeUtc).Take(10))
            ReadInitial(file.FullName);

        _watcher = new FileSystemWatcher(_rootDirectory, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += (_, args) => ReadNew(args.FullPath);
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => ReadNew(args.FullPath);

    private void ReadInitial(string path)
    {
        lock (_locks.GetOrAdd(path, _ => new object()))
        {
            var context = _contexts.GetOrAdd(path, _ => new CodexTranscriptContext());
            AgentEvent? sessionStart = null;
            AgentEvent? last = null;
            using var stream = OpenShared(path);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                var parsed = TryParse(line, context);
                if (parsed?.Type == AgentEventType.SessionStart) sessionStart ??= parsed;
                if (parsed is not null) last = parsed;
            }
            _positions[path] = stream.Length;
            if (sessionStart is not null) EventReceived?.Invoke(this, sessionStart);
            if (last is not null && last != sessionStart) EventReceived?.Invoke(this, last);
        }
    }

    private void ReadNew(string path)
    {
        if (!File.Exists(path)) return;
        lock (_locks.GetOrAdd(path, _ => new object()))
        {
            try
            {
                using var stream = OpenShared(path);
                var position = _positions.GetOrAdd(path, 0);
                if (position > stream.Length) position = 0;
                stream.Seek(position, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var context = _contexts.GetOrAdd(path, _ => new CodexTranscriptContext());
                while (reader.ReadLine() is { } line)
                {
                    var parsed = TryParse(line, context);
                    if (parsed is not null) EventReceived?.Invoke(this, parsed);
                }
                _positions[path] = stream.Length;
            }
            catch (IOException) { }
        }
    }

    private static AgentEvent? TryParse(string line, CodexTranscriptContext context)
    {
        try { return CodexTranscriptParser.ParseLine(line, context); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public void Dispose()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }
}
