using AgentPowerShell.Core;
using AgentPowerShell.Cli;
using AgentPowerShell.Events;
using AgentPowerShell.Daemon;
using AgentPowerShell.LlmProxy;
using AgentPowerShell.Mcp;
using AgentPowerShell.Protos;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Xunit;

namespace AgentPowerShell.Tests;

public sealed class ExecutionPolicyTests
{
    [Fact]
    public void PolicyEngine_Uses_First_Matching_File_Rule()
    {
        var policy = new ExecutionPolicy
        {
            FileRules =
            [
                new FileRule { Name = "allow-workspace", Pattern = "/workspace/**", Operations = ["read"], Decision = PolicyDecision.Allow },
                new FileRule { Name = "deny-all", Pattern = "/**", Operations = ["read"], Decision = PolicyDecision.Deny }
            ]
        };

        var result = new PolicyEngine(policy).EvaluateFile(new FileAccessRequest("/workspace/a.txt", "read"));

        Assert.Equal(PolicyDecision.Allow, result.Decision);
        Assert.Equal("allow-workspace", result.RuleName);
    }

    [Fact]
    public void PolicyEngine_Expands_Command_Brace_Globs()
    {
        var policy = new ExecutionPolicy
        {
            CommandRules =
            [
                new CommandRule { Name = "dangerous", Pattern = "{rm,shutdown,reboot}", Decision = PolicyDecision.Deny }
            ]
        };

        var result = new PolicyEngine(policy).EvaluateCommand(new CommandRequest("shutdown -h now"));

        Assert.Equal(PolicyDecision.Deny, result.Decision);
        Assert.Equal("dangerous", result.RuleName);
    }

    [Fact]
    public void PolicyEngine_Matches_Command_Rules_Against_Windows_Executable_Paths()
    {
        var policy = new ExecutionPolicy
        {
            CommandRules =
            [
                new CommandRule { Name = "allow-powershell", Pattern = "powershell", Decision = PolicyDecision.Allow }
            ]
        };

        var result = new PolicyEngine(policy).EvaluateCommand(
            new CommandRequest("\"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile"));

        Assert.Equal(PolicyDecision.Allow, result.Decision);
        Assert.Equal("allow-powershell", result.RuleName);
    }

    [Fact]
    public void PolicyEngine_Matches_Port_Ranges()
    {
        var policy = new ExecutionPolicy
        {
            NetworkRules =
            [
                new NetworkRule { Name = "localhost", Domain = "localhost", Ports = ["1-65535"], Decision = PolicyDecision.Allow }
            ]
        };

        var result = new PolicyEngine(policy).EvaluateNetwork(new NetworkRequest("localhost", 9120));

        Assert.Equal(PolicyDecision.Allow, result.Decision);
    }

    [Fact]
    public void PolicyEngine_Uses_Network_Rules_For_Dns_Queries()
    {
        var policy = new ExecutionPolicy
        {
            NetworkRules =
            [
                new NetworkRule { Name = "allow-example-dns", Domain = "*.example.com", Ports = ["53"], Decision = PolicyDecision.Allow }
            ]
        };

        var result = new PolicyEngine(policy).EvaluateDns("api.example.com");

        Assert.Equal(PolicyDecision.Allow, result.Decision);
        Assert.Equal("allow-example-dns", result.RuleName);
    }

    [Fact]
    public void PolicyEngine_Defaults_To_Deny_When_No_Rules_Match()
    {
        var result = new PolicyEngine(new ExecutionPolicy()).EvaluateEnvironment(new EnvironmentVariableRequest("PATH", "read"));

        Assert.Equal(PolicyDecision.Deny, result.Decision);
        Assert.Equal("default-environment-deny", result.RuleName);
    }

    [Fact]
    public void PolicyLoader_Parses_Default_Policy_Yaml()
    {
        var yaml = """
            version: "1"
            name: default
            description: Default policy
            file_rules:
              - name: allow-workspace-read
                pattern: "/workspace/**"
                operations: [read, stat]
                decision: allow
            command_rules:
              - name: approve-anything
                pattern: "*"
                decision: approve
            env_rules:
              - name: allow-path-read
                pattern: "PATH"
                actions: [read]
                decision: allow
            """;

        var policy = new PolicyLoader().LoadFromYaml(yaml);

        Assert.Equal("default", policy.Name);
        Assert.Single(policy.FileRules);
        Assert.Equal(PolicyDecision.Approve, policy.CommandRules.Single().Decision);
        Assert.Equal(PolicyDecision.Allow, policy.EnvRules.Single().Decision);
    }

