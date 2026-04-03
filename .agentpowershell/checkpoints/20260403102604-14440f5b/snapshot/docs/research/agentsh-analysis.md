# agentsh Architecture Analysis

> Research document for the agentpowershell project. Based on source code analysis of the agentsh codebase.

## 1. High-Level Architecture

agentsh is a Go-based policy-enforced execution gateway that sandboxes AI agent workloads. Its architecture follows a **daemon + shim + policy engine** pattern:

```
┌─────────────────────────────────────────────────────────────┐
│  AI Agent (Claude, etc.)                                    │
│    └── calls /bin/sh -c "some command"                      │
│          └── /bin/sh is actually the agentsh SHIM binary    │
│                └── shim contacts the DAEMON via IPC         │
│                      └── daemon evaluates POLICY            │
│                            └── allow / deny / approve       │
│                                  └── executes via real shell│
└─────────────────────────────────────────────────────────────┘
```

### Core Components

| Component | Location | Purpose |
|-----------|----------|---------|
| CLI | `cmd/agentsh/main.go`, `internal/cli/` | Entry point, subcommands via cobra |
| Server/Daemon | `internal/server/server.go` | gRPC + HTTP + Unix socket daemon |
| Policy Engine | `internal/policy/` | YAML-based rule evaluation |
| Shell Shim | `internal/shim/`, `shim/` | Binary that replaces `/bin/sh` |
| Session Manager | `internal/session/` | Session lifecycle, workspace mounts |
| Event System | `internal/events/` | Event types, pub/sub broker |
| Event Store | `internal/store/` | SQLite, JSONL, webhook, OTEL backends |
| Approvals | `internal/approvals/` | Human-in-the-loop approval flow |
| LLM Proxy | `internal/llmproxy/` | HTTP proxy for LLM API interception |
| MCP Inspection | `internal/mcpinspect/` | MCP protocol security monitoring |
| FS Monitor | `internal/fsmonitor/` | FUSE-based filesystem interception |
| Net Monitor | `internal/netmonitor/` | DNS interception, eBPF network monitoring |
| ptrace Tracer | `internal/ptrace/` | Syscall-level interception (Linux) |
| seccomp | `internal/seccomp/` | seccomp-bpf filter generation |
| Landlock | `internal/landlock/` | Landlock LSM integration (Linux) |
| IPC Monitor | `internal/ipc/` | Unix socket / named pipe monitoring |
| Minifilter Driver | `drivers/windows/agentsh-minifilter/` | Windows kernel minifilter for FS/registry |
| Platform | `internal/platform/` | OS-specific implementations (darwin, linux, windows) |

## 2. Daemon Architecture

### Server (`internal/server/server.go`)

The daemon is a long-running process that exposes three transport interfaces:

1. **HTTP API** (`127.0.0.1:18080`) -- REST endpoints for session management, exec, events
2. **gRPC** (optional, `127.0.0.1:9090`) -- High-performance streaming for exec and event tailing (`CreateSession`, `Exec`, `ExecStream`, `EventsTail`)
3. **Unix Socket** (`./data/agentsh.sock`) -- Low-latency local IPC, same HTTP API surface

The `Server` struct holds:
- `store *composite.Store` -- Fan-out event storage (SQLite + JSONL + webhook + OTEL)
- `broker *events.Broker` -- Pub/sub event distribution
- `sessions *session.Manager` -- Session lifecycle
- Session/idle timeout reaping
- Optional threat feed syncer
- Optional pprof HTTP endpoint

### Startup Flow

1. Load and validate config (`config.yml`)
2. Create policy manager, load default policy, compile engine
3. Check kernel capabilities (ptrace, seccomp user-notify, eBPF availability)
4. Open event stores (SQLite, JSONL, webhook, OTEL)
5. Create session manager
6. Start HTTP/gRPC/Unix socket listeners
7. Start optional threat feed syncer
8. Install signal handlers for graceful shutdown

### Authentication

Supports multiple auth modes (`internal/auth/`):
- `none` -- Local dev mode (refuses non-loopback binding)
- `api_key` -- Header-based API key (`X-API-Key`)
- `oidc` -- Enterprise SSO via OIDC with group-to-policy mapping
- `hybrid` -- Combined API key + OIDC
- `webauthn` -- Hardware key authentication for approvals

Critical safety check: API-mode approvals require authentication to prevent self-approval by agents.

## 3. Shell Shim Mechanism

