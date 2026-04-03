# .NET Platform APIs for Enforcement

> Research document for the agentpowershell project. Covers .NET APIs available for security enforcement on Windows, Linux, and macOS.

## 1. Windows Enforcement APIs

### 1.1 Job Objects

Job Objects provide process-level containment -- limiting resources, restricting process creation, and grouping processes for management.

**Key APIs** (via P/Invoke from `kernel32.dll`):

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool SetInformationJobObject(
    IntPtr hJob,
    JobObjectInfoType infoType,
    IntPtr lpJobObjectInfo,
    uint cbJobObjectInfoLength);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool QueryInformationJobObject(
    IntPtr hJob,
    JobObjectInfoType infoType,
    IntPtr lpJobObjectInfo,
    uint cbJobObjectInfoLength,
    out uint lpReturnLength);
```

**Capabilities for agentpowershell**:
- `JOBOBJECT_BASIC_LIMIT_INFORMATION`: CPU time limits, process count limits, priority class
- `JOBOBJECT_EXTENDED_LIMIT_INFORMATION`: Memory limits (job/process), working set limits
- `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`: Kill all processes when job handle closes
- `JOB_OBJECT_LIMIT_ACTIVE_PROCESS`: Limit number of active processes
- `JOB_OBJECT_LIMIT_BREAKAWAY_OK` / `JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK`: Control process breakaway
- `JOB_OBJECT_UILIMIT_*`: Desktop, display settings, exit windows, global atoms, handles, read clipboard, system parameters, write clipboard
- Completion port notifications for process creation/exit events

**NuGet packages**: None needed; P/Invoke only. Consider `Microsoft.Windows.CsWin32` for generated P/Invoke signatures.

### 1.2 AppContainer

AppContainer provides a lightweight sandbox with capability-based access control.

**Key APIs**:

```csharp
[DllImport("userenv.dll", SetLastError = true)]
static extern int CreateAppContainerProfile(
    string pszAppContainerName,
    string pszDisplayName,
    string pszDescription,
    IntPtr pCapabilities,  // SID_AND_ATTRIBUTES[]
    uint dwCapabilityCount,
    out IntPtr ppSidAppContainerSid);

[DllImport("userenv.dll")]
static extern int DeleteAppContainerProfile(string pszAppContainerName);

// Use with STARTUPINFOEX + PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool UpdateProcThreadAttribute(
    IntPtr lpAttributeList,
    uint dwFlags,
    IntPtr Attribute,
    IntPtr lpValue,
    IntPtr cbSize,
    IntPtr lpPreviousValue,
    IntPtr lpReturnSize);
```

**Capabilities for agentpowershell**:
- Network isolation (no network by default; add `internetClient` capability for outbound)
- File system isolation (access only to AppContainer-specific folders)
- Registry isolation
- Window station isolation
- Named object isolation

### 1.3 ETW (Event Tracing for Windows)

ETW provides real-time event monitoring without modifying target processes.

**.NET APIs** (`System.Diagnostics.Tracing`):

```csharp
// Consuming ETW events
using Microsoft.Diagnostics.Tracing;        // NuGet: Microsoft.Diagnostics.Tracing.TraceEvent
using Microsoft.Diagnostics.Tracing.Session; // NuGet: Microsoft.Diagnostics.Tracing.TraceEvent

var session = new TraceEventSession("AgentPSMonitor");
session.EnableProvider("Microsoft-Windows-PowerShell");      // PowerShell events
session.EnableProvider("Microsoft-Windows-Kernel-Process");  // Process create/exit
session.EnableProvider("Microsoft-Windows-Kernel-File");     // File operations
session.EnableProvider("Microsoft-Windows-Kernel-Network");  // Network activity

