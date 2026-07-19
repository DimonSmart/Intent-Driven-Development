# Common End-to-End Workflows

## Purpose

This document defines platform-independent IDD workflow routing. It describes
how natural-language requests move through intent, implementation, checking,
normalization, and optional Factory orchestration without making the route
itself durable product intent.

## Routing Dimensions

### What Changes

Classify the request by the thing that changes:

- `product truth`: desired product behavior, constraint, acceptance rule,
  public contract, or durable architecture changes.
- `implementation only`: code structure changes while current product intent
  remains unchanged.
- `intent structure`: current intent is moved, split, merged, renamed, or
  cross-referenced without changing product meaning.
- `implementation versus intent`: observed behavior may not match current
  intent.
- `raw imported knowledge`: external or existing product knowledge needs to be
  imported into IDD intent.
- `project initialization`: the project needs an `.idd/intent/` structure.
- `unknown`: the request does not provide enough information to choose safely.

### Product Operation

For product truth changes, classify the requested operation separately from
document ownership:

- `add`: introduce behavior, a constraint, an interaction, or a product rule.
- `modify`: change existing behavior, constraints, acceptance criteria, or
  product rules.
- `remove`: remove existing behavior, a capability, a contract, or a product
  rule.

Adding behavior can still update an existing spec. Removing behavior can still
leave the owning spec in place when it contains other current intent.

### Request Clarity

Classify clarity as:

- `clear`: the desired product or implementation outcome is actionable.
- `ambiguous`: a product decision is needed before writing intent or code.
- `research-required`: the correct decision depends on investigation that
  should be represented as a spike or focused check.

### Requested Scope

Classify how much of the workflow the user authorizes in the current request:

- `route-only`: classify and describe the workflow without invoking another
  skill or changing files.
- `intent-only`: perform only intent-side work. Do not implement product code or
  create a Factory Work Plan.
- `implementation-only`: implement or check against current intent without
  changing product intent.
- `end-to-end`: continue through all requested intent, implementation, and
  conformance stages.

Requested scope is independent from what changes and from execution depth. Use
the narrowest scope that satisfies the explicit request. Explicit limits such
as "only", "do not change files", "do not implement", and "do not change
specs" take precedence over the normal complete lifecycle.

The complete lifecycle describes what is eventually needed for safe delivery.
It does not grant permission to execute stages outside the requested scope.

Do not assign requested scope when an explicitly named skill or `idd-skip`
bypasses routing.

### Execution Depth

Classify execution depth independently from the product operation and requested
scope:

- `focused`: one primary product owner, localized implementation, no complex
  migration, no staged rollout, and no multiple review gates.
- `orchestrated`: multiple subsystems, independent implementation tasks,
  migration, compatibility transition, public contract change, high regression
  risk, sequenced phases, multiple roles, review gates, or major capability
  removal.
- `not-applicable`: routing, audits, lint checks, brainstorms, and pure intent
  reads that do not execute implementation.

Diff size alone is not enough to choose Factory. Execution depth may describe a
later implementation stage even when the current requested scope stops at
routing or intent work.

## Shared Invariants

- Current numbered documents directly under `.idd/intent/` are normative
  product intent.
- Git stores history.
- Add, modify, and remove apply only to product truth changes.
- Implementation-only refactoring does not change product truth.
- Intent normalization does not change product meaning.
- Implementation evidence is not product intent by itself.
- Factory may read intent, but must not create or change product intent.
- Plans, route classifications, preservation records, and review notes are
  temporary workflow evidence.
- Obsolete ordinary specs are deleted, not archived.
- A new spec is created only when no current owner exists.
- Every executed workflow stage is checked against current intent where
  applicable.
- An expected complete workflow must never be interpreted as permission to
  exceed the current requested scope.

## Workflow Family: Product Change

Product changes use `idd-intent-change` first. The ownership outcome is decided
there: existing spec update, new spec required, ADR required, spike required,
delete owning spec, or unclear product intent.

### Add Behavior

```text
brainstorm if the request is unclear
-> idd-intent-change(operation: add)
-> existing owner or new document handoff
-> idd-code-implement or Factory
-> idd-code-check-implementation
```

### Modify Behavior

```text
find current owner
-> idd-intent-change(operation: modify)
-> identify changed and preserved behavior
-> idd-code-implement or Factory
-> idd-code-check-implementation
```

