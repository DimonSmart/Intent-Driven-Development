# IDD Use Cases

Intent-Driven Development separates durable product intent from temporary implementation work.

Use `idd-intent` for the normal workflow. Add optional `idd-factory` only when implementation benefits from explicit multi-step orchestration and independent review.

In Factory, the original Request defines one complete Task. A Factory run
decomposes the Task into Work items: bounded Subtasks and Review checkpoints.
The final Task review validates the complete Task result.

For project-specific commands, use `idd-verification-configure` to create `.idd/verification.yaml`. It assigns checks by `direct`, `subtask`, `checkpoint`, and `final` context without putting commands in product intent.

## Find the Right Action

| Situation | What to do |
| --- | --- |
| An existing repository does not use IDD yet | Run `idd-project-init`. It can offer interactive bootstrap when implementation exists without current intent. |
| Existing implementation has little or unreliable product documentation | Use `idd-intent-bootstrap` to discover and confirm the initial current intent model. |
| Existing documents already express current product knowledge | Use `idd-intent-import` to normalize that knowledge into IDD. |
| Project terminology is genuinely ambiguous or project-specific | Explicitly run `idd-glossary-build` to create or update the optional glossary. |
| You need to confirm that IDD is installed and initialized correctly | Follow [Verify Installation](verify-installation.md). |
| You are starting from an idea | Run `idd-project-init`, then clarify the first product behavior with `idd-intent-brainstorm`. |
| The requested feature is still unclear | Use `idd-intent-brainstorm` before changing intent or code. |
| Product behavior must be added, changed, or removed | Use `idd-intent-change`, then implement the updated intent. |
| Current intent is already correct and only code must change | Use `idd-code-implement`. |
| You need to check whether code still matches intent | Use `idd-code-check-implementation`. |
| Existing behavior has been confirmed as product truth but is missing from established intent | Use `idd-code-update-intent`. |
| Intent documents need review or cleanup | Use `idd-intent-audit`, `idd-intent-lint`, or `idd-intent-normalize-current`. |
| The implementation task is large or naturally multi-stage | Install `idd-factory` and use `idd-factory-run`. |
| A new IDD release is available | Follow [Updating IDD](updating-idd.md), then start a new session. |
| The request must deliberately bypass IDD | Use `idd-skip`. |

## Initialize a Repository

Open the target repository and run:

```text
idd-project-init
```

The workflow creates the minimal project-owned IDD structure and records the integration in the active agent instructions. It does not copy plugin skills into the project and does not create an empty glossary.

When the repository contains meaningful implementation but no current `IDD-NNNN` documents, initialization asks whether to analyze the whole repository, analyze selected product areas, or skip bootstrap.

