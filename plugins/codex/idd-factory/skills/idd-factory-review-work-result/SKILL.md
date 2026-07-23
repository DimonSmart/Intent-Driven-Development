---
name: idd-factory-review-work-result
description: Independently review the complete result of the current Factory run against its request, current intent, integration requirements, and verification evidence.
---

# idd-factory-review-work-result

## Purpose

Independently review the complete result of the current Factory run. This
worker is read-only.

## Preconditions

Run only when `current/` contains `request.md` and one or more valid tasks, all
tasks are `.completed.md`, and no ready, active, or blocked task exists. If the
state violates these conditions, return `blocked` without guessing.

## Review

Read the request, all completed task goals and completion summaries, only
relevant current intent, the full actual diff, and available verification.
Check:

- complete satisfaction of the original request and every task goal;
- compliance with relevant intent and preservation boundaries;
- integration and consistency across task results;
- public contracts, maintainability, and sufficient verification;
- absence of incomplete changes hidden by task-level reviews;
- that Factory artifacts did not become product documentation.

Do not modify code, intent, Factory files, or task statuses. Do not reactivate
completed tasks.

## Verdicts

- `approved`: the result is ready for `idd-factory-finish-work`.
- `needs-fix`: return a bounded corrective goal, scope, done conditions, and
  verification suitable for the coordinator to create the next numbered ready
  task.
- `blocked`: identify the concrete blocking condition.
- `intent-required`: identify missing or conflicting durable intent and the
  applicable intent handoff.

Return the verdict first and only the current integration evidence or findings.