session.Source.Dynamic.All += (TraceEvent data) => {
    // Process events
};
session.Source.Process();
```

**Key ETW Providers**:
| Provider | GUID | Events |
|----------|------|--------|
| `Microsoft-Windows-PowerShell` | `{A0C1853B-5C40-4B15-8766-3CF1C58F985A}` | Script block logging, module logging, command execution |
| `Microsoft-Windows-Kernel-Process` | `{22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716}` | Process create, exit, image load |
| `Microsoft-Windows-Kernel-File` | `{EDD08927-9CC4-4E65-B970-C2560FB5C289}` | File create, read, write, delete |
| `Microsoft-Windows-Kernel-Network` | `{7DD42A49-5329-4832-8DFD-43D979153A88}` | TCP/UDP connect, accept, send, receive |
| `Microsoft-Windows-DNS-Client` | `{1C95126E-7EEA-49A9-A3FE-A378B03DDB4D}` | DNS queries and responses |

**NuGet**: `Microsoft.Diagnostics.Tracing.TraceEvent` (4.x+)

### 1.4 Minifilter Driver Communication

For communicating with the agentsh minifilter driver from .NET:

```csharp
[DllImport("fltlib.dll", SetLastError = true)]
static extern int FilterConnectCommunicationPort(
    string lpPortName,       // L"\\AgentshPort"
    uint dwOptions,
    IntPtr lpContext,
    ushort wSizeOfContext,
    IntPtr lpSecurityAttributes,
    out IntPtr hPort);

[DllImport("fltlib.dll", SetLastError = true)]
static extern int FilterSendMessage(
    IntPtr hPort,
    IntPtr lpInBuffer,
    uint dwInBufferSize,
    IntPtr lpOutBuffer,
    uint dwOutBufferSize,
    out uint lpBytesReturned);

[DllImport("fltlib.dll", SetLastError = true)]
static extern int FilterGetMessage(
    IntPtr hPort,
    IntPtr lpMessageBuffer,
    uint dwMessageBufferSize,
    IntPtr lpOverlapped);

[DllImport("fltlib.dll", SetLastError = true)]
static extern int FilterReplyMessage(
    IntPtr hPort,
    IntPtr lpReplyBuffer,
    uint dwReplyBufferSize);
```

This is the exact mechanism used by the agentsh Windows minifilter. The .NET daemon would use `FilterConnectCommunicationPort` to connect to `\\AgentshPort`, then `FilterGetMessage`/`FilterReplyMessage` for async policy decisions.

### 1.5 ConPTY (Windows Pseudo Console)

For terminal emulation and output capture:

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
static extern int CreatePseudoConsole(
    COORD size,
    IntPtr hInput,
    IntPtr hOutput,
    uint dwFlags,
    out IntPtr phPC);

[DllImport("kernel32.dll", SetLastError = true)]
static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

[DllImport("kernel32.dll", SetLastError = true)]
static extern void ClosePseudoConsole(IntPtr hPC);
```

Use with `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` in `CreateProcess` to capture all terminal output.

**.NET built-in**: `System.Diagnostics.Process` with `RedirectStandardOutput`/`RedirectStandardError` handles most cases without ConPTY.

### 1.6 Named Pipes

For IPC between the shim and daemon:

```csharp
// .NET built-in (System.IO.Pipes)
using var server = new NamedPipeServerStream(
    "agentpowershell",
    PipeDirection.InOut,
    maxConnections: 10,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous);

await server.WaitForConnectionAsync(cancellationToken);

// Client side
using var client = new NamedPipeClientStream(
    ".",
    "agentpowershell",
    PipeDirection.InOut,
    PipeOptions.Asynchronous);

await client.ConnectAsync(timeoutMs, cancellationToken);
```

**Security**: Named pipes support ACLs via `PipeSecurity` class. Set appropriate DACL to prevent unauthorized access.

### 1.7 Windows Process Creation Hooks

```csharp
// Process creation with Job Object assignment
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
static extern bool CreateProcessW(
    string lpApplicationName,
    string lpCommandLine,
    IntPtr lpProcessAttributes,
    IntPtr lpThreadAttributes,
    bool bInheritHandles,
    uint dwCreationFlags,           // CREATE_SUSPENDED | EXTENDED_STARTUPINFO_PRESENT
    IntPtr lpEnvironment,
    string lpCurrentDirectory,
    ref STARTUPINFOEX lpStartupInfo,
    out PROCESS_INFORMATION lpProcessInformation);
```

Pattern: Create process suspended -> assign to Job Object -> resume. This ensures no process runs outside the job.

### 1.8 AMSI (Antimalware Scan Interface)

PowerShell integrates AMSI for script content scanning. agentpowershell could register as an AMSI provider:

```csharp
[DllImport("amsi.dll", SetLastError = true)]
static extern int AmsiInitialize(string appName, out IntPtr amsiContext);

[DllImport("amsi.dll", SetLastError = true)]
static extern int AmsiScanString(
    IntPtr amsiContext,
    string content,
    string contentName,
    IntPtr amsiSession,
    out AMSI_RESULT result);
```

