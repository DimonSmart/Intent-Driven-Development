# idd-factory-review-task

## Purpose

Perform one read-only semantic review work item over the integrated product. Final review is represented by an ordinary persisted graph node with capability `semantic-review`; it is not a global workflow phase.

Runtime owns scheduling, strict final verification, corrections, graph mutation, persistence, and finalization.

## Inputs and boundaries

Review the supplied review contract, original Factory request when this is final review, relevant durable intent, completed dependency/result references, current product changes, and compact authoritative runtime verification observations/evidence.

Review current product semantics, not worker transcripts or orchestration history. Do not recursively inventory `.idd/factory`, attempt directories, `bin`, or `obj`. Do not rerun deterministic Factory checks merely to reconfirm runtime evidence.

Do not modify product files, Factory state, graph history, `.idd/factory.yaml`, durable intent, or verification policy. Do not select a next role, skill, or runtime phase.

## Defects become graph work

A semantic finding completes the review operation; it does not make the reviewer an implementer.

For a concrete bounded defect, return `correction-required` (compatibility alias `needs-fix` may be accepted) with:

```text
payload.correctiveSubtask:
  id?
  capability: implementation | documentation | ...
  contractMarkdown
  verificationCheckIds[]?
  verificationExpectations?
```

Runtime materializes that correction as a new graph work item. A final review with a defect remains immutable evidence; after correction and strict verification, runtime materializes a fresh final review node.

If review discovers a prerequisite/investigation rather than a direct correction, return `additional-work-required` with a typed capability/goal/reason requirement. Use `global-replan-required` only when the remaining global strategy itself is invalid.

## Outcomes

Return protocol version 2 with role `final-reviewer` and one outcome:

- `approved` — no material semantic defect remains; for final review include `payload.commitMessage` with `subject`, `why[]`, `result[]`;
- `correction-required` — bounded semantic defect that should become corrective graph work;
- `additional-work-required` — typed prerequisite/investigation should become graph work;
- `global-replan-required` — remaining global strategy must be restructured;
- `needs-clarification` — explicit user input is required;
- `intent-required` — durable product meaning is missing;
- `blocked` — external condition prevents review.

Never describe failed, blocked, or unverified semantics as approved. `recommendedNextWorkflow` in an `intent-required` payload, when present, refers only to a durable-intent editing workflow outside Factory runtime orchestration.
