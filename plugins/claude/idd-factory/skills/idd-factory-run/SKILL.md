---
name: idd-factory-run
description: Run or resume one file-backed IDD Factory workflow through decomposition, sequential implementation, independent reviews, and commit-message handoff.
---

# idd-factory-run

## Purpose

Coordinate one resumable Factory run from a user request or from the files in
`.idd/factory/current/`.

Factory is temporary execution orchestration. It may read current
`.idd/intent/`, but it never creates product requirements, resolves unknown
product decisions, or treats tasks and results as product intent.

This skill is the public Factory entry point and stays in the coordinator's
main context. Run decomposers, implementers, and reviewers as isolated workers
when the platform supports isolated execution.

## Accepted Requests

- A natural-language task.
- Markdown supplied in the request.
- A local text or Markdown file path. Copy its substantive contents into the
  request snapshot; do not retain only a temporary path.
- An explicit request to continue or cancel the current Factory run.

## Workspace

On first explicit Factory use:

1. Require `.idd/intent/` and explicit use of the `idd-factory` plugin.
2. Create `.idd/factory/.gitignore` from the packaged asset when absent.
3. Create `.idd/factory/current/` and `.idd/factory/results/` when absent.
4. Add `idd-factory` to `.idd/plugins.json` when necessary.
5. Do not copy skills into the project or modify managed agent-instruction
   blocks without a separate need.

The packaged `.gitignore` keeps `current/` and `results/` local by default.
Users may change that policy themselves; Factory does not commit its artifacts.

Only one run may be active. For a new request, require `current/` to be empty.
If it is not empty, do not change it: summarize the existing run and ask the
user to explicitly continue or cancel it. Never merge or replace runs.

Legacy `.idd/factory/work/*/work-plan.md` state is unsupported. Report it
clearly and do not migrate or mix it with `current/`.

## Current State Contract

`current/` contains only:

- exactly one `request.md` while a run exists; and
- a flat ordered list named `<sequence>-<slug>.<status>.md`.

Sequence numbers are three digits, start at `001`, and have no gaps. Slugs are
stable lowercase kebab-case result names. Supported statuses are `ready`,
`active`, `completed`, and `blocked`; the filename is the only status source.

Before starting or resuming, validate all of these invariants:

- no nested directories or unknown files exist;
- every task filename is valid, numbers are unique and contiguous;
- at most one task is active and at most one is blocked;
- active and blocked tasks never coexist;
- tasks before an active or blocked task are completed and tasks after it are
  ready;
- without active or blocked tasks, completed tasks precede ready tasks.

On any violation, stop with `CORRUPT_FACTORY_STATE`. List the violations and
files and offer safe manual repair options. Never guess or silently repair the
state.

Allowed task transitions are:

```text
ready -> active
active -> completed
active -> blocked
blocked -> active
```

Only the coordinator renames task files. Never reactivate a completed task.

## New Run

1. Bootstrap and validate the workspace; require an empty `current/`.
2. Pass the complete source request to `idd-factory-decompose-work` in an
   isolated context.
3. Handle its result:
   - `NEEDS_CLARIFICATION`: ask all blocking questions together and create no
     workspace files. Repeat decomposition after the answer.
   - `INTENT_REQUIRED`: stop and route to the applicable intent workflow. After
     intent changes, reread relevant intent and decompose again.
   - `FOCUSED_HANDOFF`: when Factory was selected implicitly, hand off to one
     `idd-code-implement`; when the user explicitly requested Factory, one
     bounded task is allowed.
   - `BLOCKED`: report the concrete blocker and create no partial workspace.
   - `READY`: create the complete workspace atomically enough that a failure
     cannot be mistaken for a valid run.
4. Write `request.md` as:

```md
# Factory Request

<substantive original request>

## Resolved Clarifications

<only decisions actually confirmed by the user>
```

   Omit `Resolved Clarifications` when no clarification occurred. Do not add
   dates, status, planned statuses, or invented product interpretation.
5. Write all ordered tasks as `.ready.md`, then validate the workspace and
   report the compact task list.

Each task uses this base shape:

```md
# <Task title>

## Goal

<one concrete result>

## Scope

- <bounded area>

## Done When

- <verifiable completion condition>

## Verification

- <focused check>
```

Do not add status, timestamps, owner, agent, model, attempts, dependencies,
history, or logs. Ordering already expresses dependencies.

## Task Loop

