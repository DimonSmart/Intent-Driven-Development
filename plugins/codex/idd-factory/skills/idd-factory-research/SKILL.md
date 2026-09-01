---
name: idd-factory-research
description: Perform one focused read-only research work item in an isolated worker context and return findings for dependent graph work.
---

# idd-factory-research

## Purpose

Perform one focused read-only `research` work item and return a durable result that another graph node can consume. This worker answers the supplied research contract; it does not coordinate the Factory.

## Inputs and boundaries

Read the supplied immutable work-item contract, relevant durable intent, completed dependency result summaries/references, prior result references for this same item, and focused repository evidence needed to answer the research question.

Do not modify product files, Factory state, graph history, `.idd/factory.yaml`, durable intent, or verification policy. Do not select another role/skill or decide what runtime should execute next.

Keep investigation proportional to the contract. Return findings and evidence rather than speculative implementation changes.

## Dynamic discovery

If research exposes a concrete missing prerequisite that should be performed separately, return `additional-work-required` with a flat `payload` containing non-empty `capability`, `task`, and `reason` fields.

Use `global-replan-required` only when the discovery invalidates the global remaining strategy. A local prerequisite is not a global replan.

## Outcomes

Return one JSON object with `outcome` and only its outcome-specific fields. Use
one outcome:

- `completed` — include concise findings, evidence/references, unresolved concerns if any;
- `additional-work-required` — runtime should materialize a focused dependency;
- `global-replan-required` — the global remaining strategy must change;
- `intent-required` — durable product meaning is missing;
- `blocked` — an external condition prevents useful research.

Runtime persists the result reference and owns all scheduling and ordered-plan mutation.

Do not return invocation identity, role, capability outside an outcome payload,
work-item ID, attempt ID, run ID, protocol or schema version, skill, execution
profile, result path, or other runtime bookkeeping.
