# idd-factory-review-work-result

## Purpose

Review the whole result after all Factory Work Plan tasks are complete.

This skill is the final review workflow contract. The local final-reviewer role
prompt is an optional reference for review focus; it does not own product
intent.

## Routing

Use this workflow only for the complete result of the current active Factory
run. Do not select it as a replacement for a general integration review.

Do not use this workflow automatically based on task size, complexity, uncertainty,
or similarity to the user request. Use it only when the current user explicitly
invokes this command or names this factory workflow directly.

## Rules

- Review the whole branch/result, not only the last task.
- Verify that all tasks in the work plan are either complete or explicitly
  deferred.
- Verify that implementation still matches current specs.
- Verify that tests and commands provide reasonable evidence.
- Verify that temporary factory artifacts are not accidentally becoming product
  documentation.
- Do not update specs or code during review.
- Do not read unrelated previous work plans.

## Workflow

1. Read the explicit work plan and per-task review outputs for the current work
   directory.
2. Review the full diff/result against relevant current specs.
3. Use the local `references/roles/` final-reviewer prompt when present.
4. Check that `.idd/factory/work/` artifacts remain temporary and are not placed
   under `.idd/intent/`.
5. Write `final-review.md` in the same work directory when a file artifact is
   useful.

## Output Format

```md
# Factory Work Result Review

## Scope

- Work plan:
- Tasks reviewed:
- Specs used:

## Verdict

`approved | needs-fix | blocked`

## Completed Work

- Task:
- Evidence:

## Spec Compliance

- Finding:

## Integration Risks

- Risk:

## Verification Evidence

- Command:
- Result:

## Required Fixes

- Fix:

## Cleanup Readiness

- Factory artifacts can be deleted: yes/no
- Reason:
```
