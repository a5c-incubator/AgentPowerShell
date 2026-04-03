# Cross-Platform Notes

AgentPowerShell targets Windows, Linux, and macOS through shared abstractions plus platform-specific projects.

## Windows

The repository currently has two concrete Windows-specific runtime slices:

- Job Object assignment for native child processes.
- AppContainer launch for native network-client commands when the effective network policy is deny-all.

That AppContainer path gives a verified host-level block for direct native commands such as `curl.exe https://example.com` under a deny-all policy. It is intentionally conservative today: mixed allowlists still use explicit-target policy checks rather than claiming full host-level outbound allow/deny parity.

Other Windows-oriented capabilities discussed in the architecture, such as ETW, AMSI, ConPTY, named pipes, and minifilter-oriented integration points, are still design direction rather than completed runtime coverage.

## Linux

The Linux project captures the intended abstraction boundaries, but the repository does not yet provide runtime-complete cgroups, Landlock, seccomp-bpf, ptrace, eBPF, or namespace enforcement. Today the Linux platform code primarily turns policy into enforcement plans plus shared behavior; it should not be treated as production-grade sandboxing.

## macOS

The macOS project likewise reflects the planned abstraction surface for Endpoint Security, sandbox-exec, Network Extension, FSEvents, RLIMIT, and XPC, but those integrations are not yet wired into a verified runtime enforcement path. As with Linux, the current macOS code is closer to planning/scaffolding than finished native enforcement.

## Practical Guidance

- Keep shared models and policy logic in `AgentPowerShell.Core`.
- Put platform-native behavior behind explicit abstractions.
- Validate cross-platform projects in CI for every change.
- Treat Docker support as Linux-container packaging that is smoke-tested on Linux and Windows runners; GitHub-hosted macOS runners do not provide Docker.
- Treat native Linux/macOS install validation as a separate verification step from Windows install coverage.
- Prefer runtime-specific integration tests before claiming full feature parity on a platform.
- Phrase current support in terms of verified behavior, not architectural intent.
