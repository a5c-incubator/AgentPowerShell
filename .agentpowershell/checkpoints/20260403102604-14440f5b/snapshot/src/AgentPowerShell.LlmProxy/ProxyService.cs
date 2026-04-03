using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AgentPowerShell.Core;

namespace AgentPowerShell.LlmProxy;

public sealed class ProxyService(
    ProviderRouter router,
    DlpRedactor redactor,
    UsageTracker usageTracker,
    ProxyTelemetry telemetry,
    HttpClient httpClient,
    AgentPowerShellConfig config)
{
    public async Task<HttpResponseMessage> ForwardAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var route = router.Resolve(request.RequestUri?.AbsolutePath ?? "/");
        var sessionId = request.Headers.TryGetValues("X-Session-ID", out var values)
            ? values.FirstOrDefault() ?? "default-session"
            : "default-session";

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var redactedRequest = redactor.Redact(body);
        var projectedTokens = Math.Max(redactedRequest.Length / 4, 1);
        if (!usageTracker.TryAcquire(config.LlmProxy.RequestsPerMinute, config.LlmProxy.TokensPerMinute, projectedTokens))
        {
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":\"rate_limit\"}", Encoding.UTF8, "application/json")
            };
        }

        using var upstream = new HttpRequestMessage(request.Method, new Uri(route.Upstream, request.RequestUri?.PathAndQuery ?? "/"))
        {
            Content = string.IsNullOrEmpty(redactedRequest)
                ? null
                : new StringContent(redactedRequest, Encoding.UTF8, request.Content?.Headers.ContentType?.MediaType ?? "application/json")
        };

        CopyHeaders(request.Headers, upstream.Headers);
        var upstreamResponse = await httpClient.SendAsync(upstream, cancellationToken).ConfigureAwait(false);
        var responseBody = upstreamResponse.Content is null
            ? string.Empty
            : await upstreamResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var redactedResponse = redactor.Redact(responseBody);
        var usage = UsageTracker.ExtractUsage(responseBody);

        await telemetry.PublishAsync(
            new ProxyResponseContext(sessionId, route.Provider, request.RequestUri?.AbsolutePath ?? "/", (int)upstreamResponse.StatusCode, redactedResponse, usage),
            cancellationToken).ConfigureAwait(false);

        var proxied = new HttpResponseMessage(upstreamResponse.StatusCode)
        {
            Content = new StringContent(redactedResponse, Encoding.UTF8, upstreamResponse.Content?.Headers.ContentType?.MediaType ?? "application/json")
        };
        CopyHeaders(upstreamResponse.Headers, proxied.Headers);
        return proxied;
    }

    private static void CopyHeaders(HttpHeaders source, HttpHeaders destination)
    {
        foreach (var header in source)
        {
            destination.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
