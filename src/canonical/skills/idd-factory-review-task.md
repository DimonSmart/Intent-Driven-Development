# idd-factory-review-task

## Purpose

Review the result of one completed task against the task brief, relevant specs,
code quality, and verification evidence.

This skill is the review workflow contract. The local task-reviewer role prompt
is an optional reference for review focus; it does not own product intent.

## Routing

Use this workflow only when an active Factory Work Plan requires review of one
bounded task. Do not select it as a replacement for a general code review.

Do not use this workflow automatically based on task size, complexity, uncertainty,
or similarity to the user request. Use it only when the current user explicitly
invokes this command or names this factory workflow directly.

## Rules

- Review one task only.
- Use the task brief and relevant specs as the review scope.
- Do not broaden into unrelated code review.
- Do not treat old factory plans as context.
- Classify findings clearly.
- Critical and important findings must block the task.
- Minor findings may be recorded for final review.

## Workflow

1. Read the explicit task brief and the relevant section of the current Factory
   Work Plan.
2. Read only the relevant current specs needed for the task scope.
3. Review the implementation evidence and verification output for that task.
4. Use the local `references/roles/` task-reviewer prompt when present.
5. Write a task review in the same `.idd/factory/work/<current-work-dir>/`
   directory when a file artifact is useful.

## Output Format

```md
# Factory Task Review

## Scope

- Work plan:
- Task:
- Diff/review evidence:

## Verdict

`approved | needs-fix | blocked | out-of-scope`

## Spec Compliance

- Result:
- Evidence:

## Code Quality

- Result:
- Findings:

## Tests and Verification

- Commands reviewed:
- Result:

## Required Fixes

### 1. <finding>

Severity: `critical | important | minor`

Evidence:

Required change:

## Notes for Final Review

- Note:
```
