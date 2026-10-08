# IDD Use Cases

Intent-Driven Development separates durable product intent from temporary implementation work.

Use `idd-intent` for the normal workflow. Add optional `idd-factory` when implementation benefits from explicit multi-step orchestration.

In Factory, the original Request defines one complete Task. A Factory run
repeatedly plans bounded batches of tasks. Executors complete each task in order,
and strict final verification validates the complete result.

Projects may optionally keep durable implementation guardrails in
`.idd/engineering/`. The layer is absent by default and is not created by
`idd-project-init`. Direct implementation and Factory workers both apply it when
present. `idd-engineering-change` remains the ordinary owner of explicit durable
Rule add, modify, and remove operations; a new Factory entry may invoke that
owner during preflight before `.idd/factory/current/request.md` exists. When existing documentation already states accepted
durable Engineering decisions, `idd-intent-import` can migrate those decisions
alongside Product Intent without asking for the same decision again. Current
code patterns are never promoted automatically.

For project-specific commands, use `idd-verification-configure` to create
`.idd/verification.yaml`. It assigns checks by `direct`, `subtask`,
`checkpoint`, and `final` context without putting commands in product intent
or Engineering Rules.

See [Engineering Guardrails](engineering-guardrails.md) for the Intent versus
Engineering boundary and rule format.

## Selecting a workflow

Use the explicit user commands when you want to invoke one directly. Other
registered workflows are automatically selected from requests phrased in
ordinary language. In Claude Code these automatic workflows are hidden from
the slash-command menu while remaining available to the model. Codex retains
all skills and may still show them for explicit invocation because the platform
does not provide an equivalent documented menu-visibility setting.

Internal protocols (Factory planner, Factory worker and Intent document creation)
are references belonging to other skills, not standalone commands.

## Find the Right Action

| Situation | What to do |
| --- | --- |
| An existing repository does not use IDD yet | Run `idd-project-init`. It can offer interactive bootstrap when implementation exists without current intent. |
| Existing implementation has little or unreliable product documentation | Ask to reconstruct and confirm the current Product Intent from the implementation. |
| Existing documents already express current product knowledge and/or accepted durable Engineering decisions | Ask to import the supplied knowledge into separate Intent and Engineering layers. |
| Project terminology is genuinely ambiguous or project-specific | Explicitly run `idd-glossary-build` to create or update the optional glossary. |
| You need to confirm that IDD is installed and initialized correctly | Follow [Verify Installation](verify-installation.md). |
| You are starting from an idea | Run `idd-project-init`, then ask to clarify the first product behavior. |
| The requested feature is still unclear | Ask for product clarification without changing intent or code. |
| Product behavior must be added, changed, or removed | Describe the new behavior and ask to update Product Intent before implementation. |
| Current intent is already correct and only code must change | Ask to implement the already-confirmed behavior without changing Product Intent. |
| You need to check whether code still matches intent | Ask to audit implementation conformance with an explicit focused or project-wide scope. |
| Existing behavior has been confirmed as product truth but is missing from established intent | Ask to record the confirmed existing behavior in current Product Intent. |
| Intent documents need semantic structure/ownership review | Ask for a semantic structure audit, a mechanical validity check, or focused normalization, as appropriate. |
| The implementation task is large or naturally multi-stage | Install `idd-factory` and use `idd-factory-run`. |
| A new IDD release is available | Follow [Updating IDD](updating-idd.md), then start a new session. |
| The request must deliberately bypass IDD | Use `idd-skip`. |

## Audit Existing Intent and Implementation

Use the mechanisms for different questions:

```text
Check .idd/intent/ for mechanical consistency errors.

Check whether Product Intent documents have correct ownership and structure.

Audit whether the implementation conforms to current Product Intent.
```

For drift audit, state the scope explicitly. It may be one feature, one intent
document, an implementation area, a mismatch, or `project-wide`. A project-wide
audit builds an Intent-driven map first rather than performing a generic code
review.

Build/test/lint/analyzer commands remain project verification policy in
`.idd/verification.yaml`; they are evidence used by implementation and Factory
workflows, not a separate audit command.

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
Analyze the existing implementation and propose an initial current Product Intent.
```

Or limit discovery:

```text
Reconstruct and propose current Product Intent for the desktop application and shared contracts.
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
Import and propose current product intent from ./docs, relevant tests, the public API, and confirmed application behavior.
```

Import classifies supplied durable knowledge. Product behavior goes to Intent;
explicit accepted implementation-only durable decisions may go to Engineering.
Suggestions, alternatives, historical context, and code-derived patterns do not
become Rules. Import is not the reverse-discovery workflow for an undocumented
codebase.

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
Help me clarify the first useful product behavior. Do not change files yet.
```

After the intent is clear, record it and implement the smallest useful slice.

[Read the new-project guide](new-project.md)

## Clarify Product Intent

Use when product behavior, boundaries, constraints, or expected outcomes are not yet clear:

```text
Clarify this feature before changing product intent or code.
```

The result should clarify product meaning rather than produce an implementation plan.

## Add, Change, or Remove Product Behavior

Describe the product change rather than expected code edits:

```text
Users must be able to compare two local folders without modifying either side. Update Product Intent before implementing the behavior.
```

The workflow updates the current owning intent document. When required, it also
creates a new owning spec, ADR, or spike through an internal document-creation
workflow, without a separate public skill. A new spec is created only when no
existing spec owns that product area.

## Implement from Current Intent

For focused implementation when current intent is already correct:

```text
Implement the folder comparison behavior from current Product Intent. Do not change product requirements.
```

The workflow reads relevant intent, applies every Always Engineering Rule plus
semantically relevant Conditional Engineering Rules when the optional layer
exists, inspects the affected code, implements the change, and verifies the
result.

## Verify an Existing Implementation

Use after bootstrap, refactoring, agent-generated changes, migrations, or
whenever implementation may have diverged from product intent or applicable
Engineering Guardrails:

```text
Audit the comparison workflow implementation against current Product Intent.
```

## Update Intent from Confirmed Behavior

When one existing implementation behavior has been explicitly confirmed as product truth and an established intent model already exists:

```text
Record the confirmed retry behavior in the existing Product Intent model.
```

Do not use this narrow workflow as a replacement for initial codebase bootstrap. Do not promote accidental implementation details into requirements.

## Audit and Normalize Intent

Diagnostic review without edits:

```text
Check Product Intent documents for semantic structure and ownership problems.
```

Mechanical consistency checks:

```text
Check current IDD documents for mechanical consistency errors.
```

Focused structural cleanup without changing product meaning:

```text
Normalize the documents for topic X without changing product meaning. Propose the changes first.
```

Audit and lint may inspect an existing glossary, but they do not build or maintain it. Use `idd-glossary-build` explicitly for glossary changes.

## Use Factory for Larger Work

Install optional `idd-factory`, then provide the complete task once:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

For a new end-to-end run, Factory first performs durable preflight: it may
coordinate Product Intent preparation through the normal Intent workflows and
explicit durable Engineering preparation through `idd-engineering-change`, then
validates coverage before active Factory state is created. The planner and
workers themselves remain implementation-only and never mutate
`.idd/intent/*` or `.idd/engineering/*`. Factory is not used for bootstrap or
glossary maintenance.

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

Ordinary explicit durable implementation-policy mutations route to
`engineering-change` and `idd-engineering-change`. Raw supplied knowledge
continues to route to `idd-intent-import` even when it includes already-decided
Engineering policy. Product behavior continues to route to `product-change`;
repeated implementation patterns alone do not select Engineering.

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