### Remove Behavior

```text
find owner and dependent scenarios
-> idd-intent-change(operation: remove)
-> identify immediate removal, transition, or replacement
-> remove obsolete intent
-> remove or change implementation
-> verify removed behavior is absent
-> verify dependent scenarios remain preserved
-> run idd-intent-lint when the document set changes
```

When removing behavior, check references from other specs, shared contracts,
public APIs, data formats, saved settings, migration or deprecation
requirements, remaining intent in the owning document, and whether a durable
non-goal is needed.

For `intent-only`, stop after the intent-side stage and report the remaining
complete lifecycle without starting implementation. For `route-only`, do not
start the product-change workflow at all.

## Workflow Family: Implementation Change

### Refactor While Preserving Behavior

```text
read relevant intent
-> identify preservation boundary
-> idd-code-implement(mode: preserve-current-intent) or Factory
-> verification
-> idd-code-check-implementation
```

If implementation work reveals that product behavior must change, stop the
implementation workflow and route to `idd-intent-change`. Do not silently
expand an `implementation-only` request into an intent change.

## Workflow Family: Intent Maintenance

### Normalize Current Intent

```text
idd-intent-audit if the problem is broad
-> choose a concrete focus
-> idd-intent-normalize-current --mode propose
-> check for semantic movement
-> idd-intent-normalize-current --mode apply
-> idd-intent-lint
```

Normalization may change ownership, location, grouping, references, and
document boundaries. It must not change product behavior, constraints,
exceptions, acceptance criteria, compatibility contracts, or non-goals.

## Bug and Mismatch Entry Points

Bug reports route by the relationship between implementation and current
intent:

- Clear intent and violating implementation: `idd-code-check-implementation`,
  then `idd-code-implement`, then `idd-code-check-implementation`.
- Implementation matches current intent but the user wants different behavior:
  `idd-intent-change(operation: modify)`, then implementation, then check.
- Correct intent is unclear: `idd-code-check-implementation`, then
  `idd-intent-brainstorm` or a spike.

A bug is not a separate top-level workflow family.

## Focused and Orchestrated Execution

Use focused execution when one implementation pass can safely satisfy current
intent. Use Factory only for coordinated multi-task implementation, sequencing,
temporary planning, review gates, or high-risk preservation boundaries. Factory
remains optional and must not become a dependency of `idd-intent`.

Do not create or execute Factory work when requested scope is `route-only` or
`intent-only`. For `implementation-only`, Factory may be used only when current
intent is already sufficient and execution is orchestrated.

## Preservation Boundary

Each workflow should identify temporary preservation evidence:

- Behavior expected to change.
- Behavior expected to remain unchanged.
- Public contracts to preserve.
- Compatibility or data constraints.
- Unresolved preservation questions.

The boundary is not saved as a standalone `.idd/intent/` document. Durable
preserved behavior belongs in ordinary behavior, acceptance criteria,
constraints, verification, or non-goals of the owning current spec.

## Complete Workflow and Current Handoff

Routing must distinguish:

- the `Expected complete workflow`, which describes the safe lifecycle;
- the `Current handoff`, which is allowed in the current request;
- the `Stop after` boundary, which follows requested scope and clarity gates.

For `route-only`, the current handoff is none. For `intent-only`, stop before
implementation. For `implementation-only`, do not modify intent. For
`end-to-end`, continue through the complete workflow unless ambiguity, required
research, missing intent, verification failure, or another safety gate blocks
progress.

## Workflow Completion Rules

Complete the current request when all stages inside its requested scope have
finished and their applicable checks pass. Do not claim that the complete
product lifecycle has finished when the request intentionally stopped at
routing or intent work.

For `end-to-end`, product changes complete after intent is updated,
implementation is performed, and `idd-code-check-implementation` verifies
changed, removed, and preserved behavior. Implementation-only work completes
after verification proves current intent was preserved. Normalization completes
after semantic movement is checked and `idd-intent-lint` passes.

If `idd-intent-lint` reports errors, the normalization workflow is not complete.
Fix the errors or report them explicitly as unresolved blockers. Do not present
normalization as completed while mechanical consistency errors remain. Warnings
may remain only when they do not indicate mechanical inconsistency and are
explicitly reflected in the report.
