---
name: idd-factory-finish-work
description: Create a compact commit-message handoff for an approved Factory run, then safely clear its current temporary state.
---

# idd-factory-finish-work

## Purpose

Create a compact Git commit-message handoff for an approved Factory run, then
clear its temporary current state. This workflow does not run Git commands.

## Preconditions

- The final review verdict is `approved` for the current actual diff.
- `current/` passes the state invariants from `idd-factory-run`.
- It contains one `request.md`, one or more tasks, and every task is completed.

Stop without cleanup if any precondition fails.

## Result Directory

Choose a short lowercase kebab-case work slug that describes the overall
implemented result without a date, status, or agent name. Write:

```text
.idd/factory/results/<work-slug>/commit-message.md
```

Never overwrite a result directory. If the slug exists, try `<work-slug>-2`,
then `-3`, and so on.

The result directory contains only `commit-message.md`. Do not copy the request,
tasks, reviews, or other execution state into `results/`.

## Commit Message

Derive the message from `request.md`, completed task goals, the actual diff,
and final review. The diff takes precedence over planning assumptions.

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

Do not include full file lists, test logs, attempts, task statuses, resolved
findings, tokens, model or timing data, internal prompts, the full request, or
claims not supported by the diff.

## Safe Finish

1. Create the collision-safe result directory.
2. Write and verify `commit-message.md` completely.
3. Only after the result exists and is readable, clear the contents of
   `.idd/factory/current/`.
4. Leave the empty `current/`, all of `results/`, and `.idd/factory/.gitignore`
   in place.
5. Report the exact commit-message path and that current state was cleared.

If result creation fails, leave `current/` unchanged so the run can resume.
Factory never commits, pushes, creates a pull request, or deletes results.
