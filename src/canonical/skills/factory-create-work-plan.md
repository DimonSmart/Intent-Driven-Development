# factory-create-work-plan

## Purpose

Create a temporary Factory Work Plan from the current user request, current
`.specs/` intent, and relevant repository evidence.

Factory Work Plans are temporary execution state. They are not product intent,
not product specifications, not ADRs, and not durable project documentation.

In future versions, Factory Work Plan tasks may be backed by an external Work
Item Provider. The current implementation uses temporary local markdown files
only.

## Rules

- Read `.specs/README.md`, `.specs/INDEX.md`, and only relevant current specs.
- Do not read the whole `.specs/` directory by default.
- Do not write or modify code.
- Do not write into `.specs/`.
- Do not treat implementation as product intent.
- If requested durable behavior is missing from specs, stop and route to
  `spec-change` or `spec-brainstorm`.
- If intent is unclear, route to `spec-brainstorm`.
- If an architecture decision is missing, route to `spec-new-document` for an
  ADR or spike when appropriate.
- Create a work plan only for behavior already covered by current specs or
  explicitly confirmed as task-only implementation work.
- The work plan is temporary execution state.
- The work plan must include cleanup instructions.
- Do not read old factory work plans unless the user explicitly provides an
  exact path.

## Workflow

1. Identify whether the user is asking for planned implementation orchestration,
   task slicing, multi-step execution, or factory-style work.
2. Read `.specs/README.md` and `.specs/INDEX.md`.
3. Read only the relevant current specs, ADRs, and active spikes needed to
   understand the requested implementation task.
4. Inspect repository evidence needed to identify likely code and test areas.
5. Stop and route to the appropriate spec skill if durable product intent is
   missing, unclear, or wrong.
6. Create one work directory using this shape:
   `.idd/factory/work/<yyyyMMdd-HHmmss>-<slug>/`.
7. Write the work plan to:
   `.idd/factory/work/<yyyyMMdd-HHmmss>-<slug>/work-plan.md`.

## Output Format

```md
# Factory Work Plan

## Identity

- Created:
- Work slug:
- User request:
- Work directory:

## Temporary Artifact Notice

This plan is temporary execution state.
It is not product intent.
It is not a specification.
It must not be reused automatically for unrelated future work.

## Relevant Current Intent

- Specs:
- ADRs:
- Active spikes, if relevant:

## Scope

- In scope:
- Out of scope:

## Repository Map

- Candidate implementation areas:
- Candidate test areas:
- Important conventions:

## Tasks

### Task 1: <title>

- Purpose:
- Inputs:
- Expected code areas:
- Expected tests:
- Dependencies:
- Review focus:

## Verification Plan

- Commands:
- Manual checks, if needed:

## Review Gates

- Per-task review:
- Final review:

## Risks and Open Questions

- Risk:
- Mitigation:

## Cleanup Policy

Delete this work directory at finish unless the user explicitly asks to keep or
commit it.
```
