namespace AgentPowerShell.Core;

public sealed record FileAccessRequest(string Path, string Operation);

public sealed record CommandRequest(string CommandLine)
{
    private string RawExecutableToken =>
        CommandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
        ?? string.Empty;

    public string ExecutableName => Path.GetFileName(RawExecutableToken.Trim('"'));

    public string ExecutableStem => Path.GetFileNameWithoutExtension(ExecutableName);
}

public sealed record NetworkRequest(string Domain, int Port);

public sealed record EnvironmentVariableRequest(string Variable, string Action);
