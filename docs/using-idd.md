# Using IDD

Intent-Driven Development separates two kinds of work:

- durable product intent that must survive implementation changes;
- temporary work used to produce or verify one implementation.

The plugin boundary mirrors that lifecycle:

```text
idd-intent    durable product memory
idd-factory   temporary implementation organization
```

Install `idd-intent` for the normal IDD workflow. Install `idd-factory` only when a change needs explicit multi-step orchestration.

## Route an IDD Request

You can describe an IDD request in natural language. When no skill is named
explicitly, IDD uses `idd-route` to classify the request and choose the smallest
safe workflow.

The route separates what changes from how deep execution needs to be:

- product changes use `add`, `modify`, or `remove`, then update intent before
  implementation;
- implementation-only work preserves current intent;
- intent normalization changes structure without changing product meaning;
- focused execution uses one implementation workflow;
- orchestrated execution can use optional Factory for multi-step work.

The route also records the requested scope for the current user request:

- `route-only` — describe the route and stop;
- `intent-only` — perform only intent-side work and do not implement product
  code;
- `implementation-only` — implement or check from current intent without
  changing intent;
- `end-to-end` — continue through all requested intent, implementation, and
  conformance stages.

The expected complete workflow describes the safe lifecycle, not permission to
perform every stage immediately. For example, "update the specification only"
must stop before implementation even when the eventual lifecycle also requires
code and conformance checking.

An explicitly named skill bypasses Router. `idd-skip` is never selected
automatically.

For the canonical workflow reference, see
[`src/canonical/methodology/common-workflows.md`](../src/canonical/methodology/common-workflows.md).

## Start a New Product Area

Begin by clarifying what the product must do:

```text
Use idd-intent-brainstorm to clarify this feature before we change product intent.
```

When the product area is understood, create or update the owning intent document:

```text
Use idd-intent-change to record the requested behavior in current product intent.
```

IDD prefers updating an existing owning specification. It creates a new document only when no current document owns the area or when a genuinely new durable owner is needed.

## Import an Existing Product

Use existing documentation, source code, tests, and confirmed behavior as evidence:

```text
Use idd-intent-import to propose durable product intent from the current repository.
```

Import is not a request to copy implementation details into specifications. It extracts stable behavior, constraints, decisions, and verification rules while leaving plans, statuses, and local implementation mechanics out.

## Change Product Behavior

Describe the product change rather than the edits you expect:

```text
Use idd-intent-change. Users must be able to compare two local folders without modifying either side.
```

The intent workflow determines which current document owns the behavior and updates that document. Git preserves the history; current intent stays current.

## Implement from Intent

For focused implementation:

```text
Use idd-code-implement for the folder comparison behavior.
```

The workflow reads relevant intent, inspects the repository, implements the change, and verifies the result against the specification.

## Check an Existing Implementation

To review whether code still matches the product definition:

```text
Use idd-code-check-implementation for the comparison workflow.
```

This is useful after refactoring, agent-generated changes, or migration to a different architecture.

## Update Intent from Confirmed Behavior

Sometimes the implementation contains confirmed product behavior that the intent does not yet capture:

```text
Use idd-code-update-intent for the confirmed retry behavior.
```

This workflow should update intent only from behavior that has been explicitly confirmed as product truth. Accidental implementation details must not become requirements.

## Audit and Normalize Intent

For a diagnostic review without edits:

```text
idd-intent-audit
```

For mechanical consistency checks:

```text
idd-intent-lint
```

For focused structural cleanup without changing product meaning:

```text
idd-intent-normalize-current
```

## Use Factory for Larger Work

First install the optional `idd-factory` plugin. It depends on `idd-intent`.
The public entry point coordinates decomposition, sequential implementation,
independent task reviews, final review, and safe finalization:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Use Factory when the implementation requires an explicit multi-step plan or multiple review stages. Factory may consume product intent, but it must not invent or silently modify it.

When Factory discovers missing or contradictory intent, it must stop and route the work back to an `idd-intent` workflow.

Factory keeps one active workspace:

```text
.idd/factory/
  .gitignore
  current/
    request.md
    001-first-outcome.completed.md
    002-next-outcome.active.md
    003-final-outcome.ready.md
  results/
```

Tasks are a flat, gap-free numbered sequence. Their `.ready.md`, `.active.md`,
`.completed.md`, or `.blocked.md` suffix is the only status source. At most one
task is active or blocked. A new request never replaces a nonempty `current/`;
the user must explicitly continue or cancel the existing run.

To resume after interruption:

```text
Continue the current IDD Factory work.
```

Factory validates the files and diff before continuing. It reviews each task
in an independent context, performs a final integration review, and writes a
compact Git-compatible handoff to
`.idd/factory/results/<work-slug>/commit-message.md` before clearing
`current/`. The `current/` and `results/` directories are ignored by default.
They are temporary execution artifacts, not product intent, and Factory never
commits or deletes result handoffs automatically.

## Skip IDD Deliberately

For a request that must be performed without IDD routing or durable intent changes:

```text
idd-skip
```

This escape hatch is provided by `idd-intent`. It is explicit, not the default workflow.
