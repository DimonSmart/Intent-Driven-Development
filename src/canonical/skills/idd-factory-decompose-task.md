# idd-factory-decompose-task

## Purpose

Create the smallest safe initial task graph that lets the Factory make useful progress. A complete up-front plan is neither required nor preferred when later work depends on facts that are not known yet.

This skill is the semantic contract for capability `initial-decomposition` and role `task-decomposer`. Runtime owns scheduling, state transitions, graph mutation, persistence, verification, and selection of later capabilities.

## Inputs and boundaries

Read the complete request, relevant durable intent, and only repository evidence needed to identify safe work boundaries and dependencies. When `.idd/verification.yaml` exists, use only real top-level stable check IDs from that valid policy. When it is absent, use no invented check IDs and leave fallback verification to runtime.

Do not implement product changes, mutate Factory state, edit intent or verification policy, choose the next role/skill, or attempt to predict every future task.

Return `intent-required` rather than inventing missing durable product meaning. Return `needs-clarification` only when a user decision is genuinely required before any safe progress can be represented.

## Dynamic decomposition

`ready` returns `payload.workItems`. Each node contains:

```text
id
sequence
kind: subtask | review-checkpoint
definitionState: executable | outline
capability?          # required for executable; optional for outline
contractMarkdown
dependencies[]
coveredWorkItems[]
verificationCheckIds[]
verificationExpectations?  # object keyed by stable check ID: must-pass | may-fail
intentReferences[]
```

Use `executable` only when the work contract is self-contained enough to dispatch in a fresh context. Use `outline` for known future scope whose exact implementation contract depends on earlier results. An outline must still state its goal, dependency boundary, and why refinement is deferred.

The initial graph may contain only the work needed now plus useful outlines. Do not manufacture speculative detail merely to make the plan look complete. At least one root node must be executable or safely refinable unless the result reports a real blocker.

Capabilities describe the required kind of work, not an agent identity. Use only capabilities supported by Factory policy, such as `implementation`, `research`, `documentation`, or `semantic-review`. Never return a role or skill name as a scheduling decision.

`verificationExpectations` are deterministic intermediate expectations. Omitted checks and `must-pass` are treated as strict. `may-fail` is allowed only when a specific intermediate RED condition is intentionally expected; it never relaxes final verification.

## Intent-required payload

`payload.missingIntentDecisions` must be non-empty. Each item contains:

```text
area
whyBlocking
requiredDecisions[]
intentReferences[]
recommendedNextWorkflow?
```

`recommendedNextWorkflow` refers only to a user-facing durable-intent workflow (for example `idd-intent-change`), never to Factory execution routing.

## Result outcomes

Return protocol version 2 with role `task-decomposer` and one outcome:
`ready`, `needs-clarification`, `intent-required`, `focused-handoff`, or `blocked`.

The worker never creates Factory files and never chooses what runtime does next.
