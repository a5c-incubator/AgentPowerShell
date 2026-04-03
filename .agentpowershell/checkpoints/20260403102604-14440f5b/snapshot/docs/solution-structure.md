# agentpowershell Solution Structure

> .NET solution layout with project details, dependencies, and key types.

## Solution Layout

```
agentpowershell.sln
src/
  AgentPowerShell.Core/              -- Core types, interfaces, policy engine
  AgentPowerShell.Daemon/            -- Background service / daemon
  AgentPowerShell.Shim/              -- PowerShell shim binary
  AgentPowerShell.Cli/               -- CLI entry point
  AgentPowerShell.Events/            -- Event system and audit
  AgentPowerShell.LlmProxy/         -- LLM reverse proxy
  AgentPowerShell.Mcp/              -- MCP integration
  AgentPowerShell.Platform.Windows/  -- Windows enforcement
  AgentPowerShell.Platform.Linux/    -- Linux enforcement
  AgentPowerShell.Platform.MacOS/    -- macOS enforcement
  AgentPowerShell.Protos/           -- Protobuf / gRPC definitions
tests/
  AgentPowerShell.Tests/             -- Unit tests
  AgentPowerShell.IntegrationTests/  -- Integration tests
  AgentPowerShell.Platform.Tests/    -- Platform-specific tests
```

**Total projects: 14** (11 src + 3 test)

## Project Details

---

### AgentPowerShell.Core

**Purpose**: Core types, interfaces, and the policy engine. Zero platform-specific code. This is the central dependency for all other projects.

**Target Framework**: `net9.0`

**Key Classes / Interfaces**:

| Type | Description |
|------|-------------|
| `IPolicyEngine` | Policy evaluation interface (command, file, network, env, registry, signal, MCP, package) |
| `PolicyEngine` | First-match-wins rule evaluator with pre-compiled globs/regexes |
| `PolicyLoader` | YAML policy loading with strict parsing, variable expansion |
| `PolicyManager` | Policy lifecycle: load, reload, resolve by name/env/manifest |
| `PolicyValidator` | Schema validation, unknown field rejection |
| `Policy` | Root policy model (version, name, all rule collections) |
| `CommandRule` | Command name/path glob, argument regex, depth-aware context |
| `FileRule` | Path glob + operation type + redirect support |
| `NetworkRule` | Domain glob, CIDR range, port matching |
| `UnixSocketRule` | Socket path glob + operation types |
| `RegistryRule` | Registry path glob + operations + priority + cache TTL |
| `SignalRule` | Signal name/number/group + target + redirect/absorb |
| `DnsRedirectRule` | Regex hostname + IP rewrite + visibility mode |
| `ConnectRedirectRule` | Regex host:port + destination rewrite + TLS handling |
| `ResourceLimits` | Memory, CPU, disk, PID, file descriptor limits |
| `EnvPolicy` | Environment variable allow/deny glob patterns |
| `McpRules` | Tool whitelists, blocked patterns, rate limits |
| `ProcessContext` | Parent-conditional policy with identity matching and chain rules |
| `PolicyDecision` | Decision result: verdict, rule name, message, approval/redirect info |
| `Decision` | Enum: Allow, Deny, Approve, Audit, Redirect, Absorb |
| `ISessionManager` | Session lifecycle interface |
| `Session` | Session model: ID, state, workspace, policy, env, history, checkpoints |
| `SessionManager` | Thread-safe session CRUD with mutex, UUID IDs, reaping |
| `IShellInterceptor` | Shell interception abstraction |
| `IPlatformEnforcer` | Platform enforcement abstraction |
| `IApprovalHandler` | Approval workflow interface |
| `ApprovalRequest` | Request model: ID, expiry, session, kind, target, rule, fields |
| `ApprovalResult` | Result: approved/denied/timeout |
| `IEventEmitter` | Event emission interface |
| `IEventStore` | Event storage backend interface |
| `BaseEvent` | Abstract event base with full metadata |
| `ConfigModel` | Daemon configuration model (server, auth, logging, sessions, etc.) |
| `ConfigLoader` | YAML config loading and validation |
| `PlatformCapabilities` | Feature detection record |
| `CommandContext` | Context for command evaluation (command, args, env, depth, caller) |
| `FileAccessContext` | Context for file evaluation (path, operation, process) |
| `NetworkContext` | Context for network evaluation (domain, IP, port, protocol) |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `YamlDotNet` | 16.x | YAML policy and config parsing |
| `DotNet.Glob` | 3.x | Glob pattern matching for file paths, domains, commands |
| `System.IO.Hashing` | 9.x | Hash computation (xxHash for content hashing) |
| `Microsoft.Extensions.Logging.Abstractions` | 9.x | Logging abstractions |
| `Microsoft.Extensions.Options` | 9.x | Options pattern for configuration |

