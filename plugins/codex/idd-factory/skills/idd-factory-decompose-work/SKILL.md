---
name: idd-factory-decompose-work
description: Analyze one supplied request against relevant intent and repository evidence, then return clarification, a focused handoff, or ordered bounded Factory tasks.
---

# idd-factory-decompose-work

## Purpose

Analyze one supplied request and return a bounded, ordered decomposition for a
Factory run. This is an isolated planning operation, not the coordinator.

## Inputs

- The complete source request or text-file contents.
- Any user-confirmed clarifications.
- `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant current
  numbered intent documents.
- Only repository evidence needed to identify task boundaries and verification.

Do not read previous Factory runs or write Factory state.

## Rules

- Decide whether one focused implementation or coordinated Factory execution
  is appropriate.
- Ask only questions that block safe decomposition or implementation, and
  return them in one compact set.
- Return `INTENT_REQUIRED` when required durable behavior is missing,
  contradictory, or would require an invented product decision.
- Split work by independently verifiable outcomes, not by individual files.
- Keep tasks sequential. Do not create a dependency graph or parallel stages.
- Use the fewest bounded tasks that preserve safe implementation and review.
- Every task must be short, self-contained, and use the task format defined by
  `idd-factory-run`.
- Do not create `request.md`, task files, product specifications, code, or tests.

## Results

- `READY`: return a concise work slug and the complete ordered task contents.
- `NEEDS_CLARIFICATION`: return all blocking questions and no partial tasks.
- `INTENT_REQUIRED`: identify the missing or conflicting intent and the
  applicable intent handoff.
- `FOCUSED_HANDOFF`: explain why one `idd-code-implement` operation is safer and
  sufficient.
- `BLOCKED`: identify another concrete condition that prevents safe planning.

## Output

Return the result token first, followed only by evidence needed by the
coordinator. For `READY`, include relevant intent, repository areas, work slug,
and ordered task Markdown. Do not add statuses, timestamps, agents, attempt
counts, or speculative requirements.
