---
name: idd-factory-decompose-task
description: Decompose one Factory Task into ordered Subtasks and Review checkpoints.
---

# idd-factory-decompose-task

## Purpose

Produce the smallest safe ordered decomposition for one complete Factory
request. This skill is the complete semantic contract for the
`task-decomposer` role.

## Inputs and boundaries

Read the complete request, relevant current intent, and only repository evidence
needed for task boundaries, dependencies, checkpoint placement, and stable
verification IDs. The project verification policy may be inspected only to
select existing stable IDs; the worker does not execute mandatory checks. Do
not read previous runs, write code or state, edit intent, or delegate.

When `.idd/verification.yaml` exists, every `verificationCheckIds[]` value must
be copied exactly from a top-level key under `checks:`. Never put test class
names, test method names, commands, filters, descriptions, or invented
identifiers in this field. If an existing valid policy has no configured check
ID covering a required property, return `needs-clarification` or `blocked`; do
not fabricate one. An existing malformed policy is a blocking policy error and
must not be treated as missing or replaced with fallback.

When `.idd/verification.yaml` is absent, continue normally. Use
`verificationCheckIds: []` for every work item, preserve the required
verification properties in human-readable contract Markdown, and let the
deterministic Runtime apply repository/platform fallback at its verification
gates. Absence alone is never `needs-clarification` or `blocked`: do not ask the
user to create a policy, create one, or invent stable IDs.

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
and verification IDs before creating state. The runtime owns workflow routing,
machine protocol validation, persisted state, and authoritative verification.
The worker never creates files or chooses the next role or skill. Relevant
project and domain skills may be used normally for focused semantic analysis.
