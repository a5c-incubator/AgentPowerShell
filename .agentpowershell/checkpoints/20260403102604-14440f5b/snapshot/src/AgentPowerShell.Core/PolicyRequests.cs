namespace AgentPowerShell.Core;

public sealed record FileAccessRequest(string Path, string Operation);

public sealed record CommandRequest(string CommandLine)
{
    public string ExecutableName =>
        CommandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
        ?? string.Empty;
}

public sealed record NetworkRequest(string Domain, int Port);

public sealed record EnvironmentVariableRequest(string Variable, string Action);
