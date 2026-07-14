# idd-factory-create-work-plan

## Purpose

Create a temporary Factory Work Plan from the current user request, current
`.idd/intent/` intent, and relevant repository evidence.

This skill is the workflow contract. Factory role prompts are optional local
references that may help structure planning, but they do not own durable
product intent.

Factory Work Plans are temporary execution state. They are not product intent,
not product specifications, not ADRs, and not durable project documentation.

In future versions, Factory Work Plan tasks may be backed by an external Work
Item Provider. The current implementation uses temporary local markdown files
only.

## Routing

Use this workflow when the request requires coordinated multi-task
implementation, temporary planning, sequencing, task-level reviews, or final
review. Do not use Factory when one focused `idd-code-implement` operation is
sufficient, when the user only asks to change intent, or while intent is not
ready.

The workflow may receive a temporary route classification and preservation
boundary from `idd-route`. Use them as execution constraints, not as product
intent.

## Rules

- Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant current specs.
- Do not read the whole `.idd/intent/` directory by default.
- Do not write or modify code.
- Do not write into `.idd/intent/`.
- Do not treat implementation as product intent.
- If requested durable behavior is missing from specs, do not create a
  speculative work plan. Route to `idd-intent-brainstorm`,
  `idd-intent-change`, or `idd-intent-new-document` as appropriate.
- If intent is unclear, route to `idd-intent-brainstorm`.
- If an architecture decision is missing, route to `idd-intent-new-document` for an
  ADR or spike when appropriate.
- Create a work plan only for behavior already covered by current specs or
  explicitly confirmed as task-only implementation work.
- Do not create a work plan before validating current intent for the requested
  work.
- If `.idd/factory/.gitignore` is missing, create it from the packaged factory
  asset before writing factory work files.
- The work plan is temporary execution state.
- Include any route classification and preservation boundary as temporary
  execution evidence.
- Do not convert preservation boundary text into new product intent.
- The work plan must include cleanup instructions.
- Do not read old factory work plans unless the user explicitly provides an
  exact path.

## Workflow

1. Read `.idd/intent/README.md` and `.idd/intent/INDEX.md`.
2. Read only the relevant current specs, ADRs, and active spikes needed to
   understand the requested implementation task.
3. Inspect repository evidence needed to identify likely code and test areas.
4. Stop and route to the appropriate intent skill if durable product intent is
   missing, unclear, or wrong. Factory must not invent missing product intent.
5. Ensure `.idd/factory/.gitignore` exists, creating it from the packaged
   template if needed.
6. Use local `references/roles/` role prompts only when they are present and
   relevant to this workflow.
7. Create one work directory using this shape:
   `.idd/factory/work/<yyyyMMdd-HHmmss>-<slug>/`.
8. Write the work plan to:
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

## Route Classification

- Classification:
- Operation:
- Execution depth:
- Source:

## Preservation Boundary

- Behavior expected to change:
- Behavior expected to remain unchanged:
- Public contracts to preserve:
- Compatibility or data constraints:
- Unresolved preservation questions:

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
