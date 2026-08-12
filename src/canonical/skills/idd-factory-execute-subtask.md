# idd-factory-execute-subtask

## Purpose

Implement exactly one self-contained active Subtask in a fresh context. Current
`.idd/intent/` remains normative.

## Inputs and boundaries

Read the supplied Subtask, optional run context, relevant intent, current diff,
focused repository evidence, and retry/verification-failure evidence. Do not
read the full request, unrelated Subtasks, checkpoints, or previous transcripts.

Make the smallest coherent implementation change. Do not select work, mutate
Factory state, change intent, perform review or finalization, broaden scope, or
delegate. Runtime verification is authoritative; worker verification claims are
diagnostic only.

## Structured result

Return worker protocol version 1 with role `implementer` and one outcome:
`completed`, `needs-replan`, `blocked`, or `intent-required`.

For `completed`, payload contains a concise `summary`, `declaredChanges[]`,
`concerns[]`, and optional `verificationClaims[]`. Use `needs-replan` when the
contract, ordering, repository reality, or assigned verification scope is
insufficient. Use `intent-required` only for missing durable product meaning and
`blocked` for an external or human-decision condition.
