# IDD Factory Workflow

IDD Factory is the optional execution layer for implementation work that
benefits from coordinated stages, explicit review boundaries, or safe
continuation after interruption.

Factory remains temporary. It may read `.idd/intent/`, but it does not create
product truth and does not turn execution state into permanent specifications.

## Factory Vocabulary

Request
: The original user instruction that defines a Factory Task.

Task
: The complete unit of work accepted by Factory.

Subtask
: One bounded executable part produced by decomposing the Task.

Review checkpoint
: An independent review boundary covering completed Subtasks.

Work item
: A persisted Subtask or Review checkpoint.

Factory run
: One resumable execution instance of a Task.

## When to Use Factory

Use Factory when the work involves one or more of the following:

- several independently verifiable implementation outcomes;
- an ordered migration or compatibility transition;
- changes spanning multiple subsystems;
- high regression risk;
- a need for independent implementation and review contexts;
- a final integration review across all stages.

Use `idd-code-implement` instead when one bounded implementation pass is enough.

## Install Factory

Claude Code:

```bash
claude plugin install idd-factory@intent-driven-development
```

Codex:

```bash
codex plugin add idd-factory@intent-driven-development
```

`idd-factory` depends on `idd-intent`.

When present, `.idd/verification.md` assigns checks to execution `subtask`, review `checkpoint`, and integrated `final` contexts. Factory stores only check IDs in contracts and resolves their current commands at execution time.

## Run a Complete Factory Task

Give Factory the complete task once:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Or provide the request directly:

```text
Use idd-factory-run to migrate the storage subsystem, update all consumers,
preserve saved-data compatibility, and verify the integrated result.
```

Under normal conditions, this single invocation carries the requested work
through to completion.

Factory first checks whether current intent is sufficient. It then decomposes
implementation into small Subtasks and places independent review
checkpoints only where early review protects later work. After all work items
complete, Factory performs one final integrated review and prepares a concise
commit-message handoff.

After bootstrap, Factory uses `.idd/factory/current/` as its persisted memory.
Each Subtask, checkpoint, replanning action, and final-review action is handled
by a new one-step coordinator context. The public `idd-factory-run` dispatcher
receives only a compact result, starts the next fresh step automatically, and
does not retain the detailed history of previous steps.

The user normally does not invoke internal worker skills separately.

## Intent Preflight

Intent changes are not Factory tasks.

Before writing `.idd/factory/current/`, Factory analyzes the complete request
against current `.idd/intent/`. When durable behavior is missing or
contradictory, decomposition returns `INTENT_REQUIRED` without a partial plan.
Factory runs the appropriate intent workflow, rereads current intent, and
decomposes the original request again.

Only after intent is sufficient does Factory create implementation work. An
Subtask must not edit `.idd/intent/`, invoke an intent-changing workflow,
or use an intent update as its goal, dependency, or completion condition.

If missing intent is discovered during execution or checkpoint review, the
coordinator handles it outside the work-item list and updates affected
implementation contracts before resuming.

## Subtasks and Review Checkpoints

Execution and review boundaries are separate.

A Subtask is a small self-contained implementation contract. Successful
execution records its result, changed areas, focused verification, and concerns,
then completes without automatically starting an independent reviewer.

A Review checkpoint is a separate ordered work item. It reviews one contiguous
group of preceding completed Subtasks. Several mechanical or closely
related tasks may share one checkpoint.

Factory uses checkpoints when early independent review protects dependent later
work, for example after:

- a new foundation or abstraction;
- a public contract change;
- a persisted-data or compatibility boundary;
- a security or concurrency boundary;
- a risky migration group.

Factory does not create a terminal checkpoint that merely duplicates the
mandatory final integrated review.

When checkpoint review finds a material problem, Factory creates a new
corrective Subtask immediately before that checkpoint. Completed tasks
remain immutable. After correction, the same checkpoint reviews the covered
group again.

`idd-factory-review-checkpoint` reviews active Review checkpoints; final Task
review is performed by `idd-factory-review-task`.

## Self-Contained Contracts

Factory preserves the complete original request in `request.md`, but execution
workers and checkpoint reviewers do not reread it.

Subtasks contain the local context, requirements, boundaries, completion
conditions, and verification needed for implementation. Review checkpoints
contain their covered task list, review scope, and checkpoint-level
verification.

When several work items share substantial constraints or references, Factory may
create a compact `run-context.md`. It contains only genuinely shared context.
Factory does not copy the complete request into this file or repeat the entire
request across tasks.

The original request remains available to the coordinator for clarification and
replanning and to the final reviewer for checking that decomposition did not
lose any requirement.

## Clarification and Intent Boundaries

Factory may pause when:

- the task is materially ambiguous;
- a product decision has not been made;
- current intent conflicts with the request;
- an external condition prevents safe work.

Questions are limited to information required for safe execution.

After a clarification or mid-run intent change, Factory updates affected active
and ready Subtasks, checkpoints, and shared run context before resuming.

## Continue an Interrupted Run

If the Coding Agent closes, context ends, execution is cancelled, or a tool
fails, continue the existing run with:

```text
Continue the current IDD Factory work.
```

Each Factory step runs in a fresh coordinator context; persisted state is its
only memory. Factory validates saved state before a new one-step coordinator decides whether
to resume a Subtask, review an active checkpoint, activate the next item,
perform final review, or finish the result. An interruption never requires the
previous coordinator context: persisted state and repository evidence suffice.

Do not start a different Factory request while unfinished work exists.

If Factory cannot dispatch the required specialized worker, it preserves the
current item and reports a resumable `BLOCKED` outcome.

## Cancel a Run

To discard only Factory orchestration state:

```text
Cancel the current IDD Factory work.
```

Cancellation does not revert implementation changes and does not delete previous
Factory results.

## Result

After successful completion, Factory writes:

```text
.idd/factory/results/<work-slug>_<yyyy-MM-dd_HH-mm-ssZ>/commit-message.md
```

The UTC timestamp distinguishes repeated runs while the work slug keeps the
result recognizable. The file contains a concise Git-compatible explanation of
why the change was made and what was implemented.

Factory itself does not commit, push, or create a pull request.

## Temporary State

Factory keeps local temporary state under:

```text
.idd/factory/
  current/
    request.md
    run-context.md        # optional
    001-*.ready.md        # Subtask or Review checkpoint
  results/
```

These directories are ignored by default. They are not product intent.

For exact formats, statuses, worker boundaries, and advanced manual invocation,
see the [Factory Skills Reference](factory-skills.md).
