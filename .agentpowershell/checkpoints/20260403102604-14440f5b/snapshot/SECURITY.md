# Security Model

AgentPowerShell is designed to reduce risk around automated shell execution by enforcing policy, centralizing approval, and recording activity.

## Core Controls

- Policy-based command, file, environment, and network decisions
- Session tracking and expiry
- Authentication modes for local and service-facing use cases
- Approval integration points for operator escalation
- Event logging and reporting
- LLM proxy redaction and usage tracking
- MCP tool inventory, pinning, and cross-server detection
- Workspace checkpoints for reversible changes

## Current Scope

This repository provides a functional managed implementation and test coverage for the shared control plane. Platform-native enforcement components and signing/distribution flows should be treated as evolving work, not as production-complete hardening.

## Reporting

Open a private security report through your normal project disclosure channel before filing a public issue.
