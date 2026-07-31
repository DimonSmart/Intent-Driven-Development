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

`current/` contains one `request.md`, an optional `run-context.md`, and
contiguous `<sequence>-<slug>.<status>.md` tasks. Valid statuses are `ready`,
`active`, `completed`, and `blocked`; filenames are authoritative. Require valid
flat files, at most one active or blocked task, never both, completed tasks
before it, and ready tasks after it. Stop invalid state as
`CORRUPT_FACTORY_STATE`; never guess repairs.

Allowed transitions:

```text
ready -> active
active -> completed
active -> blocked
blocked -> active
```

Only the coordinator renames files. Completed tasks are immutable.

## Intent Preflight and Start

Run `idd-factory-decompose-work` with the complete request before creating
Factory state. Treat this decomposition as an intent preflight as well as task
planning.

- `NEEDS_CLARIFICATION`: ask all questions together and decompose again after
  the answer; create no partial state.
- `INTENT_REQUIRED`: create no Factory state and no task for the intent change.
  Run the applicable `idd-intent` workflow using the returned missing or
  conflicting durable behavior, reread current intent, and decompose the
  original request again.
- `FOCUSED_HANDOFF`: use one `idd-code-implement` when Factory was implicit; an
  explicit Factory request may use one bounded implementation task.
- `BLOCKED`: report the planning blocker; create no state.
- `READY`: require implementation-only tasks, then write `request.md`, optional
  `run-context.md`, and all ordered `.ready.md` tasks and validate.

Repeat clarification and intent preflight until decomposition reaches a
non-intent outcome. Do not preserve partial tasks across an intent change.
A `READY` result is invalid if any task owns an edit to `.idd/intent/`, invokes
an intent-changing workflow, or uses an intent update as its goal or completion
condition. Treat such a result as `INTENT_REQUIRED`; resolve intent first and
decompose again.

`request.md` preserves the original request and an optional
`## Resolved Clarifications` section containing only confirmed user decisions.
Append later confirmed decisions without rewriting the original request.

`run-context.md` is optional. Create it only when several tasks share substantial
context. Keep only compact cross-task constraints, shared assumptions, and
references. Do not copy the complete request or place task-specific requirements
there.

Each task is a self-contained implementation contract when read with
`run-context.md`, if present. Factory tasks are implementation-only and never
own durable intent changes. Each task contains:

- title;
- `Goal`;
- `Context`;
- `Scope`;
- `Requirements`;
- `Done When`;
- `Verification`;
- optional `Out of Scope`, `Preservation Boundaries`, `Dependencies`, and
  `Intent References` sections when they add concrete information.

Omit empty optional sections. Do not add status, dates, owners, attempts,
history, or logs. Workers must not need `request.md` or other task files to
understand, implement, or review the active task.

## Task Loop

1. Activate the lowest ready task.
2. Run `idd-factory-execute-task` with the active task and optional
   `run-context.md`:
   - `DONE`: run fresh `idd-factory-review-task`;
   - `NEEDS_REPLAN`: replan;
   - `BLOCKED`: classify the blocker;
   - `INTENT_REQUIRED`: handle intent outside the task list.
3. Apply review:
   - `approved`: clear findings/blocker, append `Completion`, mark completed;
   - `needs-fix`: keep active with only current actionable findings and repeat;
   - `needs-replan`: replan;
   - `blocked`: classify the blocker;
   - `intent-required`: handle intent outside the task list.

When execution or review returns `INTENT_REQUIRED`, never turn the intent change
into the active task, a corrective task, or a `Completion`. Persist only the
minimum resumable blocker when necessary, run the applicable `idd-intent`
workflow as coordinator-owned orchestration, reread current intent, and revise
`run-context.md` plus active and ready implementation contracts. Remove any
intent-editing scope, revalidate the implementation-only plan, reactivate the
implementation task, and continue.

### Replanning

`NEEDS_REPLAN` is internal, never a Factory outcome. Confirm the prerequisite is
inside the request and current active/ready implementation work. Atomically
revise `run-context.md` and only active and ready tasks by moving the minimum
prerequisite forward or, when necessary, reordering, splitting, or merging them.
Keep every revised task self-contained and implementation-only, remove duplicate
scope, preserve completed tasks and original request text, restore valid
numbering/state, and continue.

When the prerequisite is not covered, classify it as a user clarification,
`INTENT_REQUIRED`, or a genuine blocker. Resolve `INTENT_REQUIRED` through the
intent workflow outside the task list.

### Blocker Classification

Treat worker `BLOCKED` as proposed. If remaining planned work resolves it,
replan instead.

When an exact non-intent user decision resolves it, persist a `Blocker` whose
`Resume when` contains the question, mark blocked, and ask. The answer is enough
to append the clarification, update `run-context.md` and affected active/ready
task contracts, reactivate, and continue; do not require a separate continue
command.

Use `INTENT_REQUIRED` for unknown durable behavior, but never represent the
required intent change as a Factory task. Otherwise persist the genuine external
or repository blocker and stop before later tasks.

## Records and Reporting

`Completion` contains `Result`, `Verification`, and `Concerns`.
A persisted `Blocker` uses these literal fields:

```text
Reason:
Verified:
Not verified:
Resume when:
```

`Review Findings` contains only current actionable findings and never coexists
with `Blocker`. Blocked work never receives `Completion`.

Every stop or finish reports separately:

```text
Factory outcome: <outcome>
Implementation assessment: <assessment>
Verification assessment: <assessment>
```

Missing verification never becomes approval. Never describe the blocked task or
run as approved, review passed, completed, accepted, or finished.

## Resume and Finish

After validation:

- active: inspect task and diff; review first if implementation appears complete;
- blocked: when `Resume when` is satisfied, record any clarification or resolved
  intent, update affected active/ready implementation contracts, reactivate, and
  continue without a separate command;
- unchanged implementation with only missing evidence: invoke
  `idd-factory-execute-task` in verification-only resume mode, limited to
  `Not verified`;
- completed plus ready: activate the lowest ready task;
- all completed: run `idd-factory-review-work-result`.

Final review `approved` invokes `idd-factory-finish-work`; `needs-fix` creates the
next self-contained implementation-only corrective task. Final-review
`intent-required` is resolved through the intent workflow outside the task list,
then the coordinator creates only the implementation correction required by the
updated intent. Never reopen completed tasks.

Cancel only explicitly: warn about worktree changes, clear only `current/`,
preserve `results/`, and do not revert code or create a commit message.

## Outcomes

`COMPLETED`, `FOCUSED_HANDOFF`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`,
`BLOCKED`, or `CORRUPT_FACTORY_STATE`.
