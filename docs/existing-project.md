# Start Using IDD in an Existing Project

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

When implementation already exists but no current `IDD-NNNN` documents exist,
initialization offers to analyze the whole repository, analyze selected product
areas, or skip bootstrap for now.

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
2. asks you to confirm or correct the scope;
3. discovers candidate users, scenarios, behavior, contracts, domain concepts,
   integrations, and architecture boundaries;
4. separates durable product meaning from replaceable or incidental
   implementation details;
5. asks targeted questions only where a technical choice changes future
   implementation freedom;
6. presents a proposed initial set of specs, ADRs, and active spikes;
7. writes current intent only after explicit approval;
8. runs `idd-intent-lint`.

For example, the workflow may ask whether SQLite is a required compatibility
choice, an accepted architecture decision, a replaceable persistence
implementation, or still unresolved. Package presence alone does not make a
technology part of product intent.

### Code and partial documentation both exist

Bootstrap may use documentation, tests, public APIs, observed behavior, and
temporary information supplied by the project owner as separate evidence
sources.

Use bootstrap when the product meaning still has to be reconstructed and
confirmed. Use import when the meaning is already expressed and mainly needs
normalization.

## 4. Provide Temporary Project Context

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

## 5. Review The Proposed Intent

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

## 6. Verify The Structure

Run:

```text
idd-intent-lint
```

For a broader diagnostic review:

```text
idd-intent-audit
```

After bootstrap, a separate conformance check can compare the implementation
with the newly confirmed model:

```text
Use idd-code-check-implementation for the bootstrapped product areas.
```

This check does not automatically authorize implementation changes.

## 7. Start Normal Development

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
bootstrap because Factory must not create or change product intent.

## What Happens To Existing Documentation?

IDD does not require deleting existing documentation immediately.

Use the following rule:

- keep documents that still serve a clear audience or operational purpose;
- move durable product truth into `.idd/intent/`;
- avoid maintaining two competing sources of product truth;
- remove or archive stale plans and obsolete specifications when it is safe;
- let Git preserve historical versions.

The target state is a small, current intent model—not a second copy of the
repository's source tree or documentation history.

## Next

- [Browse common IDD use cases](using-idd.md)
- [Understand the methodology](methodology.md)
- [Use Factory for larger work](factory-workflow.md)