Or consume AMSI events via ETW to monitor what PowerShell is scanning.

## 2. Linux Enforcement APIs

### 2.1 seccomp-bpf

System call filtering at the kernel level.

**P/Invoke**:

```csharp
// Via libseccomp or direct syscall
[DllImport("libc", SetLastError = true)]
static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

// PR_SET_SECCOMP = 22, SECCOMP_MODE_FILTER = 2
// Or use seccomp() syscall directly via syscall(2)

[DllImport("libc", SetLastError = true)]
static extern int syscall(long number, uint operation, uint flags, IntPtr args);
// __NR_seccomp = 317 (x86_64)
```

**NuGet**: No established .NET wrapper. Options:
- Direct syscall via P/Invoke to `libc`
- Shell out to a native helper binary
- Use `libseccomp` C library via P/Invoke

**seccomp user-notify** (`SECCOMP_USER_NOTIF`): Allows a supervisor process to intercept and decide on syscalls made by the sandboxed process. This is the most powerful mode for agentpowershell on Linux.

### 2.2 ptrace

Process tracing for syscall interception.

```csharp
[DllImport("libc", SetLastError = true)]
static extern long ptrace(int request, int pid, IntPtr addr, IntPtr data);

// Key operations:
// PTRACE_ATTACH = 16
// PTRACE_SETOPTIONS = 0x4200
// PTRACE_SYSCALL = 24
// PTRACE_GETREGS = 12
// PTRACE_SETREGS = 13
// PTRACE_PEEKDATA = 2
// PTRACE_POKEDATA = 5
```

For .NET, ptrace is complex due to threading requirements (the tracer thread must be the one that receives waitpid results). Consider using a native helper process.

### 2.3 Landlock LSM

Linux Security Module for filesystem access control (kernel 5.13+).

```csharp
[DllImport("libc", SetLastError = true)]
static extern int syscall(long number, /* landlock args */);

// __NR_landlock_create_ruleset = 444 (x86_64)
// __NR_landlock_add_rule = 445
// __NR_landlock_restrict_self = 446
```

Landlock is self-restricting: once applied, the process cannot escalate its own permissions. Ideal for one-shot sandbox setup.

**Ruleset types**:
- `LANDLOCK_ACCESS_FS_EXECUTE`, `LANDLOCK_ACCESS_FS_WRITE_FILE`, `LANDLOCK_ACCESS_FS_READ_FILE`
- `LANDLOCK_ACCESS_FS_READ_DIR`, `LANDLOCK_ACCESS_FS_REMOVE_DIR`, `LANDLOCK_ACCESS_FS_REMOVE_FILE`
- `LANDLOCK_ACCESS_FS_MAKE_CHAR`, `LANDLOCK_ACCESS_FS_MAKE_DIR`, `LANDLOCK_ACCESS_FS_MAKE_REG`
- `LANDLOCK_ACCESS_FS_MAKE_SOCK`, `LANDLOCK_ACCESS_FS_MAKE_FIFO`
- `LANDLOCK_ACCESS_FS_MAKE_BLOCK`, `LANDLOCK_ACCESS_FS_MAKE_SYM`
- Network rules (kernel 6.8+): `LANDLOCK_ACCESS_NET_BIND_TCP`, `LANDLOCK_ACCESS_NET_CONNECT_TCP`

### 2.4 eBPF

Extended Berkeley Packet Filter for kernel-level monitoring and filtering.

**From .NET**: eBPF programs must be compiled in C and loaded via `bpf()` syscall. .NET can:
- Load pre-compiled eBPF objects via `bpf()` syscall P/Invoke
- Use `libbpf` via P/Invoke
- Read BPF maps via file descriptors

```csharp
[DllImport("libc", SetLastError = true)]
static extern int syscall(long number, int cmd, IntPtr attr, uint size);
// __NR_bpf = 321 (x86_64)
```

**Practical approach**: Write eBPF programs in C, compile with `clang`, load from .NET using a native helper or `libbpf` bindings.

**NuGet**: No established .NET eBPF library. Consider `Tmds.Linux` for raw syscall access.

### 2.5 cgroups v2

