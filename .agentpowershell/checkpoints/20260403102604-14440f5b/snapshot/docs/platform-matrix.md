# Platform Enforcement Matrix

> Detailed mapping of each enforcement mechanism per platform with implementation details, .NET API surface, and maturity assessment.

## Legend

| Maturity | Description |
|----------|-------------|
| **Stable** | Well-established API, production-ready, low risk |
| **Mature** | Established API, minor platform quirks, moderate risk |
| **Experimental** | Newer API or complex P/Invoke, higher implementation risk |
| **Native Required** | Requires a native helper binary (C/Objective-C/Swift), cannot be done purely in .NET |
| **N/A** | Not applicable to this platform |

---

## Process Containment

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Process group containment** | Job Objects (`CreateJobObject`, `AssignProcessToJobObject` via kernel32.dll) | cgroups v2 (filesystem operations on `/sys/fs/cgroup/`) | sandbox-exec with Seatbelt profiles (`/usr/bin/sandbox-exec -f profile.sb`) | Win: P/Invoke kernel32.dll; Linux: `System.IO.File` on cgroupfs; macOS: `Process.Start` sandbox-exec | Win: **Stable**; Linux: **Stable**; macOS: **Mature** (deprecated API) |
| **Kill-on-close** | `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` flag on Job Object | cgroup `kill` file (`echo 1 > cgroup.kill`) | `kill -TERM -pgid` (process group) | Win: P/Invoke `SetInformationJobObject`; Linux: filesystem; macOS: `Process.Kill()` | Win: **Stable**; Linux: **Stable**; macOS: **Mature** |
| **Process creation limit** | `JOB_OBJECT_LIMIT_ACTIVE_PROCESS` on Job Object | `pids.max` in cgroups v2 | `RLIMIT_NPROC` via `setrlimit` | Win: P/Invoke kernel32.dll; Linux: filesystem; macOS: P/Invoke libc | Win: **Stable**; Linux: **Stable**; macOS: **Stable** |
| **Breakaway prevention** | Clear `JOB_OBJECT_LIMIT_BREAKAWAY_OK` and `SILENT_BREAKAWAY_OK` | Not needed (cgroup inheritance automatic) | Not needed (sandbox inherited) | Win: P/Invoke kernel32.dll | Win: **Stable**; Linux: N/A; macOS: N/A |
| **AppContainer sandbox** | `CreateAppContainerProfile` + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` via userenv.dll / kernel32.dll | N/A (use namespaces) | N/A | Win: P/Invoke userenv.dll + kernel32.dll | Win: **Mature** |
| **Namespace isolation** | N/A | `unshare(CLONE_NEWNET \| CLONE_NEWNS \| CLONE_NEWPID)` via libc | N/A | Linux: P/Invoke libc `unshare` / `setns` | Linux: **Mature** |

---

## Process Creation Control

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Suspend-assign-resume** | `CreateProcessW` with `CREATE_SUSPENDED`, assign to Job Object, then `ResumeThread` | `clone` + ptrace `PTRACE_ATTACH` before exec | ES Framework `ES_EVENT_TYPE_AUTH_EXEC` | Win: P/Invoke kernel32.dll; Linux: P/Invoke libc; macOS: Native dylib | Win: **Stable**; Linux: **Experimental**; macOS: **Native Required** |
| **Syscall interception** | N/A (use ETW for monitoring) | ptrace `PTRACE_SYSCALL` with register inspection / seccomp `SECCOMP_USER_NOTIF` | N/A | Linux: P/Invoke libc (ptrace), or native helper process | Linux: **Experimental** (complex threading) |
| **Process execution auth** | ETW `Microsoft-Windows-Kernel-Process` (notify only, cannot block) | seccomp-bpf `SECCOMP_RET_USER_NOTIF` for sync blocking | ES Framework `ES_EVENT_TYPE_AUTH_EXEC` (sync blocking) | Win: `TraceEvent` NuGet; Linux: P/Invoke libc; macOS: native dylib | Win: **Mature** (notify only); Linux: **Experimental**; macOS: **Native Required** |
| **Completion port notifications** | Job Object I/O completion port (`JOBOBJECT_ASSOCIATE_COMPLETION_PORT`) | cgroup `cgroup.events` / inotify on cgroup files | N/A | Win: P/Invoke kernel32.dll `GetQueuedCompletionStatus`; Linux: `FileSystemWatcher` | Win: **Stable**; Linux: **Mature** |

---

## File System Enforcement

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Kernel-level file blocking** | Minifilter driver (agentsh-minifilter) -- intercepts CREATE, READ, WRITE, DELETE, RENAME | Landlock LSM (`landlock_create_ruleset`, `landlock_add_rule`, `landlock_restrict_self`) syscalls 444-446 | Endpoint Security `ES_EVENT_TYPE_AUTH_OPEN`, `AUTH_RENAME` | Win: P/Invoke fltlib.dll (`FilterConnectCommunicationPort`, `FilterGetMessage`, `FilterReplyMessage`); Linux: P/Invoke libc syscall(); macOS: native dylib | Win: **Mature** (requires driver); Linux: **Mature** (kernel 5.13+); macOS: **Native Required** |
| **Async policy decisions** | Minifilter `MSG_POLICY_CHECK_FILE` with PENDING response | seccomp `SECCOMP_USER_NOTIF` for async file syscall decisions | ES Framework deferred response | Win: fltlib.dll async message loop; Linux: seccomp notify fd; macOS: native helper | Win: **Mature**; Linux: **Experimental**; macOS: **Native Required** |
| **Path redirect** | Minifilter reparse point or redirect at CREATE | ptrace syscall argument rewrite (scratch page + `PTRACE_POKEDATA`) | ES Framework cannot redirect (deny + re-exec to new path) | Win: fltlib.dll; Linux: ptrace P/Invoke; macOS: native workaround | Win: **Mature**; Linux: **Experimental**; macOS: **Experimental** |
| **Soft delete (trash)** | Minifilter intercepts DELETE, redirects to trash directory | ptrace intercepts `unlinkat`, injects `mkdirat` + `renameat2` to trash | Intercept at shell level (proxy `Remove-Item` cmdlet) | Win: fltlib.dll; Linux: ptrace + syscall injection; macOS: proxy cmdlet | Win: **Mature**; Linux: **Experimental**; macOS: **Stable** (limited) |
| **File system monitoring** | ETW `Microsoft-Windows-Kernel-File` or minifilter notifications | inotify / fanotify / eBPF | FSEvents framework | Win: `TraceEvent` NuGet or `FileSystemWatcher`; Linux: `FileSystemWatcher` or P/Invoke; macOS: `FileSystemWatcher` | Win: **Stable**; Linux: **Stable**; macOS: **Stable** |
| **FUSE overlay** | N/A (use minifilter or Projected FS) | FUSE loopback filesystem with policy hooks | FUSE via macFUSE (third-party) | Linux: native FUSE helper; macOS: macFUSE (third-party, not recommended) | Linux: **Mature**; macOS: **Experimental** |

---

## Registry Enforcement (Windows Only)

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Registry operation blocking** | Minifilter driver -- intercepts CREATE_KEY, SET_VALUE, DELETE_KEY, DELETE_VALUE, RENAME_KEY, QUERY_VALUE | N/A | N/A | P/Invoke fltlib.dll (same communication channel as file enforcement) | **Mature** (requires driver) |
| **Registry monitoring** | ETW `Microsoft-Windows-Kernel-Registry` provider | N/A | N/A | `TraceEvent` NuGet | **Stable** |
| **Per-rule cache with TTL** | Minifilter in-kernel cache (configurable max entries, default 4096) | N/A | N/A | Cache managed in driver, configured via `MSG_UPDATE_CACHE` | **Mature** |

---

## Network Enforcement

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Outbound connection blocking** | AppContainer network isolation (no network by default, add `internetClient` for outbound) or WFP (Windows Filtering Platform) | eBPF cgroup-attached programs + seccomp on `connect` / Landlock `LANDLOCK_ACCESS_NET_CONNECT_TCP` (kernel 6.8+) | Network Extension `NEFilterDataProvider` or sandbox-exec `deny network*` | Win: P/Invoke; Linux: eBPF via native helper + P/Invoke libc; macOS: native System Extension | Win: **Mature**; Linux: **Mature** (eBPF) / **Experimental** (Landlock net, kernel 6.8+); macOS: **Native Required** |
| **DNS interception** | ETW `Microsoft-Windows-DNS-Client` (monitor) or Winsock LSP (intercept) | eBPF UDP interceptor / iptables REDIRECT to local DNS proxy | Network Extension `NEDNSProxyProvider` | Win: `TraceEvent` NuGet; Linux: native eBPF or `Process.Start("iptables")`; macOS: native System Extension | Win: **Mature** (monitor only); Linux: **Mature**; macOS: **Native Required** |
| **DNS redirect** | Custom DNS resolver via `DnsQuery_A` P/Invoke or transparent proxy | eBPF-based DNS rewrite / local DNS proxy on loopback | Network Extension DNS proxy | Win: P/Invoke dnsapi.dll; Linux: eBPF / socket; macOS: native helper | Win: **Experimental**; Linux: **Mature**; macOS: **Native Required** |
| **Connect redirect** | WFP redirect or transparent proxy | eBPF connect redirect / ptrace sockaddr rewrite | Network Extension transparent proxy | Win: WFP driver; Linux: eBPF + correlation maps; macOS: native helper | Win: **Experimental** (needs WFP driver); Linux: **Mature**; macOS: **Native Required** |
| **Network monitoring** | ETW `Microsoft-Windows-Kernel-Network` | eBPF / conntrack / `/proc/net` | Network Extension | Win: `TraceEvent` NuGet; Linux: P/Invoke or filesystem; macOS: native helper | Win: **Stable**; Linux: **Stable**; macOS: **Native Required** |

---

## Resource Limits

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Memory limit** | Job Object `JOBOBJECT_EXTENDED_LIMIT_INFORMATION.ProcessMemoryLimit` / `JobMemoryLimit` | cgroups v2 `memory.max` | `RLIMIT_DATA` via `setrlimit` (limited effectiveness) | Win: P/Invoke kernel32.dll; Linux: `File.WriteAllText` on cgroupfs; macOS: P/Invoke libc | Win: **Stable**; Linux: **Stable**; macOS: **Mature** (less precise) |
| **CPU limit** | Job Object `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION` with `CPU_RATE_CONTROL_HARD_CAP` | cgroups v2 `cpu.max` (quota/period) | `RLIMIT_CPU` via `setrlimit` (total seconds, not percentage) | Win: P/Invoke kernel32.dll; Linux: `File.WriteAllText` on cgroupfs; macOS: P/Invoke libc | Win: **Stable**; Linux: **Stable**; macOS: **Mature** (less precise) |
| **PID / process count limit** | Job Object `JOBOBJECT_BASIC_LIMIT_INFORMATION.ActiveProcessLimit` | cgroups v2 `pids.max` | `RLIMIT_NPROC` via `setrlimit` | Win: P/Invoke kernel32.dll; Linux: filesystem; macOS: P/Invoke libc | Win: **Stable**; Linux: **Stable**; macOS: **Stable** |
| **Open file limit** | Job Object UI restriction `JOB_OBJECT_UILIMIT_HANDLES` (approximate) | cgroups v2 + `RLIMIT_NOFILE` | `RLIMIT_NOFILE` via `setrlimit` | Win: P/Invoke; Linux: P/Invoke libc; macOS: P/Invoke libc | Win: **Mature**; Linux: **Stable**; macOS: **Stable** |
| **Disk write limit** | FSRM quotas or minifilter tracking | cgroups v2 `io.max` (per device) | N/A (no built-in mechanism) | Win: WMI/COM; Linux: filesystem; macOS: N/A | Win: **Experimental**; Linux: **Mature**; macOS: N/A |
| **Working set limit** | Job Object `JOBOBJECT_EXTENDED_LIMIT_INFORMATION.MinimumWorkingSetSize` / `MaximumWorkingSetSize` | cgroups v2 `memory.high` (soft limit) | N/A | Win: P/Invoke kernel32.dll; Linux: filesystem | Win: **Stable**; Linux: **Stable**; macOS: N/A |
| **Resource usage monitoring** | `QueryInformationJobObject` with `JobObjectBasicAndIoAccountingInformation` | `memory.current`, `cpu.stat`, `pids.current` in cgroup files | `getrusage` via P/Invoke libc | Win: P/Invoke kernel32.dll; Linux: filesystem reads; macOS: P/Invoke libc | Win: **Stable**; Linux: **Stable**; macOS: **Stable** |

---

## IPC (Inter-Process Communication)

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Shim-to-daemon transport** | Named pipes (`\\.\pipe\agentpowershell`) | Unix domain socket (`/run/agentpowershell.sock`) | Unix domain socket (`/tmp/agentpowershell.sock`) | `System.IO.Pipes.NamedPipeServerStream` / `NamedPipeClientStream`; or `System.Net.Sockets.Socket` with `UnixDomainSocketEndPoint` | **Stable** (all platforms) |
| **gRPC over IPC** | gRPC over named pipe | gRPC over Unix domain socket | gRPC over Unix domain socket | `Grpc.AspNetCore` server + `Grpc.Net.Client` with `unix://` or `net.pipe://` address | **Stable** (all platforms) |
| **Peer credential validation** | Named pipe impersonation (`ImpersonateNamedPipeClient`) | `SO_PEERCRED` socket option | `SCM_CREDS` / `LOCAL_PEERCRED` | Win: P/Invoke advapi32.dll; Linux: P/Invoke libc `getsockopt`; macOS: P/Invoke libc | Win: **Stable**; Linux: **Stable**; macOS: **Mature** |
| **ACL / permissions** | `PipeSecurity` DACL on named pipe | File permissions (chmod) on socket file | File permissions on socket file | Win: `System.IO.Pipes.PipeSecurity`; Linux/macOS: `File.SetUnixFileMode` | **Stable** (all platforms) |
| **IPC monitoring (external)** | Named pipe monitoring (enumerate `\\.\pipe\*`) | Unix socket monitoring (`/proc/net/unix`, abstract namespace) | Unix socket monitoring | Win: P/Invoke; Linux: filesystem + P/Invoke; macOS: filesystem | Win: **Mature**; Linux: **Mature**; macOS: **Mature** |

