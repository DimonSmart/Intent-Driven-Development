# Intent Preflight

Intent Preflight is the bounded entry stage for an end-to-end Factory request.
It determines whether durable intent is ready before a new Factory run is
created. It is not a Factory work item, a planner phase, or a second runtime.

The user's logical request remains authoritative. Preserve all user-authored
request text and all explicitly supplied textual request inputs exactly. A host
may represent supplied text using a request-local attachment or pasted-file
reference; that reference is transport metadata, not durable request semantics.
Before Factory starts, mechanically materialize such supplied text into one
self-contained logical request. Do not substitute a route summary,
implementation plan, paraphrase, or semantic rewrite.

## Inputs

Use:

- the complete visible user request;
- any pasted or attached textual content explicitly supplied by the user as part
  of that request, including content that the host represents only by a
  request-local file or attachment reference;
- requested scope;
- `.idd/intent/README.md` and `.idd/intent/INDEX.md`;
- only current intent documents relevant to the request;
- an existing route result when one is already available.

Before classification, resolve and read request-supplied pasted or attached
text when the visible request contains only a reference to it. The reference or
local path is transport metadata, not a substitute for the supplied semantic
content. Treat the visible request together with that exact resolved content as
the authoritative logical request for preflight.

Materialization is a transport normalization, not a semantic transformation:

- expand only text explicitly supplied by the user as request input; never read
  arbitrary local or repository files merely because a path appears in text;
- preserve the supplied text exactly, including whitespace and Unicode, while
  replacing host-only attachment dependence with the exact content;
- prefer the host's file/attachment reader when it provides the supplied text;
  when a local text file such as Codex `pasted-text.txt` must be decoded, use
  strict UTF-8 rather than shell-default or locale-dependent decoding;
- reject invalid Unicode or U+FFFD replacement characters instead of silently
  continuing with corrupted text;
- if supplied request text cannot be resolved losslessly, stop before runtime
  creation and report the input transport problem;
- the same materialized logical request is the semantic source for intent
  preparation and the request passed to Factory Runtime.

The resulting Factory request must be self-contained: interpreting
`request.md`, resuming the run, and performing final verification must not require a
host-local temporary attachment to still exist.

Do not read the entire intent tree by default. Missing documentation is evidence
about storage, not proof that a product decision is missing.

## Requested scope

- `route-only`: report the route; do not prepare intent or start Factory.
- `intent-only`: prepare and validate intent; do not start Factory.
- `implementation-only`: never change `.idd/intent`. Start Factory only when
  current intent is sufficient; otherwise return `INTENT_REQUIRED`.
- `end-to-end`: prepare intent when necessary, validate it, then start Factory.

Explicit scope limits override the normal meaning of an explicit
`idd-factory-run` invocation.

## Classification

Classify the logical request relation to current intent as exactly one of:

Before choosing a relation, compare every explicit durable claim in the
materialized logical request with the relevant current intent. An explicit
claim that adds or supersedes observable behavior, a durable architecture
boundary, or a platform/safety/compatibility constraint is
`ExplicitIntentChange` when the request supplies the decision. Do not classify
such a request as `Covered` merely because the surrounding feature already has
an owning specification, and do not classify it as `ImplementationOnly` merely
because the request also contains concrete implementation instructions.

A concrete technical setting can be private implementation detail or durable
intent depending on context. When current intent normatively fixes that setting
as part of supported product/platform behavior and the request explicitly
changes it to enable the requested behavior, the contradiction is a durable
intent change and must be resolved before Factory starts.

### `Covered`

Current durable intent already contains enough product meaning for the request.
Do not write intent. An end-to-end or permitted implementation-only request may
start Factory immediately.

### `ExplicitIntentChange`

The request explicitly defines new or changed durable product behavior and
contains the decisions needed to record it safely. This includes clear changes
that supersede current intent. A contradiction with old intent is not a blocker
when the request unambiguously states the new authoritative behavior.

For end-to-end or intent-only scope, invoke the existing `idd-intent-change`
workflow with the materialized logical request, classification, scope, and
relevant current intent. That workflow may hand off to
`idd-intent-new-document` for a distinct owner, ADR, or spike. Do not reproduce
document-ownership or formatting logic in preflight.

For implementation-only scope, do not write intent; return `INTENT_REQUIRED`
because the authorized durable source remains insufficient.

### `MissingIntentDecision`

A durable product decision required for safe implementation cannot be
determined from either the logical request or current intent. Return
`INTENT_REQUIRED` only with a non-empty `missingIntentDecisions` payload. Each
item contains:

```text
area
whyBlocking
requiredDecisions[]
intentReferences[]
recommendedNextWorkflow?
```

Do not use this result merely to ask that an already explicit request be copied
into a specification.

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
preflight succeeds and Factory may plan a `research` work item.

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

After any intent write, and before Factory creation, compare the resulting
current intent with the same materialized logical request that will be passed to
Factory. Validate that:

- the main requested behavior is owned by current intent;
- material non-goals, safety, durability, and compatibility constraints remain;
- adjacent current intent is not internally contradictory;
- no other unresolved durable decision remains;
- private implementation suggestions did not become normative product shape.

Coverage produces `Covered` or `MissingIntentDecision`. Do not defer a coverage
gap to a Factory worker. If the intent workflow or coverage check fails, do not
start Factory or create Factory work items.

## Evidence and recovery

Keep preflight evidence outside product specifications. Report, when available:

```text
Intent preparation:
  status: unchanged | updated | blocked
  beforeHash: <intent tree hash>
  afterHash: <intent tree hash>
  changedPaths: [...]
  source: materialized-logical-user-request
```

An intent update completes before the runtime launcher is called. If the later
runtime launch fails, keep the durable intent change; do not roll it back
automatically.

## Existing runs

Do not repeat initial preflight or rematerialize host attachments for an ordinary
continue. A valid persisted run already owns a self-contained `request.md`.
Runtime-level `INTENT_REQUIRED` remains valid when planning, research,
implementation, or review discovers a genuinely missing durable decision.

When an existing run returns structured `missingIntentDecisions`, compare those
decisions with the persisted logical Factory request and current intent. If the
request already supplies them and scope permits intent writes, invoke the
existing intent workflow, validate coverage, then continue the exact persisted
Factory operation. If a decision is genuinely absent, ask the user and preserve
the run. Factory implementation and research workers never edit intent.
