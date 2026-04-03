/**
 * @process agentpowershell/spec-convergence
 * @description Close the gap between the documented/requested agentpowershell specification
 *   and the actual repository implementation through repeated assessment, implementation,
 *   verification, and parity review loops.
 *
 * @inputs {
 *   projectRoot: string,
 *   requestPath: string,
 *   architectureDoc: string,
 *   readmePath: string,
 *   targetParityScore: number,
 *   maxImplementationBatches: number
 * }
 * @outputs {
 *   success: boolean,
 *   parityScore: number,
 *   batchesCompleted: number,
 *   remainingGaps: string[],
 *   finalReview: object
 * }
 */

import { defineTask } from '@a5c-ai/babysitter-sdk';

export async function process(inputs, ctx) {
  const {
    projectRoot = 'C:/work/agentpowershell',
    requestPath = 'request.task.md',
    architectureDoc = 'docs/architecture.md',
    readmePath = 'README.md',
    targetParityScore = 90,
    maxImplementationBatches = 6
  } = inputs;

  const specification = await ctx.task(specificationAuditTask, {
    projectRoot,
    requestPath,
    architectureDoc,
    readmePath
  });

  const backlog = await ctx.task(backlogPlanningTask, {
    projectRoot,
    specification,
    targetParityScore
  });

  let parityScore = specification.initialParityScore ?? 0;
  let batchNumber = 0;
  let currentBacklog = backlog;
  let currentVerification = null;
  const history = [];

  while (batchNumber < maxImplementationBatches && parityScore < targetParityScore) {
    batchNumber++;

    const implementation = await ctx.task(implementationBatchTask, {
      projectRoot,
      specification,
      backlog: currentBacklog,
      batchNumber,
      targetParityScore
    });

    currentVerification = await ctx.task(verificationBatchTask, {
      projectRoot,
      specification,
      implementation,
      batchNumber
    });

    const parityReview = await ctx.task(parityScoringTask, {
      projectRoot,
      specification,
      implementation,
      verification: currentVerification,
      batchNumber,
      targetParityScore
    });

    parityScore = parityReview.parityScore ?? parityScore;
    history.push({
      batchNumber,
      implementation,
      verification: currentVerification,
      parityReview
    });

    if (parityScore >= targetParityScore) {
      break;
    }

    currentBacklog = await ctx.task(backlogRefinementTask, {
      projectRoot,
      specification,
      previousBacklog: currentBacklog,
      parityReview,
      verification: currentVerification,
      batchNumber
    });
  }

  const finalReview = await ctx.task(finalParityReviewTask, {
    projectRoot,
    specification,
    history,
    currentBacklog,
    finalVerification: currentVerification,
    parityScore,
    targetParityScore
  });

  return {
    success: parityScore >= targetParityScore && (finalReview.approved ?? false),
    parityScore,
    targetParityScore,
    batchesCompleted: batchNumber,
    remainingGaps: finalReview.remainingGaps ?? [],
    finalReview,
    history
  };
}

