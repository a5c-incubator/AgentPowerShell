/**
 * @process agentpowershell/full-build
 * @description Build a PowerShell-based policy-enforced shell execution gateway (agentpowershell)
 *   as a drop-in replacement for agentsh, using C#/.NET, with full feature parity.
 *   Cross-platform: Windows, Linux, macOS.
 *
 * @inputs { projectName: string, referenceRepoPath: string, powershellRepoPath: string }
 * @outputs { success: boolean, phases: object[], artifacts: object }
 *
 * @skill cross-platform-path-handler specializations/cli-mcp-development/skills/cross-platform-path-handler/SKILL.md
 * @skill shell-completion-generator specializations/cli-mcp-development/skills/shell-completion-generator/SKILL.md
 * @agent cli-ux-architect specializations/cli-mcp-development/agents/cli-ux-architect/AGENT.md
 * @agent cli-testing-architect specializations/cli-mcp-development/agents/cli-testing-architect/AGENT.md
 * @agent shell-security-auditor specializations/cli-mcp-development/agents/shell-security-auditor/AGENT.md
 * @agent shell-portability-expert specializations/cli-mcp-development/agents/shell-portability-expert/AGENT.md
 */

import { defineTask } from '@a5c-ai/babysitter-sdk';

export async function process(inputs, ctx) {
  const {
    projectName = 'agentpowershell',
    referenceRepoPath = '/tmp/agentsh-research',
    powershellRepoPath = '/tmp/powershell-research',
    projectRoot = 'C:/work/agentpowershell'
  } = inputs;

  // ============================================================================
  // PHASE 1: DEEP RESEARCH & ARCHITECTURE
  // ============================================================================

  // 1a: Deep research of agentsh architecture and PowerShell internals
  const researchResult = await ctx.task(deepResearchTask, {
    projectName,
    referenceRepoPath,
    powershellRepoPath,
    projectRoot
  });

  // 1b: Architecture design based on research
  const architectureResult = await ctx.task(architectureDesignTask, {
    projectName,
    research: researchResult,
    projectRoot
  });

  // Breakpoint: Review architecture before implementation
  let archFeedback = null;
  for (let attempt = 0; attempt < 3; attempt++) {
    if (archFeedback) {
      await ctx.task(architectureRefineTask, {
        projectName,
        architecture: architectureResult,
        feedback: archFeedback,
        attempt: attempt + 1,
        projectRoot
      });
    }
    const archApproval = await ctx.breakpoint({
      question: `Review the architecture for ${projectName}. The project will use C#/.NET with full agentsh feature parity. Approve to proceed with implementation?`,
      title: 'Architecture Review',
      options: ['Approve', 'Request changes'],
      expert: 'owner',
      tags: ['architecture-review'],
      previousFeedback: archFeedback || undefined,
      attempt: attempt > 0 ? attempt + 1 : undefined
    });
    if (archApproval.approved) break;
    archFeedback = archApproval.response || archApproval.feedback || 'Changes requested';
  }

  // ============================================================================
  // PHASE 2: PROJECT SCAFFOLDING & CORE INFRASTRUCTURE
  // ============================================================================

  const scaffoldResult = await ctx.task(projectScaffoldTask, {
    projectName,
    architecture: architectureResult,
    projectRoot
  });

  // 2a: Core types, config, and policy engine
  const coreResult = await ctx.task(coreInfrastructureTask, {
    projectName,
    architecture: architectureResult,
    scaffold: scaffoldResult,
    projectRoot
  });

  // 2b: Verify core builds and tests pass
  const coreBuildResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'core-infrastructure',
    description: 'Verify core types, config loading, and policy engine compile and pass tests'
  });

  // ============================================================================
  // PHASE 3: POWERSHELL SHIM & PROCESS INTERCEPTION
  // ============================================================================

  const shimResult = await ctx.task(powershellShimTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const shimTestResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'powershell-shim',
    description: 'Verify PowerShell shim interception works on the current platform'
  });

  // ============================================================================
  // PHASE 4: PLATFORM-SPECIFIC ENFORCEMENT
  // ============================================================================

  // Parallel platform enforcement implementations
  const [windowsResult, linuxResult, macosResult] = await ctx.parallel.all([
    () => ctx.task(platformEnforcementTask, {
      platform: 'windows',
      projectName,
      architecture: architectureResult,
      referenceRepoPath,
      projectRoot
    }),
    () => ctx.task(platformEnforcementTask, {
      platform: 'linux',
      projectName,
      architecture: architectureResult,
      referenceRepoPath,
      projectRoot
    }),
    () => ctx.task(platformEnforcementTask, {
      platform: 'macos',
      projectName,
      architecture: architectureResult,
      referenceRepoPath,
      projectRoot
    })
  ]);

  const platformTestResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'platform-enforcement',
    description: 'Verify platform-specific enforcement compiles (cross-compile check for all targets)'
  });

  // ============================================================================
  // PHASE 5: EVENT SYSTEM, AUDIT & SESSIONS
  // ============================================================================

  const eventSystemResult = await ctx.task(eventSystemTask, {
    projectName,
    architecture: architectureResult,
    projectRoot
  });

  const sessionResult = await ctx.task(sessionManagementTask, {
    projectName,
    architecture: architectureResult,
    projectRoot
  });

  const auditTestResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'event-audit-session',
    description: 'Verify event system, audit logging, and session management pass tests'
  });

  // Breakpoint: Mid-project checkpoint
  await ctx.breakpoint({
    question: 'Core infrastructure, shim, platform enforcement, events, and sessions are implemented. Review progress before continuing to advanced features?',
    title: 'Mid-Project Checkpoint',
    options: ['Continue', 'Request changes'],
    expert: 'owner',
    tags: ['mid-checkpoint']
  });

  // ============================================================================
  // PHASE 6: NETWORK MONITORING & FILE SYSTEM MONITORING
  // ============================================================================

  const [netMonitorResult, fsMonitorResult] = await ctx.parallel.all([
    () => ctx.task(networkMonitorTask, {
      projectName,
      architecture: architectureResult,
      referenceRepoPath,
      projectRoot
    }),
    () => ctx.task(fileSystemMonitorTask, {
      projectName,
      architecture: architectureResult,
      referenceRepoPath,
      projectRoot
    })
  ]);

  const monitorTestResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'monitors',
    description: 'Verify network and filesystem monitoring compile and pass tests'
  });

  // ============================================================================
  // PHASE 7: APPROVAL SYSTEM & AUTHENTICATION
  // ============================================================================

  const approvalResult = await ctx.task(approvalSystemTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const authResult = await ctx.task(authenticationTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const approvalTestResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'approval-auth',
    description: 'Verify approval system and authentication pass tests'
  });

  // ============================================================================
  // PHASE 8: LLM PROXY & MCP INTEGRATION
  // ============================================================================

  const llmProxyResult = await ctx.task(llmProxyTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const mcpResult = await ctx.task(mcpIntegrationTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const advancedTestResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'llm-mcp',
    description: 'Verify LLM proxy and MCP integration pass tests'
  });

  // ============================================================================
  // PHASE 9: CLI, REPORTING & WORKSPACE CHECKPOINTS
  // ============================================================================

  const cliResult = await ctx.task(cliImplementationTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const reportingResult = await ctx.task(reportingTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const checkpointResult = await ctx.task(workspaceCheckpointTask, {
    projectName,
    architecture: architectureResult,
    referenceRepoPath,
    projectRoot
  });

  const cliTestResult = await ctx.task(buildAndTestTask, {
    projectRoot,
    phase: 'cli-reporting-checkpoints',
    description: 'Verify CLI commands, reporting, and workspace checkpoints pass tests'
  });

  // ============================================================================
  // PHASE 10: INTEGRATION TESTING & QUALITY CONVERGENCE
  // ============================================================================

  let qualityScore = 0;
  const targetQuality = 85;
  let qualityIteration = 0;
  const maxQualityIterations = 4;

  while (qualityIteration < maxQualityIterations && qualityScore < targetQuality) {
    qualityIteration++;

    const integrationResult = await ctx.task(integrationTestTask, {
      projectRoot,
      iteration: qualityIteration,
      previousScore: qualityScore
    });

    const scoringResult = await ctx.task(qualityScoringTask, {
      projectName,
      projectRoot,
      integrationResult,
      iteration: qualityIteration,
      targetQuality
    });

    qualityScore = scoringResult.overallScore || 0;

    if (qualityScore < targetQuality && qualityIteration < maxQualityIterations) {
      await ctx.task(qualityRefinementTask, {
        projectRoot,
        scoringResult,
        iteration: qualityIteration
      });
    }
  }

  // ============================================================================
  // PHASE 11: DOCUMENTATION & PACKAGING
  // ============================================================================

  const [docsResult, packagingResult] = await ctx.parallel.all([
    () => ctx.task(documentationTask, {
      projectName,
      architecture: architectureResult,
      projectRoot
    }),
    () => ctx.task(packagingTask, {
      projectName,
      architecture: architectureResult,
      projectRoot
    })
  ]);

  // ============================================================================
  // PHASE 12: FINAL REVIEW
  // ============================================================================

  const finalReview = await ctx.task(finalReviewTask, {
    projectName,
    projectRoot,
    qualityScore,
    targetQuality,
    phases: {
      research: researchResult,
      architecture: architectureResult,
      scaffold: scaffoldResult,
      core: coreResult,
      shim: shimResult,
      platforms: { windows: windowsResult, linux: linuxResult, macos: macosResult },
      events: eventSystemResult,
      sessions: sessionResult,
      netMonitor: netMonitorResult,
      fsMonitor: fsMonitorResult,
      approval: approvalResult,
      auth: authResult,
      llmProxy: llmProxyResult,
      mcp: mcpResult,
      cli: cliResult,
      reporting: reportingResult,
      checkpoints: checkpointResult,
      docs: docsResult,
      packaging: packagingResult
    }
  });

  // Final approval
  await ctx.breakpoint({
    question: `${projectName} implementation complete. Quality: ${qualityScore}/${targetQuality}. ${finalReview.verdict}. Approve?`,
    title: 'Final Project Review',
    options: ['Approve', 'Request changes'],
    expert: 'owner',
    tags: ['final-approval']
  });

  return {
    success: qualityScore >= targetQuality,
    projectName,
    qualityScore,
    targetQuality,
    finalReview,
    artifacts: {
      architecture: 'docs/architecture.md',
      report: 'docs/final-report.md'
    }
  };
}

// ============================================================================
// TASK DEFINITIONS
// ============================================================================

export const deepResearchTask = defineTask('deep-research', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Deep research: agentsh architecture & PowerShell internals',
  execution: { model: 'claude-opus-4-6' },
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior systems engineer and security architect',
      task: `Conduct deep research on agentsh and PowerShell to inform the architecture of ${args.projectName}`,
      context: {
        referenceRepoPath: args.referenceRepoPath,
        powershellRepoPath: args.powershellRepoPath,
        projectRoot: args.projectRoot,
        goal: 'Build a PowerShell-based policy-enforced shell execution gateway in C#/.NET with full agentsh feature parity'
      },
      instructions: [
        `Research the agentsh codebase at ${args.referenceRepoPath}:`,
        '- Read cmd/agentsh/main.go and understand the CLI entry points',
        '- Study internal/policy/ for the policy engine design',
        '- Study internal/shim/ for the shell shim mechanism',
        '- Study internal/server/ for the daemon architecture',
        '- Study internal/platform/ for OS-specific implementations',
        '- Study internal/events/ for the event system',
        '- Study internal/session/ for session management',
        '- Study internal/approval/ for the approval flow',
        '- Study internal/llmproxy/ for LLM proxy features',
        '- Study internal/mcpclient/ and internal/mcpinspect/ for MCP integration',
        '- Study internal/netmonitor/ and internal/fsmonitor/ for monitoring',
        '- Study internal/ptrace/ and internal/seccomp/ for Linux enforcement',
        '- Study drivers/windows/ for Windows minifilter driver approach',
        '- Study the default-policy.yml and config.yml for configuration format',
        `Research PowerShell internals at ${args.powershellRepoPath}:`,
        '- Understand how PowerShell modules and profiles work',
        '- Study how PowerShell handles process execution (Start-Process, Invoke-Expression)',
        '- Understand the PowerShell host interface for building custom hosts',
        '- Study the PowerShell remoting and session architecture',
        '- Research .NET interop for system-level hooks (P/Invoke, ETW, minifilters)',
        'Write all research findings to docs/research/ in the project root as markdown files',
        'Create docs/research/agentsh-analysis.md with full architecture breakdown',
        'Create docs/research/powershell-internals.md with relevant PS internals',
        'Create docs/research/dotnet-platform-apis.md with .NET APIs for each enforcement mechanism'
      ],
      outputFormat: 'JSON with summary, keyFindings, architecturalInsights, platformApis, risks'
    },
    outputSchema: {
      type: 'object',
      required: ['summary', 'keyFindings'],
      properties: {
        summary: { type: 'string' },
        keyFindings: { type: 'array', items: { type: 'string' } },
        architecturalInsights: { type: 'array', items: { type: 'string' } },
        platformApis: { type: 'object' },
        risks: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const architectureDesignTask = defineTask('architecture-design', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Design agentpowershell architecture',
  execution: { model: 'claude-opus-4-6' },
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET architect specializing in cross-platform security tools',
      task: `Design the complete architecture for ${args.projectName} - a C#/.NET PowerShell policy-enforced shell gateway`,
      context: {
        research: args.research,
        projectRoot: args.projectRoot,
        techStack: 'C# / .NET 9, cross-platform (Windows/Linux/macOS)',
        featureParity: 'Full agentsh feature parity'
      },
      instructions: [
        'Design a .NET solution structure with clear project organization',
        'Map each agentsh Go package to equivalent .NET projects/namespaces',
        'Design the core abstractions: IPolicyEngine, IShellInterceptor, IEventEmitter, ISessionManager',
        'Design the platform abstraction layer with runtime OS detection',
        'Plan the PowerShell shim strategy (custom PSHost, module-based interception, or binary shim)',
        'Design the gRPC/named-pipe IPC between shim and daemon',
        'Plan Windows enforcement: minifilter driver, ETW tracing, Job Objects, AppContainer',
        'Plan Linux enforcement: seccomp-bpf via P/Invoke, ptrace, eBPF',
        'Plan macOS enforcement: Endpoint Security Framework, sandbox-exec',
        'Design the policy YAML format (compatible with agentsh format)',
        'Design the CLI command structure matching agentsh commands',
        'Design the event system with structured JSON logging',
        'Plan the LLM proxy with provider routing and DLP',
        'Plan MCP tool whitelisting and inspection',
        'Create docs/architecture.md with full architecture document',
        'Create docs/solution-structure.md with .NET solution layout',
        'Create docs/platform-matrix.md with platform-specific enforcement matrix'
      ],
      outputFormat: 'JSON with architecture summary, solution structure, platform strategies, and key design decisions'
    },
    outputSchema: {
      type: 'object',
      required: ['summary', 'solutionStructure', 'designDecisions'],
      properties: {
        summary: { type: 'string' },
        solutionStructure: { type: 'object' },
        designDecisions: { type: 'array', items: { type: 'string' } },
        platformStrategies: { type: 'object' }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const architectureRefineTask = defineTask('architecture-refine', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Refine architecture based on feedback',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET architect',
      task: 'Refine the architecture based on user feedback',
      context: {
        architecture: args.architecture,
        feedback: args.feedback,
        attempt: args.attempt,
        projectRoot: args.projectRoot
      },
      instructions: [
        'Review the feedback carefully',
        'Update the architecture documents in docs/ to address the feedback',
        'Ensure all concerns are addressed'
      ],
      outputFormat: 'JSON with changes made and updated summary'
    },
    outputSchema: {
      type: 'object',
      required: ['changes'],
      properties: {
        changes: { type: 'array', items: { type: 'string' } },
        summary: { type: 'string' }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const projectScaffoldTask = defineTask('project-scaffold', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Scaffold .NET solution and projects',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: `Create the .NET solution structure for ${args.projectName}`,
      context: {
        architecture: args.architecture,
        projectRoot: args.projectRoot
      },
      instructions: [
        'Create the .NET solution file (agentpowershell.sln)',
        'Create src/AgentPowerShell.Core/ - core types, interfaces, policy engine',
        'Create src/AgentPowerShell.Daemon/ - background service/daemon',
        'Create src/AgentPowerShell.Shim/ - PowerShell shim binary',
        'Create src/AgentPowerShell.Cli/ - CLI entry point',
        'Create src/AgentPowerShell.Platform.Windows/ - Windows-specific enforcement',
        'Create src/AgentPowerShell.Platform.Linux/ - Linux-specific enforcement',
        'Create src/AgentPowerShell.Platform.MacOS/ - macOS-specific enforcement',
        'Create src/AgentPowerShell.Events/ - event system and audit',
        'Create src/AgentPowerShell.LlmProxy/ - LLM proxy',
        'Create src/AgentPowerShell.Mcp/ - MCP integration',
        'Create tests/AgentPowerShell.Tests/ - test project',
        'Create tests/AgentPowerShell.IntegrationTests/ - integration tests',
        'Set up project references, NuGet packages (YamlDotNet, Grpc.Net, System.CommandLine)',
        'Create .editorconfig, global.json, Directory.Build.props',
        'Create a basic default-policy.yml compatible with agentsh format',
        'Ensure dotnet build succeeds with empty placeholder classes'
      ],
      outputFormat: 'JSON with filesCreated, projectsCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated', 'projectsCreated'],
      properties: {
        filesCreated: { type: 'array', items: { type: 'string' } },
        projectsCreated: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const coreInfrastructureTask = defineTask('core-infrastructure', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement core types, config, and policy engine',
  execution: { model: 'claude-opus-4-6' },
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior C#/.NET developer specializing in security tools',
      task: 'Implement the core infrastructure: types, configuration, and policy engine',
      context: {
        architecture: args.architecture,
        scaffold: args.scaffold,
        projectRoot: args.projectRoot
      },
      instructions: [
        'In AgentPowerShell.Core, implement:',
        '- PolicyRule, PolicyDecision (allow/deny/approve/redirect/audit/soft_delete)',
        '- FileRule, CommandRule, NetworkRule, EnvRule with glob matching',
        '- PolicyEngine with first-match-wins evaluation',
        '- YAML policy loading using YamlDotNet',
        '- Configuration model (config.yml equivalent) with session, server, policy settings',
        '- Core event types (FileEvent, ProcessEvent, NetworkEvent, etc.)',
        '- IShellInterceptor interface for the shim abstraction',
        '- IPlatformEnforcer interface for OS-specific enforcement',
        '- IApprovalHandler interface for approval flow',
        'Write comprehensive unit tests for the policy engine',
        'Write tests for YAML policy parsing with edge cases',
        'Write tests for configuration loading',
        'Ensure all tests pass with dotnet test'
      ],
      outputFormat: 'JSON with filesCreated, filesModified, testsWritten, testsPassing'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated', 'testsWritten'],
      properties: {
        filesCreated: { type: 'array', items: { type: 'string' } },
        filesModified: { type: 'array', items: { type: 'string' } },
        testsWritten: { type: 'number' },
        testsPassing: { type: 'boolean' }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const buildAndTestTask = defineTask('build-and-test', (args, taskCtx) => ({
  kind: 'shell',
  title: `Build and test: ${args.phase}`,
  description: args.description,
  shell: {
    command: `cd "${args.projectRoot}" && dotnet build --verbosity minimal 2>&1 && dotnet test --verbosity minimal 2>&1`
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const powershellShimTask = defineTask('powershell-shim', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement PowerShell shim and process interception',
  execution: { model: 'claude-opus-4-6' },
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer with deep PowerShell and systems programming expertise',
      task: 'Implement the PowerShell shim that intercepts and governs shell execution',
      context: {
        architecture: args.architecture,
        referenceRepoPath: args.referenceRepoPath,
        projectRoot: args.projectRoot
      },
      instructions: [
        `Study the agentsh shell shim at ${args.referenceRepoPath}/cmd/agentsh-shell-shim/ and ${args.referenceRepoPath}/internal/shim/`,
        'Implement the AgentPowerShell.Shim project:',
        '- Binary shim that replaces pwsh/powershell in PATH',
        '- When invoked, connects to the daemon via named pipes (Windows) or Unix sockets',
        '- Forwards commands through the policy engine before execution',
        '- Captures stdout/stderr/exit codes and forwards them back',
        '- Supports both interactive and non-interactive modes',
        '- Handles PowerShell-specific features: pipeline, modules, remoting',
        'Implement IPC protocol between shim and daemon:',
        '- gRPC over named pipes (Windows) or Unix domain sockets (Linux/macOS)',
        '- Define .proto files for the IPC protocol',
        '- CommandRequest, CommandResponse, EventStream messages',
        'Implement daemon auto-start on first shim invocation',
        'Handle the shim activation: environment variable or symlink-based',
        'Write unit and integration tests for shim functionality',
        'Test with actual PowerShell commands (Get-Process, Get-ChildItem, etc.)'
      ],
      outputFormat: 'JSON with filesCreated, filesModified, testsWritten'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: {
        filesCreated: { type: 'array', items: { type: 'string' } },
        filesModified: { type: 'array', items: { type: 'string' } },
        testsWritten: { type: 'number' }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const platformEnforcementTask = defineTask('platform-enforcement', (args, taskCtx) => ({
  kind: 'agent',
  title: `Implement ${args.platform} enforcement`,
  execution: { model: 'claude-opus-4-6' },
  agent: {
    name: 'general-purpose',
    prompt: {
      role: `Senior .NET developer specializing in ${args.platform} system programming`,
      task: `Implement ${args.platform}-specific runtime enforcement for agentpowershell`,
      context: {
        platform: args.platform,
        architecture: args.architecture,
        referenceRepoPath: args.referenceRepoPath,
        projectRoot: args.projectRoot
      },
      instructions: args.platform === 'windows' ? [
        `Study ${args.referenceRepoPath}/drivers/windows/ and ${args.referenceRepoPath}/internal/platform/ for Windows patterns`,
        'Implement AgentPowerShell.Platform.Windows:',
        '- Job Objects for process containment and resource limits',
        '- AppContainer sandbox for restricted execution',
        '- ETW (Event Tracing for Windows) for file/network/process monitoring',
        '- Windows minifilter driver integration (or WFP for network)',
        '- ConPTY integration for terminal handling',
        '- Named pipe server for IPC',
        '- Windows service registration for the daemon',
        'Use P/Invoke for Win32 APIs not exposed by .NET',
        'Write platform-specific tests (skip on non-Windows with appropriate attributes)'
      ] : args.platform === 'linux' ? [
        `Study ${args.referenceRepoPath}/internal/ptrace/, ${args.referenceRepoPath}/internal/seccomp/, and ${args.referenceRepoPath}/internal/landlock/`,
        'Implement AgentPowerShell.Platform.Linux:',
        '- seccomp-bpf filters for syscall filtering via P/Invoke',
        '- ptrace-based process monitoring',
        '- Landlock LSM for filesystem sandboxing',
        '- eBPF programs for network monitoring (via libbpf)',
        '- cgroups v2 for resource limits',
        '- Unix domain socket server for IPC',
        '- systemd service file for daemon',
        'Use P/Invoke for Linux syscalls',
        'Write platform-specific tests (skip on non-Linux)'
      ] : [
        `Study ${args.referenceRepoPath}/macos/ for macOS patterns`,
        'Implement AgentPowerShell.Platform.MacOS:',
        '- Endpoint Security Framework integration (requires entitlements)',
        '- sandbox-exec (Seatbelt) profiles for sandboxing',
        '- Network Extension framework for network filtering',
        '- RLIMIT-based resource limits',
        '- Unix domain socket server for IPC',
        '- launchd plist for daemon',
        'Use P/Invoke for macOS system calls',
        'Write platform-specific tests (skip on non-macOS)'
      ],
      outputFormat: 'JSON with filesCreated, platformFeatures implemented'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: {
        filesCreated: { type: 'array', items: { type: 'string' } },
        platformFeatures: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const eventSystemTask = defineTask('event-system', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement event system and structured logging',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: 'Implement the event system with structured JSON logging and audit trails',
      context: { architecture: args.architecture, projectRoot: args.projectRoot },
      instructions: [
        'Implement AgentPowerShell.Events:',
        '- Event types: FileEvent, ProcessEvent, NetworkEvent, DnsEvent, PtyEvent, LlmEvent',
        '- Event bus with pub/sub pattern',
        '- Structured JSON event serialization (System.Text.Json)',
        '- Event store with append-only file-based storage',
        '- Summary and detailed output modes',
        '- Event filtering and querying',
        '- Real-time event streaming via IPC',
        'Write comprehensive tests for event serialization and filtering'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const sessionManagementTask = defineTask('session-management', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement session management',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: 'Implement session lifecycle management',
      context: { architecture: args.architecture, projectRoot: args.projectRoot },
      instructions: [
        'Implement session creation, tracking, and teardown',
        'Session state persistence across daemon restarts',
        'Session-scoped policy binding',
        'Session timeout and cleanup',
        'Multi-session support with isolation',
        'Write tests for session lifecycle'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const networkMonitorTask = defineTask('network-monitor', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement network monitoring',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET network security developer',
      task: 'Implement network monitoring and DNS interception',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/internal/netmonitor/ for patterns`,
        'Implement DNS query interception and logging',
        'Implement TCP/UDP connection monitoring',
        'Policy evaluation for network rules (allow/deny domains and ports)',
        'Integration with platform-specific network filtering',
        'Write tests for network policy evaluation'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const fileSystemMonitorTask = defineTask('filesystem-monitor', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement filesystem monitoring',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: 'Implement filesystem monitoring and soft-delete quarantine',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/internal/fsmonitor/ and ${args.referenceRepoPath}/internal/trash/`,
        'Implement filesystem event monitoring using FileSystemWatcher and platform hooks',
        'Implement soft-delete quarantine with restore capability',
        'Policy evaluation for file rules with glob matching',
        'Write tests for file monitoring and quarantine'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const approvalSystemTask = defineTask('approval-system', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement approval system',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: 'Implement the approval system with multiple authentication methods',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/internal/approval/ and ${args.referenceRepoPath}/internal/approvals/`,
        'Implement approval prompts: TTY terminal prompts, TOTP, WebAuthn, remote REST API',
        'Implement approval timeout and escalation',
        'Implement the approval dialog (Windows WPF/MAUI, cross-platform terminal)',
        'Write tests for approval flow'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const authenticationTask = defineTask('authentication', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement authentication (API key, OIDC, hybrid)',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET security developer',
      task: 'Implement authentication: API key, OIDC, and hybrid modes',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/internal/auth/`,
        'Implement API key authentication',
        'Implement OIDC authentication flow',
        'Implement hybrid mode (API key + OIDC)',
        'Token management and refresh',
        'Write tests for auth flows'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const llmProxyTask = defineTask('llm-proxy', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement LLM proxy with DLP',
  execution: { model: 'claude-opus-4-6' },
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer with AI/LLM integration expertise',
      task: 'Implement the embedded LLM proxy with provider routing and DLP redaction',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/internal/llmproxy/`,
        'Implement HTTP reverse proxy for LLM API requests',
        'Implement provider routing (OpenAI, Anthropic, etc.)',
        'Implement DLP redaction for sensitive data in prompts/responses',
        'Implement request/response logging as LlmEvents',
        'Rate limiting and token tracking',
        'Write tests for proxy routing and DLP'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const mcpIntegrationTask = defineTask('mcp-integration', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement MCP tool whitelisting and inspection',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer with MCP protocol expertise',
      task: 'Implement MCP tool whitelisting, version pinning, and cross-server exfiltration detection',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/internal/mcpclient/ and ${args.referenceRepoPath}/internal/mcpinspect/ and ${args.referenceRepoPath}/internal/mcpregistry/`,
        'Implement MCP tool whitelisting with version pinning',
        'Implement MCP server inspection and monitoring',
        'Implement cross-server exfiltration detection',
        'Implement MCP registry for tool discovery',
        'Write tests for MCP integration'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const cliImplementationTask = defineTask('cli-implementation', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement CLI commands',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET CLI developer',
      task: 'Implement the agentpowershell CLI with all commands matching agentsh',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/cmd/agentsh/main.go and the CLI structure`,
        'Using System.CommandLine, implement:',
        '- agentpwsh exec <session-id> -- <command> (execute command in session)',
        '- agentpwsh start (start daemon)',
        '- agentpwsh stop (stop daemon)',
        '- agentpwsh session create/list/destroy',
        '- agentpwsh policy validate/show',
        '- agentpwsh report (generate session report)',
        '- agentpwsh status (show daemon and session status)',
        '- agentpwsh checkpoint create/restore/list',
        '- agentpwsh config show/set',
        'Support --output json and --events summary/detailed flags',
        'Implement human-friendly and JSON output modes',
        'Write tests for CLI argument parsing'
      ],
      outputFormat: 'JSON with filesCreated, commandsImplemented'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: {
        filesCreated: { type: 'array', items: { type: 'string' } },
        commandsImplemented: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const reportingTask = defineTask('reporting', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement session reporting',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: 'Implement session reporting with markdown and violation detection',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        `Study ${args.referenceRepoPath}/internal/report/`,
        'Implement markdown report generation',
        'Implement automatic violation/findings detection',
        'Implement timeline visualization in reports',
        'Support CI/CD integration for report generation',
        'Write tests for report generation'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const workspaceCheckpointTask = defineTask('workspace-checkpoint', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Implement workspace checkpoints with rollback',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: 'Implement workspace checkpoints with dry-run rollback preview',
      context: { architecture: args.architecture, referenceRepoPath: args.referenceRepoPath, projectRoot: args.projectRoot },
      instructions: [
        'Implement workspace snapshot creation (git-based or file-based)',
        'Implement dry-run rollback preview showing what would change',
        'Implement actual rollback to checkpoint',
        'Implement checkpoint listing and management',
        'Write tests for checkpoint operations'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const integrationTestTask = defineTask('integration-test', (args, taskCtx) => ({
  kind: 'agent',
  title: `Integration testing (iteration ${args.iteration})`,
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior QA engineer',
      task: 'Run comprehensive integration tests and identify failures',
      context: { projectRoot: args.projectRoot, iteration: args.iteration, previousScore: args.previousScore },
      instructions: [
        'Run dotnet build to verify compilation',
        'Run dotnet test to execute all unit tests',
        'Run integration tests that test end-to-end flows:',
        '- Policy loading and evaluation',
        '- Shim-to-daemon communication',
        '- Event capture and logging',
        '- Session lifecycle',
        'Check cross-compilation for all target platforms',
        'Identify any failing tests or compilation errors',
        'Fix any issues found and re-run tests'
      ],
      outputFormat: 'JSON with testResults, buildSuccess, issuesFound, issuesFixed'
    },
    outputSchema: {
      type: 'object',
      required: ['buildSuccess'],
      properties: {
        buildSuccess: { type: 'boolean' },
        testResults: { type: 'object' },
        issuesFound: { type: 'array', items: { type: 'string' } },
        issuesFixed: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const qualityScoringTask = defineTask('quality-scoring', (args, taskCtx) => ({
  kind: 'agent',
  title: `Quality scoring (iteration ${args.iteration})`,
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior quality assurance engineer',
      task: 'Score the overall quality of the agentpowershell implementation',
      context: {
        projectRoot: args.projectRoot,
        integrationResult: args.integrationResult,
        iteration: args.iteration,
        targetQuality: args.targetQuality
      },
      instructions: [
        'Review the codebase for completeness against agentsh feature parity',
        'Score test coverage and test quality (25%)',
        'Score code quality: readability, .NET best practices, error handling (25%)',
        'Score feature completeness against agentsh (30%)',
        'Score cross-platform readiness (20%)',
        'Provide specific recommendations for improvement',
        'Calculate overall weighted score 0-100'
      ],
      outputFormat: 'JSON with overallScore, dimensionScores, recommendations'
    },
    outputSchema: {
      type: 'object',
      required: ['overallScore', 'recommendations'],
      properties: {
        overallScore: { type: 'number' },
        dimensionScores: { type: 'object' },
        recommendations: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const qualityRefinementTask = defineTask('quality-refinement', (args, taskCtx) => ({
  kind: 'agent',
  title: `Quality refinement (iteration ${args.iteration})`,
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET developer',
      task: 'Address quality issues identified in scoring',
      context: {
        projectRoot: args.projectRoot,
        scoringResult: args.scoringResult,
        iteration: args.iteration
      },
      instructions: [
        'Review the quality scoring recommendations',
        'Address the highest-priority issues first',
        'Fix any failing tests',
        'Improve test coverage where recommended',
        'Refactor code quality issues',
        'Ensure dotnet build and dotnet test pass after changes'
      ],
      outputFormat: 'JSON with issuesFixed, testsAdded'
    },
    outputSchema: {
      type: 'object',
      required: ['issuesFixed'],
      properties: {
        issuesFixed: { type: 'array', items: { type: 'string' } },
        testsAdded: { type: 'number' }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const documentationTask = defineTask('documentation', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Write project documentation',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Technical writer with .NET expertise',
      task: 'Write comprehensive documentation for agentpowershell',
      context: { architecture: args.architecture, projectRoot: args.projectRoot },
      instructions: [
        'Write README.md with project overview, quick start, and feature list',
        'Write docs/getting-started.md with installation and first-use guide',
        'Write docs/policy-reference.md with policy format documentation',
        'Write docs/cli-reference.md with all CLI commands',
        'Write docs/configuration.md with config.yml reference',
        'Write docs/cross-platform.md with platform-specific notes',
        'Write docs/agent-integration.md with CLAUDE.md/AGENTS.md examples',
        'Write SECURITY.md with security model description',
        'Write CONTRIBUTING.md'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const packagingTask = defineTask('packaging', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Set up packaging and distribution',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Senior .NET DevOps engineer',
      task: 'Set up packaging and distribution for agentpowershell',
      context: { architecture: args.architecture, projectRoot: args.projectRoot },
      instructions: [
        'Set up dotnet publish profiles for self-contained single-file executables:',
        '- win-x64, win-arm64',
        '- linux-x64, linux-arm64',
        '- osx-x64, osx-arm64',
        'Create Dockerfile for containerized usage',
        'Create GitHub Actions workflow for CI/CD build and release',
        'Create NuGet package configuration for the SDK/library components',
        'Create installation scripts (install.ps1, install.sh)',
        'Set up code signing configuration placeholder'
      ],
      outputFormat: 'JSON with filesCreated'
    },
    outputSchema: {
      type: 'object',
      required: ['filesCreated'],
      properties: { filesCreated: { type: 'array', items: { type: 'string' } } }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const finalReviewTask = defineTask('final-review', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Final project review',
  execution: { model: 'claude-opus-4-6' },
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'Principal engineer and technical reviewer',
      task: 'Conduct final comprehensive review of the agentpowershell implementation',
      context: {
        projectName: args.projectName,
        projectRoot: args.projectRoot,
        qualityScore: args.qualityScore,
        targetQuality: args.targetQuality,
        phases: args.phases
      },
      instructions: [
        'Review overall architecture and code organization',
        'Verify feature parity with agentsh',
        'Review security posture of the implementation',
        'Check cross-platform readiness',
        'Review test coverage and quality',
        'Assess documentation completeness',
        'Provide final verdict and any remaining recommendations',
        'Write docs/final-report.md with the complete review'
      ],
      outputFormat: 'JSON with verdict, approved, confidence, strengths, concerns, followUpTasks'
    },
    outputSchema: {
      type: 'object',
      required: ['verdict', 'approved', 'confidence'],
      properties: {
        verdict: { type: 'string' },
        approved: { type: 'boolean' },
        confidence: { type: 'number' },
        strengths: { type: 'array', items: { type: 'string' } },
        concerns: { type: 'array', items: { type: 'string' } },
        followUpTasks: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));
