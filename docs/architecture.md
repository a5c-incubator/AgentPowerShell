# agentpowershell Architecture

> Target architecture document for the agentpowershell project. This file describes the intended end-state and defense-in-depth direction; parts of it are broader than the current verified runtime implementation.

## 1. System Overview

agentpowershell is a security gateway that interposes between AI agents and the PowerShell execution environment. It enforces command, file, network, and resource policies through a daemon+shim architecture, emitting structured audit events and supporting human-in-the-loop approval workflows.

```
+---------------------------------------------------------------+
|  AI Agent (Claude Code, Copilot, custom)                      |
|    invokes: pwsh -c "some command"                            |
|          pwsh is actually the agentpowershell SHIM            |
|                shim contacts DAEMON via IPC                   |
|                      daemon evaluates POLICY                  |
|                            allow / deny / approve             |
|                                  executes via real pwsh       |
+---------------------------------------------------------------+
```

### Design Principles

1. **Defense in depth**: Multiple enforcement layers (PowerShell SDK, OS kernel, IPC protocol)
2. **Policy compatibility**: YAML format compatible with agentsh policies
3. **Cross-platform**: Windows, Linux, macOS with platform-specific enforcement
4. **Fail-safe**: Configurable fail-open or fail-closed with consecutive failure tracking
5. **Auditability**: Every action produces a structured event with full provenance
6. **Performance**: Pre-compiled rules, async IPC, batch event writes

## 2. Core Abstractions

### IPolicyEngine

Central rule evaluation engine. Loads YAML policy, pre-compiles globs/regexes, evaluates commands/files/network against first-match-wins rules.

```csharp
public interface IPolicyEngine
{
    PolicyDecision EvaluateCommand(CommandContext ctx);
    PolicyDecision EvaluateFileAccess(FileAccessContext ctx);
    PolicyDecision EvaluateNetwork(NetworkContext ctx);
    PolicyDecision EvaluateEnvironment(EnvContext ctx);
    PolicyDecision EvaluateUnixSocket(SocketContext ctx);
    PolicyDecision EvaluateRegistry(RegistryContext ctx);
    PolicyDecision EvaluateSignal(SignalContext ctx);
    PolicyDecision EvaluateMcpTool(McpToolContext ctx);
    PolicyDecision EvaluatePackage(PackageContext ctx);
    void Reload(Policy policy);
}
```

### IShellInterceptor

Abstraction over shell-level command interception. The PowerShell shim binary implements this to capture commands before execution.

```csharp
public interface IShellInterceptor
{
    Task<ShellResult> ExecuteAsync(string command, ShellExecutionOptions options, CancellationToken ct);
    Task InstallAsync(ShimInstallOptions options);
    Task UninstallAsync(ShimUninstallOptions options);
    bool IsInstalled();
}
```

### IEventEmitter

Structured event emission with fan-out to multiple stores.

```csharp
public interface IEventEmitter
{
    void Emit(BaseEvent evt);
    Task EmitAsync(BaseEvent evt, CancellationToken ct);
    void Subscribe(string sessionId, IEventSubscriber subscriber);
    void Unsubscribe(string sessionId, IEventSubscriber subscriber);
}
```

### ISessionManager

Session lifecycle management with per-session policy engines and workspaces.

```csharp
public interface ISessionManager
{
    Task<Session> CreateSessionAsync(CreateSessionRequest req, CancellationToken ct);
    Task<Session> GetSessionAsync(string sessionId, CancellationToken ct);
    Task EndSessionAsync(string sessionId, CancellationToken ct);
    Task PauseSessionAsync(string sessionId, CancellationToken ct);
    Task ResumeSessionAsync(string sessionId, CancellationToken ct);
    Task<SessionCheckpoint> CheckpointAsync(string sessionId, CancellationToken ct);
    Task RestoreCheckpointAsync(string sessionId, string checkpointId, CancellationToken ct);
    IReadOnlyList<Session> ListSessions();
    void ReapExpired();
}
```

### IPlatformEnforcer

OS-specific enforcement abstraction with implementations per platform.

```csharp
public interface IPlatformEnforcer
{
    PlatformCapabilities GetCapabilities();
    Task<IProcessSandbox> CreateSandboxAsync(SandboxOptions options, CancellationToken ct);
    Task<IFileSystemEnforcer> CreateFileSystemEnforcerAsync(FileSystemEnforcerOptions options, CancellationToken ct);
    Task<INetworkEnforcer> CreateNetworkEnforcerAsync(NetworkEnforcerOptions options, CancellationToken ct);
    Task<IResourceLimiter> CreateResourceLimiterAsync(ResourceLimitOptions options, CancellationToken ct);
    Task<IProcessMonitor> CreateProcessMonitorAsync(ProcessMonitorOptions options, CancellationToken ct);
}
```