Resource control via the cgroup filesystem.

**From .NET**: Pure filesystem operations (no P/Invoke needed):

```csharp
// Create a cgroup
Directory.CreateDirectory("/sys/fs/cgroup/agentpowershell");

// Set limits
File.WriteAllText("/sys/fs/cgroup/agentpowershell/memory.max", "536870912"); // 512MB
File.WriteAllText("/sys/fs/cgroup/agentpowershell/cpu.max", "50000 100000"); // 50% CPU
File.WriteAllText("/sys/fs/cgroup/agentpowershell/pids.max", "100");

// Assign process
File.WriteAllText("/sys/fs/cgroup/agentpowershell/cgroup.procs", pid.ToString());

// Monitor
string usage = File.ReadAllText("/sys/fs/cgroup/agentpowershell/memory.current");
```

### 2.6 Unix Domain Sockets

For IPC between shim and daemon:

```csharp
// .NET built-in (System.Net.Sockets)
var endpoint = new UnixDomainSocketEndPoint("/run/agentpowershell.sock");

// Server
var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
listener.Bind(endpoint);
listener.Listen(backlog: 10);
var client = await listener.AcceptAsync();

// Client
var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
await socket.ConnectAsync(endpoint);
```

Also available: `System.IO.Pipes.NamedPipeServerStream` on Unix (uses Unix domain sockets under the hood).

### 2.7 Namespaces

Linux namespaces for isolation:

```csharp
[DllImport("libc", SetLastError = true)]
static extern int unshare(int flags);
// CLONE_NEWNET = 0x40000000  (network namespace)
// CLONE_NEWNS  = 0x00020000  (mount namespace)
// CLONE_NEWPID = 0x20000000  (PID namespace)

[DllImport("libc", SetLastError = true)]
static extern int setns(int fd, int nstype);
```

## 3. macOS Enforcement APIs

### 3.1 Endpoint Security Framework

Apple's modern security framework (macOS 10.15+) for process, file, and network monitoring.

**Access from .NET**: Requires a native helper or Objective-C bridge. The ES framework is C-based:

```c
// Native C code (compiled as dylib, called from .NET via P/Invoke)
es_new_client(&client, ^(es_client_t *c, const es_message_t *msg) {
    switch (msg->event_type) {
        case ES_EVENT_TYPE_AUTH_EXEC:
            // Intercept process execution
            es_respond_auth_result(c, msg, ES_AUTH_RESULT_ALLOW, false);
            break;
        case ES_EVENT_TYPE_AUTH_OPEN:
            // Intercept file open
            break;
    }
});
es_subscribe(client, events, event_count);
```

**Key events**:
- `ES_EVENT_TYPE_AUTH_EXEC` -- Process execution authorization
- `ES_EVENT_TYPE_AUTH_OPEN` -- File open authorization
- `ES_EVENT_TYPE_AUTH_RENAME` -- File rename authorization
- `ES_EVENT_TYPE_NOTIFY_FORK` -- Process fork notification
- `ES_EVENT_TYPE_NOTIFY_EXIT` -- Process exit notification

**Requirements**: System Extension entitlement, Full Disk Access, notarization.

### 3.2 sandbox-exec (deprecated but functional)

Profile-based sandboxing using Seatbelt profiles:

```csharp
// Launch sandboxed process
var psi = new ProcessStartInfo("/usr/bin/sandbox-exec");
psi.ArgumentList.Add("-f");
psi.ArgumentList.Add("/path/to/profile.sb");
psi.ArgumentList.Add("pwsh");
```

Profile format (Scheme-based):
```scheme
(version 1)
(deny default)
(allow file-read* (subpath "/usr"))
(allow file-read* file-write* (subpath "/workspace"))
(allow network-outbound (remote tcp "registry.npmjs.org:443"))
(deny network* (local ip "*:*"))
```

**Note**: Apple has deprecated sandbox-exec and the profile format is undocumented. Use Endpoint Security Framework instead for new development.

### 3.3 Network Extension Framework

For network filtering on macOS:

**Access**: Requires System Extension + Network Extension entitlement. Must be implemented as a native macOS System Extension.

**Types**:
- `NEFilterDataProvider` -- Content filter for network traffic
- `NEDNSProxyProvider` -- DNS proxy
- `NETransparentProxyProvider` -- Transparent proxy