### How It Works

The shim replaces `/bin/sh` (and optionally `/bin/bash`) by:

1. **Rename** the original shell to `/bin/sh.real`
2. **Write** the agentsh shim binary as `/bin/sh`
3. Store configuration in `/etc/agentsh/shim.conf`

When an AI agent or any process invokes `/bin/sh -c "command"`, it actually runs the shim, which:
1. Reads the session ID from environment (`AGENTSH_SESSION_ID`)
2. Contacts the daemon via IPC
3. The daemon evaluates the command against policy
4. If allowed, the daemon executes via the real shell (`/bin/sh.real`)
5. Returns output/exit code back through the shim

### Shim Installation (`internal/shim/install.go`)

```go
type InstallShellShimOptions struct {
    Root        string  // Filesystem root (default "/")
    ShimPath    string  // Path to shim binary
    InstallBash bool    // Also replace /bin/bash
    BashOnly    bool    // Only replace bash, leave sh alone
    Force       bool    // Write force=true to shim.conf
}
```

The installation is:
- **Idempotent**: Checks if already installed, handles missing `.real` files
- **Atomic**: Uses atomic file writes for config
- **Plan-based**: `PlanInstallShellShim` generates a plan of actions before executing
- **Reversible**: `PlanUninstallShellShim` restores original binaries

### MCP Server Detection

The shim detects MCP server launches using glob patterns:
- `@modelcontextprotocol/*`
- `mcp-server-*`
- `*-mcp-server`
- `mcp_server_*`

When detected, the shim wraps stdio with an inspection bridge for tool poisoning detection.

### Platform-Specific Shim Binaries

Located in `shim/`:
- `shim/linux/envshim.c` -- C-based LD_PRELOAD shim for environment variable interception
- `shim/darwin/envshim.c` -- macOS DYLD_INSERT_LIBRARIES equivalent
- `shim/windows/envshim.cpp` + `inject.cpp` -- Windows DLL injection

## 4. Policy Engine Internals

### Policy Model (`internal/policy/model.go`)

Policies are YAML files with this top-level structure:

```yaml
version: 1
name: "policy-name"
description: "..."

file_rules: [...]        # File operation rules
network_rules: [...]     # Network access rules
command_rules: [...]     # Command execution rules
unix_socket_rules: [...] # Unix domain socket rules
registry_rules: [...]    # Windows registry rules (Windows only)
signal_rules: [...]      # Inter-process signal rules
dns_redirects: [...]     # DNS interception/redirect rules
connect_redirects: [...]  # TCP connection redirect rules
resource_limits: {...}   # Memory, CPU, disk, PID limits
env_policy: {...}        # Environment variable filtering
audit: {...}             # Audit settings
env_inject: {...}        # Environment injection
mcp_rules: {...}         # MCP tool/server allowlists
process_contexts: {...}  # Parent-conditional policies
process_identities: {...} # Cross-platform process identification
package_rules: [...]     # Package install checks
transparent_commands: {...} # Wrapper commands (env, sudo)
```

### Rule Types

**FileRule**: Path glob matching + operation type (read, write, create, delete, rename, chmod, stat, list). Supports redirect_to for path redirection and preserve_tree for maintaining directory structure.

**NetworkRule**: Domain glob, CIDR range, and port matching.

**CommandRule**: Command basename/path matching (exact or glob), argument regex patterns, depth-aware context (direct vs nested), environment filtering (allow/deny patterns per command).

**UnixSocketRule**: Socket path glob matching, operation types (connect, bind, listen, sendto).

**RegistryRule** (Windows): Registry path glob matching, operation types (read, write, delete, create, rename), priority ordering, per-rule cache TTL.

**SignalRule**: Signal name/number/group matching (@fatal, @job), target specification (self, children, external, system), decisions including redirect and absorb.

**DnsRedirectRule**: Regex hostname matching, IP rewrite, visibility modes (silent, audit_only, warn), failure policies.

**ConnectRedirectRule**: Regex host:port matching, destination rewrite, TLS handling (passthrough, rewrite_sni).

### Decision Types

```go
type Decision struct {
    PolicyDecision    types.Decision  // What the policy says
    EffectiveDecision types.Decision  // Actual enforcement after approval
    Rule              string          // Matching rule name
    Message           string          // User-facing message
    Approval          *types.ApprovalInfo
    Redirect          *types.RedirectInfo
    FileRedirect      *types.FileRedirectInfo
    EnvPolicy         ResolvedEnvPolicy
    ThreatFeed        string          // Threat feed match info
}
```

