# idd-factory-decompose-task

## Required Reference

Read `references/project-verification.md` before resolving policy checks or
repository/platform fallback.

## Purpose

Produce the smallest safe ordered decomposition for one Factory Task defined by
the supplied request.

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
- Never create a Subtask or Review checkpoint for editing, linting, auditing, or
  otherwise changing `.idd/intent/`.
- Order independently executable outcomes, not files. Each Subtask must
  be completable and locally verifiable without implementation from later tasks.
- Keep Subtasks small enough for one bounded worker context.
- Separate execution boundaries from review boundaries. Several adjacent
  Subtasks may share one later Review checkpoint.
- Use the fewest Review checkpoints that protect later work:
  - place one after a risky foundation, public contract, persisted-data change,
    security boundary, concurrency boundary, or other result that later tasks
    depend on;
  - group adjacent mechanical migrations or similar low-risk tasks under one
    checkpoint;
  - omit a checkpoint when no later work depends on early independent review;
  - do not add a terminal checkpoint that only duplicates final integrated
    review.
- Every checkpoint must cover a contiguous sequence of preceding Subtasks
  since the previous checkpoint and must not cover another checkpoint.
- Identify every covered Subtask by its stable `<sequence>-<slug>` identity.
  A `## Covers` entry must never include `.ready`, `.active`, `.completed`, or
  `.blocked`, and must not include a `.md` extension; status filenames change
  during execution and are not stable references.
- Produce optional compact `run-context.md` only when multiple work items share
  substantial constraints, assumptions, or references.
- Put every Subtask-specific requirement and preservation boundary in its owning
  Subtask. Put checkpoint-specific risks and evidence in the checkpoint.
- Make every Subtask understandable and implementable without reading the
  complete request or other work-item files.
- Do not copy the complete request into `run-context.md` or repeat it across
  Subtasks.
- Do not use vague references such as "the corresponding part of the request",
  "the requirements above", or "preserve existing behavior" without naming the
  concrete requirement or preservation boundary.
- Use the Subtask, Review checkpoint, and run-context formats from
  `idd-factory-run`.
- Do not create Factory state, product intent, code, or tests.
- Read `.idd/verification.yaml` when present before producing items. Resolve
  `subtask` checks for each Subtask and `checkpoint` checks for each
  Review checkpoint from its complete scope; record stable check IDs only, never
  commands, timeout, or instructions. Put shared costly checks at checkpoint or
  final instead of every subtask. Invalid or unresolved verification-policy
  entries from `.idd/verification.yaml` block planning before Factory state is
  created.
- If a read-only command is rejected by execution policy or fails because of its
  form, make at most two narrower, simpler alternatives: split a compound
  command, remove recursion or wildcards, then read a specific directory or
  file. An equivalent read-only tool is allowed. Do not repeat a command,
  elevate permissions, change approval or sandbox policy, or write. Return
  `BLOCKED` only after required information remains unavailable after those
  alternatives; record only `Reason`, `Not verified`, and `Resume when`.

## Results

Return `READY`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`, `FOCUSED_HANDOFF`, or
`BLOCKED`.

For `READY`, include relevant intent, repository areas, a work slug, optional
`run-context.md` Markdown, and all ordered Subtask and Review checkpoint
Markdown. Each checkpoint `## Covers` list uses only stable
`<sequence>-<slug>` Subtask identities. Return no partial items with
clarification and no statuses, timestamps, agents, attempts, or speculative
requirements.
