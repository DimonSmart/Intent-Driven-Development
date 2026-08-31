---
name: idd-factory-replan
description: Propose bounded semantic changes to remaining Factory graph work without mutating authoritative runtime state.
---

# idd-factory-replan

## Purpose

Propose a bounded global restructuring of the *remaining* task graph when a persisted `global-replan-required` trigger proves that the current global strategy is no longer correct.

This skill is not the normal mechanism for discovering one prerequisite. Local discoveries belong in `additional-work-required`; deferred detail belongs in scoped refinement. Runtime owns candidate validation and atomic graph mutation.

## Inputs

Read the supplied original request, relevant durable intent, persisted replan trigger, remaining graph definitions, minimal immutable completed-work references, run context, and supplied authoritative verification evidence.

Completed work and its contract/result provenance are immutable.

## Result protocol

Return protocol version 2 with role `factory-replanner` and one outcome:
`replan-proposed`, `intent-required`, `needs-clarification`, or `blocked`.

`replan-proposed` contains `payload.operations`. Prefer the smallest set of operations necessary to restore a correct strategy. Supported operations are:

- `add-work` — add an executable or outline work item;
- `refine-work` — replace the definition of mutable remaining work or replace it with a new node;
- `supersede-work` — retire mutable work that is no longer required;
- `change-dependencies` — change dependencies of mutable remaining work;
- `reorder-work` — reorder mutable remaining work without implying execution order beyond dependencies;
- `update-checkpoint-coverage` — adjust a mutable semantic review boundary;
- `update-run-context` — update compact runtime context when useful.

Compatibility aliases from protocol-v1 may be accepted by runtime, but new proposals should use the operations above.

Every added/refined work item uses the dynamic task-graph schema:

```text
id
sequence?
kind
definitionState
capability?
contractMarkdown
dependencies[]
coveredWorkItems[]
verificationCheckIds[]
verificationExpectations?
```

Use only real stable verification IDs from a valid `.idd/verification.yaml`. Never invent IDs or edit verification policy.

## Boundaries

- Do not modify completed or superseded work.
- Do not mark work complete or select operational statuses.
- Do not modify implementation, intent, Factory state, graph history, or `.idd/factory.yaml`.
- Do not perform implementation or semantic review.
- Do not select a role, skill, workflow phase, or next operation.
- Do not use Factory event/history replay as memory; supplied persisted state is authoritative.
- Do not use global replan when a local additional dependency or scoped refinement is sufficient.

For `intent-required`, use the standard `missingIntentDecisions` structure. Any `recommendedNextWorkflow` refers only to an intent-editing workflow outside Factory runtime orchestration.
