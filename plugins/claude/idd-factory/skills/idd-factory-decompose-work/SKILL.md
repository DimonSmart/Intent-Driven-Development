---
name: idd-factory-decompose-work
description: Analyze one supplied request against relevant intent and repository evidence, then return clarification, a focused handoff, or ordered bounded Factory tasks.
context: fork
agent: Explore
argument-hint: "[request text or file contents]"
allowed-tools: Read Glob Grep
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
- Produce an optional compact `run-context.md` only when multiple tasks share
  substantial constraints, assumptions, or references.
- Put every task-specific requirement and preservation boundary in the owning
  task contract. Use `run-context.md` only for genuinely shared context.
- Make every task understandable, implementable, and reviewable without reading
  the complete request or other task files.
- Do not copy the complete request into `run-context.md` or repeat it across
  tasks. Distribute only the context and requirements needed for each task.
- Do not use vague references such as "the corresponding part of the request",
  "the requirements above", or "preserve existing behavior" without naming the
  concrete requirement or preservation boundary.
- Use the task and run-context formats from `idd-factory-run`.
- Do not create Factory state, product intent, code, or tests.

## Results

Return `READY`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`, `FOCUSED_HANDOFF`, or
`BLOCKED`. For `READY`, include relevant intent, repository areas, a work slug,
optional `run-context.md` Markdown, and all ordered self-contained task Markdown.
Return no partial tasks with clarification and no statuses, timestamps, agents,
attempts, or speculative requirements.
