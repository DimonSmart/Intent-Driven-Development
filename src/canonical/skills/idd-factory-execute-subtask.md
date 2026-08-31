# idd-factory-execute-subtask

## Purpose

Execute one `implementation` work item in a fresh semantic context. It may include documentation changes when required by the contract. The supplied immutable work-item contract and current `.idd/intent/` are normative.

Runtime owns selection, retries, ordered-plan mutation, persistence, verification, and scheduling. This worker never acts as coordinator.

## Inputs and boundaries

Read the supplied work-item contract, relevant durable intent, completed dependency result summaries/references, prior result references for this same work item, focused repository evidence, and runtime-supplied verification observations.

Do not read unrelated work merely to reconstruct a global plan. Do not mutate Factory state, graph history, `.idd/factory.yaml`, intent, or verification policy. Do not select another worker, role, skill, or runtime phase.

Make the smallest coherent product change needed by the contract. Runtime verification is authoritative. Do not run Factory verification checks merely to classify success or repair a hidden gate; there is no `verification-fix` orchestration mode.

## Dynamic dependency discovery

If the assigned work cannot safely continue because a concrete additional prerequisite has been discovered, return `additional-work-required` rather than broadening scope or choosing an agent.

`payload.additionalWork` (or `payload.requirement`) contains:

```text
capability
 goal
reason
context?
constraints[]?
expectedOutput?
verificationCheckIds[]?
verificationExpectations?  # check ID -> must-pass | may-fail
```

The requirement states *what work is needed*, not who should perform it. Runtime validates the capability, materializes a new graph node, persists the dependency, and later resumes this work item with the dependency result.

Use `global-replan-required` only when the discovery invalidates the global remaining strategy and cannot be represented as local additional work. `needs-replan` may be accepted by older adapters but should not be emitted by new workers.

## Verification expectations

Do not reinterpret a failed authoritative check. Runtime classifies intermediate failures deterministically from the work-item's persisted `verificationExpectations`:
- `must-pass` or an unspecified expectation: failure is unexpected;
- `may-fail`: the named intermediate failure may be expected;
- final verification is always strict.

## Outcomes

Return protocol version 2 with role `implementer` and one outcome:

- `completed` — assigned semantic work is finished; include concise `summary`, `declaredChanges[]`, `concerns[]`, optional diagnostic `verificationClaims[]`;
- `additional-work-required` — a local typed prerequisite was discovered;
- `global-replan-required` — the global remaining strategy must change;
- `intent-required` — durable product meaning is missing;
- `blocked` — an external/non-semantic condition prevents progress.

For `intent-required`, return the standard non-empty `payload.missingIntentDecisions` structure. `recommendedNextWorkflow`, when present, refers only to a user-facing durable-intent workflow, never Factory scheduling.