### IApprovalHandler

Human-in-the-loop approval workflow supporting multiple modalities.

```csharp
public interface IApprovalHandler
{
    Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct);
    Task<ApprovalRequest?> GetPendingAsync(string requestId, CancellationToken ct);
    Task RespondAsync(string requestId, ApprovalResponse response, CancellationToken ct);
    IReadOnlyList<ApprovalRequest> ListPending(string? sessionId = null);
}
```

## 3. Daemon Design

The daemon is a long-running .NET process built on `Microsoft.Extensions.Hosting` (Generic Host). It is the single source of truth for policy evaluation, session state, event storage, and approval workflows.

### Host Configuration

```csharp
Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<IPolicyEngine, PolicyEngine>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IEventEmitter, CompositeEventEmitter>();
        services.AddSingleton<IApprovalHandler, ApprovalManager>();
        services.AddSingleton<IPlatformEnforcer>(PlatformEnforcerFactory.Create());
        services.AddHostedService<IpcServer>();
        services.AddHostedService<HttpApiServer>();
        services.AddHostedService<SessionReaper>();
        services.AddHostedService<ThreatFeedSyncer>();
    });
```

### Transport Layer

The daemon exposes three transport interfaces, matching agentsh:

| Transport | Binding | Purpose |
|-----------|---------|---------|
| gRPC over named pipes / UDS | `\\.\pipe\agentpowershell` (Win) / `/run/agentpowershell.sock` (Unix) | Primary shim-to-daemon IPC. Low latency per-command policy checks, exec streaming |
| HTTP REST | `127.0.0.1:18080` | Session management, event queries, approval API, LLM proxy |
| gRPC over TCP | `127.0.0.1:9090` (optional) | High-performance streaming for event tailing and remote clients |

### gRPC Service Definitions

```protobuf
service AgentPowerShell {
    rpc CreateSession(CreateSessionRequest) returns (CreateSessionResponse);
    rpc EndSession(EndSessionRequest) returns (EndSessionResponse);
    rpc Exec(ExecRequest) returns (ExecResponse);
    rpc ExecStream(ExecRequest) returns (stream ExecOutput);
    rpc EvaluatePolicy(PolicyEvalRequest) returns (PolicyEvalResponse);
    rpc EventsTail(EventsTailRequest) returns (stream Event);
    rpc RequestApproval(ApprovalRequest) returns (ApprovalResponse);
    rpc GetStatus(StatusRequest) returns (StatusResponse);
    rpc Checkpoint(CheckpointRequest) returns (CheckpointResponse);
}
```

### Startup Flow

1. Load and validate config (`config.yml`) via `YamlDotNet`
2. Create `PolicyManager`, load default policy, compile `PolicyEngine` with pre-compiled globs/regexes
3. Detect platform, instantiate `IPlatformEnforcer` (Windows/Linux/macOS)
4. Query platform capabilities (ETW availability, minifilter status, kernel version for Landlock, etc.)
5. Open event stores (SQLite primary, JSONL append-only, optional webhook + OTEL)
6. Create `SessionManager` with session base directory
7. Start IPC server (named pipe / Unix domain socket)
8. Start HTTP API server (Kestrel)
9. Optionally start gRPC TCP listener
10. Start background services: session reaper, threat feed syncer
11. Install signal/console handlers for graceful shutdown

### Authentication

```csharp
public enum AuthMode { None, ApiKey, Oidc, Hybrid, WebAuthn }
```

| Mode | Mechanism | Use Case |
|------|-----------|----------|
| `None` | Loopback-only binding enforced | Local development |
| `ApiKey` | `X-API-Key` header validation against keys file | Service-to-service |
| `Oidc` | JWT validation with group-to-policy mapping | Enterprise SSO |
| `Hybrid` | API key + OIDC combined | Multi-tenant |
| `WebAuthn` | FIDO2 hardware key | High-assurance approval |

Safety: API-mode approvals require authentication to prevent agent self-approval.

## 4. Shim Design

The shim is a native-AOT compiled .NET binary that replaces `pwsh` in the agent's PATH. It is intentionally minimal -- all logic lives in the daemon.

### Installation Strategy

Unlike agentsh which replaces `/bin/sh`, agentpowershell uses a PATH-based shim:

1. **PATH prepend**: Place the shim binary in a directory that appears before the real `pwsh` in `$PATH` / `$env:PATH`
2. **Shim binary**: Named `pwsh` (or `pwsh.exe` on Windows), connects to daemon via IPC
3. **Config file**: `agentpowershell-shim.conf` stores daemon socket path, session ID, real pwsh path
4. **Fallback**: If daemon is unreachable, configurable fail-open or fail-closed behavior

