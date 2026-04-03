using AgentPowerShell.Core;
using AgentPowerShell.Events;
using AgentPowerShell.LlmProxy;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var configPath = Path.Combine(Environment.CurrentDirectory, "config.yml");
var config = File.Exists(configPath) ? new ConfigLoader().LoadFromFile(configPath) : new AgentPowerShellConfig();
var eventStorePath = Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "llm-events.jsonl");

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new AppendOnlyEventStore(eventStorePath));
builder.Services.AddSingleton<IEventSink>(services =>
{
    var bus = new EventBus();
    var store = services.GetRequiredService<AppendOnlyEventStore>();
    bus.Subscribe((record, cancellationToken) => new ValueTask(store.AppendAsync(record, cancellationToken)));
    return bus;
});
builder.Services.AddSingleton<ProviderRouter>();
builder.Services.AddSingleton<DlpRedactor>();
builder.Services.AddSingleton<UsageTracker>();
builder.Services.AddSingleton<ProxyTelemetry>();
builder.Services.AddHttpClient<ProxyService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "llm-proxy", status = "ok" }));
app.MapMethods("/{**path}", ["GET", "POST"], async (HttpRequest request, ProxyService proxyService, CancellationToken cancellationToken) =>
{
    using var upstream = new HttpRequestMessage(new HttpMethod(request.Method), request.Path + request.QueryString)
    {
        Content = request.Body.CanRead ? new StreamContent(request.Body) : null
    };

    foreach (var header in request.Headers)
    {
        if (!upstream.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && upstream.Content is not null)
        {
            upstream.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    var response = await proxyService.ForwardAsync(upstream, cancellationToken).ConfigureAwait(false);
    var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    return Results.Content(body, response.Content?.Headers.ContentType?.MediaType ?? "application/json", Encoding.UTF8, (int)response.StatusCode);
});

app.Run();
