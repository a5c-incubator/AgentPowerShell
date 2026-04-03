# Cross-Platform Notes

AgentPowerShell targets Windows, Linux, and macOS through shared abstractions plus platform-specific projects.

## Windows

The repository currently has the first real Windows-specific enforcement slice in place through Job Object assignment for native child processes. Other Windows-oriented capabilities discussed in the architecture, such as ETW, AppContainer, AMSI, ConPTY, named pipes, and minifilter-oriented integration points, are still design direction rather than completed runtime coverage.

## Linux

The Linux project captures the intended abstraction boundaries, but the repository does not yet provide runtime-complete cgroups, Landlock, seccomp-bpf, ptrace, eBPF, or namespace enforcement. Current Linux support should be treated as buildable structure plus shared policy behavior, not production-grade sandboxing.

## macOS

The macOS project likewise reflects the planned abstraction surface for Endpoint Security, sandbox-exec, Network Extension, FSEvents, RLIMIT, and XPC, but those integrations are not yet wired into a verified runtime enforcement path.

## Practical Guidance

- Keep shared models and policy logic in `AgentPowerShell.Core`.
- Put platform-native behavior behind explicit abstractions.
- Validate cross-platform projects in CI for every change.
- Treat Docker support as Linux-container packaging that is smoke-tested on Linux and Windows runners; GitHub-hosted macOS runners do not provide Docker.
- Prefer runtime-specific integration tests before claiming full feature parity on a platform.
- Phrase current support in terms of verified behavior, not architectural intent.