**Project References**: None (leaf dependency)

---

### AgentPowerShell.Protos

**Purpose**: Protobuf service and message definitions shared between shim and daemon. Generates C# code from `.proto` files.

**Target Framework**: `net9.0`

**Key Files**:

| File | Description |
|------|-------------|
| `agentpowershell.proto` | Main service definition (CreateSession, Exec, ExecStream, EvaluatePolicy, EventsTail) |
| `policy.proto` | Policy evaluation request/response messages |
| `events.proto` | Event message definitions |
| `session.proto` | Session management messages |
| `approval.proto` | Approval request/response messages |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `Google.Protobuf` | 3.x | Protobuf runtime |
| `Grpc.Tools` | 2.x | Proto compiler and C# code generation |

**Project References**: None

---

### AgentPowerShell.Daemon

**Purpose**: Long-running background service built on Generic Host. Hosts the IPC server, HTTP API, policy engine, session manager, event store, and approval manager. This is the central process that all shims connect to.

**Target Framework**: `net9.0`

**Key Classes**:

| Type | Description |
|------|-------------|
| `DaemonHost` | Generic Host setup: services, hosted services, configuration |
| `IpcServer` | `IHostedService` -- gRPC server over named pipe / Unix domain socket |
| `HttpApiServer` | `IHostedService` -- Kestrel-based REST API for sessions, events, approvals |
| `GrpcPolicyService` | gRPC service implementation for `AgentPowerShell` service |
| `ShellExecutor` | Executes commands via hosted PowerShell runspace with custom PSHost |
| `AgentPSHost` | Custom `PSHost` implementation wrapping ConsoleHost |
| `AgentPSHostUserInterface` | Custom `PSHostUserInterface` for I/O interception |
| `ConstrainedRunspaceFactory` | Creates PowerShell runspaces with ISS restrictions and ConstrainedLanguage |
| `ApprovalManager` | `IApprovalHandler` implementation with TTY/API/TOTP/WebAuthn/dialog modes |
| `TtyApprovalProvider` | TTY-based interactive approval |
| `ApiApprovalProvider` | REST API-based remote approval |
| `TotpApprovalProvider` | TOTP-based approval (RFC 6238) |
| `WebAuthnApprovalProvider` | FIDO2 hardware key approval |
| `DialogApprovalProvider` | Platform-native dialog approval |
| `SessionReaper` | `IHostedService` -- reaps expired/idle sessions on interval |
| `ThreatFeedSyncer` | `IHostedService` -- syncs threat feed data |
| `AuthMiddleware` | HTTP middleware for API key / OIDC / hybrid auth |
| `OidcAuthHandler` | OIDC JWT validation with group-to-policy mapping |
| `PlatformEnforcerFactory` | Creates platform-appropriate `IPlatformEnforcer` |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Extensions.Hosting` | 9.x | Generic Host |
| `Microsoft.AspNetCore.App` (framework ref) | 9.x | Kestrel HTTP server |
| `Grpc.AspNetCore` | 2.x | gRPC server |
| `Microsoft.PowerShell.SDK` | 7.x | PowerShell hosting (runspace, PSHost, ISS) |
| `Serilog.Extensions.Hosting` | 8.x | Structured logging |
| `Serilog.Sinks.Console` | 6.x | Console log output |
| `Serilog.Sinks.File` | 6.x | File log output |
| `Fido2.Models` + `Fido2` | 4.x | WebAuthn / FIDO2 support |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | 8.x | OIDC JWT validation |
| `OtpNet` | 1.x | TOTP generation/validation |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Protos`
- `AgentPowerShell.Events`

