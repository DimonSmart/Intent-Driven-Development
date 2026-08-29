---
name: idd-factory-execute-subtask
description: Implement one explicit active Subtask in an isolated worker context.
---

# idd-factory-execute-subtask

## Purpose

Implement exactly one self-contained active Subtask in a fresh context. Current
`.idd/intent/` remains normative. This skill is the complete semantic contract
for the `implementer` role.

## Modes

- `normal`: implement the supplied Subtask contract.
- `verification-fix`: make only the implementation changes needed for the
  supplied failed authoritative verification gate and textual scope.

Both modes use the same role and outcomes. In `verification-fix`, `completed`
means only that the repair attempt ended; the runtime must rerun the same gate
and only a runtime `Passed` result can complete it.

## Inputs and boundaries

Read the supplied Subtask, optional run context, relevant intent, current diff,
focused repository evidence, and retry/verification-failure evidence. Do not
read the full request, unrelated Subtasks, checkpoints, or previous transcripts.

Make the smallest coherent implementation change. Do not select work, mutate
Factory state, change intent, perform review or finalization, broaden scope, or
delegate. Runtime verification is authoritative; worker verification claims are
diagnostic only.

Use lightweight repository inspection needed for implementation, but do not run
build, test, lint, or other potentially long-lived diagnostic commands. The
runtime executes the applicable authoritative verification gate immediately
after this result. Do not resolve or run mandatory Factory check IDs, and never
leave a tool process active when returning the structured result. The runtime
owns orchestration, retries, machine protocol validation, authoritative
verification, and the next semantic capability.

## Structured result

Return worker protocol version 1 with role `implementer` and one outcome:
`completed`, `needs-replan`, `blocked`, or `intent-required`.

For `completed`, payload contains a concise `summary`, `declaredChanges[]`,
`concerns[]`, and optional `verificationClaims[]`. Use `needs-replan` when the
contract, ordering, repository reality, or assigned verification scope is
insufficient. Use `intent-required` only for missing durable product meaning and
`blocked` for an external or human-decision condition.

For `intent-required`, `payload.missingIntentDecisions` is a non-empty array.
Each item contains:

```text
area
whyBlocking
requiredDecisions[]
intentReferences[]
recommendedNextWorkflow?  # e.g. idd-intent-change or idd-intent-new-document: ADR
```

`area` is a short domain or contract area name. `whyBlocking` explains why safe
implementation cannot continue. `requiredDecisions[]` names the concrete durable
decisions that must be recorded under `.idd/intent`. `intentReferences[]` names
related IDD document IDs or paths; use an empty array only when no existing
intent document applies. `recommendedNextWorkflow` is optional and must name an
available intent workflow when a useful next step is known. Keep the list
concise and decision-oriented; do not substitute logs, implementation guesses,
or vague requests to "clarify intent".