Decision values: `allow`, `deny`, `approve` (requires human approval), `audit` (allow but log), `redirect`, `absorb`.

### Evaluation Order

1. Rules are evaluated **in YAML order** (first match wins)
2. Registry rules are sorted by **priority** (higher first)
3. Process contexts can override global rules based on parent identity
4. Chain rules in process contexts are evaluated before context-specific rules
5. Threat feed check overlays domain-level deny/audit on top of regular rules

### Engine Compilation (`internal/policy/engine.go`)

At startup, the engine pre-compiles all rules:
- File paths -> `gobwas/glob` compiled patterns
- Domain patterns -> glob with '.' separator
- CIDRs -> `net.IPNet` objects
- Command names -> basename maps + glob patterns + full path maps
- Arg patterns -> compiled `regexp.Regexp`
- Registry paths -> escaped glob patterns (backslash handling for Windows)
- DNS/connect redirects -> compiled regex patterns
- Environment allow/deny -> compiled glob patterns

### Depth-Aware Rules (`internal/policy/rules.go`)

The `ContextConfig` supports depth-based policy differentiation:
- `direct` (depth 0): User-typed commands
- `nested` (depth 1+): Script-spawned/sub-process commands
- `min_depth` / `max_depth`: Fine-grained depth ranges

### Process Context System

The `ProcessContext` enables parent-conditional policies:
- **Identity matching**: Cross-platform process identification (comm, exe_path, cmdline, bundle_id, exe_name)
- **Chain rules**: Ancestry-based escape hatch detection with logical composition (AND/OR/NOT)
- **Consecutive pattern detection**: Detects shell laundering (e.g., repeated shell-to-shell chains)
- **Taint propagation**: Tracks whether processes descend from AI tools
- **Race policy**: Configurable handling for PID recycling and validation errors

### Variable Expansion

`NewEngineWithVariables` supports policy templates with variables like `${PROJECT_ROOT}`, expanded at session creation.

### Policy Loading and Signing

- `LoadFromFile`/`LoadFromBytes` with strict YAML parsing (unknown fields rejected)
- Policy manager supports a manifest-based trust model with signing verification
- Multiple policy resolution: by name, environment variable, allowed list

## 5. Event System

### Event Types (`internal/events/types.go`)

Comprehensive event taxonomy covering 70+ event types across categories:

| Category | Events |
|----------|--------|
| File | open, read, write, create, delete, rename, stat, chmod, dir_create, dir_delete, dir_list |
| Network | dns_query, net_connect, net_listen, net_accept, dns_redirect, connect_redirect |
| Process | process_start, process_spawn, process_exit, process_tree_kill |
| Environment | env_read, env_write, env_list, env_blocked |
| Trash | soft_delete, trash_restore, trash_purge |
| Shell | shell_invoke, shell_passthrough, session_autostart |
| Command | command_intercept, command_redirect, command_blocked, path_redirect |
| Resource | resource_limit_set, resource_limit_warning, resource_limit_exceeded, resource_usage_snapshot |
| IPC | unix_socket_connect, unix_socket_bind, unix_socket_blocked, named_pipe_open, named_pipe_blocked |
| Seccomp | seccomp_blocked, notify_handler_panic |
| Signal | signal_sent, signal_blocked, signal_redirected, signal_absorbed |
| MCP | mcp_tool_seen, mcp_tool_changed, mcp_tool_called, mcp_detection, mcp_tool_call_intercepted, mcp_cross_server_blocked |
| Package | package_check_started, package_check_completed, package_blocked, package_approved, package_warning |
| Policy | policy_loaded, policy_changed |

### Base Event (`internal/events/base.go`)

Every event is self-contained with rich metadata:
- **Identity**: hostname, machine_id, container_id, K8s namespace/pod/node/cluster
- **Network**: IPv4/IPv6 addresses, primary interface, MAC address
- **Timestamps**: RFC3339, Unix microseconds, monotonic nanoseconds, sequence number
- **OS**: OS, version, distro, kernel, architecture
- **Platform**: variant, FS/net/process/IPC backend identifiers
- **Versioning**: agentsh version/commit, event schema version, policy version

### Event Broker (`internal/events/broker.go`)

