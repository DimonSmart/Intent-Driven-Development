# idd-factory-review-work-result

Resolve current final-policy checks with context `final` for the complete
Factory diff. Final verification is sufficient only when its assigned checks
have evidence; it does not imply every repository test. Record confirmation
refusals, user instructions, and unavailable checks as `Not verified`.

## Purpose

Independently review the complete result of the current Factory run. This worker
is read-only.

## Preconditions

Run only when `current/` contains `request.md`, optional `run-context.md`, and
one or more valid work items, all work items are `.completed.md`, and no ready,
active, or blocked item exists. If the state violates these conditions, return
`blocked` without guessing.

## Review

Read the original request, optional run context, all completed execution-task
contracts and completions, all completed review checkpoints and completions,
only relevant current intent, the full actual diff, and available verification.
Check:

- complete satisfaction of the original request and every execution-task goal;
- consistency between the original request, shared context, execution contracts,
  and checkpoint results;
- compliance with relevant intent and preservation boundaries;
- integration and consistency across all execution results;
- public contracts, maintainability, and sufficient integrated verification;
- whether checkpoints covered the risky boundaries they claimed to protect;
- absence of incomplete changes hidden by grouped checkpoint reviews;
- absence of intent-changing work recorded as a Factory execution task;
- that Factory artifacts did not become product documentation.

Assess implementation and verification independently. A favorable integrated
implementation assessment does not compensate for missing required verification.

Do not modify code, intent, Factory files, or work-item statuses. Do not reopen
completed items.

## Verdicts

- `approved`: the integrated implementation has no material findings and all
  required verification has conclusive evidence; the result is ready for
  `idd-factory-finish-work`.
- `needs-fix`: return a bounded self-contained implementation-only corrective
  execution task suitable for the coordinator to append after completed items.
  The mandatory next final review is the review gate; do not add a terminal
  checkpoint solely for this correction.
- `blocked`: identify the concrete blocking condition.
- `intent-required`: identify missing or conflicting durable intent and the
  applicable intent handoff. Do not define a corrective task until intent is
  resolved outside the work-item list.

## Output

Return the verdict first, then keep the assessments separate:

```text
Verdict: <approved | needs-fix | blocked | intent-required>

Implementation assessment:
<integrated implementation result and material findings>

Verification assessment:
<conclusive evidence and required evidence that remains incomplete>
```

For `needs-fix`, append only the complete corrective execution-task contract.
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

Do not describe a blocked result as approved, review passed, completed,
accepted, or finished. The coordinator owns the Factory outcome.