export const specificationAuditTask = defineTask('specification-audit', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Audit requested spec against current repo state',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'principal engineer conducting spec-to-implementation gap analysis',
      task: 'Audit the current repository against the requested agentpowershell specification and identify the highest-impact gaps',
      context: {
        projectRoot: args.projectRoot,
        requestPath: args.requestPath,
        architectureDoc: args.architectureDoc,
        readmePath: args.readmePath
      },
      instructions: [
        'Read the requested specification from request.task.md.',
        'Read README.md and docs/architecture.md to understand the documented intent.',
        'Inspect the current repository state, especially src/, tests/, docs/, .github/, Dockerfile, install scripts, and platform projects.',
        'Identify what is already implemented, partially implemented, scaffolded, or missing.',
        'Produce a parity assessment against the stated goals: PowerShell execution parity direction, daemon+shim usability, policy enforcement, sandboxing/security, cross-platform support, packaging, CI/CD, and documentation.',
        'Prioritize the gaps by user impact and implementation dependency order.',
        'Return a concrete backlog seed that can drive implementation batches.'
      ],
      outputFormat: 'JSON with implementedAreas, partialAreas, missingAreas, prioritizedGaps, initialParityScore, and backlogSeed'
    },
    outputSchema: {
      type: 'object',
      required: ['implementedAreas', 'partialAreas', 'missingAreas', 'prioritizedGaps', 'initialParityScore', 'backlogSeed'],
      properties: {
        implementedAreas: { type: 'array', items: { type: 'string' } },
        partialAreas: { type: 'array', items: { type: 'string' } },
        missingAreas: { type: 'array', items: { type: 'string' } },
        prioritizedGaps: { type: 'array', items: { type: 'string' } },
        initialParityScore: { type: 'number' },
        backlogSeed: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const backlogPlanningTask = defineTask('backlog-planning', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Plan convergence backlog',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'technical lead creating an execution backlog for an unfinished systems project',
      task: 'Turn the repository gap audit into a concrete implementation backlog with verification gates',
      context: {
        projectRoot: args.projectRoot,
        specification: args.specification,
        targetParityScore: args.targetParityScore
      },
      instructions: [
        'Group the identified gaps into implementation batches that can realistically be completed and verified incrementally.',
        'Each batch should name target files/modules, expected user-facing outcome, and required verification commands.',
        'Prefer batches that make the project more usable before polishing peripheral areas.',
        'Call out any risky or likely-blocking areas explicitly.',
        'Return backlog items ordered by execution priority.'
      ],
      outputFormat: 'JSON with batches, risks, and successCriteria'
    },
    outputSchema: {
      type: 'object',
      required: ['batches', 'risks', 'successCriteria'],
      properties: {
        batches: { type: 'array', items: { type: 'object' } },
        risks: { type: 'array', items: { type: 'string' } },
        successCriteria: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const implementationBatchTask = defineTask('implementation-batch', (args, taskCtx) => ({
  kind: 'agent',
  title: `Implement prioritized batch ${args.batchNumber}`,
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'senior .NET systems engineer executing a prioritized implementation batch',
      task: 'Implement the next highest-priority backlog batch directly in the repository and leave it in a buildable, testable state',
      context: {
        projectRoot: args.projectRoot,
        specification: args.specification,
        backlog: args.backlog,
        batchNumber: args.batchNumber,
        targetParityScore: args.targetParityScore
      },
      instructions: [
        'Choose the next highest-priority unfinished batch from the backlog.',
        'Implement the code, tests, docs, scripts, and configuration changes required for that batch.',
        'Prefer real behavior over additional scaffolding or placeholders.',
        'Run the relevant local verification commands after making changes and include the results in the summary.',
        'If the batch reveals a narrower root cause than expected, solve that root cause rather than papering over symptoms.',
        'Return exactly what was changed, which files were touched, and what remains from the batch.'
      ],
      outputFormat: 'JSON with selectedBatch, filesChanged, commandsRun, outcomes, remainingWithinBatch, and blockers'
    },
    outputSchema: {
      type: 'object',
      required: ['selectedBatch', 'filesChanged', 'commandsRun', 'outcomes', 'remainingWithinBatch', 'blockers'],
      properties: {
        selectedBatch: { type: 'object' },
        filesChanged: { type: 'array', items: { type: 'string' } },
        commandsRun: { type: 'array', items: { type: 'string' } },
        outcomes: { type: 'array', items: { type: 'string' } },
        remainingWithinBatch: { type: 'array', items: { type: 'string' } },
        blockers: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const verificationBatchTask = defineTask('verification-batch', (args, taskCtx) => ({
  kind: 'agent',
  title: `Verify implementation batch ${args.batchNumber}`,
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'QA engineer verifying a systems-tool implementation batch',
      task: 'Verify the latest implementation batch against the requested specification and the batch goals',
      context: {
        projectRoot: args.projectRoot,
        specification: args.specification,
        implementation: args.implementation,
        batchNumber: args.batchNumber
      },
      instructions: [
        'Re-run the relevant build, test, and user-surface validation commands needed to verify the batch.',
        'Check that the claimed outcomes are actually present in the codebase and observable behavior.',
        'Identify regressions, incomplete areas, test gaps, and documentation mismatches introduced or left behind by the batch.',
        'Return a clear pass/fail assessment with evidence.'
      ],
      outputFormat: 'JSON with passed, evidence, regressions, testGaps, and unresolvedIssues'
    },
    outputSchema: {
      type: 'object',
      required: ['passed', 'evidence', 'regressions', 'testGaps', 'unresolvedIssues'],
      properties: {
        passed: { type: 'boolean' },
        evidence: { type: 'array', items: { type: 'string' } },
        regressions: { type: 'array', items: { type: 'string' } },
        testGaps: { type: 'array', items: { type: 'string' } },
        unresolvedIssues: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const parityScoringTask = defineTask('parity-scoring', (args, taskCtx) => ({
  kind: 'agent',
  title: `Score parity after batch ${args.batchNumber}`,
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'technical reviewer scoring project parity against an explicit product spec',
      task: 'Score how close the repository is to fulfilling the requested agentpowershell specification after the latest batch',
      context: {
        projectRoot: args.projectRoot,
        specification: args.specification,
        implementation: args.implementation,
        verification: args.verification,
        batchNumber: args.batchNumber,
        targetParityScore: args.targetParityScore
      },
      instructions: [
        'Score the repository from 0-100 against the requested spec and documented architecture.',
        'Weight real usability and tested behavior more heavily than documented intent.',
        'Explain the highest-impact remaining gaps that still block a claim of spec fulfillment.',
        'Recommend the next batch focus if the target score has not been reached.'
      ],
      outputFormat: 'JSON with parityScore, strengths, remainingGaps, and nextFocus'
    },
    outputSchema: {
      type: 'object',
      required: ['parityScore', 'strengths', 'remainingGaps', 'nextFocus'],
      properties: {
        parityScore: { type: 'number' },
        strengths: { type: 'array', items: { type: 'string' } },
        remainingGaps: { type: 'array', items: { type: 'string' } },
        nextFocus: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const backlogRefinementTask = defineTask('backlog-refinement', (args, taskCtx) => ({
  kind: 'agent',
  title: `Refine backlog after batch ${args.batchNumber}`,
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'technical lead refining an implementation backlog from fresh evidence',
      task: 'Refine the remaining backlog using the latest verification and parity review results',
      context: {
        projectRoot: args.projectRoot,
        specification: args.specification,
        previousBacklog: args.previousBacklog,
        parityReview: args.parityReview,
        verification: args.verification,
        batchNumber: args.batchNumber
      },
      instructions: [
        'Remove completed work from the backlog.',
        'Promote unresolved regressions or failed verification findings above lower-priority enhancements.',
        'Split oversized remaining batches if needed so the next implementation step is concrete and verifiable.',
        'Return the updated backlog in execution order.'
      ],
      outputFormat: 'JSON with batches, risks, and rationale'
    },
    outputSchema: {
      type: 'object',
      required: ['batches', 'risks', 'rationale'],
      properties: {
        batches: { type: 'array', items: { type: 'object' } },
        risks: { type: 'array', items: { type: 'string' } },
        rationale: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));

export const finalParityReviewTask = defineTask('final-parity-review', (args, taskCtx) => ({
  kind: 'agent',
  title: 'Final parity review',
  agent: {
    name: 'general-purpose',
    prompt: {
      role: 'principal engineer deciding whether the repository now fulfills its requested specification',
      task: 'Conduct the final parity review and decide whether the project can honestly be described as fulfilling the requested agentpowershell spec',
      context: {
        projectRoot: args.projectRoot,
        specification: args.specification,
        history: args.history,
        currentBacklog: args.currentBacklog,
        finalVerification: args.finalVerification,
        parityScore: args.parityScore,
        targetParityScore: args.targetParityScore
      },
      instructions: [
        'Review the full convergence history, with emphasis on the latest verified repository state.',
        'Decide whether the project now fulfills the requested spec well enough to claim completion.',
        'If not, list the remaining concrete blockers and explain why they matter.',
        'If yes, identify residual risks without understating them.',
        'Return a clear approved boolean.'
      ],
      outputFormat: 'JSON with approved, verdict, parityScore, remainingGaps, strengths, residualRisks, and nextSteps'
    },
    outputSchema: {
      type: 'object',
      required: ['approved', 'verdict', 'parityScore', 'remainingGaps', 'strengths', 'residualRisks', 'nextSteps'],
      properties: {
        approved: { type: 'boolean' },
        verdict: { type: 'string' },
        parityScore: { type: 'number' },
        remainingGaps: { type: 'array', items: { type: 'string' } },
        strengths: { type: 'array', items: { type: 'string' } },
        residualRisks: { type: 'array', items: { type: 'string' } },
        nextSteps: { type: 'array', items: { type: 'string' } }
      }
    }
  },
  io: {
    inputJsonPath: `tasks/${taskCtx.effectId}/input.json`,
    outputJsonPath: `tasks/${taskCtx.effectId}/output.json`
  }
}));
