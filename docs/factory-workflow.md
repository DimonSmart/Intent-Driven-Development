# IDD Factory Workflow

IDD Factory is the optional execution layer for implementation work that benefits from several coordinated stages, explicit review boundaries, or safe continuation after interruption.

Factory remains temporary. It may read `.idd/intent/`, but it does not create product truth and does not turn execution state into permanent specifications.

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

## Run a Complete Factory Task

Give Factory the complete task once:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Or provide the request directly:

```text
Use idd-factory-run to migrate the storage subsystem, update all consumers, preserve saved-data compatibility, and verify the integrated result.
```

Under normal conditions, this single invocation carries the requested work through to completion.

Factory examines the request and current intent, asks only questions that block safe work, decomposes the task when useful, implements the stages in order, reviews the results, and prepares a concise commit-message handoff.

The user normally does not invoke the internal worker skills separately.

## Clarification and Intent Boundaries

Factory may pause before implementation when:

- the task is materially ambiguous;
- a product decision has not been made;
- current intent conflicts with the request;
- an external condition prevents safe work.

Questions should be limited to information required for safe execution.

When durable product behavior is missing or contradictory, Factory stops with `INTENT_REQUIRED`. Resolve the product decision through an `idd-intent` workflow, then continue Factory.

## Continue an Interrupted Run

A normal Factory run does not require a deliberate interruption.

If the Coding Agent closes, context ends, execution is cancelled, or a tool fails, continue the existing run with:

```text
Continue the current IDD Factory work.
```

Factory validates its saved state and the current repository diff before deciding whether to continue implementation, review the active stage, perform final review, or finish the result.

Do not start a different Factory request while unfinished work exists.

## Cancel a Run

To discard only the Factory orchestration state:

```text
Cancel the current IDD Factory work.
```

Cancellation does not revert implementation changes and does not delete previous Factory results.

## Result

After successful completion, Factory writes:

```text
.idd/factory/results/<work-slug>_<yyyy-MM-dd_HH-mm-ssZ>/commit-message.md
```

The UTC timestamp distinguishes repeated runs while the leading work slug keeps the result recognizable in directory listings. The file contains a concise Git-compatible explanation of why the change was made and what was implemented. A commit or publication workflow can use it together with the actual Git diff.

Factory itself does not commit, push, or create a pull request.

## Temporary State

Factory keeps local temporary state under:

```text
.idd/factory/
  current/
  results/
```

These directories are ignored by default. They are not product intent.

For the exact task-file lifecycle, statuses, worker boundaries, and advanced manual invocation examples, see the [Factory Skills Reference](factory-skills.md).