---

## Signal Control

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **Signal interception** | N/A (Windows uses Ctrl events, not Unix signals) | ptrace intercepts `kill`, `tkill`, `tgkill`, `rt_sigqueueinfo` / seccomp filter on signal syscalls | Limited (no equivalent to ptrace signal interception) | Linux: P/Invoke libc ptrace; macOS: N/A | Linux: **Experimental** |
| **Signal blocking** | `SetConsoleCtrlHandler` for Ctrl+C/Break | seccomp `SECCOMP_RET_ERRNO` on signal syscalls | `signal(SIG_IGN)` for individual signals | Win: P/Invoke kernel32.dll; Linux: seccomp P/Invoke; macOS: P/Invoke libc | Win: **Stable** (limited); Linux: **Experimental**; macOS: **Mature** (limited) |
| **Signal redirect** | N/A | ptrace syscall argument rewrite (change signal number or target PID) | N/A | Linux: ptrace P/Invoke | Linux: **Experimental** |

---

## PowerShell-Specific Enforcement

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **ConstrainedLanguage mode** | `InitialSessionState.LanguageMode = ConstrainedLanguage` | Same | Same | `Microsoft.PowerShell.SDK` -- `InitialSessionState` | **Stable** (all platforms) |
| **Command visibility restriction** | `InitialSessionState` command whitelist + `SessionStateCommandEntry.Visibility` | Same | Same | `Microsoft.PowerShell.SDK` | **Stable** (all platforms) |
| **Custom PSHost** | Custom `PSHost` subclass wrapping `ConsoleHost` | Same | Same | `Microsoft.PowerShell.SDK` -- `PSHost` abstract class | **Stable** (all platforms) |
| **Module signing enforcement** | `Set-ExecutionPolicy AllSigned` + `InitialSessionState` module restriction | Same (but `ExecutionPolicy` less enforced on non-Windows) | Same | `Microsoft.PowerShell.SDK` | Win: **Stable**; Linux/macOS: **Mature** (weaker enforcement) |
| **Script block logging** | ETW `Microsoft-Windows-PowerShell` provider (native integration) | PowerShell operational log (if syslog configured) | PowerShell operational log | Win: `TraceEvent` NuGet; Linux/macOS: log file monitoring | Win: **Stable**; Linux/macOS: **Mature** |
| **AMSI integration** | AMSI via amsi.dll (PowerShell auto-submits script blocks) | N/A (AMSI is Windows-only) | N/A | Win: P/Invoke amsi.dll or consume via ETW | Win: **Stable**; Linux: N/A; macOS: N/A |
| **Provider restriction** | `InitialSessionState` provider removal (limit PSDrive access) | Same | Same | `Microsoft.PowerShell.SDK` | **Stable** (all platforms) |
| **Proxy cmdlets** | Binary module with cmdlets shadowing dangerous commands | Same | Same | `Microsoft.PowerShell.SDK` -- `Cmdlet` base class | **Stable** (all platforms) |