---

### AgentPowerShell.Shim

**Purpose**: Lightweight binary shim that replaces `pwsh` in PATH. Compiled as native-AOT for fast startup and small binary size. Connects to daemon via IPC for all operations.

**Target Framework**: `net9.0` with `PublishAot = true`

**Key Classes**:

| Type | Description |
|------|-------------|
| `ShimMain` | Entry point: parse args, read config, connect to daemon, forward command |
| `ShimConfig` | Reads `agentpowershell-shim.conf` (daemon socket, real pwsh path, session ID) |
| `DaemonClient` | gRPC client over named pipe / Unix domain socket |
| `ShimInstaller` | Plan-based shim installation (PATH prepend, config write, backup real pwsh path) |
| `ShimUninstaller` | Reverses installation (restore PATH, remove config) |
| `McpDetector` | Detects MCP server launches via glob pattern matching on command args |
| `StdioBridge` | Wraps MCP server stdio for inspection when MCP server detected |
| `FallbackHandler` | Executes via real pwsh when daemon is unreachable (if fail-open configured) |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `Grpc.Net.Client` | 2.x | gRPC client |
| `Google.Protobuf` | 3.x | Protobuf serialization |

**Project References**:
- `AgentPowerShell.Protos`

**Build Notes**:
- Native AOT compilation for fast cold-start (<50ms target)
- Single-file publish, no .NET runtime dependency
- Trimmed for minimal binary size
- Platform-specific RIDs: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`

---

### AgentPowerShell.Cli

**Purpose**: CLI entry point providing the `agentpowershell` command with subcommands matching agentsh (exec, start, stop, session, policy, report, status, checkpoint, config, shim, llm-proxy, version).

**Target Framework**: `net9.0`

**Key Classes**:

| Type | Description |
|------|-------------|
| `Program` | Entry point, root command definition |
| `ExecCommand` | `exec <command>` -- execute through policy engine |
| `StartCommand` | `start` -- start daemon |
| `StopCommand` | `stop` -- stop daemon |
| `SessionCommand` | `session create/list/info/end` -- session management |
| `PolicyCommand` | `policy validate/list/show/test` -- policy management |
| `ReportCommand` | `report` -- generate audit reports |
| `StatusCommand` | `status` -- show daemon and session status |
| `CheckpointCommand` | `checkpoint create/restore/list` -- checkpoint management |
| `ConfigCommand` | `config show/validate/init` -- configuration management |
| `ShimCommand` | `shim install/uninstall/status` -- shim management |
| `LlmProxyCommand` | `llm-proxy start/status/providers` -- LLM proxy management |
| `VersionCommand` | `version` -- version information |
| `OutputFormatter` | Formats output as table, JSON, or CSV |
| `DaemonConnector` | Connects to running daemon via IPC for CLI operations |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `System.CommandLine` | 2.x | CLI parsing and subcommand routing |
| `Grpc.Net.Client` | 2.x | gRPC client for daemon communication |
| `Spectre.Console` | 0.49.x | Rich console output (tables, progress, colors) |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Protos`
- `AgentPowerShell.Daemon` (for `start` command which hosts the daemon in-process)

---

### AgentPowerShell.Events

**Purpose**: Event system including event types, pub/sub broker, and composite fan-out event store with SQLite, JSONL, webhook, and OTEL backends.

**Target Framework**: `net9.0`

**Key Classes**:

