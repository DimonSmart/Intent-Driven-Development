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

On first explicit use, require `.idd/intent/` and verify that the packaged
Factory assets are available. The initializer coordinator installs the Factory
`.gitignore` and creates `current/` and `results/` when needed. Only one run may
exist. A new request requires absent or empty `current/`; otherwise summarize it
and require continue or cancel.

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

Before state exists, run intent preflight and spawn a fresh generic child agent.
Assign it the `task-decomposer` role by passing a dispatch `message` with the
complete request, workspace path, durable-intent path,
`.agents/skills/idd-factory-decompose-task/SKILL.md`, and
`.agents/skills/idd-factory-decompose-task/references/roles/task-decomposer.md`.
Also pass its `project-verification.md` reference when required by the worker
skill.

Read `references/methodology-version.json` before initialization. Pass its
`methodologyVersion` to the initializer, which records `Methodology version:`
in `current/request.md`; carry it through finalization and require the finalizer
to include the same value in `factory-result.json`.

- `NEEDS_CLARIFICATION`: ask all questions together; create no partial state.
- `INTENT_REQUIRED`: create no state, run the intent workflow, reread intent,
  and decompose the complete original request again.
- `FOCUSED_HANDOFF`: use one `idd-code-implement` when Factory was implicit; an
  explicit Factory request may use one bounded Subtask.
- `BLOCKED`: report the planning blocker; create no state.
- `READY`: reject intent-changing execution scope and validate the complete
  result contract. Do not write files. Spawn a fresh `factory-step-coordinator`
  with `Action: INITIALIZE`, the complete original request, methodology version,
  confirmed clarifications when applicable, and the complete validated `READY`
  result in its dispatch message. Require `Step result: ADVANCED` for `factory
  initialization`, then discard that context and dispatch a different fresh
  coordinator in `CONTINUE` mode.

The fresh step coordinator appends confirmed later decisions only to
`## Resolved Clarifications` in `request.md`. Each Subtask is self-contained with optional `run-context.md`;
workers do not need the original request. A Review checkpoint contains its
contiguous `Covers`, review scope, and focused verification. Do not add a
terminal checkpoint that duplicates final integrated review.

## Dispatch

After successful initialization or a valid resume, spawn a fresh generic child agent and assign
it the `factory-step-coordinator` role through the dispatch `message`. Include
`Action: CONTINUE`, the worktree path, resume request, confirmed blocker answer when present,
`.agents/skills/idd-factory-coordinate-step/SKILL.md`,
`.agents/skills/idd-factory-coordinate-step/references/roles/factory-step-coordinator.md`,
and `.agents/skills/idd-factory-coordinate-step/references/project-verification.md`.
Do not pass detailed parent history, worker reports, or test logs.

- On `Step result: ADVANCED`, discard the completed step context and invoke a
  new fresh step context.
- On `Step result: STOPPED`, report its allowed Factory outcome and compact
  reason/resume condition.
- On `Step result: FINISHED`, report the commit-message path and completion.

Resume of an existing run always uses `CONTINUE`; never initialize it again.
Do not directly own a monolithic work loop, execute a worker, inspect each
completed step's diff, or retain corrective-cycle detail. `NEEDS_REPLAN` is
internal, never a Factory outcome. The step coordinator handles activation,
Completion/Blocker records, checkpoint correction, replanning, intent
orchestration, final review, and finalization.

For Codex dispatch, read `references/codex-dispatch.md`. Every call to
`spawn_agent` uses only `message`; do not provide `items`, never provide both,
and use `fork_context = false`. Dispatch means spawning a generic child agent,
waiting for its terminal result, and validating that result against its role
contract before changing Factory state. Reading another skill and following it
in the root context is not dispatch. The Factory runner must not execute coordinator,
implementation, review, or finalization work in its own context. The root
context is read-only and must never modify repository or Factory-state files.
If a required child agent cannot be
spawned or waited for, stop the attempt as `BLOCKED` with the actual technical
reason; never fall back to self-execution.

## Resume and Cancel

For a blocked state without a new answer, report the saved exact `Resume when`.
When the answer satisfies it, dispatch it to one fresh step; no separate
continue command is required. An interrupted active Subtask is resumed by a
fresh step, which may use verification-only resume when implementation is
unchanged and evidence is missing.

Cancel only explicitly: warn about worktree changes, then dispatch a fresh step
coordinator with `Action: CONTINUE` and the cancellation request. That
coordinator clears only `current/`, preserves `results/`, and does not revert
code or create a commit message. The read-only root does not clear files itself.

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

Do not emit a public Factory outcome as a progress message. Emit the final
response object only after the Factory attempt has actually finished or stopped.