---

## Audit and Monitoring

| Feature | Windows | Linux | macOS | .NET API / P/Invoke | Maturity |
|---------|---------|-------|-------|---------------------|----------|
| **SQLite event store** | `Microsoft.Data.Sqlite` | Same | Same | `Microsoft.Data.Sqlite` NuGet | **Stable** (all platforms) |
| **JSONL event log** | `System.IO.StreamWriter` with rotation | Same | Same | `System.IO` | **Stable** (all platforms) |
| **Webhook event sink** | `HttpClient` with batching | Same | Same | `System.Net.Http.HttpClient` | **Stable** (all platforms) |
| **OTEL export** | `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Same | Same | `OpenTelemetry` NuGet | **Stable** (all platforms) |
| **HMAC integrity chain** | `System.Security.Cryptography.HMACSHA256` | Same | Same | `System.Security.Cryptography` | **Stable** (all platforms) |
| **ETW consumption** | `TraceEvent` NuGet for PowerShell, Kernel providers | N/A | N/A | `Microsoft.Diagnostics.Tracing.TraceEvent` | Win: **Stable** |
| **Structured logging** | Serilog with console + file sinks | Same | Same | `Serilog` NuGet | **Stable** (all platforms) |

---

## Summary Risk Assessment

### Low Risk (proceed immediately)

- PowerShell ConstrainedLanguage + InitialSessionState (all platforms)
- Named pipe / Unix domain socket IPC (all platforms)
- gRPC communication (all platforms)
- Event system: SQLite, JSONL, webhook, OTEL (all platforms)
- Job Objects for process containment and resource limits (Windows)
- cgroups v2 for resource limits and containment (Linux)
- RLIMIT for basic resource limits (macOS)
- Custom PSHost and proxy cmdlets (all platforms)
- CLI via System.CommandLine (all platforms)

### Medium Risk (requires careful implementation)

- Minifilter driver communication via fltlib.dll (Windows -- requires driver to be installed)
- ETW monitoring (Windows -- well-documented but complex API)
- Landlock filesystem enforcement (Linux -- kernel 5.13+ required, clean API)
- AppContainer sandbox (Windows -- complex setup but well-documented)
- sandbox-exec (macOS -- deprecated but functional)
- Peer credential validation (Linux/macOS -- socket option differences)
- FUSE filesystem overlay (Linux -- adds complexity)

### High Risk (consider native helpers or phased approach)

- ptrace syscall interception (Linux -- complex threading model, recommend native helper)
- seccomp user-notify (Linux -- newer API, less documentation)
- eBPF programs (Linux -- must be compiled in C, loaded via helper)
- Endpoint Security Framework (macOS -- requires native Objective-C/C dylib, entitlements, notarization)
- Network Extension (macOS -- requires native System Extension, Apple review)
- WFP driver for network enforcement (Windows -- kernel-mode driver required)
- DNS interception / redirect (all platforms -- different mechanism per platform)
- Connect redirect with TLS handling (all platforms -- complex across platforms)
