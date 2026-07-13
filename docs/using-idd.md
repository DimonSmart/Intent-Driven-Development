# Using IDD

Intent-Driven Development separates two kinds of work:

- durable product intent that must survive implementation changes;
- temporary work used to produce or verify one implementation.

The `idd` plugin provides workflows for both while keeping the boundary explicit.

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

Factory coordinates temporary planning, implementation, and review:

```text
idd-factory-create-work-plan
idd-factory-execute-work-plan
idd-factory-review-work-result
idd-factory-finish-work
```

Use Factory when the implementation requires an explicit multi-step plan or multiple review stages. Factory may consume product intent, but it must not invent or silently modify it.

When Factory discovers missing or contradictory intent, it should stop and route the work back to an intent workflow.

## Skip IDD Deliberately

For a request that must be performed without IDD routing or durable intent changes:

```text
idd-skip
```

This is an explicit escape hatch, not the default workflow.

## What Belongs in Intent

Keep:

- product behavior;
- user scenarios;
- domain contracts;
- accepted architecture decisions;
- important constraints and non-goals;
- acceptance criteria and verification rules.

Do not keep:

- task lists;
- implementation plans;
- progress and status notes;
- review notes;
- chat summaries;
- one-off delivery files;
- commands that merely describe the current toolchain.

For the underlying principles, see [Methodology](methodology.md).
