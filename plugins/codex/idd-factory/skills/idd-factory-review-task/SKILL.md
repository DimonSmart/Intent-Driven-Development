---
name: idd-factory-review-task
description: Independently review one active Factory review checkpoint across its covered completed execution tasks, focused diff, and checkpoint verification evidence.
---

# idd-factory-review-task

## Purpose

Independently review one explicit active review checkpoint. The skill name is
retained for compatibility; it does not review every execution task.

## Inputs

Read:

- the active review checkpoint;
- every completed execution task named by its `Covers` section;
- optional `run-context.md`;
- only relevant current intent;
- checkpoint-local diff and evidence derived from covered tasks' `Changes`,
  checkpoint scope, and checkpoint verification.

Do not read `request.md`, unrelated execution tasks, later work items, or the
complete run.

## Rules

- Confirm the supplied item is the only active item and is a review checkpoint.
- Confirm every `Covers` entry exists, is completed, is an execution task, and
  forms the contiguous group required by the checkpoint.
- Check covered goals, requirements, done conditions, named preservation
  boundaries, shared context, public contracts, integration surface, code
  quality, and checkpoint-level verification.
- Review only the checkpoint and its necessary integration surface.
- Assess implementation and verification separately.
- Return only current material findings; do not accumulate history or prolong
  loops for stylistic preferences.
- Use `needs-fix` when one bounded corrective execution task can resolve the
  checkpoint findings.
- Use `needs-replan` when coverage, checkpoint placement, contracts, or ordering
  prevent safe review.
- Use `blocked` only for an external condition or exact non-intent user decision.
- Use `intent-required` only for missing or conflicting durable behavior
  discovered while reviewing implementation.
- Do not modify code, intent, request, run context, execution tasks, checkpoint,
  or filenames.

## Verdicts

- `approved`: no material findings and all required checkpoint verification is
  conclusive.
- `needs-fix`: return one bounded self-contained implementation-only corrective
  execution task suitable for insertion immediately before the checkpoint.
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
Corrective execution task:
<a complete self-contained execution-task contract>
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
