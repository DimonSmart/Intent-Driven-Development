# Intent Preflight

Intent Preflight remains the Product-specific semantic analysis used by the
Factory entry workflow. For a new Factory run, the entry workflow wraps it in a
broader durable preflight that may also coordinate explicit Engineering
management before active Factory state exists.

Factory preflight is not a planner task, worker task, runtime, state machine, or
transaction subsystem.

## Logical request

Resolve one complete self-contained logical request before semantic analysis.

Preserve user-authored request text and explicitly supplied textual inputs
losslessly. Materialize pasted text, attachments whose textual content is part of
the request, and request-local textual documents that the user explicitly cites
as part of the task. Host-local paths and attachment identifiers are transport
metadata, not durable semantics.

The materialized request is authoritative. Do not substitute a route summary,
generated summary, implementation plan, planner result, Engineering summary,
Intent diff, or extracted bullet list.

The same complete logical request is used for Product analysis, Engineering
handoff, durable coverage validation, and eventual
`.idd/factory/current/request.md`.

## Modes

New:
- no active simplified Factory run is being continued;
- run durable preflight on the materialized current request;
- create Factory temporary state only after durable preparation and coverage
  validation succeed.

Replacement:
- resolve one complete self-contained replacement request first;
- leave the existing active Factory state untouched during preflight;
- if Engineering management is required, block the replacement in this
  iteration before any durable write;
- otherwise keep existing replacement Product Intent semantics and replace
  temporary state only after successful preflight and coverage validation.

Continue:
- do not repeat initial preflight.

A legacy `.idd/factory/current/state.json` is not an active state-machine
contract. Do not interpret or migrate it. The active simplified-run marker is
`.idd/factory/current/request.md`.

## Requested scope

- `route-only`: describe the route; do not mutate Product Intent or
  Engineering and do not start Factory.
- `intent-only`: perform only durable knowledge-side work. Product Intent
  preparation and Engineering management are both allowed when the request
  requires them; implementation and Factory orchestration do not start.
- `implementation-only`: never mutate `.idd/intent/*` or
  `.idd/engineering/*`. Proceed only when the request needs no durable change.
  If either durable layer must change, stop and report the required workflow.
- `end-to-end`: prepare required durable knowledge, validate it, then start
  implementation/Factory when no blocker remains.

Explicit scope limits win.

## Product Intent classification

Classify the complete logical request against current durable Product Intent as
one of:

### AlreadyCovered

Current Intent already determines the requested durable product behavior. Do not
write Product Intent.

### ExplicitIntentChange

The request explicitly defines new or changed durable product behavior and
contains enough decisions to record it safely. Resolve the safe current owner or
normal `idd-intent-new-document` handoff during analysis, but defer the actual
Product Intent mutation until required Engineering management has completed as
`success` or `no-op`.

Product Intent mutation remains owned by the normal Intent workflows, primarily
`idd-intent-change`.

### MissingIntentDecision

Safe implementation requires a durable product decision that is not determined
by the request or current Intent. Stop before durable writes and ask the minimum
question. Do not guess or turn the decision into a Factory task.

### ImplementationOnly

The request changes implementation without changing product truth. Do not write
Product Intent.

Technical uncertainty is not automatically missing Intent. When product
semantics are known but implementation needs investigation, durable preflight
may succeed and Factory can plan implementation/research work later.

## Factory-level Engineering disposition

Factory preflight does not duplicate the semantic planning already owned by
`idd-engineering-change`. At entry level determine only whether Engineering
management is a separate durable concern.

### NoEngineeringManagementNeeded

The request contains no explicit new/changed/removed durable
implementation-only project decision.

Ordinary implementation details, task-local technical suggestions, temporary
migration instructions, class/file names, package suggestions, methods, DI
wiring, implementation order, test steps, and research hypotheses do not by
themselves create Engineering Rules.

### EngineeringManagementRequired

The request explicitly contains one or more durable implementation-only project
decisions.

Invoke normal `idd-engineering-change` with the complete logical request, not
a lossy Engineering-only summary. That workflow alone owns semantic grouping,
current owner resolution, `new`, equivalence, modify, add/remove/no-op
planning, allocator handling, mutation, and Mechanical Engineering Validation.

Handle its result exactly:

```text
success   -> durable Engineering prepared; continue
no-op     -> durable Engineering already satisfied; continue
ambiguous -> stop before active Factory state
blocked   -> stop before active Factory state
```

Do not introduce a Factory-level `EngineeringAlreadyCovered`. If an explicit
decision is already represented, `idd-engineering-change` is responsible for
returning `no-op`.

### EngineeringDecisionMissing

Correct continuation requires a durable Engineering decision, but the complete
request does not actually make that decision. Stop before durable writes and ask
the minimum question. Factory must not silently choose a project-wide policy.

## Engineering boundary and discovery

Use the established Engineering boundary:

> Could another implementation completely preserve the product contract and
> still violate this constraint?

If yes, the constraint may be Engineering when the request explicitly makes it a
durable project decision.

