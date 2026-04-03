# PowerShell Internals for Shim Design

> Research document for the agentpowershell project. Based on source code analysis of the PowerShell repository and PowerShell SDK documentation.

## 1. PowerShell Execution Architecture

### Core Layers

```
┌─────────────────────────────────────────┐
│  Host Application (ConsoleHost, ISE,    │
│  VS Code, Custom PSHost)                │
├─────────────────────────────────────────┤
│  Runspace (execution environment)       │
│    ├── InitialSessionState              │
│    ├── SessionState                     │
│    └── ExecutionContext                  │
├─────────────────────────────────────────┤
│  Pipeline (command execution chain)     │
│    ├── CommandProcessor                 │
│    ├── ScriptCommandProcessor           │
│    └── NativeCommandProcessor           │
├─────────────────────────────────────────┤
│  System.Management.Automation.dll       │
│  (.NET runtime)                         │
└─────────────────────────────────────────┘
```

### Key Source Locations in the PowerShell Repo

| Component | Path |
|-----------|------|
| PSHost abstract class | `src/System.Management.Automation/engine/hostifaces/MshHost.cs` |
| Runspace | `src/System.Management.Automation/engine/hostifaces/Connection.cs` |
| LocalRunspace | `src/System.Management.Automation/engine/hostifaces/LocalConnection.cs` |
| LocalPipeline | `src/System.Management.Automation/engine/hostifaces/LocalPipeline.cs` |
| RunspaceFactory | `src/System.Management.Automation/engine/hostifaces/ConnectionFactory.cs` |
| InitialSessionState | `src/System.Management.Automation/engine/InitialSessionState.cs` |
| SessionState | `src/System.Management.Automation/engine/SessionState.cs` |
| ExecutionContext | `src/System.Management.Automation/engine/ExecutionContext.cs` |
| CommandProcessor | `src/System.Management.Automation/engine/CommandProcessor.cs` |
| NativeCommandProcessor | `src/System.Management.Automation/engine/NativeCommandProcessor.cs` |
| ScriptCommandProcessor | `src/System.Management.Automation/engine/ScriptCommandProcessor.cs` |
| ConsoleHost | `src/Microsoft.PowerShell.ConsoleHost/` |
| HostUtilities | `src/System.Management.Automation/engine/hostifaces/HostUtilities.cs` |

## 2. PSHost Interface

### Overview

`PSHost` is the abstract base class that hosting applications must implement. From `MshHost.cs`:

```csharp
public abstract class PSHost
{
    public abstract string Name { get; }
    public abstract System.Version Version { get; }
    public abstract System.Guid InstanceId { get; }
    public abstract PSHostUserInterface UI { get; }
    public abstract CultureInfo CurrentCulture { get; }
    public abstract CultureInfo CurrentUICulture { get; }
    public abstract void SetShouldExit(int exitCode);
    public abstract void EnterNestedPrompt();
    public abstract void ExitNestedPrompt();
    public abstract void NotifyBeginApplication();
    public abstract void NotifyEndApplication();
}
```

Key properties documented in the source:
- There is a **1:1 relationship** between PSHost instance and Runspace
- Methods can be called from **any thread** in **any order**
- The host must be **thread-safe**
- Scripts access the host via `$Host`; cmdlets via `this.Host`
- Maximum nesting depth: 128 (`MaximumNestedPromptLevel`)

### PSHostUserInterface

Provides the UI surface for the host:
- `ReadLine()`, `ReadLineAsSecureString()`
- `Write()`, `WriteLine()`, `WriteErrorLine()`
- `WriteDebugLine()`, `WriteVerboseLine()`, `WriteWarningLine()`
- `WriteProgress()`
- `PromptForChoice()`, `PromptForCredential()`

### Interception Opportunity

A custom PSHost is the **primary interception point** for agentpowershell:

1. Create a custom PSHost that wraps the real ConsoleHost
2. Create a Runspace with the custom host
3. Override `NotifyBeginApplication()` / `NotifyEndApplication()` to intercept native command execution
4. Use `PSHostUserInterface` to intercept I/O
5. The `$Host` variable gives scripts access, enabling detection

However, PSHost alone does not intercept individual commands -- it is a UI/lifecycle abstraction. For command interception, we need additional mechanisms.

## 3. Runspace and Session Architecture

### Runspace (`Connection.cs`)

A Runspace is the primary execution environment:
- Encapsulates a `SessionState` (variables, functions, aliases, drives)
- Hosts an `ExecutionContext`
- Manages pipeline execution
- States: `BeforeOpen`, `Opening`, `Opened`, `Closing`, `Closed`, `Disconnected`, `Connecting`, `Broken`

### RunspaceFactory

Creates Runspace instances:
```csharp
// Key creation patterns:
Runspace.CreateRunspace(host);                    // Default session state
Runspace.CreateRunspace(host, initialSessionState); // Custom session state
```

### InitialSessionState (`InitialSessionState.cs`)

Configures what is available when a Runspace opens:
- Built-in cmdlets, providers, aliases, functions
- Module auto-loading configuration
- Language mode settings
- Execution policy
- Early startup initialization (AMSI on Windows, type system warmup)

