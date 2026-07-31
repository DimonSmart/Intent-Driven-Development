# idd-factory-run

## Purpose

Coordinate one resumable Factory run from a request or
`.idd/factory/current/`. Factory may read current intent but never invent
product requirements or treat temporary tasks/results as product intent.
Dispatch bounded workers in isolated contexts when supported.

## Workspace and State

On first explicit use, require `.idd/intent/`, install the packaged Factory
`.gitignore`, create `current/` and `results/`, and register `idd-factory` when
needed. Do not copy skills or alter managed instructions without separate need.

Only one run may exist. A new request requires empty `current/`; otherwise
summarize it and require continue or cancel. Do not merge runs or migrate legacy
`.idd/factory/work/*/work-plan.md` state.

`current/` contains one `request.md` and contiguous
`<sequence>-<slug>.<status>.md` tasks. Valid statuses are `ready`, `active`,
`completed`, and `blocked`; filenames are authoritative. Require valid flat
files, at most one active or blocked task, never both, completed tasks before it,
and ready tasks after it. Stop invalid state as `CORRUPT_FACTORY_STATE`; never
guess repairs.

Allowed transitions:

```text
ready -> active
active -> completed
active -> blocked
blocked -> active
```

Only the coordinator renames files. Completed tasks are immutable.

## Start

Run `idd-factory-decompose-work` with the complete request.

- `NEEDS_CLARIFICATION`: ask all questions together and decompose again after
  the answer; create no partial state.
- `INTENT_REQUIRED`: run the intent workflow, reread intent, and decompose again.
- `FOCUSED_HANDOFF`: use one `idd-code-implement` when Factory was implicit; an
  explicit Factory request may use one bounded task.
- `BLOCKED`: report the planning blocker; create no state.
- `READY`: write `request.md` and all ordered `.ready.md` tasks, then validate.

`request.md` preserves the original request and an optional
`## Resolved Clarifications` section containing only confirmed user decisions.
Append later confirmed decisions without rewriting the original request.

Each task contains only title, `Goal`, `Scope`, `Done When`, and `Verification`.
Do not add status, dates, owners, attempts, dependencies, history, or logs.

## Task Loop

1. Activate the lowest ready task.
2. Run `idd-factory-execute-task`:
   - `DONE`: run fresh `idd-factory-review-task`;
   - `NEEDS_REPLAN`: replan;
   - `BLOCKED`: classify the blocker;
   - `INTENT_REQUIRED`: persist the intent blocker and use its workflow.
3. Apply review:
   - `approved`: clear findings/blocker, append `Completion`, mark completed;
   - `needs-fix`: keep active with only current actionable findings and repeat;
   - `needs-replan`: replan;
   - `blocked`: classify the blocker;
   - `intent-required`: persist the intent blocker, run its workflow, revise
     active/ready tasks if needed, reactivate, and continue.

After either `INTENT_REQUIRED`, reread relevant intent, revise only active and
ready tasks when needed, reactivate the blocked task, and continue.

### Replanning

`NEEDS_REPLAN` is internal, never a Factory outcome. Confirm the prerequisite is
inside the request and current active/ready work. Atomically revise only active
and ready tasks by moving the minimum prerequisite forward or, when necessary,
reordering, splitting, or merging them. Remove duplicate scope, preserve
completed tasks and original request text, restore valid numbering/state, and
continue.

When the prerequisite is not covered, classify it as a user clarification,
`INTENT_REQUIRED`, or a genuine blocker.

### Blocker Classification

Treat worker `BLOCKED` as proposed. If remaining planned work resolves it,
replan instead.

When an exact non-intent user decision resolves it, persist a `Blocker` whose
`Resume when` contains the question, mark blocked, and ask. The answer is enough
to append the clarification, reactivate, and continue; do not require a separate
continue command.

Use `INTENT_REQUIRED` for unknown durable behavior. Otherwise persist the genuine
external or repository blocker and stop before later tasks.

## Records and Reporting

`Completion` contains `Result`, `Verification`, and `Concerns`.
`Blocker` contains `Reason`, `Verified`, `Not verified`, and `Resume when`.
`Review Findings` contains only current actionable findings and never coexists
with `Blocker`. Blocked work never receives `Completion`.

Every stop or finish reports separately:

```text
Factory outcome: <outcome>
Implementation assessment: <assessment>
Verification assessment: <assessment>
```

Missing verification never becomes approval.

## Resume and Finish

After validation:

- active: inspect task and diff; review first if implementation appears complete;
- blocked: when `Resume when` is satisfied, record any clarification, reactivate,
  and continue without a separate command;
- unchanged implementation with only missing evidence: use verification-only
  resume limited to `Not verified`;
- completed plus ready: activate the lowest ready task;
- all completed: run `idd-factory-review-work-result`.

Final review `approved` invokes `idd-factory-finish-work`; `needs-fix` creates the
next corrective ready task; `blocked` and `intent-required` use the same handling.
Never reopen completed tasks.

Cancel only explicitly: warn about worktree changes, clear only `current/`,
preserve `results/`, and do not revert code or create a commit message.

## Outcomes

`COMPLETED`, `FOCUSED_HANDOFF`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`,
`BLOCKED`, or `CORRUPT_FACTORY_STATE`.
