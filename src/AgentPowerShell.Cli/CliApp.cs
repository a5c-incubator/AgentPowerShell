using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPowerShell.Core;
using AgentPowerShell.Daemon;
using AgentPowerShell.Events;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Cli;

public static class CliApp
{
    public static int Run(string[] args)
    {
        var previousExitCode = Environment.ExitCode;
        Environment.ExitCode = 0;

        try
        {
            var invokeResult = BuildRootCommand().Invoke(args);
            return invokeResult != 0 ? invokeResult : Environment.ExitCode;
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }

    public static RootCommand BuildRootCommand()
    {
        var outputOption = new Option<string>("--output", () => "text");
        var eventsOption = new Option<string>("--events", () => "summary");

        var root = new RootCommand("AgentPowerShell CLI");
        root.AddGlobalOption(outputOption);
        root.AddGlobalOption(eventsOption);
        root.AddCommand(BuildVersionCommand());
        root.AddCommand(BuildExecCommand(outputOption));
        root.AddCommand(BuildStartCommand(outputOption));
        root.AddCommand(BuildStopCommand(outputOption));
        root.AddCommand(BuildSessionCommand(outputOption));
        root.AddCommand(BuildNetworkCommand(outputOption));
        root.AddCommand(BuildPolicyCommand(outputOption));
        root.AddCommand(BuildReportCommand(outputOption, eventsOption));
        root.AddCommand(BuildStatusCommand(outputOption));
        root.AddCommand(BuildCheckpointCommand(outputOption));
        root.AddCommand(BuildConfigCommand(outputOption));
        return root;
    }

    public static Parser CreateParser() => new(BuildRootCommand());

    private static Command BuildVersionCommand()
    {
        var version = new Command("version", "Print the version");
        version.SetHandler(() => Console.WriteLine("agentpowershell 0.1.0-dev"));
        return version;
    }

    private static Command BuildExecCommand(Option<string> outputOption)
    {
        var command = new Command("exec", "Execute a command in a session");
        var sessionIdArgument = new Argument<string>("session-id");
        var commandArgument = new Argument<string[]>("command") { Arity = ArgumentArity.OneOrMore };
        command.AddArgument(sessionIdArgument);
        command.AddArgument(commandArgument);
        command.SetHandler(async (string sessionId, string[] commandLine, string output) =>
        {
            var workingDirectory = Environment.CurrentDirectory;
            var executable = commandLine[0];
            var arguments = commandLine.Skip(1).ToArray();
            var environment = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var config = LoadConfig();
            using var store = new SessionStore(GetSessionsPath());
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var processor = new ShimCommandProcessor(store, config);
            var response = await processor.ExecuteAsync(new ShimCommandRequest
            {
                SessionId = sessionId,
                InvocationName = executable,
                ExecutablePath = executable,
                Arguments = [.. arguments],
                WorkingDirectory = workingDirectory,
                Interactive = arguments.Length == 0,
                Environment = environment
            }, CancellationToken.None).ConfigureAwait(false);

            Emit(output, new
            {
                command = "exec",
                sessionId = response.SessionId,
                executable,
                arguments,
                exitCode = response.ExitCode,
                policyDecision = response.PolicyDecision,
                eventType = response.Events.LastOrDefault()?.EventType,
                stdout = response.Stdout,
                stderr = response.Stderr,
                denialReason = response.DenialReason
            });
            Environment.ExitCode = response.ExitCode;
        }, sessionIdArgument, commandArgument, outputOption);
        return command;
    }

    private static Command BuildStartCommand(Option<string> outputOption)
    {
        var command = new Command("start", "Start daemon");
        command.SetHandler((string output) =>
        {
            var stateStore = new DaemonStateStore(GetDaemonStatePath());
            var status = stateStore.GetStatus();
            if (status.IsRunning)
            {
                Emit(output, new { command = "start", status = "already-running", processId = status.ProcessId, startedAt = status.StartedAt });
                return;
            }

            var plan = DaemonLaunchResolver.Resolve(Environment.CurrentDirectory, AppContext.BaseDirectory);
            if (plan is null)
            {
                Emit(output, new
                {
                    command = "start",
                    status = "unavailable",
                    message = "Unable to locate a daemon command, binary, or project. Set AGENTPOWERSHELL_DAEMON_PATH or AGENTPOWERSHELL_DAEMON_CMD."
                });
                Environment.ExitCode = 1;
                return;
            }

            var startInfo = DaemonLaunchResolver.CreateStartInfo(plan);
            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start daemon process.");
            var state = new DaemonProcessState(process.Id, DateTimeOffset.UtcNow, Environment.CurrentDirectory);
            stateStore.Save(state);
            Emit(output, new { command = "start", status = "started", processId = process.Id, startedAt = state.StartedAt, launch = new { fileName = plan.FileName, arguments = plan.Arguments } });
        }, outputOption);
        return command;
    }

    private static Command BuildStopCommand(Option<string> outputOption)
    {
        var command = new Command("stop", "Stop daemon");
        command.SetHandler((string output) =>
        {
            var stateStore = new DaemonStateStore(GetDaemonStatePath());
            var state = stateStore.Load();
            if (state is null)
            {
                Emit(output, new { command = "stop", status = "not-running" });
                return;
            }

            try
            {
                var process = Process.GetProcessById(state.ProcessId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                // Treat missing process as already stopped and clean state below.
            }

            stateStore.Delete();
            Emit(output, new { command = "stop", status = "stopped", processId = state.ProcessId });
        }, outputOption);
        return command;
    }

    private static Command BuildSessionCommand(Option<string> outputOption)
    {
        var command = new Command("session", "Session management");
        var create = new Command("create", "Create session");
        create.SetHandler(async (string output) =>
        {
            var config = LoadConfig();
            using var store = new SessionStore(GetSessionsPath());
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var session = await store.GetOrCreateAsync(null, Environment.CurrentDirectory, config.Sessions, CancellationToken.None).ConfigureAwait(false);
            Emit(output, new { command = "session create", sessionId = session.SessionId, session });
        }, outputOption);

        var list = new Command("list", "List sessions");
        list.SetHandler(async (string output) =>
        {
            var store = new SessionStore(Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var sessions = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
            var summarized = sessions.Select(ToSessionSummary).ToArray();
            if (string.Equals(output, "json", StringComparison.OrdinalIgnoreCase))
            {
                Emit(output, new { command = "session list", count = summarized.Length, sessions = summarized });
                return;
            }

            if (summarized.Length == 0)
            {
                Console.WriteLine("No sessions.");
                return;
            }

            foreach (var session in summarized)
            {
                Console.WriteLine($"{session.Id} | active={session.IsActive} | cwd={Abbreviate(session.WorkingDirectory, 60)} | created={session.CreatedAt:O} | last={session.LastActivityAt:O} | expires={session.ExpiresAt:O} | policy={session.PolicyPath}");
            }
        }, outputOption);

        var show = new Command("show", "Show one session");
        var showSessionId = new Argument<string?>("session-id", () => null);
        show.AddArgument(showSessionId);
        show.SetHandler(async (string? id, string output) =>
        {
            using var store = new SessionStore(GetSessionsPath());
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var sessions = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);

            var session = string.IsNullOrWhiteSpace(id)
                ? sessions.Count == 0 ? null : sessions[^1]
                : await store.GetAsync(id, CancellationToken.None).ConfigureAwait(false);

            if (session is null)
            {
                Emit(output, new
                {
                    command = "session show",
                    sessionId = id,
                    status = "not-found"
                });
                Environment.ExitCode = 1;
                return;
            }

            var summary = ToSessionSummary(session);
            if (string.Equals(output, "json", StringComparison.OrdinalIgnoreCase))
            {
                Emit(output, new { command = "session show", sessionId = summary.Id, session = summary });
                return;
            }

            Console.WriteLine($"{summary.Id} | active={summary.IsActive} | cwd={summary.WorkingDirectory} | created={summary.CreatedAt:O} | last={summary.LastActivityAt:O} | expires={summary.ExpiresAt:O} | policy={summary.PolicyPath}");
        }, showSessionId, outputOption);

        var destroy = new Command("destroy", "Destroy session");
        var sessionId = new Argument<string?>("session-id", () => null);
        destroy.AddArgument(sessionId);
        destroy.SetHandler(async (string? id, string output) =>
        {
            using var store = new SessionStore(GetSessionsPath());
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var sessions = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
            var resolvedId = string.IsNullOrWhiteSpace(id)
                ? sessions.Count == 0 ? null : sessions[^1].SessionId
                : id;
            var removed = !string.IsNullOrWhiteSpace(resolvedId)
                && await store.RemoveAsync(resolvedId, CancellationToken.None).ConfigureAwait(false);
            Emit(output, new { command = "session destroy", sessionId = resolvedId ?? id, removed, status = removed ? "removed" : "not-found" });
            if (!removed)
            {
                Environment.ExitCode = 1;
            }
        }, sessionId, outputOption);

        command.AddCommand(create);
        command.AddCommand(list);
        command.AddCommand(show);
        command.AddCommand(destroy);
        return command;
    }

    private static Command BuildPolicyCommand(Option<string> outputOption)
    {
        var command = new Command("policy", "Policy management");
        var validate = new Command("validate", "Validate policy");
        var pathArgument = new Argument<string>("path", () => "default-policy.yml");
        validate.AddArgument(pathArgument);
        validate.SetHandler((string path, string output) =>
        {
            var policy = new PolicyLoader().LoadFromFile(path);
            Emit(output, new { command = "policy validate", valid = true, policy = policy.Name });
        }, pathArgument, outputOption);

        var show = new Command("show", "Show policy");
        show.AddArgument(pathArgument);
        show.SetHandler((string path, string output) =>
        {
            var policy = new PolicyLoader().LoadFromFile(path);
            Emit(output, new { command = "policy show", policy });
        }, pathArgument, outputOption);

        command.AddCommand(validate);
        command.AddCommand(show);
        return command;
    }

    private static Command BuildNetworkCommand(Option<string> outputOption)
    {
        var command = new Command("network", "Network policy tools");
        var check = new Command("check", "Check whether a network connection would be allowed");
        var sessionIdArgument = new Argument<string>("session-id");
        var destinationArgument = new Argument<string>("destination");
        var portArgument = new Argument<int>("port");
        var protocolOption = new Option<string>("--protocol", () => "tcp");
        check.AddArgument(sessionIdArgument);
        check.AddArgument(destinationArgument);
        check.AddArgument(portArgument);
        check.AddOption(protocolOption);
        check.SetHandler(async (string sessionId, string destination, int port, string protocol, string output) =>
        {
            var config = LoadConfig();
            using var store = new SessionStore(GetSessionsPath());
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var bus = new EventBus();
            var monitor = new NetworkMonitor(store, config, bus);
            var result = await monitor.InspectConnectionAsync(
                new NetworkConnectionObservation(sessionId, destination, port, protocol),
                CancellationToken.None).ConfigureAwait(false);

            Emit(output, new
            {
                command = "network check",
                sessionId,
                destination,
                port,
                protocol,
                decision = result.Evaluation.Decision.ToString().ToLowerInvariant(),
                rule = result.Evaluation.RuleName,
                message = result.Evaluation.Message,
                allowed = result.Evaluation.Decision is PolicyDecision.Allow or PolicyDecision.Approve
            });
        }, sessionIdArgument, destinationArgument, portArgument, protocolOption, outputOption);

        command.AddCommand(check);
        return command;
    }

    private static Command BuildReportCommand(Option<string> outputOption, Option<string> eventsOption)
    {
        var command = new Command("report", "Generate session report");
        var sessionId = new Option<string?>("--session-id");
        command.AddOption(sessionId);
        command.SetHandler(async (string? id, string output, string eventsMode) =>
        {
            var config = LoadConfig();
            var resolvedSessionId = await ResolveSessionIdAsync(id, CancellationToken.None).ConfigureAwait(false);
            if (resolvedSessionId is null)
            {
                Emit(output, new { command = "report", sessionId = id, status = "no-session" });
                return;
            }

            var eventStorePath = config.Events.Stores
                .FirstOrDefault(store => string.Equals(store.Type, "jsonl", StringComparison.OrdinalIgnoreCase))
                ?.Path;
            var fullEventStorePath = string.IsNullOrWhiteSpace(eventStorePath)
                ? Path.Combine(Environment.CurrentDirectory, "data", "events.jsonl")
                : Path.GetFullPath(eventStorePath, Environment.CurrentDirectory);

            var store = new AppendOnlyEventStore(fullEventStorePath);
            var events = await store.ReadEventsAsync(CancellationToken.None).ConfigureAwait(false);
            var generator = new SessionReportGenerator();
            var report = generator.Generate(resolvedSessionId, events);
            var markdown = generator.RenderMarkdown(report);

            var reportsDirectory = Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "reports");
            Directory.CreateDirectory(reportsDirectory);
            var reportPath = Path.Combine(reportsDirectory, $"{resolvedSessionId}.md");
            await File.WriteAllTextAsync(reportPath, markdown, CancellationToken.None).ConfigureAwait(false);

            Emit(output, new
            {
                command = "report",
                sessionId = resolvedSessionId,
                events = eventsMode,
                status = "generated",
                reportPath,
                eventCounts = report.EventCounts,
                findings = report.Findings.Count,
                markdown = string.Equals(eventsMode, "detailed", StringComparison.OrdinalIgnoreCase) ? markdown : null
            });
        }, sessionId, outputOption, eventsOption);
        return command;
    }

    private static Command BuildStatusCommand(Option<string> outputOption)
    {
        var command = new Command("status", "Show daemon and session status");
        command.SetHandler(async (string output) =>
        {
            var store = new SessionStore(Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var sessions = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
            var daemon = new DaemonStateStore(GetDaemonStatePath()).GetStatus();
            Emit(output, new
            {
                command = "status",
                daemon = daemon.IsRunning ? "running" : "stopped",
                daemonProcessId = daemon.ProcessId,
                daemonStartedAt = daemon.StartedAt,
                daemonWorkingDirectory = daemon.WorkingDirectory,
                daemonProcessName = daemon.ProcessName,
                sessionCount = sessions.Count
            });
        }, outputOption);
        return command;
    }

    private static Command BuildCheckpointCommand(Option<string> outputOption)
    {
        var command = new Command("checkpoint", "Checkpoint commands");
        var managerFactory = () => new WorkspaceCheckpointManager(Environment.CurrentDirectory);

        var create = new Command("create", "Create a workspace checkpoint");
        var nameOption = new Option<string?>("--name");
        create.AddOption(nameOption);
        create.SetHandler(async (string? name, string output) =>
        {
            var checkpoint = await managerFactory().CreateAsync(name, CancellationToken.None).ConfigureAwait(false);
            Emit(output, new
            {
                command = "checkpoint create",
                checkpoint = new
                {
                    id = checkpoint.CheckpointId,
                    checkpoint.Name,
                    checkpoint.CreatedAt,
                    fileCount = checkpoint.Files.Count
                }
            });
        }, nameOption, outputOption);

        var restore = new Command("restore", "Restore a workspace checkpoint");
        var checkpointIdArgument = new Argument<string>("checkpoint-id", () => "latest");
        var dryRunOption = new Option<bool>("--dry-run");
        restore.AddArgument(checkpointIdArgument);
        restore.AddOption(dryRunOption);
        restore.SetHandler(async (string checkpointId, bool dryRun, string output) =>
        {
            var manager = managerFactory();
            WorkspaceCheckpointRestorePreview preview;
            try
            {
                preview = dryRun
                    ? await manager.PreviewRestoreAsync(checkpointId, CancellationToken.None).ConfigureAwait(false)
                    : await manager.RestoreAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (DirectoryNotFoundException exception)
            {
                Emit(output, new
                {
                    command = "checkpoint restore",
                    checkpointId,
                    dryRun,
                    status = "not-found",
                    message = exception.Message
                });
                return;
            }

            Emit(output, new
            {
                command = "checkpoint restore",
                checkpointId = preview.CheckpointId,
                dryRun,
                status = "ok",
                changes = new
                {
                    add = preview.FilesToAdd,
                    update = preview.FilesToUpdate,
                    delete = preview.FilesToDelete,
                    total = preview.TotalChanges
                }
            });
        }, checkpointIdArgument, dryRunOption, outputOption);

        var list = new Command("list", "List workspace checkpoints");
        list.SetHandler(async (string output) =>
        {
            var checkpoints = await managerFactory().ListAsync(CancellationToken.None).ConfigureAwait(false);
            Emit(output, new
            {
                command = "checkpoint list",
                checkpoints = checkpoints.Select(checkpoint => new
                {
                    id = checkpoint.CheckpointId,
                    checkpoint.Name,
                    checkpoint.CreatedAt,
                    fileCount = checkpoint.Files.Count
                }).ToArray()
            });
        }, outputOption);

        command.AddCommand(create);
        command.AddCommand(restore);
        command.AddCommand(list);

        return command;
    }

    private static Command BuildConfigCommand(Option<string> outputOption)
    {
        var command = new Command("config", "Config commands");
        var show = new Command("show", "Show config");
        show.SetHandler((string output) =>
        {
            var config = File.Exists("config.yml") ? new ConfigLoader().LoadFromFile("config.yml") : new AgentPowerShellConfig();
            Emit(output, new { command = "config show", config });
        }, outputOption);

        var set = new Command("set", "Set config value");
        var key = new Argument<string>("key");
        var value = new Argument<string>("value");
        set.AddArgument(key);
        set.AddArgument(value);
        set.SetHandler((string keyValue, string configValue, string output) =>
        {
            var path = Path.Combine(Environment.CurrentDirectory, "config.yml");
            var loader = new ConfigLoader();
            var config = File.Exists(path) ? loader.LoadFromFile(path) : new AgentPowerShellConfig();
            var updated = ApplyConfigValue(keyValue, configValue, config, out var updatedConfig);
            if (updated)
            {
                loader.SaveToFile(path, updatedConfig);
            }

            Emit(output, new { command = "config set", key = keyValue, value = configValue, updated });
        }, key, value, outputOption);

        command.AddCommand(show);
        command.AddCommand(set);
        return command;
    }

    private static void Emit(string outputMode, object payload)
    {
        if (string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        Console.WriteLine(string.Join(Environment.NewLine, payload.GetType().GetProperties().Select(property => $"{property.Name}: {property.GetValue(payload)}")));
    }

    private static AgentPowerShellConfig LoadConfig()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "config.yml");
        return File.Exists(path) ? new ConfigLoader().LoadFromFile(path) : new AgentPowerShellConfig();
    }

    private static string GetSessionsPath() => Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "sessions.json");

    private static string GetDaemonStatePath() => Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "daemon.json");