Installation and initialization commands are in the [README Quick Start](../README.md#quick-start).

## Verify Installation

After setup, or when IDD commands are unavailable in a new session, follow [Verify Installation](verify-installation.md) to confirm the marketplace, installed plugins, and repository initialization.

## Bootstrap An Undocumented Existing Product

Use when the project already works but its durable product meaning is not reliably documented:

```text
idd-intent-bootstrap
```

Or limit discovery:

```text
Use idd-intent-bootstrap for the desktop application and shared contracts.
Exclude the legacy migration utility and experiments.
```

Bootstrap maps the codebase, asks the user to confirm product boundaries, classifies discovered behavior and technical choices, and proposes a small initial set of specs, ADRs, and active spikes.

Current implementation is evidence, not product intent by itself. The workflow does not write current numbered intent until the user approves the semantic proposal.

Technical details are included only when confirmed as a durable product or compatibility contract or as an accepted architecture decision. Replaceable preferences and incidental implementation details stay out of `.idd/intent/`.

When bootstrap finds a small set of terminology candidates whose incorrect interpretation could change the understanding of intent, it may show those candidates and ask whether to hand them to `idd-glossary-build`. It does not create the glossary itself, and it does not offer one when no material ambiguity exists.

[Read the existing-project guide](existing-project.md)

## Import Existing Product Knowledge

Use when existing documentation or other source material already expresses product meaning:

```text
Use idd-intent-import to propose current product intent from ./docs, relevant tests, the public API, and confirmed application behavior.
```

Import extracts durable behavior and constraints. It does not treat every old document or implementation detail as current product truth, and it is not the reverse-discovery workflow for an undocumented codebase.

Like bootstrap, import may identify genuinely ambiguous terminology. For an apply workflow it asks for explicit consent before handing approved candidates to `idd-glossary-build`. Proposal-only import reports candidates as an optional follow-up without creating files.

[Read the existing-project guide](existing-project.md)

## Build an Optional Project Glossary

Use the glossary only when terminology itself creates a material interpretation risk:

```text
idd-glossary-build
```

Or provide a focused scope:

```text
Use idd-glossary-build for the Topic, Aspect, Ticket, and Question Core terms.
Include the Russian names used in project discussions as aliases.
```

The governing rule is:

> The glossary contains not all project terms, but only terms whose incorrect interpretation could change the understanding of product intent.

The skill excludes ordinary technical terms, ordinary domain terms, private code identifiers, and task-local wording. It proposes a small entry set and waits for explicit approval before creating or changing `.idd/intent/GLOSSARY.md`.

Each entry contains only a canonical term, a short definition, and optionally `Aliases`. Aliases may include synonyms, old names, abbreviations, spelling variants, transliterations, and names in other languages.

The glossary defines vocabulary, not behavior. Behavioral rules remain in numbered specs. The file is optional, unnumbered, and absent by default.

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

Use after bootstrap, refactoring, agent-generated changes, migrations, or whenever implementation and intent may have diverged:

```text
Use idd-code-check-implementation for the comparison workflow.
```

## Update Intent from Confirmed Behavior

When one existing implementation behavior has been explicitly confirmed as product truth and an established intent model already exists:

```text
Use idd-code-update-intent for the confirmed retry behavior.
```

Do not use this narrow workflow as a replacement for initial codebase bootstrap. Do not promote accidental implementation details into requirements.

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

Audit and lint may inspect an existing glossary, but they do not build or maintain it. Use `idd-glossary-build` explicitly for glossary changes.

## Use Factory for Larger Work

Install optional `idd-factory`, then provide the complete task once:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Factory completes the requested implementation work, decomposing it when useful and applying independent review before finalization. Factory must not create or change product intent and is not used for bootstrap or glossary maintenance.

A normal run continues automatically. Only after an unexpected interruption use:

```text
Continue the current IDD Factory work.
```

[Read the Factory workflow guide](factory-workflow.md)  
[See the Factory skills reference](factory-skills.md)

## Update IDD

IDD is actively developed, and new versions are released periodically. Follow [Updating IDD](updating-idd.md) to refresh the marketplace, update or reinstall the installed plugins, verify the versions, and load the update in a new session.

## Inspect Routing

You can describe requests naturally and let IDD choose the smallest safe workflow. To inspect the selected route without changing files:

```text
idd-route
```

Requests to reconstruct initial intent for an existing undocumented implementation route to `idd-intent-bootstrap`; existing source specifications that need normalization route to `idd-intent-import`.

Glossary construction remains manual-only. Run `idd-glossary-build` explicitly or accept an explicit bootstrap/import offer.

## Skip IDD Deliberately

When a request must be performed without IDD routing or durable intent changes:

```text
idd-skip
```

This is an explicit escape hatch, not the default workflow.

## Core Rule

Keep durable product truth in numbered documents under `.idd/intent/`.

Keep only deliberately selected ambiguous project vocabulary in the optional `GLOSSARY.md`.

Keep discovery reports, source inventories, confidence notes, plans, task states, reviews, implementation attempts, and Factory execution data temporary.

Let Git preserve history.