| Type | Description |
|------|-------------|
| `BaseEvent` | Abstract base event record with full metadata (identity, timestamps, platform, versioning) |
| `FileEvent` | File operation events (open, read, write, create, delete, rename, stat, chmod) |
| `NetworkEvent` | Network events (dns_query, net_connect, net_listen, dns_redirect) |
| `ProcessEvent` | Process events (start, spawn, exit, tree_kill) |
| `CommandEvent` | Command events (intercept, redirect, blocked, path_redirect) |
| `ShellEvent` | Shell events (invoke, passthrough, session_autostart) |
| `EnvEvent` | Environment variable events (read, write, list, blocked) |
| `ResourceEvent` | Resource limit events (set, warning, exceeded, snapshot) |
| `IpcEvent` | IPC events (socket connect/bind/blocked, pipe open/blocked) |
| `SignalEvent` | Signal events (sent, blocked, redirected, absorbed) |
| `McpEvent` | MCP events (tool_seen, tool_changed, tool_called, detection, intercepted) |
| `PackageEvent` | Package events (check_started, check_completed, blocked, approved) |
| `PolicyEvent` | Policy events (loaded, changed) |
| `TrashEvent` | Trash events (soft_delete, restore, purge) |
| `EventBroker` | Pub/sub broker with per-session `Channel<BaseEvent>` subscribers |
| `CompositeEventStore` | Fan-out store dispatching to all configured backends |
| `SqliteEventStore` | SQLite backend with batch insertion, event queries, MCP tool tracking |
| `JsonlEventStore` | Append-only JSONL with rotation (max size, max backups) |
| `WebhookEventStore` | HTTP POST with batching, flush interval, timeout, custom headers |
| `OtelEventStore` | OpenTelemetry export with filtering and span conversion |
| `IntegrityWrapper` | HMAC chain verification for tamper detection |
| `EventSerializer` | JSON serialization for events (System.Text.Json source generators) |
| `EventQuery` | Query model for event retrieval (session, type, time range, pagination) |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Data.Sqlite` | 9.x | SQLite event storage |
| `System.Threading.Channels` | 9.x | Pub/sub channels |
| `OpenTelemetry` | 1.x | OTEL export |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.x | OTLP exporter |
| `System.IO.Hashing` | 9.x | HMAC integrity chain |
| `Microsoft.Extensions.Logging.Abstractions` | 9.x | Logging |

**Project References**:
- `AgentPowerShell.Core`

---

### AgentPowerShell.LlmProxy

**Purpose**: HTTP reverse proxy for LLM API interception with DLP, rate limiting, MCP tool call inspection, and interaction storage.

**Target Framework**: `net9.0`

**Key Classes**:

| Type | Description |
|------|-------------|
| `LlmProxyService` | `IHostedService` that configures and runs the reverse proxy |
| `DialectDetector` | Auto-detects Anthropic / OpenAI / custom API formats from request |
| `DlpScanner` | Scans request/response content for sensitive patterns (API keys, PII, credentials) |
| `RateLimiter` | Per-provider RPM and TPM rate limiting with fallback token charging |
| `SseInterceptor` | Handles Server-Sent Events streaming, inspects chunks for MCP tool calls |
| `McpStreamInspector` | Extracts MCP tool calls from streamed LLM responses |
| `InteractionStore` | Persists LLM interactions for audit trail |
| `RetentionPolicy` | Configurable retention and cleanup for stored interactions |
| `ProviderRouter` | Routes requests to correct upstream based on path/headers |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `Yarp.ReverseProxy` | 2.x | High-performance reverse proxy |
| `System.Threading.RateLimiting` | 9.x | Rate limiting primitives |
| `Microsoft.Extensions.Logging.Abstractions` | 9.x | Logging |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Events`

---

### AgentPowerShell.Mcp

**Purpose**: MCP (Model Context Protocol) security monitoring -- tool discovery, content hashing, pattern detection, cross-server analysis, version pinning, and rate limiting.

**Target Framework**: `net9.0`

**Key Classes**:

