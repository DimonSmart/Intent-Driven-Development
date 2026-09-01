# idd-factory-review-checkpoint

## Purpose

Review an intermediate integrated slice without owning orchestration. A checkpoint is semantic-review graph work, not a workflow state.

## Inputs and boundaries

Use the supplied checkpoint contract, covered completed work/result references, relevant durable intent, current focused product state, and authoritative runtime verification observations/evidence. Do not rely on transcript or event-history replay.

Do not modify product files, Factory state, graph history, `.idd/factory.yaml`, durable intent, or verification policy. Do not rerun deterministic Factory checks solely to classify their outcome.

## Findings

Return `approved` when the covered slice is semantically coherent.

For a bounded defect, return `correction-required` with a flat `payload`
containing non-empty `capability`, `task`, and `reason` fields. Runtime
materializes corrective future work; completed work remains immutable.

For a missing focused prerequisite or investigation, return
`additional-work-required` with the same flat capability/task/reason payload.
Return `global-replan-required` only if the remaining global strategy cannot
stay correct through local future work.

Use `intent-required` only when a durable product decision required to judge the
covered work cannot be determined from the original request and current intent.
Missing documentation alone is not sufficient. Use `blocked` when an
external/non-semantic condition prevents review. Ordinary review work does not
own user clarification; do not return `needs-clarification`.

## Outcomes

Return one JSON object with `outcome` and only its outcome-specific fields. Use
one outcome:

- `approved`;
- `correction-required`;
- `additional-work-required`;
- `global-replan-required`;
- `intent-required`;
- `blocked`.

Do not choose a next role, skill, phase, retry, or transition. Runtime owns all operational decisions.
Do not return invocation identity, role, capability outside an outcome payload,
work-item ID, attempt ID, run ID, protocol or schema version, skill, execution
profile, result path, or other runtime bookkeeping.
