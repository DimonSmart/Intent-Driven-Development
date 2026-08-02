# idd-factory-finalize-run

## Purpose

Create the commit-message handoff for an approved Task result and safely
finalize the current Factory run. This workflow does not run Git commands.

## Preconditions

- The final review verdict is `approved` for the current actual diff.
- `current/` passes the state invariants from `idd-factory-run`.
- It contains one `request.md`, one or more Work items, and every Subtask and
  Review checkpoint is completed.

Stop without cleanup if any precondition fails.

## Result Directory

Choose a short lowercase kebab-case work slug that describes the overall
implemented result without a date, status, or agent name. Capture the current
UTC time once at finalization in `yyyy-MM-dd_HH-mm-ssZ` format and write:

```text
.idd/factory/results/<work-slug>_<timestamp>/commit-message.md
```

Write `factory-result.json` beside the commit message. It must be a valid flat
JSON object containing `schemaVersion`, `methodologyVersion`, `factoryOutcome`,
`subtaskCount`, `completedSubtaskCount`, `reviewCheckpointCount`,
`completedReviewCheckpointCount`, `correctiveSubtaskCount`, `blockedItemCount`,
`incompleteItemCount`, `finalReviewVerdict`, `verificationStatus`, and
`commitMessagePath`. Take the methodology version from the `Methodology version:`
field in `current/request.md`.

Never overwrite a result directory. If the complete timestamped name exists,
append `-2`, then `-3`, and so on.

The result directory contains only `commit-message.md` and `factory-result.json`. Do not copy the request,
Subtasks, checkpoints, reviews, or other state into `results/`.

## Commit Message

Derive the message from `request.md`, completed Subtask goals, checkpoint
results, the actual diff, and final review. The diff takes precedence over
planning assumptions.

```text
<Imperative subject, at most 72 characters, no final period>

Performed by: IDD Factory

Why:
<at most three short sentences>

Result:
- <confirmed principal result>
- <confirmed principal result>
```

Use the repository's established commit language, or English when it cannot be
determined. Include two to six result bullets and preferably stay under 1200
characters.

Do not include full file lists, test logs, attempts, item statuses, resolved
findings, tokens, model or timing data, internal prompts, the full request, or
claims not supported by the diff.

## Safe Finish

1. Create the collision-safe result directory.
2. Write and verify `commit-message.md` completely.
3. Write and parse `factory-result.json`, including its repository-relative
   `commitMessagePath`; stop without cleanup if either result file is invalid.
4. Only after both result files exist and are readable, clear the contents of
   `.idd/factory/current/`.
5. Leave the empty `current/`, all of `results/`, and `.idd/factory/.gitignore`
   in place.
6. Report the exact result directory and that current state was cleared.

If result creation fails, leave `current/` unchanged so the run can resume.
Factory never commits, pushes, creates a pull request, or deletes results.
