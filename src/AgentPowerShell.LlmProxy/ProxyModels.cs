namespace AgentPowerShell.LlmProxy;

public sealed record ProxyRoute(string Provider, Uri Upstream);

public sealed record LlmUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

public sealed record ProxyRequestContext(string SessionId, string Provider, string Path, string RequestBody);

public sealed record ProxyResponseContext(string SessionId, string Provider, string Path, int StatusCode, string ResponseBody, LlmUsage Usage);
