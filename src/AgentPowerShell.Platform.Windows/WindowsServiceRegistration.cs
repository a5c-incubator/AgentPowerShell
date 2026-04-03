namespace AgentPowerShell.Platform.Windows;

public sealed record WindowsServiceRegistration(string ServiceName, string DisplayName, string BinaryPath)
{
    public static WindowsServiceRegistration CreateDefault(string binaryPath) =>
        new("AgentPowerShell", "AgentPowerShell Daemon", binaryPath);
}