When `.idd/engineering/` exists, Factory-level applicability analysis may read
README, INDEX, and only genuinely relevant Rules needed to understand the
boundary. Do not use that reading to duplicate owner mapping, duplicate
detection, allocator logic, or semantic equivalence.

If the layer is absent, that is valid. Do not warn and do not create it merely
because Factory starts. Only `idd-engineering-change` may lazily bootstrap the
layer when its own semantic plan contains a real new Rule.

Do not replace semantic Engineering decisions with filename, keyword, extension,
project-type, embedding, similarity, or deterministic technology heuristics.

## Request-level blockers before durable writes

Before invoking any durable mutation owner, detect every blocker that can be
determined directly from the complete request and requested scope, including:

- `MissingIntentDecision`;
- `EngineeringDecisionMissing`;
- mutually exclusive requirements inside the request;
- forbidden durable mutation under the requested scope;
- inability to materialize the complete logical request;
- obviously malformed request-local source required to make the request
  complete;
- Engineering-changing replacement while
  `.idd/factory/current/request.md` exists.

A request-level blocker means:

```text
no Product Intent mutation
no Engineering mutation
no new Factory state
```

## New-run ordering

For a new run, execute durable preflight in this order:

```text
1. materialize complete logical request
2. resolve requested scope
3. non-mutating Product Intent analysis
4. Factory-level Engineering disposition
5. detect request-level blockers

6. when EngineeringManagementRequired:
       idd-engineering-change(complete logical request)

7. require Engineering result success | no-op
   ambiguous | blocked -> stop

8. perform required Product Intent mutation through existing Intent workflows
9. validate durable coverage against the complete logical request
10. only then create .idd/factory/current/request.md
11. start a fresh planner
```

Engineering management precedes the Product Intent write because
`idd-engineering-change` contains authoritative Engineering ownership and
ambiguity planning but exposes no separate durable dry-run contract. Product
semantics are still analyzed first so the Engineering handoff sees a correctly
classified complete request.

This ordering is not a transaction. If Engineering mutates successfully and a
later Intent workflow fails mechanically, report the blocker and do not create
Factory state. Do not add rollback infrastructure.

## Durable coverage validation

After durable preparation and before active Factory state, validate against the
complete logical request.

Check at least:

- requested durable Product behavior is represented by current Product Intent;
- every explicit durable Engineering concern was handled by
  `idd-engineering-change` and ended as `success` or `no-op`;
- material safety and compatibility constraints were preserved;
- explicit non-goals were preserved;
- current durable knowledge does not contradict the authoritative request;
- implementation-only detail did not leak into Product Intent;
- task-local/temporary implementation detail did not leak into Engineering;
- Product Intent and Engineering do not contradict one another;
- no durable decision required before implementation remains unresolved;
- an existing Engineering layer remains structurally valid.

Engineering coverage validation does not repeat Engineering owner mapping or
semantic equivalence.

## Request identity

Only after successful durable preflight may a new Factory run write
`.idd/factory/current/request.md`.

Its contents are the complete self-contained logical implementation request that
was analyzed, not the updated Intent documents, Engineering summary, route
result, plan, preflight report, or short rewrite.

## Replacement semantics

A normal continuation does not repeat preflight.

For an explicit complete replacement that needs no Engineering mutation, keep
the existing replacement Product Intent flow:

```text
complete replacement request
-> replacement Product preflight
-> durable coverage validation
-> only after success replace temporary Factory state
```

Do not remove/archive the old active state before successful replacement
preflight.

If the active replacement request is `EngineeringManagementRequired`, return a
blocking diagnostic before all durable writes. The active-run guard in
`idd-engineering-change` remains authoritative. Do not bypass it, auto-cancel
the run, temporarily archive/unarchive state, or invent suspended/pending
replacement lifecycle state.

The diagnostic should explain that the current run must be completed or
cancelled, then the complete replacement request started as a new Factory run.

## Planner questions after active state exists

A later fresh planner may return one `# Question` only when no next
implementation task should run before a user decision.

When the user answers:

1. if the answer changes Product Intent, use the normal outer Product Intent
   workflow when allowed, then append the exact Q/A and start a fresh planner;
2. if it is an ordinary implementation decision, change no durable knowledge,
   append the exact Q/A, and start a fresh planner;
3. if it introduces a new durable Engineering decision, do not call
   `idd-engineering-change` while the active `request.md` exists. Keep
   Factory paused and require completion/cancellation followed by a new complete
   request that includes the original request and the Engineering decision.

Never patch a Rule directly, weaken the active-run guard, convert the Engineering
decision into a worker task, or auto-cancel/restart Factory.

## Boundary

Factory entry preflight coordinates durable preparation only before active
Factory state exists.

Product Intent workflows own Product Intent mutations.
`idd-engineering-change` owns ordinary Engineering Rule mutations.

Factory planners and workers own implementation/research against prepared
durable knowledge. They never mutate `.idd/intent/*` or
`.idd/engineering/*`.

Nothing in this contract creates a new Factory runtime, state machine,
transaction/rollback subsystem, deterministic semantic classifier, JSON schema,
standalone durable-preflight skill, or Engineering guard bypass.
