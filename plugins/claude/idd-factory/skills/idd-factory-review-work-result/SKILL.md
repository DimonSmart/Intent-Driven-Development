---
name: idd-factory-review-work-result
description: Independently review the complete result of the current Factory run across all execution tasks, review checkpoints, current intent, integration requirements, and verification evidence.
context: fork
agent: Explore
allowed-tools: Read Glob Grep Bash
---

# idd-factory-review-work-result

The final reviewer owns verification for context `final` over the complete
Factory diff. Before producing a verdict, resolve the current final-policy
checks, reuse only conclusive evidence that still applies to the current check
definition and complete diff, and run every assigned automatic check that lacks
such evidence. Ask before `confirmation: required`. Present `instructions`
checks to the user and obtain the actual result. Read-only review forbids code,
intent, and Factory-state changes; it does not prohibit verification commands.
Any assigned final check that remains `Not verified` requires `blocked`, never
`approved`.

## Purpose

Independently review the complete result of the current Factory run. This worker
is read-only with respect to implementation, intent, and Factory state.

## Preconditions

Run only when `current/` contains `request.md`, optional `run-context.md`, and
one or more valid work items, all work items are `.completed.md`, and no ready,
active, or blocked item exists. If the state violates these conditions, return
`blocked` without guessing.

## Final Verification Procedure

1. Resolve checks selected by the current `.idd/verification.md` for context
   `final` and the complete Factory diff.
2. Reuse existing evidence only when it is conclusive and still applies to the
   current check definition and complete diff.
3. Run every assigned automatic check that does not have reusable conclusive
   evidence.
4. Before a check with `confirmation: required`, ask the exact user decision and
   wait for the answer.
5. For a check with `instructions`, present the instructions and wait for the
   user's actual result; do not infer success.
6. Record confirmation refusal, unavailable execution, and unconfirmed user
   instructions as `Not verified` with the precise reason and resumption
   condition.
7. Return `blocked` while any assigned final check remains `Not verified`.
8. A conclusive failed check is evidence of a defect or blocker; classify the
   resulting verdict according to whether bounded implementation work can fix it.

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
Final verification sufficiency is defined by the assigned `final` checks, not by
an assumption that every repository test must run.

Do not modify code, intent, Factory files, or work-item statuses. Do not reopen
completed items. Running assigned verification commands is part of final review
and is not a modification of implementation or Factory state.

## Verdicts

- `approved`: the integrated implementation has no material findings and all
  assigned final verification has conclusive evidence; the result is ready for
  `idd-factory-finish-work`.
- `needs-fix`: return a bounded self-contained implementation-only corrective
  execution task suitable for the coordinator to append after completed items.
  The mandatory next final review is the review gate; do not add a terminal
  checkpoint solely for this correction.
- `blocked`: identify the concrete blocking condition, including any assigned
  final check that remains `Not verified`.
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