Pub/sub system using Go channels:
- Per-session subscriber maps
- Buffered channels (default 100)
- Drop-on-slow-subscriber with atomic counter and periodic logging
- Thread-safe subscribe/unsubscribe/publish

### Event Store (`internal/store/`)

Composite fan-out store supporting multiple backends simultaneously:
- **SQLite** (`store/sqlite/`) -- Primary queryable store with batch insertion, for event queries and MCP tool tracking
- **JSONL** (`store/jsonl/`) -- Append-only log with rotation (max size, max backups)
- **Webhook** (`store/webhook/`) -- HTTP webhook with batching, flush interval, timeout
- **OTEL** (`store/otel/`) -- OpenTelemetry export with filtering and span conversion
- **Integrity wrapper** -- HMAC chain verification for tamper detection (KMS-backed)

## 6. Platform Enforcement Strategies

### Linux

**Primary: ptrace-based syscall tracer** (`internal/ptrace/`)

Intercepts four syscall categories:
- **Exec**: `execve`, `execveat` -- command allow/deny/redirect
- **File**: `openat`, `openat2`, `unlinkat`, `renameat2`, `mkdirat`, `linkat`, `symlinkat`, `fchmodat`, `fchownat` + legacy amd64 equivalents
- **Network**: `connect`, `bind` -- network allow/deny/redirect with sockaddr parsing (AF_INET, AF_INET6, AF_UNIX, AF_UNSPEC)
- **Signal**: `kill`, `tkill`, `tgkill`, `rt_sigqueueinfo`, `rt_tgsigqueueinfo`