| Type | Description |
|------|-------------|
| `McpInspector` | Parses `tools/list` responses, tracks tool definitions |
| `ContentHasher` | SHA-256 hashing of tool definitions for rug-pull detection |
| `PatternDetector` | Detects credential theft, exfiltration, hidden instructions, shell injection, path traversal |
| `CrossServerAnalyzer` | Detects coordinated attack patterns across MCP servers |
| `VersionPinner` | Tracks tool versions, alerts on changes |
| `McpRateLimiter` | Per-server rate limits on tool calls |
| `NameSimilarityChecker` | Levenshtein distance for server name spoofing detection |
| `McpRegistry` | Registry of known MCP servers and their tools for policy enforcement |
| `StdioBridge` | Wraps MCP server stdio for transparent inspection |
| `JsonRpcParser` | Parses JSON-RPC messages from MCP protocol |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `System.IO.Pipelines` | 9.x | High-performance stdio bridging |
| `Microsoft.Extensions.Logging.Abstractions` | 9.x | Logging |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Events`

---

### AgentPowerShell.Platform.Windows

**Purpose**: Windows-specific enforcement using Job Objects, minifilter driver communication, ETW, AppContainer, AMSI, ConPTY, and named pipe monitoring.

**Target Framework**: `net9.0-windows`

**Key Classes**:

| Type | Description |
|------|-------------|
| `WindowsPlatformEnforcer` | `IPlatformEnforcer` implementation for Windows |
| `JobObjectSandbox` | `IProcessSandbox` -- process containment via Job Objects (P/Invoke to kernel32.dll) |
| `JobObjectResourceLimiter` | `IResourceLimiter` -- CPU, memory, process count, working set limits |
| `MinifilterEnforcer` | `IFileSystemEnforcer` -- communication with agentsh minifilter driver via fltlib.dll |
| `MinifilterProtocol` | Message structures matching agentsh minifilter `protocol.h` |
| `EtwMonitor` | `IProcessMonitor` -- PowerShell, Kernel-Process, Kernel-File, Kernel-Network providers |
| `AppContainerSandbox` | `IProcessSandbox` -- lightweight sandbox with capability-based access (userenv.dll) |
| `NamedPipeMonitor` | `IIpcMonitor` -- named pipe interception and process identification |
| `AmsiIntegration` | AMSI scanning of script blocks (amsi.dll) |
| `ConPtyTerminal` | Pseudo console for terminal output capture (kernel32.dll) |
| `Win32PInvoke` | P/Invoke declarations (or CsWin32-generated) |
| `RegistryEnforcer` | Registry access enforcement via minifilter |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Diagnostics.Tracing.TraceEvent` | 4.x | ETW event consumption |
| `Microsoft.Windows.CsWin32` | 0.3.x | Auto-generated P/Invoke signatures |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Events`

---

### AgentPowerShell.Platform.Linux

**Purpose**: Linux-specific enforcement using cgroups v2, Landlock LSM, seccomp-bpf, ptrace (via native helper), eBPF, namespaces, and Unix socket monitoring.

**Target Framework**: `net9.0`

**Conditional compilation**: `RuntimeInformation.IsOSPlatform(OSPlatform.Linux)`

**Key Classes**:

| Type | Description |
|------|-------------|
| `LinuxPlatformEnforcer` | `IPlatformEnforcer` implementation for Linux |
| `CgroupSandbox` | `IProcessSandbox` -- cgroups v2 via filesystem operations |
| `CgroupResourceLimiter` | `IResourceLimiter` -- memory.max, cpu.max, pids.max via cgroup fs |
| `LandlockEnforcer` | `IFileSystemEnforcer` -- Landlock LSM via syscall P/Invoke (kernel 5.13+) |
| `SeccompFilter` | `IProcessSandbox` -- seccomp-bpf filter generation via libc P/Invoke |
| `SeccompUserNotify` | Seccomp user-notify supervisor for syscall interception |
| `PtraceTracer` | `IProcessMonitor` -- ptrace via native helper process for syscall tracing |
| `EbpfMonitor` | `INetworkEnforcer` -- pre-compiled eBPF programs for network monitoring |
| `NamespaceIsolation` | Network/mount/PID namespace isolation via unshare |
| `UnixSocketMonitor` | `IIpcMonitor` -- Unix domain socket + abstract namespace monitoring |
| `LinuxSyscall` | Raw syscall P/Invoke declarations |
| `FanotifyMonitor` | File access notification via fanotify (alternative to Landlock for monitoring) |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `Tmds.Linux` | 0.7.x | Raw Linux syscall access |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Events`

