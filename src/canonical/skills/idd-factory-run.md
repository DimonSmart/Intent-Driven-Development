# idd-factory-run

## Required Reference

Read `references/project-verification.md` before Factory planning, execution,
review, or verification fallback.

## Purpose

The sole public entry point for starting, resuming, or cancelling one Factory
run. It bootstraps persisted state, then acts as a thin dispatcher of fresh
`idd-factory-coordinate-step` contexts. The original user request defines the
Factory Task. `.idd/factory/current/`, not the parent coordinator transcript,
is the authoritative memory between steps.

## Workspace and State

On first explicit use, require `.idd/intent/`, install the packaged Factory
`.gitignore`, create `current/` and `results/`, and register `idd-factory` when
needed. Only one run may exist. A new request requires empty `current/`;
otherwise summarize it and require continue or cancel.

`current/` contains `request.md`, optional `run-context.md`, and contiguous
`<sequence>-<slug>.<status>.md` work items. A work item is a Subtask, identified
by a `## Goal` section, or a Review checkpoint, identified by a `## Review
Checkpoint` section. Valid statuses are `ready`, `active`, `completed`, and
`blocked`; filenames are authoritative. Require at most one active or blocked
item, never both, completed items before it, and ready items after it. Stop
invalid state as `CORRUPT_FACTORY_STATE`; never guess repairs. Completed items
are immutable.

The coordinator-step contract preserves the allowed transition `active
review-checkpoint -> ready` only when it atomically inserts a correction
immediately before the checkpoint. Subtask completion does not automatically
invoke independent review. A Subtask `Changes` is a compact list of focused
checkpoint evidence.

## Bootstrap

Before state exists, run intent preflight and `idd-factory-decompose-task` with
the complete request.

Read `references/methodology-version.json` before creating state. Record its
`methodologyVersion` as `Methodology version:` in `current/request.md`, carry it
through finalization, and require the finalizer to include the same value in
`factory-result.json`.

- `NEEDS_CLARIFICATION`: ask all questions together; create no partial state.
- `INTENT_REQUIRED`: create no state, run the intent workflow, reread intent,
  and decompose the complete original request again.
- `FOCUSED_HANDOFF`: use one `idd-code-implement` when Factory was implicit; an
  explicit Factory request may use one bounded Subtask.
- `BLOCKED`: report the planning blocker; create no state.
- `READY`: reject intent-changing execution scope; write unchanged `request.md`,
  optional compact `run-context.md`, and all ordered Subtask and Review
  checkpoint contracts as `.ready.md` files.

Append confirmed later decisions only to `## Resolved Clarifications` in
`request.md`. Each Subtask is self-contained with optional `run-context.md`;
workers do not need the original request. A Review checkpoint contains its
contiguous `Covers`, review scope, and focused verification. Do not add a
terminal checkpoint that duplicates final integrated review.

## Dispatch

After bootstrap or a valid resume, invoke `idd-factory-coordinate-step` in a
new isolated coordinator context. Pass only the worktree path, resume request,
and any confirmed answer to the current blocker. Do not pass detailed parent
history, worker reports, or test logs.

- On `Step result: ADVANCED`, discard the completed step context and invoke a
  new fresh step context.
- On `Step result: STOPPED`, report its allowed Factory outcome and compact
  reason/resume condition.
- On `Step result: FINISHED`, report the commit-message path and completion.

Do not directly own a monolithic work loop, execute a worker, inspect each
completed step's diff, or retain corrective-cycle detail. `NEEDS_REPLAN` is
internal, never a Factory outcome. The step coordinator handles activation,
Completion/Blocker records, checkpoint correction, replanning, intent
orchestration, final review, and finalization.

## Resume and Cancel

For a blocked state without a new answer, report the saved exact `Resume when`.
When the answer satisfies it, dispatch it to one fresh step; no separate
continue command is required. An interrupted active Subtask is resumed by a
fresh step, which may use verification-only resume when implementation is
unchanged and evidence is missing.

Cancel only explicitly: warn about worktree changes, clear only `current/`,
preserve `results/`, and do not revert code or create a commit message.

## Reporting and Outcomes

Every stop or finish reports separately:

```text
Factory outcome: <outcome>
Implementation assessment: <assessment>
Verification assessment: <assessment>
```

Allowed outcomes are `COMPLETED`, `FOCUSED_HANDOFF`, `NEEDS_CLARIFICATION`,
`INTENT_REQUIRED`, `BLOCKED`, and `CORRUPT_FACTORY_STATE`. Missing verification
never becomes approval. A persisted Blocker uses literal `Reason:`, `Verified:`,
`Not verified:`, and `Resume when:` fields.
