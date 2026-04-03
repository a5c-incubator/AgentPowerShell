# agentpowershell - Process Description

## Overview

Build **agentpowershell** — a C#/.NET policy-enforced shell execution gateway for PowerShell, providing full feature parity with [agentsh](https://github.com/canyonroad/agentsh). Cross-platform: Windows, Linux, macOS.

## Technology Stack

- **Language:** C# / .NET 9
- **Shell Target:** PowerShell 7+ (pwsh)
- **IPC:** gRPC over named pipes (Windows) / Unix domain sockets (Linux/macOS)
- **Policy Format:** YAML (compatible with agentsh policy format)
- **CLI Framework:** System.CommandLine
- **Testing:** xUnit + Moq

## 12-Phase Process

### Phase 1: Research & Architecture
Deep analysis of agentsh's Go codebase (40+ internal packages) and PowerShell internals. Architecture design mapping agentsh concepts to .NET equivalents. **Breakpoint** for architecture review.

### Phase 2: Scaffolding & Core
.NET solution with 10+ projects. Core types, policy engine (first-match-wins YAML rules), configuration loading. Build verification.

### Phase 3: PowerShell Shim
Binary shim replacing `pwsh` in PATH, forwarding commands through daemon via gRPC/IPC. Interactive and non-interactive modes. Daemon auto-start.

### Phase 4: Platform Enforcement (Parallel)
Three parallel tracks:
- **Windows:** Job Objects, AppContainer, ETW, minifilter, ConPTY
- **Linux:** seccomp-bpf, ptrace, Landlock, eBPF, cgroups v2
- **macOS:** ESF, sandbox-exec, Network Extension, RLIMIT

### Phase 5: Events, Audit & Sessions
Structured JSON event system (file/process/network/DNS/PTY/LLM events), session lifecycle management. **Mid-project checkpoint breakpoint**.

### Phase 6: Monitors (Parallel)
Network monitoring (DNS, TCP/UDP) and filesystem monitoring (FileSystemWatcher, soft-delete quarantine) in parallel.

### Phase 7: Approval & Authentication
Multi-method approval (TTY, TOTP, WebAuthn, REST) and authentication (API key, OIDC, hybrid).

### Phase 8: LLM Proxy & MCP
Embedded LLM proxy with provider routing and DLP. MCP tool whitelisting, version pinning, cross-server exfiltration detection.

### Phase 9: CLI, Reporting, Checkpoints
Full CLI matching agentsh commands. Markdown session reports with violation detection. Workspace checkpoints with rollback.

### Phase 10: Quality Convergence
Integration testing loop (up to 4 iterations) targeting 85/100 quality score across: tests (25%), code quality (25%), feature completeness (30%), cross-platform readiness (20%).

### Phase 11: Documentation & Packaging (Parallel)
Comprehensive docs (README, guides, references). Multi-platform packaging (self-contained builds, Docker, CI/CD, NuGet).

### Phase 12: Final Review
Complete project review with **final approval breakpoint**.

## Key Design Decisions

1. **Binary shim over PSHost** — More compatible, less invasive, matches agentsh's approach
2. **gRPC IPC** — Type-safe, cross-platform, well-supported in .NET
3. **Platform abstraction via separate projects** — Clean separation, only compiles platform-relevant code
4. **YAML policy compatibility** — Eases migration from agentsh
5. **Quality convergence loop** — Iterative improvement until quality threshold met

## Breakpoints (3 total)

1. Architecture review (with retry loop)
2. Mid-project checkpoint (after core + shim + platform + events)
3. Final project approval
