---
name: idd-factory-review-checkpoint
description: Independently review one active Review checkpoint across its covered completed Subtasks.
---

# idd-factory-review-checkpoint

For the active checkpoint, resolve its recorded IDs from current
`.idd/verification.md` using context `checkpoint` and the aggregate `Covers`
scope. Required unverified checks prevent approval. Do not run final checks.

## Purpose

Independently review one explicit active Review checkpoint across its covered
completed Subtasks.

## Inputs

Read:

- the active Review checkpoint;
- every completed Subtask named by its `Covers` section;
- optional `run-context.md`;
- only relevant current intent;
- checkpoint-local diff and evidence derived from covered tasks' `Changes`,
  checkpoint scope, and checkpoint verification.

Do not read `request.md`, unrelated Subtasks, later work items, or the
complete run.

## Rules

- Confirm the supplied item is the only active item and is a Review checkpoint.
- Confirm every `Covers` entry exists, is completed, is a Subtask, and
  forms the contiguous group required by the checkpoint.
- Check covered goals, requirements, done conditions, named preservation
  boundaries, shared context, public contracts, integration surface, code
  quality, and checkpoint-level verification.
- Review only the checkpoint and its necessary integration surface.
- Assess implementation and verification separately.
- Return only current material findings; do not accumulate history or prolong
  loops for stylistic preferences.
- Use `needs-fix` when one bounded corrective Subtask can resolve the
  checkpoint findings.
- Use `needs-replan` when coverage, checkpoint placement, contracts, or ordering
  prevent safe review.
- Use `blocked` only for an external condition or exact non-intent user decision.
- Use `intent-required` only for missing or conflicting durable behavior
  discovered while reviewing implementation.
- Do not modify code, intent, request, run context, Subtasks, checkpoint,
  or filenames.

## Verdicts

- `approved`: no material findings and all required checkpoint verification is
  conclusive.
- `needs-fix`: return one bounded self-contained implementation-only corrective
  Subtask suitable for insertion immediately before the checkpoint.
- `needs-replan`: checkpoint coverage, placement, or contracts prevent safe
  review.
- `blocked`: an external condition or user decision prevents continuation.
- `intent-required`: current intent cannot authorize the implementation result.

## Output

```text
Verdict: <approved | needs-fix | needs-replan | blocked | intent-required>

Implementation assessment:
<covered implementation result and material findings>

Verification assessment:
<conclusive and missing checkpoint evidence>
```

For `needs-fix`, append:

```text
Corrective Subtask:
<a complete self-contained Subtask contract>
```

For `needs-replan`, append:

```text
Dependency:
<minimum coverage, placement, prerequisite, or contract correction>
```

For `blocked` or `intent-required`, append:

```text
Blocker:
Reason:
<one concrete condition>

Verified:
<conclusive evidence or none>

Not verified:
<required incomplete work or evidence>

Resume when:
<condition or exact question that makes continuation safe>
```

Never describe a blocked checkpoint as approved, completed, accepted, or
finished. The coordinator owns Factory state and outcome.
