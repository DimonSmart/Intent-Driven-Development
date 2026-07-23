# IDD Use Cases

Intent-Driven Development separates durable product intent from temporary implementation work.

Use `idd-intent` for the normal workflow. Add optional `idd-factory` only when implementation benefits from explicit multi-step orchestration and independent review.

## Find the Right Action

| Situation | What to do |
| --- | --- |
| An existing repository does not use IDD yet | Run `idd-project-init`, then import confirmed product knowledge with `idd-intent-import`. |
| You are starting from an idea | Run `idd-project-init`, then clarify the first product behavior with `idd-intent-brainstorm`. |
| The requested feature is still unclear | Use `idd-intent-brainstorm` before changing intent or code. |
| Product behavior must be added, changed, or removed | Use `idd-intent-change`, then implement the updated intent. |
| Current intent is already correct and only code must change | Use `idd-code-implement`. |
| You need to check whether code still matches intent | Use `idd-code-check-implementation`. |
| Existing behavior has been confirmed as product truth but is missing from intent | Use `idd-code-update-intent`. |
| Intent documents need review or cleanup | Use `idd-intent-audit`, `idd-intent-lint`, or `idd-intent-normalize-current`. |
| The implementation task is large or naturally multi-stage | Install `idd-factory` and use `idd-factory-run`. |
| A new IDD release is available | Follow [Updating IDD](updating-idd.md), then start a new session. |
| The request must deliberately bypass IDD | Use `idd-skip`. |

## Initialize a Repository

Open the target repository and run:

```text
idd-project-init
```

The workflow creates the minimal project-owned IDD structure and records the integration in the active agent instructions. It does not copy plugin skills into the project.

Installation and initialization commands are in the [README Quick Start](../README.md#quick-start). To confirm setup, see [Verify Installation](verify-installation.md).

## Import an Existing Product

Use existing documentation, relevant tests, the public API, and confirmed behavior as evidence:

```text
Use idd-intent-import to propose current product intent from ./docs, relevant tests, the public API, and confirmed application behavior.
```

Import extracts durable behavior and constraints. It does not treat every old document or implementation detail as current product truth.

[Read the existing-project guide](existing-project.md)

## Start a New Product

Clarify the product before creating unnecessary structure:

```text
Use idd-intent-brainstorm to clarify the first useful product behavior.
```

After the intent is clear, record it and implement the smallest useful slice.

[Read the new-project guide](new-project.md)

## Clarify Product Intent

Use when product behavior, boundaries, constraints, or expected outcomes are not yet clear:

```text
Use idd-intent-brainstorm to clarify this feature before changing product intent.
```

The result should clarify product meaning rather than produce an implementation plan.

## Add, Change, or Remove Product Behavior

Describe the product change rather than expected code edits:

```text
Use idd-intent-change. Users must be able to compare two local folders without modifying either side.
```

The workflow updates the current owning intent document. It creates a new document only when no current document owns the product area.

## Implement from Current Intent

For focused implementation when current intent is already correct:

```text
Use idd-code-implement for the folder comparison behavior.
```

The workflow reads relevant intent, inspects the affected code, implements the change, and verifies the result.

## Verify an Existing Implementation

Use after refactoring, agent-generated changes, migrations, or whenever implementation and intent may have diverged:

```text
Use idd-code-check-implementation for the comparison workflow.
```

## Update Intent from Confirmed Behavior

When existing implementation behavior has been explicitly confirmed as product truth:

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

## Update IDD

IDD changes regularly. Follow [Updating IDD](updating-idd.md) to refresh the marketplace, update the installed plugins, verify the versions, and load the update in a new session.

## Inspect Routing

You can describe requests naturally and let IDD choose the smallest safe workflow. To inspect the selected route without changing files:

```text
idd-route
```

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
