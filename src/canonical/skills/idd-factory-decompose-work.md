# idd-factory-decompose-work

## Purpose

Produce the smallest safe ordered decomposition for one Factory request in an
isolated planning context.

## Inputs

Read the complete request, confirmed clarifications, relevant current intent,
and only repository evidence needed for intent preflight, task boundaries, and
verification. Do not read previous Factory runs or write Factory state.

## Rules

- Choose focused implementation or coordinated Factory execution.
- Ask all questions that block safe planning together.
- Perform intent preflight before returning implementation tasks.
- Return `INTENT_REQUIRED` instead of inventing durable behavior or creating a
  task that changes durable intent.
- For `INTENT_REQUIRED`, identify the exact missing or conflicting durable
  behavior and relevant owning intent; return no work slug, run context, or
  partial tasks.
- Never represent intent work as a Factory task. Tasks must not edit
  `.idd/intent/`, invoke an intent-changing workflow, lint or audit intent as
  their implementation outcome, or use an intent update as a dependency or
  completion condition.
- After the coordinator resolves intent, analyze the complete original request
  again against the updated current intent and create only implementation tasks.
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
`BLOCKED`.

`READY` means current intent is already sufficient and every returned task is
implementation-only. Include relevant intent, repository areas, a work slug,
optional `run-context.md` Markdown, and all ordered self-contained task Markdown.

For `INTENT_REQUIRED`, return only the missing or conflicting durable behavior,
the relevant owning intent or product area, and the intent workflow handoff
needed before decomposition is attempted again.

Return no partial tasks with clarification or intent-required results and no
statuses, timestamps, agents, attempts, or speculative requirements.