### 3.4 RLIMIT

POSIX resource limits available on macOS:

```csharp
[DllImport("libc")]
static extern int getrlimit(int resource, out RLimit rlim);

[DllImport("libc")]
static extern int setrlimit(int resource, ref RLimit rlim);

[StructLayout(LayoutKind.Sequential)]
struct RLimit {
    public ulong rlim_cur;  // soft limit
    public ulong rlim_max;  // hard limit
}

// Key resources:
// RLIMIT_CPU = 0       // CPU time
// RLIMIT_FSIZE = 1     // File size
// RLIMIT_DATA = 2      // Data segment size
// RLIMIT_STACK = 3     // Stack size
// RLIMIT_CORE = 4      // Core file size
// RLIMIT_NOFILE = 8    // Open files
// RLIMIT_NPROC = 7     // Processes per user
```

### 3.5 XPC Services

macOS inter-process communication framework:

From .NET, XPC is most easily accessed via a native helper. The daemon could be implemented as a launchd-managed XPC service.

## 4. Cross-Platform .NET Abstractions

### 4.1 System.Diagnostics.Process

Available on all platforms for process management:

```csharp
var psi = new ProcessStartInfo {
    FileName = "pwsh",
    Arguments = "-NoProfile -Command Get-Process",
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    RedirectStandardInput = true,
    UseShellExecute = false,
    CreateNoWindow = true,
    Environment = { ["AGENTPS_SESSION"] = sessionId },
    WorkingDirectory = "/workspace"
};

using var process = Process.Start(psi);
```

### 4.2 System.IO.Pipes

Cross-platform named pipe support:

```csharp
// Windows: uses Win32 named pipes (\\.\pipe\name)
// Unix: uses Unix domain sockets

// Server
var server = new NamedPipeServerStream("agentps", PipeDirection.InOut,
    maxConnections, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

// Client
var client = new NamedPipeClientStream(".", "agentps",
    PipeDirection.InOut, PipeOptions.Asynchronous);
```

### 4.3 System.Net.Sockets

Cross-platform socket support including Unix domain sockets:

```csharp
// Unix domain sockets (Linux/macOS)
var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

// TCP sockets (all platforms)
var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
```

### 4.4 System.IO.FileSystemWatcher

Cross-platform file system monitoring:

```csharp
var watcher = new FileSystemWatcher("/workspace") {
    IncludeSubdirectories = true,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                   NotifyFilters.LastWrite | NotifyFilters.CreationTime
};
watcher.Created += (s, e) => { /* file created */ };
watcher.Changed += (s, e) => { /* file modified */ };
watcher.Deleted += (s, e) => { /* file deleted */ };
watcher.Renamed += (s, e) => { /* file renamed */ };
watcher.EnableRaisingEvents = true;
```

**Limitations**: Notification only (cannot block/deny operations). Use OS-specific enforcement for blocking.

### 4.5 System.Threading.RateLimiting

.NET 7+ rate limiting primitives:

```csharp
using System.Threading.RateLimiting;

var limiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions {
    PermitLimit = 100,
    Window = TimeSpan.FromMinutes(1),
    SegmentsPerWindow = 6,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    QueueLimit = 10
});

using var lease = await limiter.AcquireAsync(1, cancellationToken);
if (lease.IsAcquired) { /* proceed */ }
```

### 4.6 gRPC (.NET)

For high-performance IPC:

```csharp
// NuGet: Grpc.Net.Client, Grpc.AspNetCore
// Server
var builder = WebApplication.CreateBuilder();
builder.Services.AddGrpc();
var app = builder.Build();
app.MapGrpcService<PolicyService>();

// Client
var channel = GrpcChannel.ForAddress("unix:///run/agentpowershell.sock");
var client = new PolicyService.PolicyServiceClient(channel);
```

**NuGet packages**: `Grpc.AspNetCore`, `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools`

### 4.7 MessagePack / Protobuf

For efficient IPC serialization:

- **Protobuf**: `Google.Protobuf` (used by gRPC, schema-first)
- **MessagePack**: `MessagePack` NuGet by neuecc (schema-less, faster for .NET)
- **System.Text.Json**: Built-in, good enough for moderate throughput

### 4.8 SQLite

For event storage (cross-platform):

