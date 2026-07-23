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
- The explicit active task, including current `Review Findings` if present.
- Only relevant current intent.
- The actual task diff and available verification evidence.

## Rules

- Check the task goal, scope, completion conditions, request, relevant intent,
  preservation boundaries, public contracts, code quality, and verification.
- Review only the supplied task and its necessary integration surface.
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

- `approved`: the task is complete and safe to mark completed.
- `needs-fix`: return only the current concrete findings.
- `blocked`: identify the external or repository condition preventing review or
  safe continuation.
- `intent-required`: identify the intent gap and applicable intent handoff.

Return the verdict first, concise evidence, focused verification assessment,
and current findings only.