**Native Dependencies**:
- `libagentps-ptrace.so` -- Native helper for ptrace operations (C, compiled separately)
- `agentps-ebpf.o` -- Pre-compiled eBPF programs (C, compiled with clang)

---

### AgentPowerShell.Platform.MacOS

**Purpose**: macOS-specific enforcement using Endpoint Security Framework (via native dylib), sandbox-exec, FSEvents, Network Extension, RLIMIT, and XPC.

**Target Framework**: `net9.0`

**Conditional compilation**: `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)`

**Key Classes**:

| Type | Description |
|------|-------------|
| `MacOSPlatformEnforcer` | `IPlatformEnforcer` implementation for macOS |
| `EndpointSecurityMonitor` | `IProcessMonitor` -- ES Framework via native dylib P/Invoke |
| `SandboxExecEnforcer` | `IProcessSandbox` -- sandbox-exec with generated Seatbelt profiles |
| `SeatbeltProfileGenerator` | Generates .sb profiles from agentpowershell policy |
| `FsEventsMonitor` | `IFileSystemEnforcer` -- FSEvents via P/Invoke for file monitoring |
| `NetworkExtensionProxy` | `INetworkEnforcer` -- Network Extension via native helper |
| `RlimitResourceLimiter` | `IResourceLimiter` -- setrlimit for CPU, files, processes |
| `XpcServiceBridge` | XPC communication bridge for launchd integration |
| `UnixSocketMonitor` | `IIpcMonitor` -- Unix domain socket monitoring |
| `DarwinPInvoke` | P/Invoke declarations for macOS-specific APIs |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| (none beyond framework) | | |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Events`

**Native Dependencies**:
- `libagentps-es.dylib` -- Native helper for Endpoint Security Framework (Objective-C/C)
- `libagentps-netex.dylib` -- Native helper for Network Extension (Swift/Objective-C)

---

### AgentPowerShell.Tests

**Purpose**: Unit tests for all non-platform-specific code.

**Target Framework**: `net9.0`

**Key Test Classes**:

| Type | Description |
|------|-------------|
| `PolicyEngineTests` | Rule evaluation: first-match-wins, glob matching, regex, depth-aware |
| `PolicyLoaderTests` | YAML loading, validation, variable expansion, unknown field rejection |
| `SessionManagerTests` | Session CRUD, reaping, ID validation, checkpointing |
| `EventBrokerTests` | Pub/sub, slow subscriber dropping, channel lifecycle |
| `CompositeEventStoreTests` | Fan-out behavior, individual backend failures |
| `SqliteEventStoreTests` | Batch insertion, queries, rotation |
| `JsonlEventStoreTests` | Append, rotation, parsing |
| `ApprovalManagerTests` | Request lifecycle, timeout, rate limiting |
| `McpInspectorTests` | Tool discovery, rug-pull detection, pattern detection |
| `DlpScannerTests` | Pattern matching for sensitive content |
| `ShimConfigTests` | Config reading/writing |
| `McpDetectorTests` | MCP server launch detection |
| `ConfigLoaderTests` | Config parsing and validation |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `xunit` | 2.x | Test framework |
| `xunit.runner.visualstudio` | 2.x | Test runner |
| `Microsoft.NET.Test.Sdk` | 17.x | Test SDK |
| `Moq` | 4.x | Mocking framework |
| `FluentAssertions` | 7.x | Assertion library |
| `Verify.Xunit` | latest | Snapshot testing for serialization |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Events`
- `AgentPowerShell.LlmProxy`
- `AgentPowerShell.Mcp`
- `AgentPowerShell.Daemon`

---

### AgentPowerShell.IntegrationTests

**Purpose**: End-to-end integration tests: shim-to-daemon communication, policy enforcement, event flow, approval workflows.

**Target Framework**: `net9.0`

**Key Test Classes**:

