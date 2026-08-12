# idd-factory-decompose-task

## Purpose

Produce the smallest safe ordered decomposition for one complete Factory
request. Read `references/project-verification.md` before assigning checks.

## Inputs and boundaries

Read the complete request, relevant current intent, and only repository evidence
needed for task boundaries, dependencies, checkpoint placement, and stable
verification IDs. Do not read previous runs, write code or state, edit intent,
or delegate.

Return `intent-required` instead of inventing durable behavior. Preserve any
explicit ordering, staging, dependency, and review boundaries from the request.
Order independently verifiable outcomes rather than files. Every Subtask is a
self-contained contract that an implementer can execute without the complete
request, other tasks, checkpoints, or worker transcripts.

Use selective checkpoints only where early independent review protects later
work. Coverage is contiguous, names only preceding Subtask IDs, never covers a
checkpoint, and does not duplicate final review.

## Structured result

Return worker protocol version 1 with role `task-decomposer` and one outcome:
`ready`, `needs-clarification`, `intent-required`, `focused-handoff`, or
`blocked`.

`ready` supplies `payload.workItems`, an ordered array. Each item contains:

```text
id
sequence
kind: subtask | review-checkpoint
contractMarkdown
dependencies[]
coveredWorkItems[]
verificationCheckIds[]
intentReferences[]
```

Subtask contract Markdown contains goal, context, scope, requirements, done
conditions, verification properties, and preservation boundaries. A checkpoint
contract contains coverage, review scope, and focused verification. Do not
include commands; use stable verification IDs.

The runtime validates identity, order, dependencies, coverage, intent boundary,
and verification IDs before creating state. The worker never creates files.
