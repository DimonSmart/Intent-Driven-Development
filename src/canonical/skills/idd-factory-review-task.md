# idd-factory-review-task

## Purpose

Perform the read-only `final-review` semantic operation over the integrated product after strict final verification. It is a runtime-owned final review operation, not an ordinary `semantic-review` work item and not an orchestration authority.

Runtime owns scheduling, strict final verification, corrections, ordered future work, persistence, commit-message generation, and finalization.

## Inputs and boundaries

Review the supplied review contract, the unchanged original Factory request,
current durable intent after any preflight preparation, completed
dependency/result references, current product changes, and compact authoritative
runtime verification observations/evidence.

Review current product semantics, not worker transcripts or orchestration history. Do not recursively inventory `.idd/factory`, attempt directories, `bin`, or `obj`. Do not rerun deterministic Factory checks merely to reconfirm runtime evidence.

Do not modify product files, Factory state, graph history, `.idd/factory.yaml`, durable intent, or verification policy. Do not select a next role, skill, or runtime phase.

## Defects become graph work

A semantic finding completes the review operation; it does not make the reviewer an implementer.

For a concrete bounded defect, return `correction-required` with:

```text
payload:
  capability: implementation | research | ...
  task: A self-contained correction contract.
  reason: Why the correction is required.
```

Runtime materializes that correction as a new graph work item. A final review with a defect remains immutable evidence; after correction and strict verification, runtime performs a fresh final review.

If review discovers a prerequisite/investigation rather than a direct correction, return `additional-work-required` with the same flat capability/task/reason payload. Use `global-replan-required` only when the remaining global strategy itself is invalid.

## Outcomes

Return one JSON object with `outcome` and only its outcome-specific fields. Use
one outcome:

- `approved` — no material semantic defect remains; no additional semantic payload is required;
- `correction-required` — bounded semantic defect that should become corrective graph work; return the flat `payload.capability`, `payload.task`, and `payload.reason` described above;
- `additional-work-required` — typed prerequisite/investigation should become graph work; return the same flat payload;
- `global-replan-required` — remaining global strategy must be restructured; `reason` and `payload` are optional semantic context;
- `intent-required` — a durable product decision required to judge the result cannot be determined from the original request and current intent; return the standard non-empty `payload.missingIntentDecisions` structure;
- `blocked` — external condition prevents review; `reason` and `payload` may describe the blocker.

Never describe failed, blocked, or unverified semantics as approved. `recommendedNextWorkflow` in an `intent-required` payload, when present, refers only to a durable-intent editing workflow outside Factory runtime orchestration.
Do not treat implementation as authoritative when it matches the request but
the resulting durable intent is incomplete or contradictory. Return a bounded
correction when implementation is wrong; return structured `intent-required`
when a genuine durable decision is absent.

Do not return invocation identity, role, capability outside an outcome payload,
work-item ID, attempt ID, run ID, protocol or schema version, skill, execution
profile, result path, or other runtime bookkeeping.
