# Cross-Platform Notes

AgentPowerShell targets Windows, Linux, and macOS through shared abstractions plus platform-specific projects.

## Windows

The architecture is designed around Job Objects, ETW, AppContainer, AMSI, ConPTY, named pipes, and minifilter-oriented integration points. The repository currently contains the managed scaffolding and tests needed to evolve these integrations.

## Linux

The platform plan is based on cgroups v2, Landlock, seccomp-bpf, ptrace, eBPF, namespaces, and Unix sockets. Build validation exists in the solution; runtime hardening work should continue in platform-specific integration tests.

## macOS

The design references Endpoint Security, sandbox-exec, Network Extension, FSEvents, RLIMIT, and XPC. As with Linux and Windows, the current repository emphasizes cross-platform structure and shared contracts over full production wiring.

## Practical Guidance

- Keep shared models and policy logic in `AgentPowerShell.Core`.
- Put platform-native behavior behind explicit abstractions.
- Validate cross-platform projects in CI for every change.
- Prefer runtime-specific integration tests before claiming full feature parity on a platform.
