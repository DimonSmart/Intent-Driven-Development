# factory-execute-work-plan

## Purpose

Execute one explicit Factory Work Plan.

Factory execution coordinates temporary implementation work. It must not turn
plans, task briefs, reviews, or logs into product intent.

In future versions, Factory Work Plan tasks may be backed by an external Work
Item Provider. The current implementation uses temporary local markdown files
only.

## Rules

- Require an explicit work plan path or an explicitly provided work plan.
- Do not search for old work plans automatically.
- Do not infer the current plan from previous factory files.
- Execute only the tasks in the work plan.
- For each task, create a bounded task brief in the same work directory when
  useful.
- Use `spec-implement` principles inside each implementation task.
- After each task, run `factory-review-task`.
- If task review fails, fix and re-review before continuing.
- After all tasks, run `factory-review-work-result`.
- Finish with `factory-finish-work`.
- Do not modify specs unless the work plan explicitly says the current user
  request includes a spec update flow.
- If implementation reveals missing or wrong product intent, stop and route to
  `spec-change` or `spec-update-from-implementation` only after explicit user
  confirmation.

## Workflow

1. Read the explicit work plan path or user-provided work plan.
2. Confirm the work directory from the plan. Supporting files belong beside the
   plan, for example `task-001-brief.md`, `task-001-review.md`, and
   `final-review.md`.
3. Use the local `references/agents/` role prompts when dispatching or
   simulating planner, implementer, reviewer, and coordinator work.
4. Execute tasks in plan order.
5. Stop on `BLOCKED` or missing context. Do not silently continue through a
   blocked task.
6. Keep temporary execution artifacts under `.idd/factory/work/`; never write
   them into `.specs/`.

## Statuses

- `DONE`
- `DONE_WITH_CONCERNS`
- `NEEDS_CONTEXT`
- `BLOCKED`

## Output Format

Report each task status with changed files, verification commands, review
result, and any concerns that must be carried into final review.