1. Select the lowest-numbered ready task and rename it to `.active.md` before
   changing implementation.
2. Run `idd-factory-execute-task` for that one task.
3. Run `idd-factory-review-task` in a fresh isolated context.
4. Apply the verdict:
   - `approved`: remove `Review Findings` and `Blocker`, append a compact
     `Completion`, then rename the task to `.completed.md`.
   - `needs-fix`: remove `Blocker`, replace `Review Findings` with only the
     latest actionable findings, keep the task active, and repeat implementation
     and review.
   - `blocked`: remove `Review Findings`, write the standard `Blocker` supplied
     by the reviewer, rename the task to `.blocked.md`, and stop before later
     tasks. Never append `Completion`.
   - `intent-required`: remove `Review Findings`, write the standard `Blocker`
     with the intent gap and handoff, rename the task to `.blocked.md`, and run
     the applicable intent workflow. After confirmed intent changes, reread
     relevant intent, update only remaining ready tasks when necessary, rename
     blocked back to active, and continue.

Use this completion shape without file lists or command logs:

```md
## Completion

Result:
<one to three sentences>

Verification:
<compact result>

Concerns:
<none or one material remaining concern>
```

Use this blocker shape without command logs, attempt history, timestamps, or
speculative diagnosis:

```md
## Blocker

Reason:
<one concrete condition preventing safe continuation or approval>

Verified:
<only conclusive implementation or verification evidence already established>

Not verified:
<required work or evidence that remains incomplete>

Resume when:
<one concrete condition that makes continuation safe>
```

`Verified` may be `none`. `Not verified` must distinguish missing evidence from
an implementation defect. `Review Findings` is reserved for `needs-fix` and is
never used as the persisted blocker record.

Use this temporary findings shape:

```md
## Review Findings

- <current actionable finding>
```

## Assessment and Outcome Reporting

Keep these concepts distinct:

- implementation assessment: what the actual diff does and whether it has
  material implementation findings;
- verification assessment: which required checks have conclusive evidence and
  which remain incomplete;
- Factory outcome: the coordinator state such as `COMPLETED`, `BLOCKED`, or
  `INTENT_REQUIRED`.

Every user-facing stop or finish report must state all three explicitly:

```text
Factory outcome: <outcome>
Implementation assessment: <compact assessment>
Verification assessment: <compact assessment>
```

A favorable implementation assessment does not override incomplete required
verification. When a task-review or final-review verdict is `blocked`, never
describe the blocked task or run as approved, review passed, completed,
accepted, or finished. Report the favorable and missing evidence separately and
keep the Factory outcome `BLOCKED`.

## Resume

After validating state:

- `.active.md`: read the task and current diff first. If implementation appears
  complete, review it before invoking the implementer; otherwise continue the
  bounded implementation without duplicating finished work.
- `.blocked.md`: show the persisted `Blocker` and do not continue until its
  `Resume when` condition is explicitly resolved. Then rename the task to
  `.active.md` before dispatching work.
- When repository evidence confirms that the current implementation diff is
  unchanged from the blocked review and the blocker contains no implementation
  defect, invoke `idd-factory-execute-task` in verification-only resume mode.
  Limit the worker to `Not verified`, preserve `Verified`, and do not repeat
  implementation or already conclusive checks.
- If the implementation changed, or unchanged state cannot be established,
  perform a normal bounded resume and treat affected prior evidence as stale.
- only completed and ready tasks: activate the lowest ready task.
- all tasks completed: run final review.

If all tasks are completed but a result is absent after interruption, repeat
final verification and finish safely.

## Final Review and Finish

Run `idd-factory-review-work-result` only when no ready, active, or blocked task
exists and all tasks are completed.

- `approved`: invoke `idd-factory-finish-work`.
- `needs-fix`: create the next numbered corrective task, normally
  `<next>-address-final-review-findings.ready.md`, and resume the task loop.
- `blocked` or `intent-required`: stop through the same structured blocker gate
  used for task review and report the separated assessments and Factory outcome.

Never change a completed task back to active.

## Cancellation

Cancel only on an explicit user request. Warn when the worktree contains
changes, clear only the contents of `current/`, leave `results/` untouched, do
not revert code, and do not create a commit message.

## Outcomes

`COMPLETED`, `FOCUSED_HANDOFF`, `NEEDS_CLARIFICATION`, `INTENT_REQUIRED`,
`BLOCKED`, or `CORRUPT_FACTORY_STATE`.
