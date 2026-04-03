using System.Diagnostics;
using System.Text.Json;

namespace AgentPowerShell.Cli;

internal sealed class DaemonStateStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public DaemonProcessState? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<DaemonProcessState>(File.ReadAllText(Path), JsonOptions);
    }

    public void Save(DaemonProcessState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(state, JsonOptions));
    }

    public void Delete()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }

    public DaemonStatusSnapshot GetStatus()
    {
        var state = Load();
        if (state is null)
        {
            return new DaemonStatusSnapshot(false, null, null, null, null);
        }

        var isRunning = TryGetProcess(state.ProcessId, out var process);
        if (!isRunning)
        {
            Delete();
            return new DaemonStatusSnapshot(false, state.ProcessId, state.StartedAt, state.WorkingDirectory, null);
        }

        return new DaemonStatusSnapshot(true, state.ProcessId, state.StartedAt, state.WorkingDirectory, process?.ProcessName);
    }

    private static bool TryGetProcess(int processId, out Process? process)
    {
        try
        {
            process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            process = null;
            return false;
        }
    }
}

internal sealed record DaemonProcessState(int ProcessId, DateTimeOffset StartedAt, string WorkingDirectory);

internal sealed record DaemonStatusSnapshot(
    bool IsRunning,
    int? ProcessId,
    DateTimeOffset? StartedAt,
    string? WorkingDirectory,
    string? ProcessName);
