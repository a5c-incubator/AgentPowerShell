namespace AgentPowerShell.Platform.Linux;

public sealed record SystemdUnitFile(string Description, string ExecStart, string WorkingDirectory)
{
    public string Render() =>
        $$"""
        [Unit]
        Description={{Description}}

        [Service]
        ExecStart={{ExecStart}}
        WorkingDirectory={{WorkingDirectory}}
        Restart=on-failure

        [Install]
        WantedBy=multi-user.target
        """;
}