    private static async Task<string?> ResolveSessionIdAsync(string? requestedSessionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedSessionId))
        {
            return requestedSessionId;
        }

        using var store = new SessionStore(GetSessionsPath());
        await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var sessions = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return sessions.Count switch
        {
            0 => null,
            _ => sessions[^1].SessionId
        };
    }

    private static bool ApplyConfigValue(string key, string value, AgentPowerShellConfig config, out AgentPowerShellConfig updatedConfig)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();
        updatedConfig = normalizedKey switch
        {
            "server.ipc_socket" => config with { Server = config.Server with { IpcSocket = value } },
            "server.http_port" when int.TryParse(value, out var port) => config with { Server = config.Server with { HttpPort = port } },
            "server.http_bind" => config with { Server = config.Server with { HttpBind = value } },
            "auth.mode" => config with { Auth = config.Auth with { Mode = value } },
            "auth.api_key" => config with { Auth = config.Auth with { ApiKey = value } },
            "auth.oidc.issuer" => config with { Auth = config.Auth with { Oidc = config.Auth.Oidc with { Issuer = value } } },
            "auth.oidc.audience" => config with { Auth = config.Auth with { Oidc = config.Auth.Oidc with { Audience = value } } },
            "auth.oidc.client_id" => config with { Auth = config.Auth with { Oidc = config.Auth.Oidc with { ClientId = value } } },
            "auth.oidc.client_secret" => config with { Auth = config.Auth with { Oidc = config.Auth.Oidc with { ClientSecret = value } } },
            "logging.level" => config with { Logging = config.Logging with { Level = value } },
            "logging.console" when bool.TryParse(value, out var loggingConsole) => config with { Logging = config.Logging with { Console = loggingConsole } },
            "logging.file" => config with { Logging = config.Logging with { File = value } },
            "logging.structured" when bool.TryParse(value, out var structured) => config with { Logging = config.Logging with { Structured = structured } },
            "sessions.max_concurrent" when int.TryParse(value, out var maxConcurrent) => config with { Sessions = config.Sessions with { MaxConcurrent = maxConcurrent } },
            "sessions.idle_timeout_minutes" when int.TryParse(value, out var idleTimeout) => config with { Sessions = config.Sessions with { IdleTimeoutMinutes = idleTimeout } },
            "sessions.max_lifetime_minutes" when int.TryParse(value, out var maxLifetime) => config with { Sessions = config.Sessions with { MaxLifetimeMinutes = maxLifetime } },
            "sessions.reap_interval_seconds" when int.TryParse(value, out var reapInterval) => config with { Sessions = config.Sessions with { ReapIntervalSeconds = reapInterval } },
            "policy.default_policy" => config with { Policy = config.Policy with { DefaultPolicy = value } },
            "policy.watch_for_changes" when bool.TryParse(value, out var watchForChanges) => config with { Policy = config.Policy with { WatchForChanges = watchForChanges } },
            "approval.mode" => config with { Approval = config.Approval with { Mode = value } },
            "approval.timeout_seconds" when int.TryParse(value, out var approvalTimeout) => config with { Approval = config.Approval with { TimeoutSeconds = approvalTimeout } },
            "approval.rest_api_endpoint" => config with { Approval = config.Approval with { RestApiEndpoint = value } },
            "approval.totp_secrets_path" => config with { Approval = config.Approval with { TotpSecretsPath = value } },
            "approval.webauthn_secrets_path" => config with { Approval = config.Approval with { WebAuthnSecretsPath = value } },
            "llm_proxy.enabled" when bool.TryParse(value, out var llmEnabled) => config with { LlmProxy = config.LlmProxy with { Enabled = llmEnabled } },
            "llm_proxy.listen_port" when int.TryParse(value, out var listenPort) => config with { LlmProxy = config.LlmProxy with { ListenPort = listenPort } },
            "llm_proxy.requests_per_minute" when int.TryParse(value, out var rpm) => config with { LlmProxy = config.LlmProxy with { RequestsPerMinute = rpm } },
            "llm_proxy.tokens_per_minute" when int.TryParse(value, out var tpm) => config with { LlmProxy = config.LlmProxy with { TokensPerMinute = tpm } },
            "shim.fail_mode" => config with { Shim = config.Shim with { FailMode = value } },
            "shim.max_consecutive_failures" when int.TryParse(value, out var maxFailures) => config with { Shim = config.Shim with { MaxConsecutiveFailures = maxFailures } },
            _ => config
        };

        return !ReferenceEquals(updatedConfig, config) && updatedConfig != config;
    }

    private static SessionListItem ToSessionSummary(AgentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionListItem(
            session.SessionId,
            session.WorkingDirectory,
            session.PolicyPath,
            session.Platform,
            session.CreatedAt,
            session.LastActivityAt,
            session.ExpiresAt,
            session.ExpiresAt > now);
    }

    private static string Abbreviate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return "..." + value[^Math.Max(0, maxLength - 3)..];
    }
}

public sealed record SessionListItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("workingDirectory")] string WorkingDirectory,
    [property: JsonPropertyName("policyPath")] string PolicyPath,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("lastActivityAt")] DateTimeOffset LastActivityAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("isActive")] bool IsActive);
