# Start Using IDD in an Existing Project

Project initialization can offer verification configuration before intent
bootstrap. Choosing defaults leaves no marker file; run
`idd-verification-configure` later to create `.idd/verification.yaml`.

Use this path when the repository already contains implementation,
documentation, requirements, ADRs, tests, or other evidence about the product.

The goal is not to copy the repository into IDD. The goal is to establish a
small current model of durable product truth under `.idd/intent/`.

## 1. Install IDD

Claude Code:

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
claude plugin install idd-intent@intent-driven-development
```

Codex:

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
codex plugin add idd-intent@intent-driven-development
```

## 2. Initialize the Repository

Run in the repository root:

```text
idd-project-init
```

This creates the minimal project-owned IDD structure:

```text
.idd/
  intent/
  plugins.json
```

It also adds one small managed IDD section to `AGENTS.md` for Codex or
`CLAUDE.md` for Claude Code while preserving unrelated project instructions.

Initialization does not create an empty `GLOSSARY.md`. The optional glossary is
created only later through explicit `idd-glossary-build` work.

When implementation already exists but no current `IDD-NNNN` documents exist,
initialization pauses for an explicit bootstrap decision. When the Coding Agent
provides structured user input, this appears as a client-native choice between
analyzing the whole repository, selecting product areas, or skipping bootstrap.
When structured input is unavailable, initialization asks one direct question
and ends the turn instead of merely mentioning analysis as a possible next step.

A project description supplied with `idd-project-init` is temporary bootstrap
context. It is not implicit consent to analyze the codebase or create current
intent documents.

## 3. Choose How To Establish Initial Intent

Existing projects have two different starting situations.

### Existing knowledge already describes the product

Use `idd-intent-import` when requirements, specifications, ADRs, public
contracts, product documentation, or other source material already expresses
the current product meaning:

```text
Use idd-intent-import to propose current product intent from ./docs, the public
API, relevant tests, and confirmed application behavior.
```

Import treats source material as evidence, not unquestionable truth. Historical
plans, stale requirements, accidental implementation details, and obsolete
documentation should not become current product intent merely because they
exist.

### Implementation exists but reliable product documentation does not

Use the interactive bootstrap workflow:

```text
idd-intent-bootstrap
```

Or identify the relevant scope:

```text
Use idd-intent-bootstrap for ./src/Product and ./src/Product.Contracts.
Exclude ./src/LegacyMigration and ./experiments.
```

Bootstrap:

1. maps the repository and detects product parts;
2. pauses for you to confirm or correct the product boundary;
3. discovers candidate users, scenarios, behavior, contracts, domain concepts,
   integrations, and architecture boundaries;
4. separates durable product meaning from replaceable or incidental
   implementation details;
5. asks blocking targeted questions only where a technical choice changes future
   implementation freedom;
6. presents a proposed initial set of specs, ADRs, and active spikes;
7. pauses for explicit approval before writing current intent;
8. runs `idd-intent-lint`.

Blocking decisions use the client's structured input UI when available and do
not auto-resolve. Without structured input, the workflow asks one direct question
and stops until the user responds.

For example, the workflow may ask first whether SQLite is durable, replaceable,
or unresolved. Only when it is durable and the distinction matters does it ask
whether SQLite is a product/compatibility contract or an accepted architecture
decision. Package presence alone does not make a technology part of product
intent.

### Code and partial documentation both exist

Bootstrap may use documentation, tests, public APIs, observed behavior, and
temporary information supplied by the project owner as separate evidence
sources.

Use bootstrap when the product meaning still has to be reconstructed and
confirmed. Use import when the meaning is already expressed and mainly needs
normalization.

## 4. Optional Terminology Discovery

Bootstrap and import may encounter project vocabulary while performing their
primary work. They must not turn that into an automatic terminology inventory.

The governing rule is:

> The glossary contains not all project terms, but only terms whose incorrect
> interpretation could change the understanding of product intent.

A candidate is worth showing only when there is a concrete ambiguity risk: for
example, multiple names denote the same concept, a familiar term has a special
project meaning, two similar concepts must be distinguished, or translations
and legacy names can lead to different interpretations.

Ordinary technical terms, ordinary domain vocabulary, private identifiers, and
frequently used words are not glossary candidates merely because they occur in
the project.

When material candidates exist, bootstrap or apply-mode import:

1. shows a compact list with the proposed canonical term, short definition,
   optional aliases, and the misunderstanding prevented;
2. asks explicitly whether to build or update the glossary;
3. treats `skip` as a successful outcome;
4. hands approved candidates to `idd-glossary-build` rather than writing the file
   directly.

Proposal-only import may report candidates as an optional follow-up but does not
start a glossary write that exceeds proposal scope.

If no material candidates exist, the workflow should not mention or offer a
glossary.

## 5. Build the Glossary Only When Needed

Run the manual-only skill explicitly:

```text
idd-glossary-build
```

Or use a focused request:

```text
Use idd-glossary-build for Topic, Aspect, Ticket, and Question Core.
Treat the Russian terms used in discussions as aliases where they denote the
same concept.
```

The skill reads only relevant intent and supplied terminology sources, proposes
a minimal entry set, and waits for approval before creating or updating
`.idd/intent/GLOSSARY.md`.

Each entry contains:

- a canonical term;
- one short definition;
- optionally `Aliases`.

Aliases may include synonyms, old names, abbreviations, spelling variants,
transliterations, and names in other languages. They must denote the same concept
as the canonical heading.

The glossary defines language, not product behavior. Behavioral rules remain in
numbered specs. The glossary is not assigned an `IDD-NNNN` identifier and is not
listed in `INDEX.md`.

## 6. Provide Temporary Project Context

During bootstrap you may provide:

- a short explanation of what the project is for;
- primary users or actors;
- product or repository roots to include;
- generated, experimental, legacy, migration, or internal-tool areas to exclude;
- additional documentation paths or external sources;
- known public contracts;
- compatibility requirements;
- technologies that are mandatory, replaceable, or undecided;
- behavior that cannot be inferred by running or reading the project.

This information guides the current discovery run. The workflow persists only
confirmed durable meaning. It does not save the conversation, scan inventory,
confidence notes, or temporary instructions as product intent.

## 7. Review The Proposed Intent

Check that the proposed documents capture:

- product purpose and actors;
- user-visible or externally observable behavior;
- important domain contracts and invariants;
- public interfaces and compatibility expectations;
- durable architectural decisions;
- meaningful verification rules;
- explicit non-goals where they prevent misunderstanding.

Check that they exclude:

- private classes, methods, file paths, and dependency wiring;
- ordinary package choices and current coding style;
- temporary migrations and fallbacks;
- task status, implementation plans, and review notes;
- obsolete or experimental behavior;
- assumptions that were not confirmed.

When a product decision remains unclear, keep it visible. Use an active spike
only when research is genuinely needed before a product or architecture
decision.

## 8. Verify The Structure

Run:

```text
idd-intent-lint
```

For a broader diagnostic review:

```text
idd-intent-audit
```

Lint treats a missing glossary as valid. When a glossary exists, it checks its
basic shape, duplicate terms and aliases, and obvious requirement or task
leakage without attempting to maintain the file.

After bootstrap, a separate conformance check can compare the implementation
with the newly confirmed model:

```text
Use idd-code-check-implementation for the bootstrapped product areas.
```

This check does not automatically authorize implementation changes.

## 9. Start Normal Development

For a focused implementation from current intent:

```text
Use idd-code-implement for <product area or requested behavior>.
```

When product behavior itself must change:

```text
Use idd-intent-change for <confirmed product change>.
```

For a large implementation task requiring several coordinated stages:

```text
Use idd-factory-run to implement the task described in <file or request>.
```

Factory requires the optional `idd-factory` plugin. Factory is not used for
bootstrap or glossary work because Factory must not create or change product
intent support artifacts.

## What Happens To Existing Documentation?

IDD does not require deleting existing documentation immediately.

Use the following rule:

- keep documents that still serve a clear audience or operational purpose;
- move durable product truth into `.idd/intent/`;
- keep only materially ambiguous shared vocabulary in the optional glossary;
- avoid maintaining two competing sources of product truth;
- remove or archive stale plans and obsolete specifications when it is safe;
- let Git preserve historical versions.

The target state is a small, current intent model—not a second copy of the
repository's source tree, terminology, or documentation history.

## Next

- [Browse common IDD use cases](using-idd.md)
- [Understand the methodology](methodology.md)
- [Use Factory for larger work](factory-workflow.md)
