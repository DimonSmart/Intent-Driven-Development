---
name: idd-factory-execute-work-plan
description: Execute an explicit temporary Factory Work Plan while preserving `.idd/intent/` as the normative product source.
---

# idd-factory-execute-work-plan

## Purpose

Execute one explicit Factory Work Plan.

Factory execution coordinates temporary implementation work. It must not turn
plans, task briefs, reviews, or logs into product intent.

Factory execution delegates bounded implementation semantics to
`idd-code-implement`.
Factory owns task sequencing, review gates, temporary artifacts, and cleanup.
It does not redefine `idd-code-implement` rules.

In future versions, Factory Work Plan tasks may be backed by an external Work
Item Provider. The current implementation uses temporary local markdown files
only.

## Routing

Use this workflow when the user provides an explicit Work Plan, the current
context unambiguously contains an active Work Plan, or plan creation has just
completed in the current workflow. Do not search for old plans in other
sessions or directories.

## Rules

- Require an explicit work plan path or an explicitly provided work plan.
- Do not search for old work plans automatically.
- Do not infer the current plan from previous factory files.
- Execute only the tasks in the work plan.
- Preserve any route classification and preservation boundary from the Work
  Plan as temporary execution constraints.
- Task briefs may quote the local preservation boundary, but must not convert it
  into product intent.
- Before the first task and before each task, verify that referenced current
  intent exists, is current, and defines the required behavior.
- Factory must not invent missing product intent. On an intent gap, stop with
  `INTENT_REQUIRED` and route to `idd-intent-brainstorm`,
  `idd-intent-change`, or `idd-intent-new-document`.
- For each task, create a bounded task brief in the same work directory when
  useful.
- Factory execution delegates bounded implementation semantics to
  `idd-code-implement`.
- Factory owns task sequencing, review gates, temporary artifacts, and cleanup.
- It does not redefine `idd-code-implement` rules.
- After each task, run `idd-factory-review-task`.
- If task review fails, fix and re-review before continuing.
- After all tasks, run `idd-factory-review-work-result`.
- Finish with `idd-factory-finish-work`.
- Do not modify specs unless the work plan explicitly says the current user
  request includes a spec update flow.
- If implementation reveals missing or wrong product intent, stop with a
  structured intent-gap report. After the intent workflow completes, reread
  `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and affected documents;
  refresh the Work Plan, task briefs, scope, and verification plan before
  continuing. Use `idd-code-update-intent` only after explicit confirmation
  that existing implementation represents product intent.
- If current intent is missing or contradicts the preservation boundary, stop
  and return to an `idd-intent` workflow instead of continuing Factory
  execution.

## Workflow

1. Read the explicit work plan path or user-provided work plan.
2. Confirm the work directory from the plan. Supporting files belong beside the
   plan, for example `task-001-brief.md`, `task-001-review.md`, and
   `final-review.md`.
3. Use the local `references/roles/` role prompts when dispatching or
   simulating planner, implementer, reviewer, and coordinator work.
4. Execute tasks in plan order.
5. Check each task against its local preservation boundary before review.
6. Stop on `BLOCKED` or missing context. Do not silently continue through a
   blocked task.
7. Keep temporary execution artifacts under `.idd/factory/work/`; never write
   them into `.idd/intent/`.

## Statuses

- `DONE`
- `DONE_WITH_CONCERNS`
- `NEEDS_CONTEXT`
- `BLOCKED`
- `INTENT_REQUIRED`

## Output Format

Report each task status with changed files, verification commands, review
result, and any concerns that must be carried into final review.
