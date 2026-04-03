namespace AgentPowerShell.Mcp;

public sealed class McpInspector(McpRegistry registry, McpVersionPinStore pinStore, McpSessionAnalyzer analyzer)
{
    private readonly HashSet<string> _whitelist = new(StringComparer.OrdinalIgnoreCase);

    public void AllowTool(string toolName) => _whitelist.Add(toolName);

    public async Task<McpInspectionResult> InspectAsync(McpToolCall call, string hash, CancellationToken cancellationToken)
    {
        var entry = registry.Lookup(call.ToolName);
        if (entry is null)
        {
            return new McpInspectionResult(false, $"Tool '{call.ToolName}' is not registered.", "not_registered");
        }

        if (_whitelist.Count > 0 && !_whitelist.Contains(call.ToolName))
        {
            return new McpInspectionResult(false, $"Tool '{call.ToolName}' is not whitelisted.", "whitelist");
        }

        var pinnedHash = pinStore.GetPinnedHash(call.ToolName);
        if (pinnedHash is null)
        {
            await pinStore.PinIfMissingAsync(call.ToolName, hash, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(pinnedHash, hash, StringComparison.Ordinal))
        {
            return new McpInspectionResult(false, $"Tool '{call.ToolName}' changed hash from pinned version.", "version_pin");
        }

        return analyzer.Analyze(call);
    }
}
