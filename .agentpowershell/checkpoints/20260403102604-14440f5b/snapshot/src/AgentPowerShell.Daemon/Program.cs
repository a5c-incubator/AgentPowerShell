using AgentPowerShell.Daemon;
using AgentPowerShell.Core;
using AgentPowerShell.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var configPath = Path.Combine(Environment.CurrentDirectory, "config.yml");
var config = File.Exists(configPath) ? new ConfigLoader().LoadFromFile(configPath) : new AgentPowerShellConfig();
var sessionStorePath = Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "sessions.json");
var eventStorePath = config.Events.Stores.FirstOrDefault(store => string.Equals(store.Type, "jsonl", StringComparison.OrdinalIgnoreCase))?.Path
    ?? Path.Combine(".agentpowershell", "events.jsonl");

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new SessionStore(sessionStorePath));
builder.Services.AddSingleton(new AppendOnlyEventStore(Path.Combine(Environment.CurrentDirectory, eventStorePath)));
builder.Services.AddSingleton(new ApprovalSecretStore(
    Path.Combine(Environment.CurrentDirectory, config.Approval.TotpSecretsPath),
    Path.Combine(Environment.CurrentDirectory, config.Approval.WebAuthnSecretsPath)));
builder.Services.AddSingleton<IEventSink>(services =>
{
    var bus = new EventBus();
    var store = services.GetRequiredService<AppendOnlyEventStore>();
    bus.Subscribe((record, cancellationToken) => new ValueTask(store.AppendAsync(record, cancellationToken)));
    return bus;
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ApprovalManager>(services => new ApprovalManager(
    services.GetRequiredService<AgentPowerShellConfig>(),
    services.GetRequiredService<ApprovalSecretStore>(),
    services.GetRequiredService<IEventSink>(),
    services.GetRequiredService<IHttpClientFactory>().CreateClient()));
builder.Services.AddSingleton<IApprovalHandler>(services => services.GetRequiredService<ApprovalManager>());
builder.Services.AddSingleton<IAuthenticationService, AuthenticationManager>();
builder.Services.AddSingleton<NetworkMonitor>();
builder.Services.AddSingleton<FilesystemMonitor>();
builder.Services.AddSingleton<QuarantineService>();
builder.Services.AddHostedService<AgentPowerShell.Daemon.Worker>();
builder.Services.AddSingleton<ShimCommandProcessor>();
builder.Services.AddHostedService<ShimIpcHostedService>();
builder.Services.AddHostedService<SessionLifecycleService>();
builder.Services.AddHostedService<FilesystemMonitorHostedService>();

var host = builder.Build();
await host.Services.GetRequiredService<SessionStore>().LoadAsync(CancellationToken.None).ConfigureAwait(false);
await host.RunAsync().ConfigureAwait(false);
