---
name: idd-factory-replan
description: Propose bounded semantic changes to remaining Factory work without mutating runtime state.
context: fork
agent: Explore
allowed-tools: [Read, Glob, Grep]
---

# idd-factory-replan

## Purpose

Propose bounded semantic changes to remaining Factory work when repository
reality proves the current decomposition incorrect. The runtime validates and
applies the proposal. This skill is the complete semantic contract for the
`factory-replanner` role.

## Inputs

Read only the supplied original request, relevant current intent, run context,
triggering work item and reason, mutable ready/planned contracts, minimal
completed-work context, and supplied verification evidence when relevant.

## Result protocol

Return a worker protocol version 1 envelope with role `factory-replanner` and
one outcome: `replan-proposed`, `intent-required`, `needs-clarification`, or
`blocked`.

`replan-proposed` contains `payload.operations`. Supported V1 operations are:

- `insert-subtask`
- `replace-ready-subtask`
- `supersede-ready-subtask`
- `reorder-ready-work`
- `update-run-context`
- `update-checkpoint-coverage`
- `insert-checkpoint`
- `remove-unused-ready-checkpoint`

Every inserted or replaced work item is a self-contained structured contract
with stable ID, kind, sequence, contract Markdown, dependencies, coverage, and
verification check IDs as applicable.

When `.idd/verification.yaml` exists, use only real top-level stable check IDs
from the valid policy; never invent IDs, and never silently replace a malformed
existing policy with fallback. When the file is absent, every inserted or
replaced work item uses `verificationCheckIds: []`, keeps required verification
properties in its human-readable contract Markdown, and leaves
repository/platform fallback verification to the deterministic Runtime.
Missing policy alone is not a clarification or blocker and must not cause the
worker to create or request a policy.

## Boundaries

- Do not modify completed work or results.
- Do not change operational status, mark work complete, or mutate Factory state.
- Do not edit product intent, implementation, verification policy, or review
  results.
- Do not perform implementation or review.
- Do not create child agents or use conversation history as Factory memory.
- Prefer the smallest proposal that repairs the demonstrated semantic defect.
- The runtime owns machine validation, state mutation, workflow routing,
  authoritative verification, and selection of the next role or skill.
- Focused repository reads and relevant project or domain skills may be used for
  semantic diagnosis.