Key interception strategy: Constrain the `InitialSessionState` to control what commands are available.

### SessionState

Runtime state for a Runspace:
- Variable scope hierarchy
- Function definitions
- Alias definitions
- Drive mappings (PSDrive)
- Provider instances
- Module state
- Scope stack (global, script, local, private)

### ExecutionContext

The runtime context passed through the pipeline:
- Current session state
- Current host reference
- Current runspace
- Error/warning/verbose/debug/information streams
- Event manager
- Module intrinsics

## 4. Pipeline and Command Processing

### LocalPipeline (`LocalPipeline.cs`)

Executes commands in a LocalRunspace:
- Default stack size: 10MB (consistent across platforms)
- Runs on a dedicated thread
- Has a `PipelineStopper` for cancellation
- Supports nested pipelines

### Command Processing Chain

1. **Parser** parses input into AST
2. **Command Discovery** resolves command names to:
   - Cmdlets (compiled C# commands)
   - Functions (PowerShell script functions)
   - Scripts (.ps1 files)
   - Native commands (external executables)
   - Aliases
3. **CommandProcessor** or subclass is selected:
   - `CommandProcessor` -- for cmdlets
   - `ScriptCommandProcessor` -- for functions/scripts
   - `NativeCommandProcessor` -- for external executables

### NativeCommandProcessor (`NativeCommandProcessor.cs`)

This is the critical component for agentpowershell. It handles execution of external programs:

- Uses `System.Diagnostics.Process` to launch native commands
- Handles input/output format (Text vs XML for minishell)
- Manages stdin/stdout/stderr streams
- Handles output encoding
- Supports process redirection

The NativeCommandProcessor is where external process execution happens. Intercepting at this level (via a custom `CommandProcessor` or by wrapping process creation) would give complete control over native command execution.

## 5. Module System

### Module Types

1. **Script modules** (`.psm1`): PowerShell script files
2. **Binary modules** (`.dll`): Compiled .NET assemblies with cmdlets
3. **Manifest modules** (`.psd1`): Metadata pointing to script/binary modules
4. **CIM modules**: Remote CIM-based modules

### Module Loading

Modules are loaded via:
- `Import-Module` cmdlet
- Auto-loading (when a command from the module is used)
- `InitialSessionState.ImportPSModule()`
- Profile scripts

### Module Paths

Standard search paths (`$env:PSModulePath`):
- User modules: `$HOME/Documents/PowerShell/Modules`
- System modules: `$PSHOME/Modules`
- Shared modules: `/usr/local/share/powershell/Modules` (Linux/macOS)

### Interception via Module

A binary module (C# DLL) can:
1. Export cmdlets that shadow built-in commands
2. Use `IModuleAssemblyInitializer` for module load hooks
3. Register engine events
4. Modify session state on import

Key interface for module initialization:
```csharp
public interface IModuleAssemblyInitializer
{
    void OnImport();
}

public interface IModuleAssemblyCleanup
{
    void OnRemove(PSModuleInfo psModuleInfo);
}
```

## 6. Profile System

### Profile Locations

PowerShell loads profiles in this order:
1. `AllUsersAllHosts` -- `$PSHOME/Profile.ps1`
2. `AllUsersCurrentHost` -- `$PSHOME/Microsoft.PowerShell_profile.ps1`
3. `CurrentUserAllHosts` -- `$HOME/Documents/PowerShell/Profile.ps1`
4. `CurrentUserCurrentHost` -- `$HOME/Documents/PowerShell/Microsoft.PowerShell_profile.ps1`

From `HostUtilities.cs`, the profile paths are assembled using `GetDollarProfile()` and passed to `$Profile`.

### Profile Interception Strategy

A profile script could:
1. Import the agentpowershell module
2. Set up proxy functions for dangerous commands
3. Configure constrained language mode
4. Register event handlers

However, profiles can be bypassed with `-NoProfile` flag, making this insufficient as the sole enforcement mechanism.

## 7. Process Execution Model

### How PowerShell Launches External Processes

In `NativeCommandProcessor.cs`, PowerShell uses `System.Diagnostics.Process`:

1. Command resolution finds the executable path
2. A `ProcessStartInfo` is configured with:
   - Filename and arguments
   - Working directory
   - Environment variables
   - Redirect flags for stdin/stdout/stderr
3. `Process.Start()` launches the process
4. Output is consumed asynchronously
5. Exit code is captured

### Interception Points for Process Execution

1. **Command resolution**: Override `CommandDiscovery` to control what commands resolve
2. **Pre-execution**: Use `EngineIntrinsics.InvokeCommand` events
3. **Process creation**: Wrap or replace the `Process.Start` call
4. **Post-execution**: Capture exit codes and output

## 8. .NET Hosting of PowerShell

### PowerShell SDK

The `Microsoft.PowerShell.SDK` NuGet package enables hosting PowerShell in any .NET application:

```csharp
// Create a PowerShell instance
using var ps = PowerShell.Create();

// Or with a custom runspace
var host = new CustomPSHost();
var iss = InitialSessionState.CreateDefault2();
var runspace = RunspaceFactory.CreateRunspace(host, iss);
runspace.Open();

using var ps = PowerShell.Create();
ps.Runspace = runspace;
ps.AddScript("Get-Process");
var results = ps.Invoke();
```

### Key APIs for Hosting

| API | Purpose |
|-----|---------|
| `PowerShell.Create()` | Create a new PowerShell instance |
| `RunspaceFactory.CreateRunspace(host, iss)` | Create with custom host and session state |
| `InitialSessionState.CreateDefault2()` | Create default session state |
| `InitialSessionState.CreateRestricted()` | Create constrained session state |
| `ps.AddCommand()` / `ps.AddScript()` | Add commands to pipeline |
| `ps.Invoke()` / `ps.InvokeAsync()` | Execute the pipeline |
| `ps.Streams` | Access output/error/warning/verbose/debug/information streams |
| `runspace.SessionStateProxy` | Access session state from outside |

### Constrained Language Mode

PowerShell supports language modes that restrict what scripts can do:
- `FullLanguage` -- No restrictions
- `ConstrainedLanguage` -- Limits .NET type access, no `Add-Type`
- `RestrictedLanguage` -- Variables and basic operators only
- `NoLanguage` -- No scripts, only commands

For agentpowershell, `ConstrainedLanguage` mode combined with a whitelist of allowed commands provides a strong baseline.

## 9. Remoting Architecture

### PowerShell Remoting (WinRM/SSH)

PowerShell remoting uses:
- **WSMan** (Windows Remote Management) on Windows
- **SSH** subsystem on Linux/macOS
- **Named pipes** for local transport

Remote runspaces maintain session state on the remote end. This architecture is relevant because agentpowershell could use a similar transport model -- the daemon acts as the "remote" end, with the shim acting as the client.

### Transport Layer

- `RunspaceConnectionInfo` -- Base class for connection info
- `WSManConnectionInfo` -- WinRM connection
- `SSHConnectionInfo` -- SSH connection
- `NamedPipeConnectionInfo` -- Local named pipe connection
- `ContainerConnectionInfo` -- Container process connection

The `NamedPipeConnectionInfo` pattern is directly applicable: agentpowershell can use named pipes (Windows) or Unix domain sockets (Linux/macOS) for daemon communication.

## 10. Interception Strategy for agentpowershell

### Recommended Layered Approach

```
┌─────────────────────────────────────────┐
│  Layer 1: Custom PSHost                 │
│  - Wraps real ConsoleHost               │
│  - Intercepts UI operations             │
│  - Controls `$Host` variable            │
├─────────────────────────────────────────┤
│  Layer 2: InitialSessionState           │
│  - Constrained language mode            │
│  - Command visibility restrictions      │
│  - Custom proxy cmdlets                 │
├─────────────────────────────────────────┤
│  Layer 3: Binary Module                 │
│  - Proxy cmdlets (Invoke-Command, etc.) │
│  - Event handlers for pipeline events   │
│  - IModuleAssemblyInitializer hooks     │
├─────────────────────────────────────────┤
│  Layer 4: Engine Event Subscribers      │
│  - PowerShell.Exiting                   │
│  - Runspace.StateChanged                │
│  - Command invocation events            │
├─────────────────────────────────────────┤
│  Layer 5: OS-Level Enforcement          │
│  - Job Objects (Windows)                │
│  - Process creation hooks               │
│  - ETW monitoring                       │
│  - Minifilter for file/registry         │
└─────────────────────────────────────────┘
```

### Critical Interception Points

1. **Custom PSHost**: Entry point, wraps all I/O
2. **InitialSessionState**: Constrain available commands at Runspace creation
3. **Command proxy cmdlets**: Binary module cmdlets that shadow dangerous commands (`Invoke-Expression`, `Start-Process`, `&` operator via function override)
4. **NativeCommandProcessor override**: The most powerful but most invasive -- requires forking or wrapping the processor
5. **Process creation at OS level**: Job Objects with process creation limits, or minifilter driver for file operations

### Key Risks

1. **Bypass via `-NoProfile`**: Profile-based enforcement can be skipped
2. **Direct .NET calls**: `[System.Diagnostics.Process]::Start()` bypasses PowerShell's command pipeline entirely
3. **Add-Type**: Can compile and run arbitrary C# code (mitigated by ConstrainedLanguage mode)
4. **Runspace escape**: Creating a new unconstrained Runspace from within PowerShell
5. **PowerShell API direct calls**: If the agent has access to the PowerShell SDK, it can create its own Runspace
6. **Binary module loading**: Malicious DLLs could bypass restrictions

### Mitigations

1. ConstrainedLanguage mode prevents direct .NET type instantiation
2. Custom PSHost + controlled Runspace prevents unconstrained Runspace creation
3. OS-level enforcement (Job Objects, minifilter) provides defense-in-depth
4. Module signing policy restricts binary module loading
5. AppContainer/sandbox provides process-level isolation
