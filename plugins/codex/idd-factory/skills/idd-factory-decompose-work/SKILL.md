---
name: idd-factory-decompose-work
description: Analyze one supplied request against relevant intent and repository evidence, then return clarification, a focused handoff, or ordered bounded Factory tasks.
---

# idd-factory-decompose-work

## Purpose

Produce the smallest safe ordered decomposition for one Factory request in an
isolated planning context.

## Inputs

Read the complete request, confirmed clarifications, relevant current intent,
and only repository evidence needed for task boundaries and verification. Do
not read previous Factory runs or write Factory state.

## Rules

- Choose focused implementation or coordinated Factory execution.
- Ask all questions that block safe planning together.
- Return `INTENT_REQUIRED` instead of inventing durable behavior.
- Order independently verifiable outcomes, not files. Verification must run
  without implementation from later tasks.
- Use the fewest short sequential tasks preserving safe implementation and
  review. Do not create dependency graphs or parallel stages.
- Use the task format from `idd-factory-run`.
- Do not create Factory state, product intent, code, or tests.

## Results

Return `READY`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`, `FOCUSED_HANDOFF`, or
`BLOCKED`. For `READY`, include relevant intent, repository areas, a work slug,
and all ordered task Markdown. Return no partial tasks with clarification and no
statuses, timestamps, agents, attempts, or speculative requirements.
