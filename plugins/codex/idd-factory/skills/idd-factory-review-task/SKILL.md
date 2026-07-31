---
name: idd-factory-review-task
description: Independently review one active Factory task against its request, relevant intent, actual diff, code quality, and verification evidence.
---

# idd-factory-review-task

## Purpose

Independently review one explicit `.active.md` task. This worker is read-only
and reviews neither later tasks nor the whole run.

## Inputs

- `.idd/factory/current/request.md`.
- The explicit active task, including current `Review Findings` or a resumed
  `Blocker` when present.
- Only relevant current intent.
- The actual task diff and available verification evidence.

## Rules

- Check the task goal, scope, completion conditions, request, relevant intent,
  preservation boundaries, public contracts, code quality, and verification.
- Review only the supplied task and its necessary integration surface.
- Assess implementation and verification independently. A favorable
  implementation assessment does not make incomplete required verification
  sufficient.
- Return only findings that are currently actionable; do not accumulate review
  history.
- Critical and important correctness, maintainability, intent, public-contract,
  or downstream-safety findings require `needs-fix`.
- Do not prolong the loop for purely stylistic preferences that do not affect
  those qualities.
- Return `intent-required` when approval would require unknown or conflicting
  product intent.
- Do not modify code, intent, request files, task contents, or filenames.

## Verdicts

- `approved`: the implementation assessment has no material findings and all
  required verification has conclusive evidence; the task is safe to mark
  completed.
- `needs-fix`: return only the current concrete implementation or verification
  findings that the implementer can resolve inside the task.
- `blocked`: identify the external or repository condition preventing review or
  safe continuation, without diagnosing or repairing that condition for the
  coordinator.
- `intent-required`: identify the intent gap and applicable intent handoff.

## Output

Return the verdict first, then keep the assessments separate:

```text
Verdict: <approved | needs-fix | blocked | intent-required>

Implementation assessment:
<what the actual diff establishes and any material implementation findings>

Verification assessment:
<which required checks are conclusive and which remain incomplete>
```

For `needs-fix`, append only:

```text
Review findings:
- <current actionable finding>
```

For `blocked` or `intent-required`, append only this structured blocker:

```text
Blocker:
Reason:
<one concrete blocking condition>

Verified:
<only conclusive evidence already established, or none>

Not verified:
<required work or evidence that remains incomplete>

Resume when:
<one concrete condition that makes continuation safe>
```

Do not describe a blocked task as approved, review passed, completed, accepted,
or finished. The coordinator owns the Factory outcome and persisted task state.