    [Fact]
    public void ConfigLoader_Parses_Config_File()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                server:
                  ipc_socket: /tmp/test.sock
                  http_port: 9123
                  http_bind: 127.0.0.1
                sessions:
                  max_concurrent: 5
                  idle_timeout_minutes: 20
                  max_lifetime_minutes: 60
                  reap_interval_seconds: 30
                policy:
                  default_policy: default-policy.yml
                  watch_for_changes: false
                events:
                  stores:
                    - type: jsonl
                      path: data/events.jsonl
                      max_size_mb: 50
                      max_backups: 3
                """);

            var config = new ConfigLoader().LoadFromFile(tempFile);

            Assert.Equal("/tmp/test.sock", config.Server.IpcSocket);
            Assert.Equal(9123, config.Server.HttpPort);
            Assert.Equal(5, config.Sessions.MaxConcurrent);
            Assert.False(config.Policy.WatchForChanges);
            Assert.Single(config.Events.Stores);
            Assert.Equal("jsonl", config.Events.Stores[0].Type);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EventBus_Publishes_And_Stores_Events()
    {
        var storePath = Path.GetTempFileName();
        File.Delete(storePath);

        try
        {
            var store = new AppendOnlyEventStore(storePath);
            var bus = new EventBus();
            bus.Subscribe((record, cancellationToken) => new ValueTask(store.AppendAsync(record, cancellationToken)));

            await bus.PublishAsync(
                new AgentPowerShell.Events.ProcessEvent(DateTimeOffset.UtcNow, "session-1", "pwsh -Command Get-Date", 0),
                CancellationToken.None);

            var lines = await store.ReadLinesAsync(CancellationToken.None);
            Assert.Single(lines);
            Assert.Contains("pwsh -Command Get-Date", lines[0], StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public void EventFilter_Filters_By_Type_And_Session()
    {
        var records = new EventRecord[]
        {
            new AgentPowerShell.Events.ProcessEvent(DateTimeOffset.UtcNow, "alpha", "pwsh", 0),
            new AgentPowerShell.Events.FileEvent(DateTimeOffset.UtcNow, "alpha", "/tmp/a", "read", "allow"),
            new AgentPowerShell.Events.ProcessEvent(DateTimeOffset.UtcNow, "beta", "git status", 0)
        };

        var filtered = EventFilter.Apply(records, new EventQuery(EventType: "process", SessionId: "alpha"));

        Assert.Single(filtered);
        Assert.Equal("alpha", filtered[0].SessionId);
        Assert.Equal("process", filtered[0].EventType);
    }

    [Fact]
    public async Task SessionStore_Persists_And_Reloads_Sessions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-store.json");
        try
        {
            var store = new SessionStore(path);
            await store.LoadAsync(CancellationToken.None);

            var created = await store.GetOrCreateAsync("session-a", Environment.CurrentDirectory, new SessionConfig(), CancellationToken.None);
            Assert.Equal("session-a", created.SessionId);

            var reloaded = new SessionStore(path);
            await reloaded.LoadAsync(CancellationToken.None);
            var sessions = await reloaded.ListAsync(CancellationToken.None);

            Assert.Single(sessions);
            Assert.Equal("session-a", sessions[0].SessionId);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SessionStore_Prunes_Expired_Sessions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-expired.json");
        try
        {
            var store = new SessionStore(path);
            await store.LoadAsync(CancellationToken.None);

            await store.GetOrCreateAsync("expired", Environment.CurrentDirectory, new SessionConfig { IdleTimeoutMinutes = -1 }, CancellationToken.None);
            var pruned = await store.PruneExpiredAsync(CancellationToken.None);
            var sessions = await store.ListAsync(CancellationToken.None);

            Assert.Equal(1, pruned);
            Assert.Empty(sessions);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task WorkspaceCheckpointManager_Creates_Lists_Previews_And_Restores_Workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-checkpoint");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "v1");
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            await File.WriteAllTextAsync(Path.Combine(root, "nested", "beta.txt"), "stable");

            var manager = new WorkspaceCheckpointManager(root);
            var checkpoint = await manager.CreateAsync("baseline", CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "v2");
            File.Delete(Path.Combine(root, "nested", "beta.txt"));
            await File.WriteAllTextAsync(Path.Combine(root, "gamma.txt"), "extra");

            var listed = await manager.ListAsync(CancellationToken.None);
            Assert.Single(listed);
            Assert.Equal(checkpoint.CheckpointId, listed[0].CheckpointId);

            var preview = await manager.PreviewRestoreAsync(checkpoint.CheckpointId, CancellationToken.None);
            Assert.Contains("nested/beta.txt", preview.FilesToAdd);
            Assert.Contains("alpha.txt", preview.FilesToUpdate);
            Assert.Contains("gamma.txt", preview.FilesToDelete);

            var restored = await manager.RestoreAsync(checkpoint.CheckpointId, CancellationToken.None);
            Assert.Equal(3, restored.TotalChanges);
            Assert.Equal("v1", await File.ReadAllTextAsync(Path.Combine(root, "alpha.txt")));
            Assert.Equal("stable", await File.ReadAllTextAsync(Path.Combine(root, "nested", "beta.txt")));
            Assert.False(File.Exists(Path.Combine(root, "gamma.txt")));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApp_Checkpoint_Commands_Emit_Json_With_Real_State()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-checkpoint-cli");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "v1");

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(0, CliApp.Run(["checkpoint", "create", "--name", "release", "--output", "json"]));
            var createPayload = writer.ToString();
            Assert.Contains("\"command\":\"checkpoint create\"", createPayload, StringComparison.Ordinal);
            Assert.Contains("\"Name\":\"release\"", createPayload, StringComparison.Ordinal);

            writer.GetStringBuilder().Clear();
            Assert.Equal(0, CliApp.Run(["checkpoint", "list", "--output", "json"]));
            var listPayload = writer.ToString();
            Assert.Contains("\"command\":\"checkpoint list\"", listPayload, StringComparison.Ordinal);
            Assert.Contains("\"Name\":\"release\"", listPayload, StringComparison.Ordinal);

            await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "v2");
            writer.GetStringBuilder().Clear();
            Assert.Equal(0, CliApp.Run(["checkpoint", "restore", "latest", "--dry-run", "--output", "json"]));
            var restorePayload = writer.ToString();
            Assert.Contains("\"command\":\"checkpoint restore\"", restorePayload, StringComparison.Ordinal);
            Assert.Contains("\"dryRun\":true", restorePayload, StringComparison.Ordinal);
            Assert.Contains("\"update\":[\"alpha.txt\"]", restorePayload, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NetworkMonitor_Publishes_Dns_And_Connection_Events()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-network");
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "default-policy.yml"),
                """
                network_rules:
                  - name: allow-example
                    domain: "*.example.com"
                    ports: ["53", "443"]
                    decision: allow
                """);

            var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);

            var bus = new EventBus();
            var sink = new List<EventRecord>();
            bus.Subscribe((record, _) =>
            {
                sink.Add(record);
                return ValueTask.CompletedTask;
            });

            var session = await store.GetOrCreateAsync("net-session", root, new SessionConfig(), CancellationToken.None);
            var monitor = new NetworkMonitor(store, new AgentPowerShellConfig(), bus);

            var dns = await monitor.InspectDnsQueryAsync(
                new DnsQueryObservation(session.SessionId, "api.example.com", ["1.1.1.1"]),
                CancellationToken.None);
            var connection = await monitor.InspectConnectionAsync(
                new NetworkConnectionObservation(session.SessionId, "api.example.com", 443, "tcp"),
                CancellationToken.None);

            Assert.Equal(PolicyDecision.Allow, dns.Evaluation.Decision);
            Assert.Equal(PolicyDecision.Allow, connection.Evaluation.Decision);
            Assert.Contains(sink, record => record is DnsEvent dnsEvent && dnsEvent.Query == "api.example.com");
            Assert.Contains(sink, record => record is AgentPowerShell.Events.NetworkEvent networkEvent && networkEvent.Port == 443);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FilesystemMonitor_Quarantines_And_Restores_Deletes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-fs");
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "default-policy.yml"),
                """
                file_rules:
                  - name: allow-delete
                    pattern: "**"
                    operations: [delete]
                    decision: allow
                """);

            var target = Path.Combine(root, "notes.txt");
            await File.WriteAllTextAsync(target, "payload");

            var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);

            var bus = new EventBus();
            var sink = new List<EventRecord>();
            bus.Subscribe((record, _) =>
            {
                sink.Add(record);
                return ValueTask.CompletedTask;
            });

            var session = await store.GetOrCreateAsync("fs-session", root, new SessionConfig(), CancellationToken.None);
            var monitor = new FilesystemMonitor(store, new AgentPowerShellConfig(), new QuarantineService(), bus);

            var deleted = await monitor.RequestDeleteAsync(new FileDeleteRequest(session.SessionId, target), CancellationToken.None);
            Assert.Equal(PolicyDecision.Allow, deleted.Evaluation.Decision);
            Assert.NotNull(deleted.Quarantine);
            Assert.False(File.Exists(target));
            Assert.True(File.Exists(deleted.Quarantine!.QuarantinePath));

            var restored = await monitor.RestoreAsync(session.SessionId, deleted.Quarantine.Id, CancellationToken.None);
            Assert.Equal(target, restored.OriginalPath);
            Assert.True(File.Exists(target));
            Assert.Contains(sink, record => record is AgentPowerShell.Events.FileEvent fileEvent && fileEvent.Operation == "restore");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApprovalManager_Approves_With_Totp_Code()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-approval");
        Directory.CreateDirectory(root);

        try
        {
            var config = new AgentPowerShellConfig
            {
                Approval = new ApprovalConfig
                {
                    Mode = "totp",
                    TotpSecretsPath = ".agentpowershell/approvals/totp-secrets.json",
                    WebAuthnSecretsPath = ".agentpowershell/approvals/webauthn-secrets.json"
                }
            };

            var bus = new EventBus();
            var sink = new List<EventRecord>();
            bus.Subscribe((record, _) =>
            {
                sink.Add(record);
                return ValueTask.CompletedTask;
            });

            var manager = new ApprovalManager(
                config,
                new ApprovalSecretStore(
                    Path.Combine(root, config.Approval.TotpSecretsPath),
                    Path.Combine(root, config.Approval.WebAuthnSecretsPath)),
                bus,
                new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

            var secret = await manager.GetOrCreateTotpSecretAsync("session-approval", CancellationToken.None);
            var code = ApprovalTotp.GenerateCode(secret);
            var response = await manager.RequestApprovalAsync(
                new ApprovalRequest(
                    "Approve dangerous command",
                    TimeSpan.FromSeconds(5),
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = "session-approval",
                        ["totpCode"] = code
                    }),
                CancellationToken.None);

            Assert.True(response.Approved);
            Assert.Equal("totp", response.Approver);
            Assert.Contains(sink, record => record is AgentPowerShell.Events.ApprovalEvent approvalEvent && approvalEvent.Mode == "totp" && approvalEvent.Approved);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApprovalManager_Approves_With_WebAuthn_Assertion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-webauthn");
        Directory.CreateDirectory(root);

        try
        {
            var config = new AgentPowerShellConfig
            {
                Approval = new ApprovalConfig
                {
                    Mode = "webauthn",
                    TotpSecretsPath = ".agentpowershell/approvals/totp-secrets.json",
                    WebAuthnSecretsPath = ".agentpowershell/approvals/webauthn-secrets.json"
                }
            };

            var manager = new ApprovalManager(
                config,
                new ApprovalSecretStore(
                    Path.Combine(root, config.Approval.TotpSecretsPath),
                    Path.Combine(root, config.Approval.WebAuthnSecretsPath)),
                new EventBus(),
                new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

            var assertion = await manager.CreateWebAuthnAssertionAsync("session-approval", "Approve deployment", CancellationToken.None);
            var response = await manager.RequestApprovalAsync(
                new ApprovalRequest(
                    "Approve deployment",
                    TimeSpan.FromSeconds(5),
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = "session-approval",
                        ["webauthnAssertion"] = assertion
                    }),
                CancellationToken.None);

            Assert.True(response.Approved);
            Assert.Equal("webauthn", response.Approver);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApprovalManager_Escalates_To_Rest_When_Tty_Is_Unavailable()
    {
        var config = new AgentPowerShellConfig
        {
            Approval = new ApprovalConfig
            {
                Mode = "tty,rest",
                RestApiEndpoint = "https://approval.local"
            }
        };

        var manager = new ApprovalManager(
            config,
            new ApprovalSecretStore(
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-totp.json"),
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-webauthn.json")),
            new EventBus(),
            new HttpClient(new StubHttpMessageHandler(request =>
            {
                Assert.Equal("https://approval.local/", request.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { approved = true, approver = "rest-api", message = "Approved remotely." })
                };
            })));

        var response = await manager.RequestApprovalAsync(
            new ApprovalRequest(
                "Approve network egress",
                TimeSpan.FromSeconds(5),
                new Dictionary<string, string> { ["sessionId"] = "session-rest" }),
            CancellationToken.None);

        Assert.True(response.Approved);
        Assert.Equal("rest-api", response.Approver);
    }

    [Fact]
    public async Task AuthenticationManager_Authenticates_ApiKey_Oidc_And_Hybrid_Modes()
    {
        var baseConfig = new AgentPowerShellConfig
        {
            Auth = new AuthConfig
            {
                Mode = "apikey",
                ApiKey = "secret-key",
                Oidc = new OidcConfig
                {
                    Issuer = "https://issuer.example",
                    Audience = "agentpowershell",
                    ClientId = "agentpowershell",
                    ClientSecret = "super-secret-signing-key-123"
                }
            }
        };

        var apiKeyManager = new AgentPowerShell.Daemon.AuthenticationManager(baseConfig);
        var apiKeyResult = await apiKeyManager.AuthenticateAsync(new AuthenticationRequest(ApiKey: "secret-key"), CancellationToken.None);
        Assert.True(apiKeyResult.Authenticated);
        Assert.Equal("apikey", apiKeyResult.Mode);

        var oidcConfig = baseConfig with { Auth = baseConfig.Auth with { Mode = "oidc" } };
        var oidcManager = new AgentPowerShell.Daemon.AuthenticationManager(oidcConfig);
        var tokenPair = await oidcManager.IssueTokenPairAsync("operator-a", ["admin"], CancellationToken.None);
        var oidcResult = await oidcManager.AuthenticateAsync(new AuthenticationRequest(BearerToken: tokenPair.AccessToken), CancellationToken.None);
        Assert.True(oidcResult.Authenticated);
        Assert.Equal("operator-a", oidcResult.Subject);
        Assert.Equal("admin", oidcResult.Role);

        var refreshed = await oidcManager.RefreshTokenPairAsync(tokenPair.RefreshToken, CancellationToken.None);
        Assert.NotEqual(tokenPair.AccessToken, refreshed.AccessToken);

        var hybridConfig = baseConfig with { Auth = baseConfig.Auth with { Mode = "hybrid" } };
        var hybridManager = new AgentPowerShell.Daemon.AuthenticationManager(hybridConfig);
        var hybridResult = await hybridManager.AuthenticateAsync(
            new AuthenticationRequest(ApiKey: "secret-key", BearerToken: refreshed.AccessToken),
            CancellationToken.None);
        Assert.True(hybridResult.Authenticated);
        Assert.Equal("hybrid", hybridResult.Mode);
    }

    [Fact]
    public async Task ProxyService_Routes_Redacts_And_Publishes_LlmEvents()
    {
        var config = new AgentPowerShellConfig
        {
            LlmProxy = new LlmProxyConfig
            {
                Providers = ["openai=https://proxy.local"],
                RequestsPerMinute = 10,
                TokensPerMinute = 1000
            }
        };

        var router = new ProviderRouter(config);
        var bus = new EventBus();
        var sink = new List<EventRecord>();
        bus.Subscribe((record, _) =>
        {
            sink.Add(record);
            return ValueTask.CompletedTask;
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal("https://proxy.local/v1/chat/completions", request.RequestUri!.ToString());
            var requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.DoesNotContain("operator@example.com", requestBody, StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "resp-1",
                    usage = new { prompt_tokens = 10, completion_tokens = 5 },
                    output = "email operator@example.com"
                })
            };
        }));

        var service = new ProxyService(router, new DlpRedactor(), new UsageTracker(), new ProxyTelemetry(bus), httpClient, config);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://agentpowershell.local/v1/chat/completions")
        {
            Content = JsonContent.Create(new { input = "reach operator@example.com with sk-secret-token-123456" })
        };
        request.Headers.Add("X-Session-ID", "session-llm");

        var response = await service.ForwardAsync(request, CancellationToken.None);
        var body = await response.Content!.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("[REDACTED:email]", body, StringComparison.Ordinal);
        Assert.Contains(sink, record => record is LlmEvent llmEvent && llmEvent.Provider == "openai" && llmEvent.TokenCount == 15);
    }

    [Fact]
    public void UsageTracker_Extracts_Usage_And_Enforces_Limits()
    {
        var tracker = new UsageTracker();
        Assert.True(tracker.TryAcquire(1, 100, 25));
        Assert.False(tracker.TryAcquire(1, 100, 25));

        var usage = UsageTracker.ExtractUsage("""{"usage":{"prompt_tokens":12,"completion_tokens":8}}""");
        Assert.Equal(20, usage.TotalTokens);
    }

    [Fact]
    public async Task McpInspector_Enforces_Whitelist_Pins_And_CrossServer_Detection()
    {
        var pinPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-mcp-pins.json");
        try
        {
            var registry = new McpRegistry();
            registry.Register("server-a", [new McpToolEntry("read_file", "server-a", "1.0.0", "hash-a")]);
            registry.Register("server-b", [new McpToolEntry("send_http", "server-b", "1.0.0", "hash-b")]);

            var pins = new McpVersionPinStore(pinPath);
            await pins.LoadAsync(CancellationToken.None);
            var analyzer = new McpSessionAnalyzer();
            var inspector = new McpInspector(registry, pins, analyzer);
            inspector.AllowTool("read_file");
            inspector.AllowTool("send_http");

            var first = await inspector.InspectAsync(
                new McpToolCall("session-mcp", "server-a", "read_file", "read", DateTimeOffset.UtcNow),
                "hash-a",
                CancellationToken.None);
            Assert.True(first.Allowed);

            var second = await inspector.InspectAsync(
                new McpToolCall("session-mcp", "server-b", "send_http", "send", DateTimeOffset.UtcNow.AddSeconds(2)),
                "hash-b",
                CancellationToken.None);
            Assert.False(second.Allowed);
            Assert.Equal("cross_server_flow", second.Rule);

            var mismatch = await inspector.InspectAsync(
                new McpToolCall("session-mcp", "server-a", "read_file", "read", DateTimeOffset.UtcNow.AddSeconds(3)),
                "hash-a-changed",
                CancellationToken.None);
            Assert.False(mismatch.Allowed);
            Assert.Equal("version_pin", mismatch.Rule);
        }
        finally
        {
            if (File.Exists(pinPath))
            {
                File.Delete(pinPath);
            }
        }
    }

    [Fact]
    public void CliApp_Parses_Core_Command_Surface()
    {
        var parser = CliApp.CreateParser();

        Assert.Empty(parser.Parse(["exec", "session-a", "pwsh", "-NoProfile"]).Errors);
        Assert.Empty(parser.Parse(["start", "--output", "json"]).Errors);
        Assert.Empty(parser.Parse(["stop"]).Errors);
        Assert.Empty(parser.Parse(["session", "create"]).Errors);
        Assert.Empty(parser.Parse(["session", "list"]).Errors);
        Assert.Empty(parser.Parse(["session", "destroy", "session-a"]).Errors);
        Assert.Empty(parser.Parse(["network", "check", "session-a", "api.nuget.org", "443"]).Errors);
        Assert.Empty(parser.Parse(["policy", "validate", "default-policy.yml"]).Errors);
        Assert.Empty(parser.Parse(["policy", "show", "default-policy.yml"]).Errors);
        Assert.Empty(parser.Parse(["report", "--session-id", "session-a", "--events", "detailed"]).Errors);
        Assert.Empty(parser.Parse(["status", "--output", "json"]).Errors);
        Assert.Empty(parser.Parse(["checkpoint", "create"]).Errors);
        Assert.Empty(parser.Parse(["checkpoint", "restore"]).Errors);
        Assert.Empty(parser.Parse(["checkpoint", "list"]).Errors);
        Assert.Empty(parser.Parse(["config", "show"]).Errors);
        Assert.Empty(parser.Parse(["config", "set", "logging.level", "Debug"]).Errors);
    }

    [Fact]
    public async Task CliApp_SessionCreate_Persists_And_Report_Generates_Markdown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-session-report");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data"));
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "config.yml"), """
                events:
                  stores:
                    - type: jsonl
                      path: data/events.jsonl
                """);

            var eventStore = new AppendOnlyEventStore(Path.Combine(root, "data", "events.jsonl"));
            await eventStore.AppendAsync(new AgentPowerShell.Events.ProcessEvent(DateTimeOffset.UtcNow, "session-report", "pwsh -NoProfile", 0), CancellationToken.None);

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(0, CliApp.Run(["session", "create", "--output", "json"]));
            var sessionPayload = writer.ToString();
            Assert.Contains("\"command\":\"session create\"", sessionPayload, StringComparison.Ordinal);
            Assert.Contains("\"sessionId\":", sessionPayload, StringComparison.Ordinal);

            writer.GetStringBuilder().Clear();
            Assert.Equal(0, CliApp.Run(["session", "list", "--output", "json"]));
            var listPayload = writer.ToString();
            Assert.Contains("\"command\":\"session list\"", listPayload, StringComparison.Ordinal);
            Assert.Contains("\"count\":1", listPayload, StringComparison.Ordinal);
            Assert.Contains("\"id\":", listPayload, StringComparison.Ordinal);
            Assert.DoesNotContain("\"SessionId\":", listPayload, StringComparison.Ordinal);

            writer.GetStringBuilder().Clear();
            Assert.Equal(0, CliApp.Run(["report", "--session-id", "session-report", "--events", "detailed", "--output", "json"]));
            var reportPayload = writer.ToString();
            Assert.Contains("\"status\":\"generated\"", reportPayload, StringComparison.Ordinal);
            Assert.Contains("\"sessionId\":\"session-report\"", reportPayload, StringComparison.Ordinal);
            Assert.Contains("Session Report: session-report", reportPayload, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, ".agentpowershell", "reports", "session-report.md")));
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApp_ConfigSet_Persists_Updated_Value()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-config-set");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "config.yml"), """
                logging:
                  level: Information
                """);

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(0, CliApp.Run(["config", "set", "logging.level", "Debug", "--output", "json"]));
            var payload = writer.ToString();
            Assert.Contains("\"updated\":true", payload, StringComparison.Ordinal);

            var config = new ConfigLoader().LoadFromFile(Path.Combine(root, "config.yml"));
            Assert.Equal("Debug", config.Logging.Level);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CliApp_CheckpointRestore_Without_Checkpoints_Returns_NotFound_Status()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-checkpoint-missing");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(0, CliApp.Run(["checkpoint", "restore", "latest", "--dry-run", "--output", "json"]));
            var payload = writer.ToString();
            Assert.Contains("\"status\":\"not-found\"", payload, StringComparison.Ordinal);
            Assert.Contains("No checkpoints are available.", payload, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApp_Exec_Runs_Command_And_Persists_Session()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-exec");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-dotnet
                    pattern: "dotnet"
                    decision: allow
                """);

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(0, CliApp.Run(["exec", "session-a", "dotnet", "--version", "--output", "json"]));
            var payload = writer.ToString();
            Assert.Contains("\"command\":\"exec\"", payload, StringComparison.Ordinal);
            Assert.Contains("\"sessionId\":\"session-a\"", payload, StringComparison.Ordinal);
            Assert.Contains("\"exitCode\":0", payload, StringComparison.Ordinal);
            Assert.Contains("\"policyDecision\":\"allow\"", payload, StringComparison.Ordinal);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var sessions = await store.ListAsync(CancellationToken.None);
            Assert.Contains(sessions, session => session.SessionId == "session-a");
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShimProcessor_Hosts_PowerShell_Commands_In_Constrained_Language_Mode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-shim-powershell-host");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-powershell
                    pattern: "powershell"
                    decision: allow
                """);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());

            var response = await processor.ExecuteAsync(
                new ShimCommandRequest
                {
                    SessionId = "session-a",
                    InvocationName = "powershell",
                    ExecutablePath = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                    Arguments = ["-NoProfile", "-Command", "$ExecutionContext.SessionState.LanguageMode"],
                    WorkingDirectory = root
                },
                CancellationToken.None);

            Assert.Equal(0, response.ExitCode);
            Assert.Equal("allow", response.PolicyDecision);
            Assert.Contains("ConstrainedLanguage", response.Stdout, StringComparison.Ordinal);
            Assert.Contains(response.Events, item => item.EventType == "process.executed.powershell-host");
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApp_Exec_Rejects_Interactive_Shell_Launches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-exec-interactive");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-powershell
                    pattern: "powershell"
                    decision: allow
                """);

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(2, CliApp.Run(["exec", "session-a", "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "--output", "json"]));
            var payload = writer.ToString();
            Assert.Contains("\"exitCode\":2", payload, StringComparison.Ordinal);
            Assert.Contains("Interactive shell sessions are not supported", payload, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApp_NetworkCheck_Reports_Denied_Connections()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-network-check");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                network_rules:
                  - name: allow-localhost
                    domain: "localhost"
                    ports: ["443"]
                    decision: allow
                """);

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(0, CliApp.Run(["network", "check", "session-a", "example.com", "443", "--output", "json"]));
            var payload = writer.ToString();
            Assert.Contains("\"decision\":\"deny\"", payload, StringComparison.Ordinal);
            Assert.Contains("\"allowed\":false", payload, StringComparison.Ordinal);
            Assert.Contains("\"rule\":\"default-network-deny\"", payload, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShimProcessor_Denies_Explicit_Network_Targets_Before_Process_Start()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-shim-network-deny");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-powershell
                    pattern: "powershell"
                    decision: allow
                network_rules:
                  - name: allow-localhost
                    domain: "localhost"
                    ports: ["443"]
                    decision: allow
                """);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());

            var response = await processor.ExecuteAsync(
                new ShimCommandRequest
                {
                    SessionId = "session-a",
                    InvocationName = "powershell",
                    ExecutablePath = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                    Arguments = ["-Command", "Invoke-WebRequest https://example.com"],
                    WorkingDirectory = root
                },
                CancellationToken.None);

            Assert.Equal(126, response.ExitCode);
            Assert.Equal("deny", response.PolicyDecision);
            Assert.Contains("No matching network rule", response.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShimProcessor_Uses_Native_Process_Launcher_For_Non_PowerShell_Commands()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-shim-native-launch");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-dotnet
                    pattern: "dotnet"
                    decision: allow
                """);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());

            var response = await processor.ExecuteAsync(
                new ShimCommandRequest
                {
                    SessionId = "session-a",
                    InvocationName = "dotnet",
                    ExecutablePath = "dotnet",
                    Arguments = ["--version"],
                    WorkingDirectory = root
                },
                CancellationToken.None);

            Assert.Equal(0, response.ExitCode);
            Assert.Equal("allow", response.PolicyDecision);
            Assert.Contains(response.Events, item => item.EventType == "process.executed.native");
            Assert.NotEmpty(response.Stdout);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShimProcessor_Uses_AppContainer_For_Native_Network_Client_When_Network_Is_Denied()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var curlExecutable = TryGetCurlExecutable();
        if (curlExecutable is null)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-shim-native-appcontainer");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-curl
                    pattern: "curl"
                    decision: allow
                network_rules:
                  - name: deny-all
                    domain: "*"
                    ports: ["1-65535"]
                    decision: deny
                """);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());

            var response = await processor.ExecuteAsync(
                new ShimCommandRequest
                {
                    SessionId = "session-with-a-very-long-id-for-appcontainer-launch",
                    InvocationName = "curl",
                    ExecutablePath = curlExecutable,
                    Arguments = ["https://example.com"],
                    WorkingDirectory = root
                },
                CancellationToken.None);

            Assert.NotEqual(0, response.ExitCode);
            Assert.Equal("allow", response.PolicyDecision);
            Assert.Contains(response.Events, item => item.EventType == "process.executed.native.appcontainer");
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShimProcessor_Denies_Disallowed_Environment_Overrides()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-shim-env-deny");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), $$"""
                command_rules:
                  - name: allow-shell
                    pattern: "{{GetShellPattern()}}"
                    decision: allow
                env_rules:
                  - name: deny-secret
                    pattern: "MY_BLOCKED_*"
                    actions: [read]
                    decision: deny
                    message: "Blocked environment override."
                """);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());
            var environment = CaptureCurrentEnvironment();
            environment["MY_BLOCKED_TOKEN"] = "secret";
            var request = new ShimCommandRequest
            {
                SessionId = "session-a",
                InvocationName = GetShellInvocation(),
                ExecutablePath = GetShellExecutable(),
                WorkingDirectory = root
            };
            foreach (var argument in GetShellEchoArguments("ok"))
            {
                request.Arguments.Add(argument);
            }

            foreach (var pair in environment)
            {
                request.Environment[pair.Key] = pair.Value;
            }

            var response = await processor.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal(126, response.ExitCode);
            Assert.Equal("deny", response.PolicyDecision);
            Assert.Contains("Blocked environment override.", response.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShimProcessor_Allows_Approved_Environment_Overrides_For_Native_Commands()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-shim-env-allow");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), $$"""
                command_rules:
                  - name: allow-shell
                    pattern: "{{GetShellPattern()}}"
                    decision: allow
                env_rules:
                  - name: allow-my-env
                    pattern: "MY_ALLOWED_*"
                    actions: [read]
                    decision: allow
                """);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());
            var environment = CaptureCurrentEnvironment();
            environment["MY_ALLOWED_TOKEN"] = "env-ok";
            var request = new ShimCommandRequest
            {
                SessionId = "session-a",
                InvocationName = GetShellInvocation(),
                ExecutablePath = GetShellExecutable(),
                WorkingDirectory = root
            };
            foreach (var argument in GetShellEchoEnvironmentArguments("MY_ALLOWED_TOKEN"))
            {
                request.Arguments.Add(argument);
            }

            foreach (var pair in environment)
            {
                request.Environment[pair.Key] = pair.Value;
            }

            var response = await processor.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal(0, response.ExitCode);
            Assert.Equal("allow", response.PolicyDecision);
            Assert.Contains("env-ok", response.Stdout, StringComparison.Ordinal);
            Assert.Contains(response.Events, item => item.EventType == "process.executed.native");
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApp_Start_Stop_And_Status_Track_Daemon_State_File()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-daemon");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "src", "AgentPowerShell.Daemon"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "AgentPowerShell.Daemon", "AgentPowerShell.Daemon.csproj"), "<Project />");

        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var daemonStatePath = Path.Combine(root, ".agentpowershell", "daemon.json");
            Directory.CreateDirectory(Path.GetDirectoryName(daemonStatePath)!);
            await File.WriteAllTextAsync(daemonStatePath, $$"""
                {
                  "processId": {{Environment.ProcessId}},
                  "startedAt": "{{DateTimeOffset.UtcNow:O}}",
                  "workingDirectory": "{{root.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                }
                """);

            Assert.Equal(0, CliApp.Run(["status", "--output", "json"]));
            var statusPayload = writer.ToString();
            Assert.Contains("\"daemon\":\"running\"", statusPayload, StringComparison.Ordinal);

            writer.GetStringBuilder().Clear();
            Assert.Equal(0, CliApp.Run(["start", "--output", "json"]));
            var startPayload = writer.ToString();
            Assert.Contains("\"status\":\"already-running\"", startPayload, StringComparison.Ordinal);

            await File.WriteAllTextAsync(daemonStatePath, $$"""
                {
                  "processId": 999999,
                  "startedAt": "{{DateTimeOffset.UtcNow:O}}",
                  "workingDirectory": "{{root.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                }
                """);
            writer.GetStringBuilder().Clear();
            Assert.Equal(0, CliApp.Run(["stop", "--output", "json"]));
            var stopPayload = writer.ToString();
            Assert.Contains("\"status\":\"stopped\"", stopPayload, StringComparison.Ordinal);
            Assert.False(File.Exists(daemonStatePath));
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DaemonLaunchResolver_Returns_Null_When_No_Candidates_Are_Available()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-daemon-launch-missing");
        Directory.CreateDirectory(root);
        var originalPath = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH");
        var originalCommand = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_CMD");

        try
        {
            Environment.SetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH", null);
            Environment.SetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_CMD", null);

            var plan = DaemonLaunchResolver.Resolve(root, root);

            Assert.Null(plan);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH", originalPath);
            Environment.SetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_CMD", originalCommand);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CliApp_SessionList_Text_Mode_Includes_Usable_Metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-session-list-text");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;

            using (var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json")))
            {
                await store.LoadAsync(CancellationToken.None);
                await store.GetOrCreateAsync("session-a", root, new AgentPowerShellConfig().Sessions, CancellationToken.None);
            }

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(0, CliApp.Run(["session", "list"]));
            var payload = writer.ToString();
            Assert.Contains("session-a", payload, StringComparison.Ordinal);
            Assert.Contains("active=", payload, StringComparison.Ordinal);
            Assert.Contains("created=", payload, StringComparison.Ordinal);
            Assert.Contains("expires=", payload, StringComparison.Ordinal);
            Assert.Contains("policy=", payload, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DaemonLaunchResolver_Uses_Explicit_Dll_Path_When_Configured()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-daemon-launch-explicit");
        Directory.CreateDirectory(root);
        var daemonDll = Path.Combine(root, "AgentPowerShell.Daemon.dll");
        File.WriteAllText(daemonDll, string.Empty);
        var originalPath = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH");

        try
        {
            Environment.SetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH", daemonDll);
            var plan = DaemonLaunchResolver.Resolve(root, root);

            Assert.NotNull(plan);
            Assert.Equal(root, plan!.WorkingDirectory);
            Assert.Single(plan.Arguments);
            Assert.Equal(daemonDll, plan.Arguments[0]);
            Assert.EndsWith(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", plan.FileName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH", originalPath);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DaemonLaunchResolver_Falls_Back_To_Source_Project_When_Repo_Is_Available()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-daemon-launch-project");
        var nested = Path.Combine(root, "tools", "shim");
        Directory.CreateDirectory(Path.Combine(root, "src", "AgentPowerShell.Daemon"));
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "src", "AgentPowerShell.Daemon", "AgentPowerShell.Daemon.csproj"), "<Project />");

        try
        {
            var plan = DaemonLaunchResolver.Resolve(nested, nested);

            Assert.NotNull(plan);
            Assert.Equal(root, plan!.WorkingDirectory);
            Assert.Equal("run", plan.Arguments[0]);
            Assert.Equal("--project", plan.Arguments[1]);
            Assert.EndsWith(Path.Combine("src", "AgentPowerShell.Daemon", "AgentPowerShell.Daemon.csproj"), plan.Arguments[2], StringComparison.OrdinalIgnoreCase);
            Assert.Equal("--no-build", plan.Arguments[3]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SessionReportGenerator_Builds_Markdown_And_Findings()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-report.jsonl");
        try
        {
            var store = new AppendOnlyEventStore(storePath);
            await store.AppendAsync(new AgentPowerShell.Events.FileEvent(DateTimeOffset.UtcNow, "report-session", "/tmp/secret.txt", "read", "deny"), CancellationToken.None);
            await store.AppendAsync(new AgentPowerShell.Events.ApprovalEvent(DateTimeOffset.UtcNow, "report-session", "tty", false, "denied"), CancellationToken.None);
            await store.AppendAsync(new AgentPowerShell.Events.NetworkEvent(DateTimeOffset.UtcNow, "report-session", "db.internal", 22, "tcp"), CancellationToken.None);

            var events = await store.ReadEventsAsync(CancellationToken.None);
            var generator = new SessionReportGenerator();
            var report = generator.Generate("report-session", events);
            var markdown = generator.RenderMarkdown(report);

            Assert.Equal(3, report.Timeline.Count);
            Assert.NotEmpty(report.Findings);
            Assert.Contains("# Session Report: report-session", markdown, StringComparison.Ordinal);
            Assert.Contains("[high] Denied file operation", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    private static Dictionary<string, string> CaptureCurrentEnvironment() =>
        Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string GetShellInvocation() => OperatingSystem.IsWindows() ? "cmd" : "sh";

    private static string GetShellPattern() => OperatingSystem.IsWindows() ? "cmd" : "sh";

    private static string GetShellExecutable() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
        : "/bin/sh";

    private static string[] GetShellEchoArguments(string value) => OperatingSystem.IsWindows()
        ? ["/c", "echo", value]
        : ["-c", $"printf '%s' '{value}'"];

    private static string[] GetShellEchoEnvironmentArguments(string variableName) => OperatingSystem.IsWindows()
        ? ["/c", "echo", $"%{variableName}%"]
        : ["-c", $"printf '%s' \"${variableName}\""];

    private static string? TryGetCurlExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe"),
            @"C:\Program Files\Git\mingw64\bin\curl.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
