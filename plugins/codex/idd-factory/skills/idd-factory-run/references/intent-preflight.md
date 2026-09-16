# Intent Preflight

Intent Preflight is the bounded entry stage for an end-to-end Factory request.
It determines whether durable intent is ready before a new Factory run or a
replacement Factory run is created. It is not a Factory work item, a planner
phase, or a second runtime.

Intent Preflight operates on an already resolved **logical Factory request**.
The caller decides which request is authoritative before invoking preflight:

```text
new run
    -> materialized current user request

replacement run
    -> resolved replacement request
```

The current visible user message is therefore not automatically the semantic
request for every preflight. A control command such as `restart Factory` may
select replacement-run mode without itself becoming the replacement request.

The logical Factory request remains authoritative. Preserve all user-authored
request text and all explicitly supplied textual request inputs exactly. A host
may represent supplied text using a request-local attachment or pasted-file
reference; that reference is transport metadata, not durable request semantics.
Before Factory starts, mechanically materialize such supplied text into one
self-contained logical request. Do not substitute a route summary,
implementation plan, paraphrase, or semantic rewrite.

## Preflight modes

### New-run mode

New-run mode applies only when the launcher is creating a Factory run and no
active run exists. The launcher is responsible for checking
`.idd/factory/current/state.json` before invoking this mode. If active state
exists, the launcher follows existing-run rules instead of entering new-run
preflight.

The logical Factory request is the current user request after lossless
materialization of user-supplied pasted or attached text that belongs to that
request.

### Replacement-run mode

Replacement-run mode applies only after the user explicitly chooses restart and
the launcher has already resolved one complete self-contained replacement
request.

The presence of `.idd/factory/current/state.json` is expected in replacement-run
mode and must not route preflight back to existing-run handling. Intent Preflight
does not cancel, archive, migrate, deserialize, or otherwise mutate the existing
Factory run.

Replacement-run mode uses exactly the same requested-scope, classification,
intent-update, durable-normalization, and coverage-validation rules as new-run
mode. Only the source and lifecycle of the logical Factory request differ.

If replacement-run preflight blocks or fails, the existing run remains untouched
and no `factory_restart` or `factory_cancel` operation is implied.

## Inputs

Use:

- the complete logical Factory request selected by the caller;
- any pasted or attached textual content explicitly supplied by the user as part
  of that request;
- requested scope;
- `.idd/intent/README.md` and `.idd/intent/INDEX.md`;
- only current intent documents relevant to the request;
- an existing route result when one is already available.

For a new run, resolve and read request-supplied pasted or attached text when the
visible request contains only a reference to it. For a replacement run, operate
on the complete replacement request resolved by the launcher; if that complete
request itself contains user-supplied pasted or attached text, materialize that
text by the same lossless rules before classification. A reference or local path
is transport metadata, not a substitute for the supplied semantic content.

Treat the selected logical Factory request together with exact resolved supplied
content as authoritative for preflight. Do not substitute chat memory, planner
output, work-item history, executor output, legacy semantic state, or a restart
control command for that request.

Materialization is a transport normalization, not a semantic transformation:

- expand only text explicitly supplied by the user as request input; never read
  arbitrary local or repository files merely because a path appears in text;
- preserve the supplied text exactly, including whitespace and Unicode, while
  replacing host-only attachment dependence with the exact content;
- prefer the host's file/attachment reader when it provides the supplied text;
- when a local text file such as Codex `pasted-text.txt` must be decoded, use
  strict UTF-8 rather than shell-default or locale-dependent decoding;
- reject invalid Unicode or U+FFFD replacement characters instead of silently
  continuing with corrupted text;
- if supplied request text cannot be resolved losslessly, stop before runtime
  creation or replacement and report the input transport problem;
- the same materialized logical request is the semantic source for intent
  preparation and the request passed to Factory Runtime.

The resulting Factory request must be self-contained: interpreting
`request.md`, resuming the run, and performing final verification must not
require a host-local temporary attachment to still exist.

Do not read the entire intent tree by default. Missing documentation is evidence
about storage, not proof that a product decision is missing.

## Requested scope

- `route-only`: report the route; do not prepare intent or start Factory.
- `intent-only`: prepare and validate intent; do not start Factory.
- `implementation-only`: never change `.idd/intent`. Start Factory only when
  current intent is sufficient; otherwise return `INTENT_REQUIRED`.
- `end-to-end`: prepare intent when necessary, validate it, then start Factory.

Explicit scope limits override the normal meaning of an explicit
`idd-factory-run` invocation. In replacement-run mode, a scope that forbids
starting Factory also forbids invoking `factory_restart`; the existing run stays
untouched.

## Classification

Classify the logical request relation to current intent as exactly one of:
`Covered`, `ExplicitIntentChange`, `MissingIntentDecision`, or
`ImplementationOnly`.

Before choosing a relation, compare every explicit durable claim in the
materialized logical request with the relevant current intent. An explicit claim
that adds or supersedes observable behavior, a durable architecture boundary,
or a platform/safety/compatibility constraint is `ExplicitIntentChange` when
the request supplies the decision. Do not classify such a request as `Covered`
merely because the surrounding feature already has an owning specification.

### `Covered`

Current durable intent already contains enough product meaning for the request.
Do not write intent. An end-to-end or permitted implementation-only request may
start Factory immediately.

### `ExplicitIntentChange`

The request explicitly defines new or changed durable product behavior and
contains the decisions needed to record it safely. This includes clear changes
that supersede current intent.

