# Intent Preflight

Intent Preflight is the bounded semantic entry stage for a Factory run. It determines whether durable intent is ready before either a new run or a replacement run is created.

Intent Preflight operates on an already resolved **logical Factory request**. The caller decides which request is authoritative before invoking preflight:

```text
new run
    -> materialized current user request

replacement run
    -> resolved replacement request
```

The current visible user message is therefore not automatically the semantic request for every preflight. A control command such as `restart Factory` may select replacement-run mode without itself becoming the replacement request.

## Modes

### New-run mode

New-run mode applies only when the launcher is creating a Factory run and no active run exists. The launcher is responsible for checking existing-run state before invoking this mode.

The logical Factory request is the current user request after lossless materialization of user-supplied pasted or attached content that belongs to that request.

### Replacement-run mode

Replacement-run mode applies only after the user explicitly chooses restart and the launcher has already resolved one complete self-contained replacement request.

The presence of `.idd/factory/current/state.json` is expected in replacement-run mode and must not make Intent Preflight route back to existing-run handling. Preflight does not cancel, archive, migrate, deserialize, or otherwise mutate the existing Factory run.

Replacement-run mode uses exactly the same semantic classification, intent-update, and coverage-validation rules as new-run mode. The only difference is the source and lifecycle of the logical Factory request.

If replacement-run preflight blocks or fails, the existing run remains untouched and no restart/cancel operation is implied.

## Inputs

Intent Preflight receives:

- the complete logical Factory request selected by the caller;
- current repository state;
- current durable intent under `.idd/intent/`;
- any exact pasted/attached content already materialized as part of that logical request;
- repository-local IDD configuration relevant to intent validation.

Treat the supplied logical Factory request as authoritative for preflight. Do not substitute chat memory, planner output, work-item history, executor output, legacy semantic state, or a control command for that request.

Transport resolution is lossless preparation, not semantic interpretation. When the logical request refers to user-supplied pasted or attached text that is part of the request, resolve the exact supplied text before classification. Do not summarize, paraphrase, reconstruct, or silently omit it.

## Classification

Classify the logical Factory request before creating or replacing a Factory run.

### `IntentChanging`

Use when fulfilling the request would add, remove, or change durable product truth, including product behavior, public contracts, compatibility promises, user-visible semantics, long-lived architectural boundaries, or explicit durable constraints.

Required result before Factory starts:

1. Update current durable intent using the normal IDD intent workflow.
2. Validate the updated intent mechanically.
3. Re-evaluate coverage against the same logical Factory request.
4. Start Factory only when the current intent explicitly covers the changed truth.

### `IntentPreserving`

Use when the request changes implementation while preserving existing durable product truth, for example a localized refactoring, bug fix whose expected behavior is already explicit, test addition that does not alter product semantics, or mechanical implementation work under an existing contract.

Do not create speculative intent merely because Factory will modify code. Existing durable intent must still be sufficient to constrain the requested work where durable truth is relevant.

### `Ambiguous`

Use when it is not safe to determine whether the request changes durable truth or when the intended durable semantics are insufficiently specified.

Do not create or replace a Factory run. Ask the user the smallest question needed to resolve the ambiguity, or use the ordinary IDD clarification workflow where appropriate.

## Coverage validation

After classification and any required intent update, validate coverage against the exact same logical Factory request that will be passed to Runtime.

Coverage is semantic, not keyword-based. The goal is to establish that every durable product truth needed to constrain the requested implementation is represented in current intent.

For `IntentChanging`, coverage must include the changed durable truth before Factory starts.

For `IntentPreserving`, coverage must be sufficient to keep implementation within current durable truth. Do not invent missing product semantics merely to make preflight pass.

If coverage cannot be established without a user decision, stop before Factory state mutation and request that decision.

## Request identity

Preflight and Runtime must operate on the same logical Factory request.

- For a new run, the materialized current user request that passed preflight is passed unchanged to `factory_run`.
- For a replacement run, the resolved replacement request that passed preflight is passed unchanged to `factory_restart`.
- Do not classify one request and invoke Runtime with another.
- Do not append launcher-only operational wording after preflight.
- Do not semantic-merge a persisted request with a partial restart delta inside Intent Preflight.

The launcher, not Intent Preflight, is responsible for resolving whether a replacement request comes from an explicitly supplied complete request or persisted `.idd/factory/current/request.md`.

## Existing runs and continuation

Ordinary `factory_continue` does not repeat initial Intent Preflight. The existing run already owns its persisted request and workflow state.

When a planner question is answered during continuation, apply the normal IDD rule to the answer itself: if the answer changes durable product truth, update and validate intent before continuing; if it is only a local implementation decision, do not write durable intent.

An explicit run-level restart is not ordinary continuation. It resolves a replacement request and invokes this contract in replacement-run mode before `factory_restart`.

## Mechanical validation

Intent Preflight may use deterministic checks to validate intent structure, canonical IDs, document shape, or other repository-defined lint rules. Mechanical code should enforce mechanical invariants; it must not replace semantic classification or semantic coverage judgment.

Conversely, do not spend semantic reasoning on facts that deterministic code can establish reliably.

## Evidence

Keep the preflight result concise and tied to the logical Factory request. A useful internal record contains:

- mode: `new-run` or `replacement-run`;
- classification: `IntentChanging`, `IntentPreserving`, or `Ambiguous`;
- relevant current intent IDs;
- whether intent was updated;
- validation result;
- coverage conclusion;
- source: `logical-factory-request`.

Do not persist a second semantic copy of the request merely for preflight evidence. Factory Runtime remains responsible for its own authoritative `request.md` once the run is created.

## Non-goals

Intent Preflight does not:

- create, continue, restart, cancel, or archive Factory state;
- resolve a replacement request from legacy semantic fields;
- migrate legacy Factory state;
- reconstruct a request from planner/work-item/executor history;
- merge an old request with an incomplete semantic delta;
- move semantic preflight into Runtime;
- change executor Technical Restart semantics.