---
name: idd-factory-review-work-result
description: Independently review the complete result of the current Factory run against its request, current intent, integration requirements, and verification evidence.
---

# idd-factory-review-work-result

## Purpose

Independently review the complete result of the current Factory run. This
worker is read-only.

## Preconditions

Run only when `current/` contains `request.md`, optional `run-context.md`, and
one or more valid tasks, all tasks are `.completed.md`, and no ready, active, or
blocked task exists. If the state violates these conditions, return `blocked`
without guessing.

## Review

Read the original request, optional run context, all completed task goals and
completion summaries, only relevant current intent, the full actual diff, and
available verification. Check:

- complete satisfaction of the original request and every task goal;
- consistency between the original request, shared run context, and task
  contracts;
- compliance with relevant intent and preservation boundaries;
- integration and consistency across task results;
- public contracts, maintainability, and sufficient verification;
- absence of incomplete changes hidden by task-level reviews;
- that Factory artifacts did not become product documentation.

Assess implementation and verification independently. A favorable integrated
implementation assessment does not compensate for missing required verification.

Do not modify code, intent, Factory files, or task statuses. Do not reactivate
completed tasks.

## Verdicts

- `approved`: the integrated implementation has no material findings and all
  required verification has conclusive evidence; the result is ready for
  `idd-factory-finish-work`.
- `needs-fix`: return a bounded self-contained corrective goal, context, scope,
  requirements, done conditions, and verification suitable for the coordinator
  to create the next numbered ready task.
- `blocked`: identify the concrete blocking condition.
- `intent-required`: identify missing or conflicting durable intent and the
  applicable intent handoff.

## Output

Return the verdict first, then keep the assessments separate:

```text
Verdict: <approved | needs-fix | blocked | intent-required>

Implementation assessment:
<integrated implementation result and material findings>

Verification assessment:
<conclusive evidence and required evidence that remains incomplete>
```

For `needs-fix`, append only the bounded corrective task definition. For
`blocked` or `intent-required`, append only this structured blocker:

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

Do not describe a blocked result as approved, review passed, completed,
accepted, or finished. The coordinator owns the Factory outcome.
