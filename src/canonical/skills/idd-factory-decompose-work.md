# idd-factory-decompose-work

## Purpose

Produce the smallest safe ordered decomposition for one Factory request. This
is an isolated planning operation, not the coordinator.

## Inputs

- The complete request and user-confirmed clarifications.
- `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant current
  intent.
- Only repository evidence needed to define task boundaries and verification.

Do not read previous Factory runs or write Factory state.

## Rules

- Choose focused implementation or coordinated Factory execution.
- Ask all questions that block safe planning in one compact set.
- Return `INTENT_REQUIRED` rather than inventing missing or conflicting durable
  behavior.
- Order independently verifiable outcomes, not files. Every task's verification
  must run after that task and earlier tasks without implementation from later
  tasks.
- Use the fewest short, sequential, self-contained tasks that preserve safe
  implementation and review; do not create dependency graphs or parallel stages.
- Use the task format defined by `idd-factory-run`.
- Do not create Factory files, product intent, code, or tests.

## Results

- `READY`: return a work slug and all ordered task contents.
- `NEEDS_CLARIFICATION`: return all blocking questions and no partial tasks.
- `INTENT_REQUIRED`: identify the intent gap and applicable handoff.
- `FOCUSED_HANDOFF`: explain why one `idd-code-implement` operation is safer.
- `BLOCKED`: identify another concrete condition preventing safe planning.

## Output

Return the result token first and only evidence needed by the coordinator. For
`READY`, include relevant intent, repository areas, work slug, and task Markdown.
Do not add statuses, timestamps, agents, attempts, or speculative requirements.
