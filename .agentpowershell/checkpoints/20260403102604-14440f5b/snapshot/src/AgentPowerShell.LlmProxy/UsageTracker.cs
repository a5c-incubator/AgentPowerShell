using System.Text.Json;

namespace AgentPowerShell.LlmProxy;

public sealed class UsageTracker
{
    private int _requestsThisMinute;
    private int _tokensThisMinute;
    private DateTimeOffset _windowStarted = DateTimeOffset.UtcNow;

    public bool TryAcquire(int requestLimitPerMinute, int tokenLimitPerMinute, int projectedTokens)
    {
        ResetWindowIfNeeded();

        if (requestLimitPerMinute > 0 && _requestsThisMinute >= requestLimitPerMinute)
        {
            return false;
        }

        if (tokenLimitPerMinute > 0 && _tokensThisMinute + projectedTokens > tokenLimitPerMinute)
        {
            return false;
        }

        _requestsThisMinute++;
        _tokensThisMinute += projectedTokens;
        return true;
    }

    public static LlmUsage ExtractUsage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("usage", out var usage))
            {
                return new LlmUsage(0, 0);
            }

            return new LlmUsage(
                usage.TryGetProperty("prompt_tokens", out var prompt) ? prompt.GetInt32() : 0,
                usage.TryGetProperty("completion_tokens", out var completion) ? completion.GetInt32() : 0);
        }
        catch (JsonException)
        {
            return new LlmUsage(0, 0);
        }
    }

    private void ResetWindowIfNeeded()
    {
        if (DateTimeOffset.UtcNow - _windowStarted < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _windowStarted = DateTimeOffset.UtcNow;
        _requestsThisMinute = 0;
        _tokensThisMinute = 0;
    }
}
