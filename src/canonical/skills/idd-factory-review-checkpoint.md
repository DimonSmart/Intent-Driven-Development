# idd-factory-review-checkpoint

## Purpose

Independently review one selective checkpoint over its explicitly covered
completed Subtasks in a fresh read-only context.

## Inputs and boundaries

Read the checkpoint contract, covered contracts/results, relevant intent,
checkpoint-local diff, and authoritative runtime verification evidence. Do not
read the full request, unrelated or later work, or worker conversations. Do not
modify code, intent, verification policy, Factory state, or delegate.

## Structured result

Return worker protocol version 1 with role `checkpoint-reviewer` and one
outcome: `approved`, `needs-fix`, `needs-replan`, `blocked`, or
`intent-required`.

`needs-fix` supplies `payload.correctiveSubtask`: a complete implementation-only
contract with ID, contract Markdown, dependencies, and verification check IDs.
Do not reopen or rewrite completed work. Use `needs-replan` for invalid coverage,
ordering, or remaining contracts. Separate implementation assessment from
verification assessment and report only material current findings.