```csharp
public class ShimInstallPlan
{
    public string RealPwshPath { get; init; }      // e.g., /usr/bin/pwsh or C:\Program Files\PowerShell\7\pwsh.exe
    public string ShimDirectory { get; init; }      // e.g., /opt/agentpowershell/bin
    public string ShimBinaryPath { get; init; }     // e.g., /opt/agentpowershell/bin/pwsh
    public string ConfigPath { get; init; }         // e.g., /etc/agentpowershell/shim.conf
    public List<ShimAction> Actions { get; init; }  // Plan of file operations
}
```

### Shim Execution Flow

1. Read `AGENTPS_SESSION_ID` from environment
2. Read shim config to find daemon socket path and real pwsh path
3. Connect to daemon via named pipe / Unix domain socket
4. Send `ExecRequest` with command, arguments, environment, working directory
5. Daemon evaluates policy:
   - **Allow**: Daemon executes command via real pwsh, streams output back
   - **Deny**: Return error message and non-zero exit code
   - **Approve**: Daemon triggers approval workflow, shim blocks until resolved
   - **Audit**: Allow but emit audit event
   - **Redirect**: Execute alternative command
6. Forward stdout/stderr/exit code to the caller

### MCP Server Detection

The shim detects MCP server launches using the same glob patterns as agentsh:

- `@modelcontextprotocol/*`
- `mcp-server-*`
- `*-mcp-server`
- `mcp_server_*`

When detected, the shim wraps stdio with an inspection bridge that monitors tool definitions for poisoning.

### PowerShell-Specific Interception

When the daemon executes commands, it uses a hosted PowerShell runspace with layered enforcement:

```
Layer 1: Custom PSHost (wraps ConsoleHost, intercepts I/O)
Layer 2: InitialSessionState with ConstrainedLanguage mode
Layer 3: Binary module with proxy cmdlets (shadows dangerous commands)
Layer 4: NativeCommandProcessor monitoring via engine events
Layer 5: OS-level enforcement (Job Objects, Landlock, etc.)
```

The `InitialSessionState` is configured with:
- `LanguageMode = PSLanguageMode.ConstrainedLanguage` (prevents `Add-Type`, direct .NET calls)
- Command visibility restrictions (whitelist of allowed cmdlets)
- Module import restrictions (signed modules only)
- Provider restrictions (limit PSDrive access)

## 5. Policy Engine

### Policy Format

YAML-based, compatible with agentsh policy format:

```yaml
version: 1
name: "default-policy"
description: "Default agentpowershell policy"

command_rules:
  - name: "allow-git"
    command: "git"
    decision: allow
  - name: "block-rm-rf"
    command: "rm"
    args: ["-rf", "/"]
    decision: deny
    message: "Recursive root deletion blocked"
  - name: "approve-install"
    command: "Install-Module"
    decision: approve

file_rules:
  - name: "workspace-rw"
    path: "${PROJECT_ROOT}/**"
    operations: [read, write, create]
    decision: allow
  - name: "delete-approve"
    path: "${PROJECT_ROOT}/**"
    operations: [delete]
    decision: approve
  - name: "system-readonly"
    path: "/usr/**"
    operations: [read]
    decision: allow
  - name: "sensitive-deny"
    path: "/etc/shadow"
    decision: deny

network_rules:
  - name: "allow-registries"
    domain: "*.npmjs.org"
    ports: [443]
    decision: allow
  - name: "deny-all"
    domain: "*"
    decision: deny

env_policy:
  allow: ["PATH", "HOME", "TERM", "LANG"]
  deny: ["*_SECRET", "*_KEY", "*_TOKEN"]

registry_rules:  # Windows only
  - name: "allow-hklm-read"
    path: "HKLM\\SOFTWARE\\**"
    operations: [read]
    decision: allow
    priority: 100

unix_socket_rules:
  - name: "allow-docker"
    path: "/var/run/docker.sock"
    operations: [connect]
    decision: approve

signal_rules:
  - name: "block-sigkill-external"
    signal: "@fatal"
    target: external
    decision: deny

dns_redirects:
  - name: "redirect-telemetry"
    pattern: ".*telemetry.*"
    ip: "127.0.0.1"
    visibility: silent

resource_limits:
  max_memory_mb: 512
  max_cpu_percent: 50
  max_pids: 100
  max_open_files: 1024
  max_disk_write_mb: 1024

mcp_rules:
  allowed_tools:
    - server: "filesystem"
      tools: ["read_file", "write_file", "list_directory"]
  blocked_patterns:
    - ".*credential.*"
    - ".*secret.*"
  rate_limits:
    default_rpm: 60
    default_tpm: 10000

process_contexts:
  - name: "ai-agent-context"
    identity:
      exe_name: ["claude", "copilot"]
    chain_rules:
      - name: "detect-shell-laundering"
        consecutive: { comm: ["sh", "bash", "pwsh"], min_count: 3 }
        decision: deny

transparent_commands:
  wrappers: ["env", "sudo", "nohup"]
```

