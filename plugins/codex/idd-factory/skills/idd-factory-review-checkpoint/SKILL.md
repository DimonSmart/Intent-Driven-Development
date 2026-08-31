---
name: idd-factory-review-checkpoint
description: Independently review one intermediate semantic-review graph work item over its covered completed work.
---

# idd-factory-review-checkpoint

## Purpose

Review an intermediate integrated slice without owning orchestration. A checkpoint is semantic-review graph work, not a workflow state.

## Inputs and boundaries

Use the supplied checkpoint contract, covered completed work/result references, relevant durable intent, current focused product state, and authoritative runtime verification observations/evidence. Do not rely on transcript or event-history replay.

Do not modify product files, Factory state, graph history, `.idd/factory.yaml`, durable intent, or verification policy. Do not rerun deterministic Factory checks solely to classify their outcome.

## Findings

Return `approved` when the covered slice is semantically coherent.

For a bounded defect, return `correction-required` with `payload.correctiveSubtask` containing capability, contract Markdown, and optional stable verification IDs/expectations. Runtime materializes corrective work as a new graph node; completed work remains immutable.

For a missing focused prerequisite or investigation, return `additional-work-required` with a typed capability/goal/reason requirement. Return `global-replan-required` only if the remaining global strategy cannot stay correct through local graph work.

Use `intent-required` only when durable product meaning is missing, or `blocked` when an external/non-semantic condition prevents review. Ordinary review work does not own user clarification; do not return `needs-clarification`.

## Outcomes

Return protocol version 2 with role `checkpoint-reviewer` and one outcome:

- `approved`;
- `correction-required`;
- compatibility alias `needs-fix`;
- `additional-work-required`;
- `global-replan-required`;
- compatibility alias `needs-replan`;
- `intent-required`;
- `blocked`.

Do not choose a next role, skill, phase, retry, or transition. Runtime owns all operational decisions.