Syscall steering engine capabilities:
- **Exec redirect**: Via fd injection (`pidfd_open` + `pidfd_getfd` + `dup3`) and filename rewrite
- **File path redirect**: Scratch page allocation (per-TGID mmap'd memory with bump allocator)
- **Soft-delete**: Intercepts unlink, injects mkdirat + renameat2 to move to trash
- **Connect redirect**: In-place sockaddr rewrite (fixed-size structs)
- Architecture support: amd64 and arm64

Production hardening:
- `max_hold_ms` timeout for parked tracees (async approval)
- Prometheus metrics: `agentsh_ptrace_tracees_active`, `agentsh_ptrace_attach_failures_total`, `agentsh_ptrace_timeouts_total`
- Graceful degradation for dead tracees

**Secondary: seccomp-bpf** (`internal/seccomp/`)

Generates BPF filters for syscall blocking. Used alongside ptrace for defense-in-depth.

**Tertiary: Landlock LSM** (`internal/landlock/`)

Linux Security Module for filesystem access control at the kernel level. Provides mandatory access control that survives privilege escalation.

**Filesystem: FUSE** (`internal/fsmonitor/fuse.go`)

FUSE-based loopback filesystem with policy hooks. Intercepts all file operations at the VFS level:
- Creates a monitored loopback mount over the workspace
- Each node operation checks policy before proceeding
- Integrates with approval manager and trash system

**Network: eBPF + DNS** (`internal/netmonitor/`)

- eBPF-based network monitoring with cgroup attachment
- UDP DNS interceptor with custom resolver
- DNS cache for correlation
- Connect redirect via correlation maps

### macOS (`internal/platform/darwin/`)

- **Endpoint Security Framework** (`es_exec.go`): Process execution monitoring via Apple's ES API
- **FSEvents** (`fsevents.go`): Filesystem event monitoring
- **Mach monitor** (`mach_monitor.go`, `mach_monitor_cgo.go`): Mach port monitoring via CGo
- **Network monitoring** (`network.go`): Network Extension or socket-level monitoring
- **CPU monitoring** (`cpu_monitor.go`): Resource usage tracking
- **Notifications** (`notify.go`): macOS notification center integration

### Windows

**Minifilter Driver** (`drivers/windows/agentsh-minifilter/`)

A kernel-mode filesystem minifilter driver that intercepts file and registry operations:

Communication protocol (`protocol.h`):
- **Driver -> User-mode**: `MSG_POLICY_CHECK_FILE`, `MSG_POLICY_CHECK_REGISTRY`, `MSG_PROCESS_CREATED`, `MSG_PROCESS_TERMINATED`
- **User-mode -> Driver**: `MSG_REGISTER_SESSION`, `MSG_UNREGISTER_SESSION`, `MSG_UPDATE_CACHE`, `MSG_SET_CONFIG`, `MSG_EXCLUDE_PROCESS`

File operations intercepted: CREATE, READ, WRITE, DELETE, RENAME
Registry operations intercepted: CREATE_KEY, SET_VALUE, DELETE_KEY, DELETE_VALUE, RENAME_KEY, QUERY_VALUE

Decision model: ALLOW, DENY, PENDING (async)

Configuration features:
- Fail mode: open (allow on failure) or closed (deny on failure)
- Policy query timeout (default 5000ms)
- Max consecutive failures before fallback
- Per-rule cache with configurable TTL and max entries (default 4096)

Metrics exposed: cache hit/miss/eviction counts, policy query counts/timeouts/failures, allow/deny decisions, active sessions, tracked processes.

**IPC Monitor (Windows)** (`internal/ipc/monitor_windows.go`):
Named pipe monitoring with capabilities for process identification.

## 7. Session Management

### Session (`internal/session/manager.go`)

```go
type Session struct {
    ID             string
    State          types.SessionState
    CreatedAt      time.Time
    LastActivity   time.Time
    Workspace      string
    WorkspaceMount string
    Policy         string
    Cwd            string
    VirtualRoot    string
    Env            map[string]string
    History        []string
    // LLM proxy, MCP registry, net namespace, mounts...
}
```

Key features:
- **Workspace isolation**: Each session gets a workspace directory, optionally FUSE-mounted
- **Virtual root**: `/workspace` abstraction over real paths
- **Multi-mount support**: Profile-based mount configurations
- **Per-session policy engine**: With variable expansion (e.g., `${PROJECT_ROOT}`)
- **LLM Proxy**: Per-session proxy URL for `ANTHROPIC_BASE_URL`/`OPENAI_BASE_URL`
- **MCP Registry**: Per-session MCP server tracking
- **Network namespace**: Optional per-session network isolation
- **TOTP secret**: For TOTP-based approval
- **Project detection**: Auto-detection of project root and git root
- **Checkpointing**: Auto-checkpoint and manual checkpoint/restore
- **Lifecycle**: Active, paused, ended states with statistics tracking

### Session Manager

- Thread-safe session CRUD with mutex
- Session ID validation (alphanumeric + `_-`, 1-128 chars)
- UUID-based session IDs
- Reaping of expired/idle sessions

## 8. Approval Flow

### Approval Manager (`internal/approvals/manager.go`)

Modes:
- `local_tty` -- Interactive TTY prompt
- `api` -- Remote approval via API endpoint (requires auth)
- `totp` -- Time-based one-time password
- `webauthn` -- Hardware key authentication
- `dialog` -- Platform-native dialog boxes

Request structure:
```go
type Request struct {
    ID        string
    CreatedAt time.Time
    ExpiresAt time.Time
    SessionID string
    CommandID string
    Kind      string   // "command" | "file" | "network"
    Target    string
    Rule      string
    Message   string
    Fields    map[string]any
}
```

Features:
- Configurable timeout (default 5 minutes)
- Rate limiting per session (max concurrent approvals)
- Pending request tracking with channels
- Platform-specific UI dialogs (`internal/approval/dialog/`): native dialogs on macOS, Linux, Windows
- Desktop notifications (`internal/approval/notify/`): platform-specific notification APIs

### WebAuthn Support (`internal/approvals/webauthn.go`, `internal/auth/webauthn.go`)

Hardware security key integration for high-assurance approval flows.

## 9. LLM Proxy and MCP Integration

### LLM Proxy (`internal/llmproxy/`)

An HTTP reverse proxy that intercepts LLM API traffic:

- **Dialect detection**: Auto-detects Anthropic, OpenAI, and custom API formats
- **DLP (Data Loss Prevention)**: Scans request/response content
- **Rate limiting**: Per-server RPM and TPM limits with fallback token charging
- **MCP interception**: Inspects MCP tool calls in LLM API streams
- **SSE interception**: Handles Server-Sent Events streaming
- **Storage**: Persists LLM interactions
- **Retention**: Configurable retention policies

### MCP Inspection (`internal/mcpinspect/`)

Security monitoring for MCP (Model Context Protocol):

- **Tool discovery**: Parses `tools/list` responses, tracks tool definitions
- **Content hashing**: Detects "rug pull" attacks (tool definition changes)
- **Pattern detection**: Credential theft, exfiltration, hidden instructions, shell injection, path traversal
- **Cross-server analysis**: Detects attack patterns across MCP servers
- **Version pinning**: Tracks tool versions, alerts on changes
- **Rate limiting**: Per-server rate limits on tool calls
- **Name similarity**: Detects server name spoofing

### MCP Registry (`internal/mcpregistry/`)

Maintains a registry of known MCP servers and their tools for policy enforcement.

## 10. Network and Filesystem Monitoring

### Network Monitoring (`internal/netmonitor/`)

- **DNS Interceptor**: UDP-based DNS proxy that evaluates domain lookups against policy, with approval integration and redirect support
- **DNS Cache**: Correlates DNS resolutions with subsequent connections
- **eBPF Collector** (`netmonitor/ebpf/`): Linux-specific cgroup-attached eBPF programs for network monitoring
- **Connect Redirect**: Transparent TCP connection redirection with correlation maps

### Filesystem Monitoring (`internal/fsmonitor/`)

- **FUSE Loopback**: Policy-enforced filesystem overlay via FUSE
- **Audit hooks**: File operation audit trail
- **Path resolution**: Cross-platform path normalization
- **Soft-delete/Trash**: Files marked for deletion are moved to quarantine instead (`internal/trash/`)

### IPC Monitoring (`internal/ipc/`)

Platform-abstracted interface for monitoring inter-process communication:

```go
type IPCMonitor interface {
    Start(ctx context.Context) error
    Stop() error
    OnSocketConnect(func(event SocketEvent))
    OnSocketBind(func(event SocketEvent))
    OnPipeOpen(func(event PipeEvent))
    ListConnections() []Connection
    Capabilities() MonitorCapabilities
}
```

Platform implementations: Linux (Unix sockets + abstract namespace), macOS (Unix sockets), Windows (named pipes), with a fallback for unsupported platforms.

## 11. Configuration Format

### `config.yml` -- Server/daemon configuration

```yaml
server:
  http: {addr, read_timeout, write_timeout, max_request_size}
  grpc: {enabled, addr}
  unix_socket: {enabled, path, permissions}
  tls: {enabled, cert_file, key_file, ca_file, client_auth}
auth:
  type: "none" | "api_key" | "oidc" | "hybrid"
  api_key: {keys_file, header_name}
  oidc: {issuer, client_id, audience, claim_mappings, allowed_groups, group_policy_map}
logging: {...}
sessions: {base_dir, ...}
policies: {dir, default, allowed, manifest_path, signing}
approvals: {enabled, mode, timeout}
sandbox: {...}
audit:
  output: "path/to/events.jsonl"
  storage: {enabled, sqlite_path, batch_size, flush_interval}
  rotation: {max_size_mb, max_backups}
  integrity: {enabled}
  webhook: {url, batch_size, flush_interval, timeout, headers}
  otel: {enabled, timeout, tls}
threat_feeds: {enabled, cache_dir, allowlist, action}
```

### `default-policy.yml` -- Policy rules

First-match-wins rule evaluation. The default policy provides:
- Full workspace read/write access, delete requires approval
- Full `/tmp` access
- Read-only system paths (`/usr`, `/lib`)
- Blocked sensitive paths (`/etc/shadow`, SSH keys)
- Network allowlist for common package registries
- Command allowlist for common dev tools

## 12. Key Architectural Patterns for agentpowershell

### Patterns to Adopt

1. **Daemon + Shim separation**: Shim is lightweight, daemon holds all state and logic
2. **First-match-wins policy evaluation**: Simple, predictable, auditable
3. **Compiled rules**: Pre-compile globs/regexes at engine creation, not per-evaluation
4. **Composite event store**: Fan-out to multiple backends simultaneously
5. **Session-scoped policy engines**: Variable expansion per session context
6. **Platform abstraction interfaces**: Same IPC monitor interface, different implementations
7. **Plan-then-execute**: Shim installation generates a plan before mutating state
8. **Fail-safe modes**: Configurable fail-open vs fail-closed with consecutive failure tracking

### Key Differences for PowerShell

1. agentsh replaces `/bin/sh` binary; PowerShell requires a different interception strategy (PSHost, module, or profile)
2. agentsh uses ptrace/seccomp (Linux-specific); .NET needs Job Objects (Windows), AppContainers, or ETW
3. agentsh minifilter driver is C; agentpowershell should use P/Invoke to communicate with it
4. agentsh uses FUSE for filesystem; Windows equivalent would be minifilter or projected filesystem
5. agentsh has env var shims via LD_PRELOAD; PowerShell has native env var scoping
