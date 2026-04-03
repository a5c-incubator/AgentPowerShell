namespace AgentPowerShell.Mcp;

public sealed class McpServer
{
    public string Name => "agentpowershell-mcp";
    public McpRegistry Registry { get; } = new();
}
