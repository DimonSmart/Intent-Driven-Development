# idd-factory-run

At preflight, require a valid `.idd/verification.md` when one exists; otherwise
use repository/platform fallback. Preserve completed recorded IDs during a
policy change, resolve their commands from the current policy, and replan only
active or ready contracts. The final reviewer always uses the current `final`
policy. Accept execution-task `DONE` only when every assigned `subtask` check has
conclusive evidence; any assigned `Not verified` keeps the item blocked.

## Purpose

Coordinate one resumable Factory run from a request or
`.idd/factory/current/`. Factory may read current intent but never invent
product requirements or treat temporary work items/results as product intent.
Dispatch bounded workers in isolated contexts when supported.

## Workspace and State

On first explicit use, require `.idd/intent/`, install the packaged Factory
`.gitignore`, create `current/` and `results/`, and register `idd-factory` when
needed. Do not copy skills or alter managed instructions without separate need.

Only one run may exist. A new request requires empty `current/`; otherwise
summarize it and require continue or cancel. Do not merge runs or migrate legacy
`.idd/factory/work/*/work-plan.md` state.

`current/` contains one `request.md`, an optional `run-context.md`, and
contiguous `<sequence>-<slug>.<status>.md` work items. Valid statuses are
`ready`, `active`, `completed`, and `blocked`; filenames are authoritative.
Require valid flat files, at most one active or blocked item, never both,
completed items before it, and ready items after it. Stop invalid state as
`CORRUPT_FACTORY_STATE`; never guess repairs.

A work item is one of:

- an execution task, identified by a `## Goal` section and no
  `## Review Checkpoint` section;
- a review checkpoint, identified by a `## Review Checkpoint` section.

Allowed transitions:

```text
ready -> active
active -> completed
active -> blocked
blocked -> active
active review-checkpoint -> ready
```

The last transition is allowed only as one atomic correction operation that
inserts a new execution task immediately before the checkpoint, updates its
coverage, and renumbers only active/ready items. Only the coordinator renames
files. Completed items are immutable.

## Start

Run `idd-factory-decompose-work` with the complete request.

- `NEEDS_CLARIFICATION`: ask all questions together and decompose again after
  the answer; create no partial state.
- `INTENT_REQUIRED`: create no state, run the intent workflow, reread intent, and
  decompose the complete original request again.
- `FOCUSED_HANDOFF`: use one `idd-code-implement` when Factory was implicit; an
  explicit Factory request may use one bounded execution task.
- `BLOCKED`: report the planning blocker; create no state.
- `READY`: reject any intent-changing execution task, then write `request.md`,
  optional `run-context.md`, and all ordered execution tasks and review
  checkpoints as `.ready.md` files.

`request.md` preserves the original request and an optional
`## Resolved Clarifications` section containing only confirmed user decisions.
Append later confirmed decisions without rewriting the original request.

`run-context.md` is optional. Create it only when several execution tasks or
review checkpoints share substantial context. Keep only compact cross-item
constraints, shared assumptions, and references. Do not copy the complete
request or place item-specific requirements there.

### Execution Task Contract

Each execution task is self-contained when read with `run-context.md`, if
present. It contains:

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
history, or logs. An execution task must not edit `.idd/intent/`, invoke an
intent-changing workflow, or own an intent update. The executor must not need
`request.md` or other work-item files.

### Review Checkpoint Contract

A review checkpoint contains:

- title;
- `Review Checkpoint`: why independent review is required before later work;
- `Covers`: a contiguous ordered list of preceding completed execution tasks
  since the previous checkpoint, including any checkpoint correction tasks;
- `Review Scope`: the contracts, integration surface, public boundaries, and
  risks to inspect;
- `Verification`: focused checkpoint-level evidence to run or inspect;
- optional `Intent References`.

A checkpoint never covers another checkpoint. Use the fewest checkpoints that
protect later work. Do not add a terminal checkpoint that only duplicates the
mandatory final integrated review.

## Work Loop

Activate the lowest ready work item.

### Execution Task

Run `idd-factory-execute-task` with the active execution task and optional
`run-context.md`.

- `DONE`: accept only when implementation is complete and every assigned
  `subtask` check has conclusive evidence. Clear any resolved blocker, append
  `Completion`, and mark the execution task completed. Execution completion does
  not invoke `idd-factory-review-task`.