```csharp
// NuGet: Microsoft.Data.Sqlite
using var connection = new SqliteConnection("Data Source=events.db");
connection.Open();

using var cmd = connection.CreateCommand();
cmd.CommandText = "INSERT INTO events (type, data, timestamp) VALUES ($type, $data, $ts)";
cmd.Parameters.AddWithValue("$type", eventType);
cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(eventData));
cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
cmd.ExecuteNonQuery();
```

**NuGet**: `Microsoft.Data.Sqlite`

## 5. Recommended NuGet Packages Summary

| Package | Purpose | Platform |
|---------|---------|----------|
| `Microsoft.PowerShell.SDK` | PowerShell hosting | All |
| `Microsoft.Data.Sqlite` | Event storage | All |
| `Grpc.AspNetCore` | gRPC server | All |
| `Grpc.Net.Client` | gRPC client | All |
| `Google.Protobuf` + `Grpc.Tools` | Protobuf codegen | All |
| `Microsoft.Diagnostics.Tracing.TraceEvent` | ETW consumption | Windows |
| `Microsoft.Windows.CsWin32` | Auto-generated P/Invoke | Windows |
| `YamlDotNet` | YAML policy parsing | All |
| `DotNet.Glob` | Glob pattern matching | All |
| `Serilog` + sinks | Structured logging | All |
| `System.IO.Hashing` | Hash computation (xxHash, CRC) | All |
| `MessagePack` | Fast binary serialization | All |
| `Tmds.Linux` | Raw Linux syscall access | Linux |

## 6. Platform Enforcement Matrix

| Mechanism | Windows | Linux | macOS | .NET API |
|-----------|---------|-------|-------|----------|
| Process containment | Job Objects | cgroups v2 + namespaces | sandbox-exec (deprecated) | P/Invoke |
| Process creation control | Job Object + CreateProcess | ptrace / seccomp | Endpoint Security | P/Invoke |
| File system enforcement | Minifilter driver | Landlock / FUSE / seccomp | Endpoint Security / sandbox-exec | P/Invoke / native helper |
| File system monitoring | ETW / minifilter | eBPF / inotify / fanotify | FSEvents / ES Framework | ETW TraceEvent / FileSystemWatcher |
| Network enforcement | WFP / AppContainer | eBPF / seccomp / Landlock 6.8+ | Network Extension / sandbox-exec | P/Invoke / native helper |
| Network monitoring | ETW | eBPF / conntrack | Network Extension | ETW TraceEvent / P/Invoke |
| Registry enforcement | Minifilter driver | N/A | N/A | P/Invoke (fltlib.dll) |
| DNS interception | DNS Client ETW / Winsock LSP | eBPF / iptables | Network Extension | P/Invoke |
| IPC | Named pipes | Unix domain sockets | Unix domain sockets / XPC | System.IO.Pipes / System.Net.Sockets |
| Resource limits | Job Objects | cgroups v2 | RLIMIT | P/Invoke / filesystem |
| Signal control | N/A (no Unix signals) | seccomp / ptrace | N/A (limited) | P/Invoke |

## 7. Architecture Recommendations

### IPC Strategy

Use **named pipes** (`System.IO.Pipes`) as the primary IPC mechanism:
- Cross-platform (Windows named pipes, Unix domain sockets)
- Built into .NET with async support
- Supports ACLs on Windows for security
- Fast enough for per-command policy checks

Fall back to **gRPC over Unix domain sockets** for streaming use cases (event tailing, long-running session management).

### Serialization

Use **Protobuf** for the wire protocol:
- Schema-first ensures forward/backward compatibility
- Fast serialization/deserialization
- Works with gRPC natively
- Strong typing across shim and daemon

### Enforcement Layering

```
Layer 1 (Always): PowerShell ConstrainedLanguage + InitialSessionState restrictions
Layer 2 (Always): Named pipe IPC to daemon for command policy checks
Layer 3 (Windows): Job Objects for resource limits + process containment
Layer 4 (Windows): Minifilter driver for file/registry enforcement
Layer 5 (Windows): ETW for monitoring/auditing
Layer 6 (Linux): cgroups v2 for resource limits
Layer 7 (Linux): Landlock for filesystem enforcement
Layer 8 (Linux): seccomp-bpf for syscall filtering
Layer 9 (macOS): Endpoint Security for process/file monitoring
Layer 10 (macOS): RLIMIT for resource limits
```