| Type | Description |
|------|-------------|
| `ShimDaemonIntegrationTests` | Shim connects to daemon, executes commands, receives policy decisions |
| `PolicyEnforcementTests` | Commands evaluated against real policies |
| `EventFlowTests` | Events emitted and stored correctly through composite store |
| `ApprovalWorkflowTests` | End-to-end approval with mock approval provider |
| `SessionLifecycleTests` | Create, execute, checkpoint, restore, end |
| `LlmProxyIntegrationTests` | Proxy forwards requests, applies DLP, rate limits |
| `McpInspectionTests` | MCP server detection and tool monitoring |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `xunit` | 2.x | Test framework |
| `xunit.runner.visualstudio` | 2.x | Test runner |
| `Microsoft.NET.Test.Sdk` | 17.x | Test SDK |
| `Microsoft.AspNetCore.Mvc.Testing` | 9.x | HTTP test server |
| `Testcontainers` | 3.x | Container-based test isolation |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Daemon`
- `AgentPowerShell.Shim` (as process reference, not project reference)
- `AgentPowerShell.Events`

---

### AgentPowerShell.Platform.Tests

**Purpose**: Platform-specific enforcement tests. Uses conditional compilation and `[OSPlatform]` attributes to run tests only on applicable platforms.

**Target Framework**: `net9.0`

**Key Test Classes**:

| Type | Description |
|------|-------------|
| `JobObjectTests` | Windows: Job Object creation, process containment, resource limits |
| `MinifilterTests` | Windows: Minifilter communication, file/registry interception |
| `EtwMonitorTests` | Windows: ETW event capture for PowerShell and kernel providers |
| `CgroupTests` | Linux: cgroup v2 creation, resource limits, process assignment |
| `LandlockTests` | Linux: Landlock ruleset creation, filesystem enforcement |
| `SeccompTests` | Linux: seccomp filter generation, syscall blocking |
| `EndpointSecurityTests` | macOS: ES Framework event monitoring |
| `SandboxExecTests` | macOS: Seatbelt profile generation and enforcement |

**NuGet Dependencies**:

| Package | Version | Purpose |
|---------|---------|---------|
| `xunit` | 2.x | Test framework |
| `xunit.runner.visualstudio` | 2.x | Test runner |
| `Microsoft.NET.Test.Sdk` | 17.x | Test SDK |
| `FluentAssertions` | 7.x | Assertion library |

**Project References**:
- `AgentPowerShell.Core`
- `AgentPowerShell.Platform.Windows`
- `AgentPowerShell.Platform.Linux`
- `AgentPowerShell.Platform.MacOS`

---

## Dependency Graph

```
AgentPowerShell.Cli
  +-- AgentPowerShell.Core
  +-- AgentPowerShell.Protos
  +-- AgentPowerShell.Daemon
        +-- AgentPowerShell.Core
        +-- AgentPowerShell.Protos
        +-- AgentPowerShell.Events
              +-- AgentPowerShell.Core

AgentPowerShell.Shim
  +-- AgentPowerShell.Protos

AgentPowerShell.LlmProxy
  +-- AgentPowerShell.Core
  +-- AgentPowerShell.Events

AgentPowerShell.Mcp
  +-- AgentPowerShell.Core
  +-- AgentPowerShell.Events

AgentPowerShell.Platform.Windows
  +-- AgentPowerShell.Core
  +-- AgentPowerShell.Events

AgentPowerShell.Platform.Linux
  +-- AgentPowerShell.Core
  +-- AgentPowerShell.Events

AgentPowerShell.Platform.MacOS
  +-- AgentPowerShell.Core
  +-- AgentPowerShell.Events
```

## Build Configuration

### Global Properties (Directory.Build.props)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

### Shim AOT Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <PublishAot>true</PublishAot>
    <StripSymbols>true</StripSymbols>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IlcOptimizationPreference>Size</IlcOptimizationPreference>
  </PropertyGroup>
</Project>
```

### Platform-Specific Conditional Compilation

```xml
<!-- AgentPowerShell.Platform.Windows -->
<PropertyGroup>
  <TargetFramework>net9.0-windows</TargetFramework>
</PropertyGroup>

<!-- AgentPowerShell.Platform.Linux / MacOS -->
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
</PropertyGroup>
<ItemGroup Condition="'$(RuntimeIdentifier)' == 'linux-x64' or '$(RuntimeIdentifier)' == 'linux-arm64'">
  <None Include="native/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
