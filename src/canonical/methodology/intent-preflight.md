# Intent Preflight

Intent Preflight is the bounded entry stage before a new Factory request or an
explicit replacement request starts implementation. It prepares durable product
truth; it is not a Factory task and it does not implement a workflow runtime.

## Logical request

Resolve one complete self-contained logical request before preflight.

Preserve user-authored request text and explicitly supplied textual inputs
losslessly. Host-local attachment or pasted-file references are transport
metadata, not durable semantics. Materialize the supplied text so later
continuation does not depend on temporary host attachments.

Do not substitute a route summary, implementation plan, planner result, old
worker result, or restart control command for the logical request.

## Modes

New:
- no simplified current Factory run is being continued;
- run preflight on the materialized current request;
- create Factory temporary state only after preflight succeeds.

Replacement:
- resolve one complete self-contained replacement request first;
- run the same intent classification and coverage rules against it;
- do not archive or replace the previous temporary Factory state until preflight
  succeeds.

Continuation of an existing simplified run does not repeat initial preflight.

A legacy `.idd/factory/current/state.json` is not an active state-machine
contract. Do not interpret or migrate it. An explicit restart may reuse a valid
persisted `request.md` as the complete replacement request.

## Requested scope

- `route-only`: describe the route; do not prepare intent or start Factory.
- `intent-only`: prepare and validate intent; do not start implementation.
- `implementation-only`: never change `.idd/intent`; proceed only when current
  intent already determines the requested product behavior.
- `end-to-end`: prepare intent when necessary, validate it, then start Factory.

Explicit scope limits win.

## Classification

Classify the logical request against current durable intent as one of:

### AlreadyCovered

Current intent already determines the requested product behavior. Do not write
intent.

### ExplicitIntentChange

The request clearly defines new or changed durable product behavior and contains
the decisions needed to record it safely. Update the smallest owning current
intent documents before implementation.

### MissingIntentDecision

Safe implementation requires a durable product decision that is not determined
by the request or current intent. Ask the user. Do not guess or turn the missing
decision into a Factory task.

### ImplementationOnly

The request changes implementation without changing product truth, such as a
refactor, cleanup, dependency update, or a bug fix whose expected behavior is
already established. Do not write intent.

Technical uncertainty is not automatically missing intent. When product
semantics are known but the implementation approach needs investigation,
preflight succeeds and Factory can plan that implementation/research work.

## Intent discovery and update

Start from:

```text
.idd/intent/README.md
-> .idd/intent/INDEX.md
-> plausible current IDD documents
```

Read only the current documents needed to classify and update the request.
Prefer modifying an existing owner over creating a new document. Keep transient
plans, task status, implementation detail, and execution evidence out of intent.

After an intent write, validate coverage against the same logical request that
Factory will receive. Ensure the requested durable behavior and material
constraints are represented without creating contradictions.

## Request identity

The exact self-contained request that passed preflight becomes
`.idd/factory/current/request.md`. Do not classify one request and execute
another. Do not append launcher-only operational instructions to the persisted
request.

## Planner questions

A later fresh planner may return one `# Question` only when no next
implementation task should run before a user decision.

When the user answers:

1. treat the exact answer as semantic input;
2. use the normal IDD intent workflow if it adds or changes durable product
   truth and scope permits intent writes;
3. if durable intent must change but scope forbids it, keep Factory stopped;
4. if it is only an implementation choice, do not write durable intent;
5. after any required intent update, append the exact question and answer to
   `answers.md`, remove `question.md`, and invoke a fresh planner.

Do not continue the old planner context.

## Boundary

Intent Preflight owns semantic preparation of durable intent. Factory owns only
lightweight temporary orchestration after that preparation. Neither layer
reconstructs a deterministic runtime, process lifecycle, retry protocol, or
state-machine transition model.
