---
name: idd-factory-decompose-work
description: Analyze one supplied request against relevant intent and repository evidence, then return clarification, a focused handoff, or ordered execution tasks with the fewest useful review checkpoints.
---

# idd-factory-decompose-work

## Purpose

Produce the smallest safe ordered decomposition for one Factory request in an
isolated planning context.

## Inputs

Read the complete request, confirmed clarifications, relevant current intent,
and only repository evidence needed for execution boundaries, checkpoint
placement, and verification. Do not read previous Factory runs or write Factory
state.

## Rules

- Choose focused implementation or coordinated Factory execution.
- Ask all questions that block safe planning together.
- Return `INTENT_REQUIRED` instead of inventing durable behavior.
- Do not produce partial work items with `INTENT_REQUIRED`; after intent changes,
  decompose the complete original request again.
- Never create an execution task or checkpoint for editing, linting, auditing, or
  otherwise changing `.idd/intent/`.
- Order independently executable outcomes, not files. Each execution task must
  be completable and locally verifiable without implementation from later tasks.
- Keep execution tasks small enough for one bounded worker context.
- Separate execution boundaries from review boundaries. Several adjacent
  execution tasks may share one later review checkpoint.
- Use the fewest review checkpoints that protect later work:
  - place one after a risky foundation, public contract, persisted-data change,
    security boundary, concurrency boundary, or other result that later tasks
    depend on;
  - group adjacent mechanical migrations or similar low-risk tasks under one
    checkpoint;
  - omit a checkpoint when no later work depends on early independent review;
  - do not add a terminal checkpoint that only duplicates final integrated
    review.
- Every checkpoint must cover a contiguous sequence of preceding execution tasks
  since the previous checkpoint and must not cover another checkpoint.
- Produce optional compact `run-context.md` only when multiple work items share
  substantial constraints, assumptions, or references.
- Put every task-specific requirement and preservation boundary in the owning
  execution task. Put checkpoint-specific risks and evidence in the checkpoint.
- Make every execution task understandable and implementable without reading the
  complete request or other work-item files.
- Do not copy the complete request into `run-context.md` or repeat it across
  execution tasks.
- Do not use vague references such as "the corresponding part of the request",
  "the requirements above", or "preserve existing behavior" without naming the
  concrete requirement or preservation boundary.
- Use the execution-task, review-checkpoint, and run-context formats from
  `idd-factory-run`.
- Do not create Factory state, product intent, code, or tests.
- Read `.idd/verification.md` when present before producing items. Resolve
  `subtask` checks for each execution subtask and `checkpoint` checks for each
  review checkpoint from its complete scope; record stable check IDs only, never
  commands, timeout, or instructions. Put shared costly checks at checkpoint or
  final instead of every subtask. Policy errors required by the run block
  planning before Factory state exists.

## Results

Return `READY`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`, `FOCUSED_HANDOFF`, or
`BLOCKED`.

For `READY`, include relevant intent, repository areas, a work slug, optional
`run-context.md` Markdown, and all ordered execution-task and review-checkpoint
Markdown. Return no partial items with clarification and no statuses, timestamps,
agents, attempts, or speculative requirements.