For end-to-end or intent-only scope, invoke the existing `idd-intent-change`
workflow with the materialized logical request. That workflow may hand off to
`idd-intent-new-document` for a distinct owner, ADR, or spike.

For implementation-only scope, do not write intent; return `INTENT_REQUIRED`
because the authorized durable source remains insufficient.

### `MissingIntentDecision`

A durable product decision required for safe implementation cannot be
determined from either the logical request or current intent. Before a new or
replacement run, return `INTENT_REQUIRED` with a concise description of the
missing decisions. Do not use this result merely to ask that an already explicit
request be copied into a specification.

### `ImplementationOnly`

The request changes implementation but not product truth: for example, a
refactor, cleanup, dependency update, or a bug fix whose expected behavior is
already determined. Do not write intent. Start Factory when scope permits.

`ImplementationOnly` requires that all requested product behavior and durable
constraints are already established by current intent. It is not a fallback for
a request that introduces or supersedes durable semantics alongside
implementation detail.

## Product decisions and technical research

Technical uncertainty is not automatically missing intent. When product and
safety semantics are known but the implementation approach needs investigation,
preflight succeeds and Factory may plan implementation or research work.

Use an intent-side spike only when research is required to choose durable
product or architecture semantics themselves.

## Durable normalization

When preparing intent, retain durable truth only:

- observable behavior and interaction semantics;
- domain and public contracts;
- durable architecture boundaries;
- safety, compatibility, and platform constraints;
- non-goals, acceptance criteria, and meaningful verification scenarios.

Do not automatically retain private type or method names, source layout,
constructor signatures, dependency-injection wiring, implementation order,
temporary workarounds, task lists, build commands, test method names, progress,
or other proposed private implementation shape.

## Coverage validation

After any intent write, and before Factory creation or replacement, compare the
resulting current intent with the same materialized logical request that will be
passed to Factory. Validate that:

- the main requested behavior is owned by current intent;
- material non-goals, safety, durability, and compatibility constraints remain;
- adjacent current intent is not internally contradictory;
- no other unresolved durable decision remains;
- private implementation suggestions did not become normative product shape.

Coverage produces `Covered` or `MissingIntentDecision`. Do not defer a known
initial coverage gap to a Factory worker. If the intent workflow or coverage
check fails, do not start or replace Factory and do not create Factory work
items.

## Request identity

Preflight and Runtime must operate on the same logical Factory request.

- For a new run, the materialized current user request that passed preflight is
  passed unchanged to `factory_run`.
- For a replacement run, the resolved replacement request that passed preflight
  is passed unchanged to `factory_restart`.
- Do not classify one request and invoke Runtime with another.
- Do not append launcher-only operational wording after preflight.
- Do not semantic-merge a persisted request with a partial restart delta inside
  Intent Preflight.

The launcher, not Intent Preflight, is responsible for resolving whether a
replacement request comes from an explicitly supplied complete request or the
persisted `.idd/factory/current/request.md`.

## Evidence and recovery

Keep preflight evidence outside product specifications. Report, when available:

```text
Intent preparation:
  mode: new-run | replacement-run
  status: unchanged | updated | blocked
  beforeHash: <intent tree hash>
  afterHash: <intent tree hash>
  changedPaths: [...]
  source: logical-factory-request
```

An intent update completes before the runtime launcher is called. If the later
runtime launch or restart fails, keep the durable intent change; do not roll it
back automatically.

## Existing runs and planner questions

Do not repeat initial preflight or rematerialize host attachments for an
ordinary continue. A valid persisted run already owns a self-contained
`request.md`.

An explicit run-level restart is not ordinary continuation. The launcher first
resolves the complete replacement request, then invokes this contract in
replacement-run mode. The active state file remains expected throughout that
preflight, and the existing run remains untouched until preflight succeeds.

A running Factory may later reach `USER_DECISION_REQUIRED` only at a planning
boundary after all currently contractable work is exhausted. This is not a
worker-generated `intent-required` outcome. The planner supplies one concrete
question because no next task can be safely contracted without a user decision.

When the user answers:

1. Treat the persisted planner question plus the user's exact answer as the
   bounded semantic input for deciding whether durable intent changes.
2. If the answer adds or changes durable product truth and scope permits intent
   writes, invoke the ordinary `idd-intent-change` workflow and validate
   coverage before resuming Factory.
3. If the answer adds or changes durable product truth but scope forbids intent
   writes, do not reinterpret it as an implementation-only choice and do not
   resume Factory with that answer. Leave the run paused until an intent update
   is permitted, or cancel it explicitly.
4. If the answer is only an implementation choice, do not write durable intent.
5. After any required intent update is complete, pass the user's exact answer to
   the same Factory run through `factory_continue`. Runtime records it separately
   from immutable `request.md` and the next planner receives it as evidence.
6. If the user declines to continue, cancel the run. Do not invent an answer.

The planner does not decide whether the answer belongs in intent, executors do
not request this pause, and runtime does not interpret the answer's product
meaning. This keeps the semantic decision in IDD while runtime owns only durable
pause/resume mechanics.

## Boundaries

Intent Preflight does not create, continue, restart, cancel, archive, or migrate
Factory state. It does not resolve a replacement request from legacy semantic
fields, reconstruct a request from planner/work-item/executor history, or merge
an old request with an incomplete semantic delta. Runtime remains responsible
for authoritative Factory state transitions, and executor **Technical Restart**
semantics are unchanged.
