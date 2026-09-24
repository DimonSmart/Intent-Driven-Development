---
name: idd-engineering-change
description: Manage explicit durable Engineering Rule add, modify, and remove mutations under `.idd/engineering/`, with batch semantic planning, lazy bootstrap, stable allocation, Factory safety guard, and direct shared mechanical validation.
---

# idd-engineering-change

Standard owner of project Engineering Rule semantic mutations: `add`, `modify`, and `remove`.

Supported operations: `add`, `modify`, and `remove`.

Read `references/engineering-guardrails.md` first. Read the `Mechanical Engineering Validation` section and perform the applicable bounded checks directly. Engineering management stays in `idd-intent`; do not create a separate plugin, runtime, MCP server, agent, workflow engine, Engineering lint skill, or validator subsystem. Do not invoke another skill merely to validate Engineering structure.

## Boundary

Create or change Rules only for explicit durable implementation-only project decisions. Current code and repeated patterns are evidence, not authority.

Ask whether another implementation could preserve the product contract and still violate the constraint. Product behavior belongs to Intent, build/test commands to Verification, temporary rules to task/Factory context, and incidental implementation detail nowhere.

Intent <-> Engineering movement requires a separate explicit semantic decision.

## Mechanical validation execution

Use bounded repository inspection operations such as read, list/glob, search/grep, exact string or regex matching, count, and compare. A short single-purpose host/shell command is allowed when useful.

Do not generate a general-purpose validator program or temporary `.ps1`, `.py`, `.sh`, `.cs`, executable, or equivalent inline multi-step program solely to perform Engineering validation.

## Phase 1 — safety gate

Before reading or planning a mutation, check `.idd/factory/current/request.md`.

If it exists, return `blocked` and mutate nothing. A directory without `request.md`, or legacy `state.json` alone, is not an active run. Do not patch `plan.md`, rewrite `TaskRelatedEngineering`, restart Factory, or re-plan automatically.

## Phase 2 — read current Engineering state

If `.idd/engineering/` exists:

1. read README and INDEX;
2. apply Mechanical Engineering Validation directly;
3. return `blocked` on structural error.

Never bootstrap over an existing partial or malformed Engineering layer.

If `.idd/engineering/` is absent, treat Engineering state as empty and create nothing yet.

A valid legacy layer without allocator remains readable at this phase.

## Phase 3 — semantic planning

Complete semantic planning before any file mutation:

1. identify every durable Engineering decision in the request;
2. group them into the minimum set of semantically coherent Rules;
3. map candidates to current Rules;
4. classify each candidate as `new`, `equivalent existing rule`, `existing rule requires modification`, or `ambiguous`;
5. resolve modify/remove targets to exactly one current Rule;
6. determine the complete add/modify/remove/no-op plan;
7. determine how many new IDs are required and verify allocator capacity.

One invocation may process multiple durable Engineering decisions. Do not create one Rule mechanically per bullet, and do not merge independent constraints merely to minimize files.

If any candidate is `ambiguous`, return `ambiguous` before mutation. Do not bootstrap, migrate the allocator, create or edit Rules, change INDEX, or advance Next ID.

Semantic equivalence is model work. Do not fake it with deterministic filename, keyword, embedding, or similarity heuristics.

## Phase 4 — materialization

Only when the complete plan contains a real mutation:

1. if the layer is completely absent and at least one new Rule is planned, materialize the packaged bootstrap;
2. if an existing valid legacy layer lacks allocator metadata, migrate the allocator;
3. allocate sequential IDs only for planned new Rules;
4. perform planned add/modify/remove mutations;
5. update INDEX;
6. write the final allocator value;
7. apply Mechanical Engineering Validation directly again.

The goal is semantic atomicity: planning and ambiguity detection finish before mutation. Do not add a filesystem transaction or rollback subsystem.

## Lazy bootstrap

For the first real add into a completely absent layer, resolve the current skill resource and copy:

```text
assets/bootstrap/.idd/engineering/README.md
assets/bootstrap/.idd/engineering/INDEX.md
```

to project `.idd/engineering/` before creating Rules.

Resolve assets relative to the current `SKILL.md` or equivalent skill resource. Create the destination directory when needed. Do not reproduce bootstrap contents from memory.

If the packaged bootstrap cannot be resolved with current host capabilities, return a clear `blocked` result instead of inventing replacement bootstrap files.

## Allocator

Canonical allocator metadata is exactly:

```text
Next ID: ENG-NNNN
```

A valid legacy layer without allocator is migrated only immediately before a real mutation: use `max(existing ENG IDs) + 1`, or `ENG-0001` when no Rules exist.

If an existing allocator is malformed, duplicated, or not strictly greater than every current Rule ID, return `blocked` instead of repairing it.

For a batch, allocate consecutive IDs only to `new` candidates. Modified and equivalent candidates consume no IDs. For example, starting from `ENG-0007`, three new Rules receive `ENG-0007`, `ENG-0008`, and `ENG-0009`, and final Next ID becomes `ENG-0010`.

If the required allocation would exceed `ENG-9999`, return `blocked` before mutation. Never create `ENG-10000` without a separate ID-schema change.

A `no-op` never creates, migrates, or advances the allocator.

## Add

An add request may produce a new Rule, modify an existing semantic owner, or be a no-op.

Equivalent existing Rule => no-op for that candidate. If an existing Rule owns the area and the explicit decision changes its semantics, preserve its stable ID and modify it rather than create a duplicate.

## Modify

Resolve the target to exactly one current Rule. Preserve `ENG-NNNN` and normally preserve filename. Zero matches => `blocked` with a missing-target diagnostic. Multiple plausible matches => `ambiguous`.

Update Rule content and the matching INDEX projection. Semantically unchanged modify => `no-op`.

## Remove

Resolve the target to exactly one current Rule. If it is unambiguously already absent, return `no-op`.

Delete the Rule document and INDEX row, preserve the allocator, and create no archive, tombstone, deprecated document, or history file. Git is history.

## Post-mutation validation

After every real mutation, apply Mechanical Engineering Validation directly.

If the workflow detects a mechanical error caused by its just-performed mutation, it may fix that mechanical error and repeat validation. Do not report `success` until the final state passes.

If a clean state cannot be obtained, return `blocked` and identify the remaining structural inconsistency. Do not add a complex rollback subsystem.

## Ownership and implementation

This skill changes durable Engineering knowledge, not application implementation. Other IDD skills may read/validate Engineering, report candidates, and hand off an explicitly confirmed candidate, but they do not mutate Rules themselves.

If the same request explicitly includes implementation, continue afterward with the normal implementation workflow. Existing consumers continue to apply every Always Rule plus semantically relevant Conditional Rules.

## Result statuses

Return exactly one of:

- `success`: at least one planned mutation completed and final Mechanical Engineering Validation passed;
- `no-op`: no files needed mutation; allocator state is unchanged;
- `ambiguous`: semantic ownership or target cannot be determined safely; no mutation occurred;
- `blocked`: safety, structural, capacity, bootstrap, or other boundary condition prevents a correct mutation.
