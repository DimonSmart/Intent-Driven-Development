# factory-finish-work

## Purpose

Finish a factory run by reporting results and cleaning temporary artifacts.

## Rules

- Summarize what was implemented.
- List specs used as intent.
- List tests and verification commands.
- List remaining risks.
- Delete `.idd/factory/work/<current-work-dir>/` unless the user explicitly
  asked to keep or commit factory artifacts.
- If deletion is unsafe because artifacts were explicitly requested as
  evidence, do not delete silently.
- Never delete `.specs/`.
- Never delete code, tests, or durable documentation as part of factory cleanup.
- Do not read unrelated previous work plans.
- Factory artifacts are temporary execution state, not product intent and not a
  specification.

## Workflow

1. Read the explicit current work plan and final review result.
2. Report implementation, specs used, verification, review result, and risks.
3. Delete only the current `.idd/factory/work/<current-work-dir>/` directory when
   cleanup is allowed.
4. Leave `.idd/factory/.gitignore` and `.idd/factory/README.md` in place.

## Output Format

```md
# Factory Work Finished

## Summary

## Specs Used

## Code Areas Changed

## Tests and Verification

## Review Result

## Remaining Risks

## Temporary Artifact Cleanup

- Work directory:
- Action: `deleted | kept | committed | not-created`
- Reason:
```