### Rule Evaluation

1. Rules evaluate in YAML order (first match wins)
2. Registry rules additionally sort by priority (higher first)
3. Process contexts override global rules based on parent process identity
4. Chain rules evaluate before context-specific rules
5. Threat feed overlays domain-level deny/audit on top of regular rules
6. Variable expansion (`${PROJECT_ROOT}`, `${SESSION_ID}`) happens at session creation

### Engine Compilation

At policy load time, the engine pre-compiles all patterns for fast evaluation:

| Pattern Type | Library | Example |
|-------------|---------|---------|
| File path globs | `DotNet.Glob` | `/workspace/**/*.cs` |
| Domain globs | `DotNet.Glob` (with `.` separator) | `*.npmjs.org` |
| CIDR ranges | `System.Net.IPNetwork` | `10.0.0.0/8` |
| Command names | `Dictionary<string,Rule>` + `DotNet.Glob` | `git`, `npm-*` |
| Argument patterns | `System.Text.RegularExpressions.Regex` (compiled) | `^-rf$` |
| Registry paths | `DotNet.Glob` (with `\` separator) | `HKLM\SOFTWARE\**` |
| DNS/connect redirects | `Regex` (compiled) | `.*telemetry.*` |
| Environment allow/deny | `DotNet.Glob` | `*_SECRET` |

### Decision Model

```csharp
public record PolicyDecision
{
    public Decision PolicyVerdict { get; init; }     // What the policy says
    public Decision EffectiveVerdict { get; init; }  // Actual enforcement after approval
    public string RuleName { get; init; }
    public string Message { get; init; }
    public ApprovalInfo? Approval { get; init; }
    public RedirectInfo? Redirect { get; init; }
    public FileRedirectInfo? FileRedirect { get; init; }
    public ResolvedEnvPolicy EnvPolicy { get; init; }
    public string? ThreatFeedMatch { get; init; }
}

public enum Decision { Allow, Deny, Approve, Audit, Redirect, Absorb }
```

### Depth-Aware Rules

The policy engine supports depth-based differentiation:

- `direct` (depth 0): Commands typed directly by the agent
- `nested` (depth 1+): Commands spawned by scripts or sub-processes
- `min_depth` / `max_depth`: Fine-grained depth ranges

This enables policies like "allow `git` directly but deny `git` from within a Makefile."

## 6. Event System

### Event Taxonomy

70+ event types organized by category, matching agentsh:

| Category | Event Types |
|----------|-------------|
| File | `file_open`, `file_read`, `file_write`, `file_create`, `file_delete`, `file_rename`, `file_stat`, `file_chmod`, `dir_create`, `dir_delete`, `dir_list` |
| Network | `dns_query`, `net_connect`, `net_listen`, `net_accept`, `dns_redirect`, `connect_redirect` |
| Process | `process_start`, `process_spawn`, `process_exit`, `process_tree_kill` |
| Environment | `env_read`, `env_write`, `env_list`, `env_blocked` |
| Shell | `shell_invoke`, `shell_passthrough`, `session_autostart` |
| Command | `command_intercept`, `command_redirect`, `command_blocked`, `path_redirect` |
| Resource | `resource_limit_set`, `resource_limit_warning`, `resource_limit_exceeded`, `resource_usage_snapshot` |
| IPC | `unix_socket_connect`, `unix_socket_bind`, `unix_socket_blocked`, `named_pipe_open`, `named_pipe_blocked` |
| Signal | `signal_sent`, `signal_blocked`, `signal_redirected`, `signal_absorbed` |
| MCP | `mcp_tool_seen`, `mcp_tool_changed`, `mcp_tool_called`, `mcp_detection`, `mcp_tool_call_intercepted`, `mcp_cross_server_blocked` |
| Package | `package_check_started`, `package_check_completed`, `package_blocked`, `package_approved`, `package_warning` |
| Policy | `policy_loaded`, `policy_changed` |
| Trash | `soft_delete`, `trash_restore`, `trash_purge` |
| Seccomp | `seccomp_blocked`, `notify_handler_panic` |

### Base Event Structure

Every event carries rich metadata:

```csharp
public abstract record BaseEvent
{
    // Identity
    public string EventId { get; init; }           // UUID
    public string EventType { get; init; }
    public string SessionId { get; init; }

    // Timestamps
    public DateTimeOffset Timestamp { get; init; }
    public long UnixMicroseconds { get; init; }
    public long MonotonicNanos { get; init; }
    public long SequenceNumber { get; init; }

    // Host identity
    public string Hostname { get; init; }
    public string MachineId { get; init; }
    public string? ContainerId { get; init; }
    public string? K8sNamespace { get; init; }
    public string? K8sPod { get; init; }

    // Platform
    public string Os { get; init; }
    public string OsVersion { get; init; }
    public string Architecture { get; init; }
    public string? KernelVersion { get; init; }

    // Versioning
    public string AgentVersion { get; init; }
    public string AgentCommit { get; init; }
    public int EventSchemaVersion { get; init; }
    public string PolicyVersion { get; init; }
}
```

### Event Broker (Pub/Sub)

```csharp
public class EventBroker
{
    private readonly ConcurrentDictionary<string, List<ChannelWriter<BaseEvent>>> _subscribers;
    private readonly int _channelCapacity = 100;
    private long _droppedCount;

    public ChannelReader<BaseEvent> Subscribe(string sessionId);
    public void Unsubscribe(string sessionId, ChannelReader<BaseEvent> reader);
    public void Publish(BaseEvent evt);
}
```

- Per-session subscriber maps using `System.Threading.Channels`
- Bounded channels with configurable capacity (default 100)
- Drop-on-slow-subscriber with atomic counter and periodic logging
- Thread-safe subscribe/unsubscribe/publish

### Composite Event Store

Fan-out to multiple backends simultaneously:

| Backend | Implementation | Purpose |
|---------|---------------|---------|
| SQLite | `Microsoft.Data.Sqlite` with batch insertion | Primary queryable store, MCP tool tracking |
| JSONL | Append-only file with rotation (max size, max backups) | Human-readable audit log |
| Webhook | HTTP POST with batching, flush interval, timeout | External SIEM integration |
| OTEL | OpenTelemetry export with filtering and span conversion | Observability pipeline |
| Integrity | HMAC chain over events for tamper detection | Compliance / forensics |

```csharp
public class CompositeEventStore : IEventStore
{
    private readonly IReadOnlyList<IEventStore> _stores;

    public async Task WriteAsync(BaseEvent evt, CancellationToken ct)
    {
        var tasks = _stores.Select(s => s.WriteAsync(evt, ct));
        await Task.WhenAll(tasks);
    }
}
```

## 7. Platform Abstraction

### IPlatformEnforcer Implementations

Each platform project implements `IPlatformEnforcer` with platform-specific enforcement:

#### Windows (`AgentPowerShell.Platform.Windows`)

```
WindowsPlatformEnforcer
  +-- JobObjectSandbox         : IProcessSandbox       (Job Objects via P/Invoke to kernel32.dll)
  +-- MinifilterEnforcer       : IFileSystemEnforcer    (Communication with agentsh minifilter via fltlib.dll)
  +-- EtwMonitor               : IProcessMonitor        (TraceEvent for PowerShell, Kernel-Process, Kernel-File, Kernel-Network)
  +-- AppContainerSandbox      : IProcessSandbox        (Optional lightweight sandbox via userenv.dll)
  +-- NamedPipeMonitor         : IIpcMonitor            (Named pipe interception)
  +-- JobObjectResourceLimiter : IResourceLimiter       (CPU, memory, process count limits)
  +-- AmsiIntegration                                   (AMSI scanning of script blocks)
  +-- ConPtyTerminal                                    (Pseudo console for output capture)
```

#### Linux (`AgentPowerShell.Platform.Linux`)

```
LinuxPlatformEnforcer
  +-- CgroupSandbox            : IProcessSandbox       (cgroups v2 via filesystem)
  +-- LandlockEnforcer         : IFileSystemEnforcer    (Landlock LSM via syscall P/Invoke)
  +-- SeccompFilter            : IProcessSandbox        (seccomp-bpf via libc P/Invoke)
  +-- PtraceTracer             : IProcessMonitor        (ptrace via native helper process)
  +-- EbpfMonitor              : INetworkEnforcer       (Pre-compiled eBPF programs via libbpf)
  +-- CgroupResourceLimiter    : IResourceLimiter       (memory.max, cpu.max, pids.max)
  +-- NamespaceIsolation                                (unshare for network/mount/PID namespaces)
  +-- UnixSocketMonitor        : IIpcMonitor            (Unix domain socket + abstract namespace)
```

#### macOS (`AgentPowerShell.Platform.MacOS`)

```
MacOSPlatformEnforcer
  +-- EndpointSecurityMonitor  : IProcessMonitor       (ES Framework via native dylib)
  +-- SandboxExecEnforcer      : IProcessSandbox       (sandbox-exec with Seatbelt profiles)
  +-- FsEventsMonitor          : IFileSystemEnforcer    (FSEvents via P/Invoke)
  +-- NetworkExtensionProxy    : INetworkEnforcer       (Network Extension via native helper)
  +-- RlimitResourceLimiter    : IResourceLimiter       (setrlimit for CPU, files, processes)
  +-- XpcService                                        (XPC for daemon communication)
  +-- UnixSocketMonitor        : IIpcMonitor            (Unix domain socket monitoring)
```

### Capability Detection

```csharp
public record PlatformCapabilities
{
    public bool CanEnforceFileSystem { get; init; }
    public bool CanEnforceNetwork { get; init; }
    public bool CanEnforceProcess { get; init; }
    public bool CanEnforceRegistry { get; init; }    // Windows only
    public bool CanMonitorEtw { get; init; }         // Windows only
    public bool CanUseLandlock { get; init; }         // Linux 5.13+
    public bool CanUseSeccomp { get; init; }          // Linux only
    public bool CanUseEbpf { get; init; }             // Linux only
    public bool CanUseEndpointSecurity { get; init; } // macOS 10.15+
    public bool HasMinifilterDriver { get; init; }    // Windows, if driver installed
    public string KernelVersion { get; init; }
    public string RuntimeIdentifier { get; init; }
}
```

## 8. IPC Protocol

### Transport

| Platform | Transport | Address |
|----------|-----------|---------|
| Windows | Named pipes | `\\.\pipe\agentpowershell` |
| Linux | Unix domain socket | `/run/agentpowershell.sock` |
| macOS | Unix domain socket | `/tmp/agentpowershell.sock` |

### Wire Protocol

gRPC over the platform-appropriate transport, with Protobuf serialization. The `.proto` files define the contract between shim and daemon.

Key message types:

```protobuf
message ExecRequest {
    string session_id = 1;
    string command = 2;
    repeated string arguments = 3;
    map<string, string> environment = 4;
    string working_directory = 5;
    int32 depth = 6;
    ProcessIdentity caller = 7;
}

message ExecResponse {
    oneof result {
        ExecOutput output = 1;
        PolicyDenied denied = 2;
        ApprovalPending pending = 3;
    }
}

message ExecOutput {
    bytes stdout = 1;
    bytes stderr = 2;
    int32 exit_code = 3;
    bool is_partial = 4;  // for streaming
}

message PolicyDenied {
    string rule_name = 1;
    string message = 2;
}

message ApprovalPending {
    string request_id = 1;
    string message = 2;
    int64 timeout_seconds = 3;
}
```

### Security

- Named pipes use platform ACLs (`PipeSecurity` on Windows, file permissions on Unix)
- Peer credential validation via `SO_PEERCRED` (Linux) / `SCM_CREDS` (macOS) / named pipe impersonation (Windows)
- Session ID validation (alphanumeric + `_-`, 1-128 chars)
- Rate limiting on IPC connections per client PID

## 9. CLI Structure

The CLI uses `System.CommandLine` to provide a command hierarchy matching agentsh:

```
agentpowershell
  exec <command>          Execute a command through the policy engine
    --session <id>        Session ID (or auto-create)
    --policy <name>       Policy to use
    --timeout <seconds>   Execution timeout
    --approve             Pre-approve (for testing)

  start                   Start the daemon
    --config <path>       Config file path
    --foreground          Run in foreground (don't daemonize)
    --debug               Enable debug logging

  stop                    Stop the daemon
    --force               Force stop without draining

  session                 Session management
    create                Create a new session
      --workspace <path>  Workspace directory
      --policy <name>     Policy name
    list                  List active sessions
    info <id>             Show session details
    end <id>              End a session

  policy                  Policy management
    validate <path>       Validate a policy file
    list                  List available policies
    show <name>           Show policy details
    test <path>           Test a command against policy (dry run)

  report                  Generate audit reports
    --session <id>        Filter by session
    --from <time>         Start time
    --to <time>           End time
    --format <fmt>        Output format (json, csv, table)

  status                  Show daemon and session status

  checkpoint              Checkpoint management
    create <session>      Create a checkpoint
    restore <session> <id> Restore a checkpoint
    list <session>        List checkpoints

  config                  Configuration management
    show                  Show current config
    validate <path>       Validate config file
    init                  Generate default config

  shim                    Shim management
    install               Install the PowerShell shim
      --force             Overwrite existing
    uninstall             Remove the PowerShell shim
    status                Show shim installation status

  llm-proxy               LLM proxy management
    start                 Start the LLM proxy
    status                Show proxy status
    providers             List configured LLM providers

  version                 Show version information
```

## 10. LLM Proxy

An HTTP reverse proxy that intercepts LLM API traffic for DLP and monitoring.

### Architecture

```
AI Agent --> agentpowershell LLM proxy --> LLM Provider API
               |
               +-- Dialect detection (Anthropic, OpenAI, custom)
               +-- DLP scanning (request + response content)
               +-- Rate limiting (per-server RPM + TPM)
               +-- MCP tool call inspection (in streamed responses)
               +-- SSE interception
               +-- Interaction storage
```

### Implementation

Built on `YARP` (Yet Another Reverse Proxy) for high-performance proxying:

```csharp
public class LlmProxyService : IHostedService
{
    // Per-session proxy URL for ANTHROPIC_BASE_URL / OPENAI_BASE_URL
    // Auto-detects API dialect from request headers/path
    // Scans content for DLP patterns before forwarding
    // Intercepts SSE streams for MCP tool call monitoring
    // Applies per-provider rate limits (RPM, TPM)
    // Stores interactions for audit trail
}
```

### DLP Patterns

- API keys / tokens in prompts
- Source code exfiltration detection
- PII detection (configurable patterns)
- Credential patterns

### Provider Routing

The proxy routes requests based on the incoming URL path and headers:

| Path prefix | Provider | Headers |
|-------------|----------|---------|
| `/v1/messages` | Anthropic | `x-api-key`, `anthropic-version` |
| `/v1/chat/completions` | OpenAI | `Authorization: Bearer` |
| Custom | Configurable | Configurable |

## 11. MCP Integration

### Tool Whitelisting

```yaml
mcp_rules:
  allowed_tools:
    - server: "filesystem"
      tools: ["read_file", "write_file"]
      version_pin: "1.0.0"
  denied_patterns:
    - ".*credential.*"
    - ".*exec.*"
```

### Security Monitoring

| Check | Description |
|-------|-------------|
| Tool discovery | Parse `tools/list` responses, track definitions |
| Content hashing | SHA-256 of tool definitions; detect rug-pull attacks |
| Pattern detection | Credential theft, exfiltration, hidden instructions, shell injection, path traversal |
| Cross-server analysis | Detect coordinated attack patterns across MCP servers |
| Version pinning | Alert on tool definition changes |
| Rate limiting | Per-server RPM/TPM limits |
| Name similarity | Detect server name spoofing (Levenshtein distance) |

### Stdio Bridge

When the shim detects an MCP server launch, it wraps the server's stdio with an inspection bridge:

```
Agent <--> Shim (inspection bridge) <--> MCP Server
                |
                +-- Parse JSON-RPC messages
                +-- Extract tools/list responses
                +-- Monitor tool calls against policy
                +-- Detect content changes (rug pull)
                +-- Emit mcp_* events
```

## 12. Approval System

### Modes

| Mode | Description | Implementation |
|------|-------------|----------------|
| `local_tty` | Interactive TTY prompt | `Console.ReadLine()` with timeout |
| `api` | Remote approval via REST | HTTP endpoint with auth requirement |
| `totp` | Time-based OTP | RFC 6238 implementation |
| `webauthn` | Hardware key (FIDO2) | `Fido2NetLib` library |
| `dialog` | Platform-native dialog | Win32 MessageBox / macOS NSAlert / Linux zenity |

### Request Lifecycle

```
Command arrives --> Policy evaluates as "approve"
    |
    v
ApprovalRequest created (UUID, expiry, session, command details)
    |
    v
Notification sent (TTY prompt / API event / desktop notification)
    |
    v
[Blocks until response or timeout]
    |
    +-- Approved --> Execute command, emit audit event
    +-- Denied --> Return error, emit audit event
    +-- Timeout --> Configurable (deny by default), emit audit event
```

### Rate Limiting

- Max concurrent pending approvals per session (configurable, default 3)
- Approval timeout (configurable, default 5 minutes)
- Cooldown between repeated approval requests for same command

## 13. Configuration Format

### config.yml (Daemon Configuration)

```yaml
server:
  http:
    addr: "127.0.0.1:18080"
    read_timeout: 30s
    write_timeout: 30s
    max_request_size: 10485760  # 10MB
  grpc:
    enabled: true
    addr: "127.0.0.1:9090"
  ipc:
    pipe_name: "agentpowershell"              # Windows
    socket_path: "/run/agentpowershell.sock"  # Linux/macOS
    permissions: "0660"
  tls:
    enabled: false
    cert_file: ""
    key_file: ""

auth:
  type: "none"  # none | api_key | oidc | hybrid
  api_key:
    keys_file: "keys.yml"
    header_name: "X-API-Key"
  oidc:
    issuer: ""
    client_id: ""
    audience: ""
    group_policy_map: {}

logging:
  level: "info"
  format: "json"
  file: "agentpowershell.log"

sessions:
  base_dir: "./data/sessions"
  idle_timeout: 30m
  max_timeout: 24h
  auto_checkpoint: true

policies:
  dir: "./policies"
  default: "default-policy"
  allowed: []
  signing:
    enabled: false
    public_key: ""

approvals:
  enabled: true
  mode: "local_tty"
  timeout: 5m
  max_concurrent: 3

audit:
  storage:
    enabled: true
    sqlite_path: "./data/events.db"
    batch_size: 100
    flush_interval: 5s
  jsonl:
    enabled: true
    path: "./data/events.jsonl"
    rotation:
      max_size_mb: 100
      max_backups: 5
  webhook:
    enabled: false
    url: ""
    batch_size: 50
    flush_interval: 10s
    timeout: 5s
  otel:
    enabled: false
    endpoint: ""
  integrity:
    enabled: false

sandbox:
  powershell:
    language_mode: "ConstrainedLanguage"
    allowed_cmdlets: []    # empty = use policy command_rules
    module_signing: true
  platform:
    fail_mode: "closed"    # open | closed
    max_consecutive_failures: 5
    policy_timeout: 5s

llm_proxy:
  enabled: false
  listen: "127.0.0.1:18081"
  providers: []

threat_feeds:
  enabled: false
  cache_dir: "./data/threat-cache"
  action: "audit"  # deny | audit
```

## 14. Data Flow Diagram

```
AI Agent
    |
    | pwsh -c "git push origin main"
    v
+-------------------+
| Shim Binary       |  (native-AOT compiled, replaces pwsh in PATH)
|   read env vars   |
|   connect to IPC  |
+--------+----------+
         |
         | gRPC/Protobuf over named pipe / UDS
         v
+-------------------+
| Daemon            |
|                   |
|  +-------------+  |    +-----------------+
|  | PolicyEngine|--+--->| Event Broker    |
|  | (compiled)  |  |    |   +-> SQLite    |
|  +------+------+  |    |   +-> JSONL     |
|         |         |    |   +-> Webhook   |
|   allow | deny    |    |   +-> OTEL      |
|   approve|        |    +-----------------+
|         v         |
|  +-------------+  |    +-----------------+
|  | Session Mgr |  |    | Approval Mgr   |
|  +------+------+  |    |  TTY/API/TOTP   |
|         |         |    |  WebAuthn/Dialog |
|         v         |    +-----------------+
|  +-------------+  |
|  | Platform    |  |    +-----------------+
|  | Enforcer    |--+--->| LLM Proxy       |
|  | (OS-level)  |  |    | DLP, rate limit |
|  +-------------+  |    +-----------------+
|         |         |
+-------------------+
         |
         v
   Real pwsh.exe
   (constrained runspace, custom PSHost)
```

## 15. Security Model

### Threat Model

| Threat | Mitigation |
|--------|------------|
| Agent bypasses shim | PATH ordering + OS-level enforcement as fallback |
| Agent creates unconstrained Runspace | ConstrainedLanguage prevents `[PowerShell]::Create()` |
| Agent uses `Add-Type` for C# escape | ConstrainedLanguage blocks `Add-Type` |
| Agent loads malicious module | Module signing policy + command whitelist |
| Agent self-approves via API | Auth requirement on approval endpoints |
| Agent kills daemon | Job Object prevents child processes from signaling parent |
| Agent modifies policy file | Policy file integrity (signing, HMAC chain) |
| Agent exfiltrates via DNS | DNS interception + network policy |
| Agent manipulates MCP tools | Content hashing, version pinning, pattern detection |
| PID recycling race | Configurable race policy (fail-closed default) |

### Bypass Prevention Layers

1. **PowerShell layer**: ConstrainedLanguage + ISS restrictions + proxy cmdlets
2. **IPC layer**: All commands route through daemon before execution
3. **OS layer**: Job Objects / cgroups / Landlock prevent filesystem and process escape
4. **Kernel layer**: Minifilter (Windows) / seccomp (Linux) / ES Framework (macOS) for defense-in-depth
5. **Network layer**: DNS interception + outbound network policy
6. **Audit layer**: Complete event trail for forensic analysis
