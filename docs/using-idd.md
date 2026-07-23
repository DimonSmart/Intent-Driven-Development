# Using IDD

Intent-Driven Development separates durable product intent from temporary implementation work.

Use `idd-intent` for the normal workflow. Add optional `idd-factory` only when implementation benefits from explicit multi-step orchestration.

## Choose a Starting Path

- [Existing project](existing-project.md): import current product knowledge from documentation, tests, public behavior, and confirmed requirements.
- [New project](new-project.md): turn an informal idea into the first current intent and implement the smallest useful slice.
- [Large implementation task](factory-workflow.md): let Factory complete a coordinated multi-stage task with independent review.

## Describe Requests Naturally

You do not need JSON or a formal command structure. Name a skill when you want a specific workflow, or describe the request naturally and let IDD choose the smallest safe path.

To inspect routing without changing files:

```text
idd-route
```

## Clarify Product Intent

Use when the product behavior or boundaries are not yet clear:

```text
Use idd-intent-brainstorm to clarify this feature before changing product intent.
```

The result should clarify product meaning, users, outcomes, boundaries, constraints, and unresolved decisions. It should not become an implementation plan.

## Add, Change, or Remove Product Behavior

```text
Use idd-intent-change. Users must be able to compare two local folders without modifying either side.
```

The workflow updates the current owning intent document. It creates a new document only when no current document owns the product area.

## Import an Existing Product

```text
Use idd-intent-import to propose current product intent from ./docs, relevant tests, the public API, and confirmed application behavior.
```

Import extracts durable behavior and constraints. It does not treat every old document or implementation detail as current truth.

[Read the existing-project guide](existing-project.md)

## Implement from Current Intent

For focused implementation:

```text
Use idd-code-implement for the folder comparison behavior.
```

The workflow reads relevant intent, inspects the affected code, implements the change, and verifies the result.

## Verify an Existing Implementation

```text
Use idd-code-check-implementation for the comparison workflow.
```

Use this after refactoring, agent-generated changes, migrations, or when implementation and intent may have diverged.

## Update Intent from Confirmed Behavior

When the implementation contains behavior that has been explicitly confirmed as product truth:

```text
Use idd-code-update-intent for the confirmed retry behavior.
```

Do not promote accidental implementation details into requirements.

## Audit and Normalize Intent

Diagnostic review without edits:

```text
idd-intent-audit
```

Mechanical consistency checks:

```text
idd-intent-lint
```

Focused structural cleanup without changing product meaning:

```text
idd-intent-normalize-current
```

## Use Factory for Larger Work

Install optional `idd-factory`, then provide the complete task once:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Factory completes the requested work, decomposing it when useful and applying independent review before finalization.

A normal run continues automatically. Only after an unexpected interruption use:

```text
Continue the current IDD Factory work.
```

[Read the Factory workflow guide](factory-workflow.md)  
[See the Factory skills reference](factory-skills.md)

## Skip IDD Deliberately

When a request must be performed without IDD routing or durable intent changes:

```text
idd-skip
```

This is an explicit escape hatch, not the default workflow.

## Core Rule

Keep durable product truth in `.idd/intent/`.

Keep plans, task states, reviews, implementation attempts, and Factory execution data temporary.

Let Git preserve history.