- worker `DONE` with any assigned `Not verified`: treat as `BLOCKED`, persist
  `Reason`, `Verified`, `Not verified`, and `Resume when`, and do not mark the
  item completed.
- `NEEDS_REPLAN`: replan.
- `BLOCKED`: classify the blocker.
- `INTENT_REQUIRED`: persist the intent blocker and resolve intent outside the
  task list.

After a valid `DONE`, activate the next ready item. Independent review happens
only when a review checkpoint becomes active.

### Review Checkpoint

Run fresh `idd-factory-review-task` with:

- the active checkpoint;
- all completed execution tasks named by `Covers`;
- optional `run-context.md`;
- only relevant current intent;
- checkpoint-local diff/evidence derived from the covered tasks' `Changes`,
  checkpoint scope, and verification.

Apply the verdict:

- `approved`: append `Completion` to the checkpoint and mark it completed;
- `needs-fix`: atomically create one self-contained corrective execution task
  immediately before the checkpoint, add it to `Covers`, return the checkpoint
  to ready, renumber only active/ready items, validate, and continue;
- `needs-replan`: replan;
- `blocked`: classify the blocker;
- `intent-required`: persist the intent blocker and resolve intent outside the
  task list.

Do not reopen completed execution tasks. Checkpoint corrections are new
execution tasks.

After `INTENT_REQUIRED`, reread relevant intent, revise `run-context.md` and only
active/ready execution tasks or checkpoints when needed, reactivate the blocked
item, and continue. Intent work is never a Factory work item or completion.

### Replanning

`NEEDS_REPLAN` is internal, never a Factory outcome. Confirm the prerequisite is
inside the request and current active/ready work. Atomically revise
`run-context.md` and only active/ready items by moving the minimum prerequisite
forward or, when necessary, reordering, splitting, merging, adding, or removing
execution tasks and review checkpoints.

Keep execution tasks self-contained, keep checkpoint coverage contiguous, use
the fewest review checkpoints, remove duplicate scope, preserve completed items
and original request text, restore valid numbering/state, and continue.

When the prerequisite is not covered, classify it as a user clarification,
`INTENT_REQUIRED`, or a genuine blocker.

### Blocker Classification

Treat worker `BLOCKED` as proposed. If remaining planned work resolves it,
replan instead.

When an exact non-intent user decision resolves it, persist a `Blocker` whose
`Resume when` contains the question, mark the active item blocked, and ask. The
answer is enough to append the clarification, update `run-context.md` and
affected active/ready items, reactivate, and continue; do not require a separate
continue command.

Use `INTENT_REQUIRED` for unknown durable behavior. Otherwise persist the
genuine external or repository blocker and stop before later items.

## Records and Reporting

Execution-task `Completion` contains `Result`, `Changes`, `Verification`, and
`Concerns`. `Changes` is a compact list of changed paths, public symbols, or
other evidence needed to focus a later checkpoint.

Review-checkpoint `Completion` contains `Result`, `Verification`, and `Concerns`.

A persisted `Blocker` uses these literal fields:

```text
Reason:
Verified:
Not verified:
Resume when:
```

Blocked work never receives `Completion`.

Every stop or finish reports separately:

```text
Factory outcome: <outcome>
Implementation assessment: <assessment>
Verification assessment: <assessment>
```

Missing verification never becomes approval. Never describe the blocked item or
run as approved, review passed, completed, accepted, or finished.

## Resume and Finish

After validation:

- active execution task: inspect its contract and diff; use verification-only resume
  mode when implementation is unchanged and only evidence is missing;
- active review checkpoint: run checkpoint review;
- blocked: when `Resume when` is satisfied, record any clarification, update
  affected active/ready items, reactivate, and continue without a separate
  command;
- completed plus ready: activate the lowest ready item;
- all items completed: run `idd-factory-review-work-result`.

Final review `approved` invokes `idd-factory-finish-work`; `needs-fix` creates
the next self-contained corrective execution task and relies on the next final
review rather than an extra terminal checkpoint; `blocked` and
`intent-required` use the same handling. Never reopen completed items.

Cancel only explicitly: warn about worktree changes, clear only `current/`,
preserve `results/`, and do not revert code or create a commit message.

## Outcomes

`COMPLETED`, `FOCUSED_HANDOFF`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`,
`BLOCKED`, or `CORRUPT_FACTORY_STATE`.
