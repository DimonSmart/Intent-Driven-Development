---
name: idd-intent-bootstrap
description: Interactively discover, classify, confirm, and write initial current product intent for an existing implemented project that lacks reliable IDD intent.
---

# idd-intent-bootstrap

Use this skill to establish initial current product intent for an existing
implemented project when reliable IDD intent documents do not yet exist.

Formula:

```text
idd-intent-bootstrap =
    adaptive codebase discovery
    + evidence classification
    + interactive semantic confirmation
    + normalized initial intent
    + lint gate
```

This skill is manually invocable. `idd-project-init` may hand off to it only
after the user explicitly agrees to analyze an existing implementation.

## Purpose

Recover a small, current, owner-confirmed product intent model from an existing
codebase, available documentation, tests, public contracts, observed behavior,
and temporary information supplied by the user.

The implementation is evidence. It is not product intent by itself.

The skill must distinguish:

- what the product observably does;
- what the product is intended to continue doing;
- which public contracts and compatibility constraints are durable;
- which architecture decisions are intentional and durable;
- which technical choices are replaceable implementation preferences;
- which details are incidental;
- which conclusions remain uncertain or conflicting.

The result belongs under `.idd/intent/` only after the user reviews and confirms
the proposed semantic model.

## When To Use

Use this skill when:

- the repository already contains meaningful implementation;
- `.idd/intent/` has no adequate current `IDD-NNNN` product model;
- existing documentation is absent, incomplete, stale, or insufficient to
  establish current intent safely;
- the user asks to understand an existing project and create its initial IDD
  intent;
- `idd-project-init` detects an existing implementation without current numbered
  intent and the user accepts the bootstrap offer.

The request may limit discovery to selected roots, projects, modules, products,
or applications. Respect explicit include and exclude boundaries.

## When Not To Use

Do not use this skill when:

- the repository is a new or effectively empty project;
- reliable source specifications already express the current product truth and
  only need migration into IDD; use `idd-intent-import`;
- current IDD intent already owns the relevant product areas and needs a normal
  product change; use `idd-intent-change`;
- one confirmed implementation behavior is missing from otherwise adequate
  current intent; use `idd-code-update-intent`;
- the request is only to document implementation patterns for Coding Agents;
- the user asks for a broad code quality, security, architecture, or
  implementation review;
- the user has not authorized creation or modification of current intent.

Do not use bootstrap as a reason to rewrite an established `.idd/intent/` tree
from the implementation.

## Inputs

Accept natural-language input. Useful optional input includes:

- product or repository roots to include;
- directories, projects, generated sources, prototypes, migrations, or legacy
  areas to exclude;
- a short description of the project's purpose;
- known users, actors, or operational environments;
- links or paths to external documentation;
- public contracts that must remain compatible;
- known obsolete or experimental areas;
- technical choices that the user already knows are mandatory, replaceable, or
  undecided.

Treat user-supplied information as evidence for the current run. Persist only
the durable confirmed meaning, not the conversation, scan instructions, source
inventory, or temporary notes.

## Existing Intent Guard

Before discovery:

1. Read `.idd/intent/README.md` and `.idd/intent/INDEX.md`.
2. Inspect current `IDD-NNNN` documents directly under `.idd/intent/`.
3. If an adequate current product model already exists, stop and recommend the
   narrower applicable workflow.
4. If only bootstrap support files exist, continue.
5. If a partial current model exists, ask whether the user wants to bootstrap
   only uncovered product areas. Never replace confirmed current intent merely
   because the implementation differs.

## Core Safety Model

### Evidence Is Not Authority

A code path, dependency, test, directory, framework, or deployment file may
prove that something exists. It does not prove that it must remain part of the
product.

Never infer requirements using this transformation:

```text
current implementation detail
    -> mandatory product intent
```

Use this transformation:

```text
observed implementation or source evidence
    -> candidate meaning
    -> classification
    -> user confirmation when semantic
    -> spec, ADR, spike, or omission
```

### Evidence Classes

Classify findings before proposing documents:

```text
observable-product-behavior
public-contract
compatibility-constraint
domain-contract
durable-architecture-candidate
current-implementation-preference
incidental-implementation-detail
obsolete-or-experimental
unresolved-question
semantic-conflict
```

Examples:

- a user-visible export operation may be `observable-product-behavior`;
- a file schema consumed by other systems may be `public-contract`;
- local offline operation may be a `compatibility-constraint`;
- SQLite may be a `durable-architecture-candidate`,
  `current-implementation-preference`, or incidental detail depending on user
  confirmation;
- a private helper name is an `incidental-implementation-detail`;
- contradictory README and runtime behavior is a `semantic-conflict`.

### Technical Detail Classification

When a technical choice may constrain future implementations, ask the user to
classify its durable meaning using the smallest useful choice set:

```text
1. Durable product or compatibility contract
2. Accepted architecture decision
3. Replaceable implementation preference
4. Incidental current detail
5. Unresolved
```

Adapt the wording to the actual finding.

Do not ask about every dependency. Ask only when the answer changes future
implementation freedom, public compatibility, supported environments,
operability, security, or an intentional architecture boundary.

Typical candidates include:

- language or runtime;
- framework;
- persistence engine or provider;
- deployment model;
- operating-system support;
- network protocol;
- public data or file format;
- offline or local-first behavior;
- module boundaries;
- required external service;
- UI toolkit;
- extension or plugin mechanism.

Package presence alone is insufficient evidence that the package belongs in
intent.

## Discovery Strategy

Use adaptive breadth-first discovery followed by focused deep reading.

### Initial Repository Map

Inspect enough repository structure to identify:

- repository roots and workspace or solution files;
- monolith, monorepo, multi-part, or multi-application structure;
- executable entry points;
- public libraries and exposed APIs;
- CLI commands, UI routes, endpoints, jobs, services, or background processes;
- domain modules and major data flows;
- tests, examples, fixtures, and sample applications;
- persisted or exchanged formats;
- configuration boundaries;
- integrations and deployment targets;
- existing README files, docs, ADRs, schemas, and API descriptions;
- generated, vendored, experimental, legacy, migration, or test-only areas.

Exclude normal dependency, build-output, generated-output, and cache
directories unless they define a public generated contract.

Do not begin by reading every source file. Build the map first, then inspect the
smallest representative set of files needed to understand product behavior and
durable boundaries.

### Project Boundary Confirmation

Present the detected product parts and ask the user to confirm or correct them
before semantic writing.

The user may:

- confirm the detected parts;
- rename or regroup them by product meaning;
- exclude obsolete, experimental, generated, migration, or internal-tool areas;
- provide additional repository or directory roots;
- provide temporary project context;
- identify a primary product when the repository contains several;
- request whole-repository or selected-area discovery.

Directory boundaries are evidence, not automatic intent-document boundaries.

### Focused Product Discovery

For each confirmed product part, investigate:

- likely purpose and intended users;
- primary user or system scenarios;
- externally observable behavior;
- public entry points and contracts;
- durable domain concepts and invariants;
- compatibility and data-preservation expectations;
- failure and recovery behavior where product-defining;
- external integrations;
- security, privacy, or operational constraints where observable or confirmed;
- architecture boundaries that may be intentionally durable;
- tests or examples that provide behavioral evidence.

Prefer converging evidence from multiple sources. For example, a public API,
integration tests, runtime wiring, and user confirmation together provide
stronger evidence than one private class.

Do not inspect Git history unless the user explicitly provides it as a source
or asks for historical investigation.

## Interactive Workflow

### 1. Establish Scope

If the request does not already define scope, ask whether to analyze:

```text
1. The whole repository
2. Selected product areas or project roots
3. Cancel without creating initial intent
```

For selected scope, ask for include and exclude roots or a semantic description
of the target product parts.

Ask whether the user has temporary context, external documents, known obsolete
areas, or compatibility requirements that the code cannot reveal.

Do not force the user to provide additional information. Continue with explicit
uncertainty when none is available.

### 2. Discover And Confirm The Project Map

Perform the initial repository map.

Present:

- detected product or application parts;
- likely responsibilities;
- public entry points;
- important source areas;
- excluded generated or non-product areas;
- uncertainties in the classification.

Ask the user to confirm or correct the map. Do not create current intent before
this boundary is accepted.

### 3. Build Candidate Product Intent

Create a temporary candidate model containing:

```text
product purpose
actors and users
major capabilities
public contracts
domain concepts and invariants
compatibility expectations
external integrations
durable architecture candidates
replaceable implementation choices
possible obsolete areas
unknowns and conflicts
```

For each material candidate track:

```text
candidate statement
classification
supporting evidence
confidence: high | medium | low
confirmation status
proposed target: spec | adr | spike | omit
```

Confidence helps review. It does not replace confirmation.

Do not write this inventory under `.idd/intent/`.

### 4. Ask Targeted Semantic Questions

Ask only high-leverage questions that affect durable meaning.

Group related questions instead of asking one question per file or dependency.
Examples:

```text
The application persists all state locally and contains no server dependency.
Is local/offline operation a product requirement, an accepted architecture
decision, or only the current implementation?
```

```text
The exported JSON shape is covered by compatibility tests and consumed by a
sample client. Is this a public compatibility contract or a replaceable internal
format?
```

```text
The repository contains a migration utility that is not referenced by the main
application. Is it part of the current product, a temporary migration tool, or
obsolete?
```

When the user cannot decide, classify the item as unresolved. Create a spike
only when the unresolved question is active and materially affects the product
or architecture. Otherwise omit it from current intent and report it.

### 5. Handle Conflicts

A conflict exists when plausible current sources imply different product
behavior, contracts, supported environments, defaults, or durable boundaries.

Do not choose silently.

For each material conflict:

- state both interpretations;
- show the strongest evidence for each;
- ask for a product decision when the conflict blocks coherent intent;
- create only non-conflicting current intent;
- use a spike when focused research is genuinely required;
- stop before writing the conflicting normative statement when no decision is
  available.

### 6. Propose The Initial Intent Model

Before writing numbered documents, present a compact proposal.

For each proposed document include:

```text
candidate ID and filename
document type: spec | adr | spike
owning product or decision area
confirmed durable meaning
important evidence
implementation details intentionally excluded
open questions
```

Prefer a small document set. Product areas, shared contracts, durable
architecture decisions, and active research questions determine document
boundaries. Repository folders and project files do not.

Offer these outcomes:

```text
Apply the proposal
Edit the proposal
Review ambiguous candidates
Cancel without writing current intent
```

Do not create or modify current numbered documents until the user explicitly
approves the semantic proposal.

### 7. Write Confirmed Current Intent

After approval:

1. Re-read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and current
   `IDD-NNNN` documents.
2. Use existing owners when bootstrapping uncovered areas in a partially
   documented project.
3. Create the minimum required specs, ADRs, and active spikes using current
   templates and `IDD-NNNN` numbering rules.
4. Write observable behavior and durable contracts without private type, method,
   source-file, or dependency-wiring names.
5. Include technical technologies or libraries only when confirmed as a product
   contract, compatibility constraint, or accepted architecture decision.
6. Keep replaceable preferences and incidental details out of current intent.
7. Update `.idd/intent/INDEX.md`.
8. Run or simulate `idd-intent-lint`.
9. Fix every mechanical error before completing.

Do not create a discovery report, source inventory, confirmation transcript, or
scan-state file under `.idd/intent/`.

If persistent discovery output is explicitly requested, place it outside
`.idd/intent/` and mark it non-normative.

## Document Selection

Use:

- `spec` for durable product behavior, actors, scenarios, domain contracts,
  public contracts, compatibility constraints, acceptance criteria, and
  verification properties;
- `adr` for a confirmed durable architecture decision whose rationale,
  alternatives, and consequences matter;
- `spike` for an active unresolved question that requires research before a
  product or architecture decision.

Do not create:

- one spec per project, namespace, directory, class, endpoint, or test;
- specs for ordinary dependencies, coding style, private architecture shape, or
  current DI wiring;
- ADRs solely because a framework or library is present;
- spikes for every low-confidence observation;
- task, migration, implementation, or cleanup documents under `.idd/intent/`.

## Relationship To Other Skills

Use `idd-project-init` first when the repository has not been initialized.

Use `idd-intent-import` when source documents already express product knowledge
that can be normalized without reconstructing meaning primarily from the
implementation.

Use `idd-intent-brainstorm` when the owner wants to define a future product
direction rather than recover the current product.

Use `idd-intent-new-document` for a normal focused new owner after bootstrap.
Bootstrap may perform equivalent document creation internally only after its
whole initial proposal is approved.

Use `idd-code-update-intent` for later, narrow transfer of explicitly confirmed
implementation behavior into an established intent model.

Use `idd-code-check-implementation` after bootstrap when the user wants a
separate conformance review of the complete implementation against the newly
confirmed intent.

## Output

Before approval, return a discovery and proposal summary in the conversation.

After apply, report:

- scope inspected;
- product parts confirmed;
- documents created or updated;
- material technical details classified as durable, replaceable, incidental, or
  unresolved;
- conflicts and unresolved questions;
- intentionally excluded implementation details;
- lint result;
- recommended next workflow.

The report is temporary workflow output and is not normative product intent.

## Quality Gate

Before completion, verify:

- the repository contained meaningful existing implementation;
- scope and product-part boundaries were confirmed;
- implementation evidence was not treated as product intent automatically;
- user-supplied temporary context was not copied as workflow history;
- public behavior and contracts are expressed without private implementation
  names;
- technical choices were included only at their confirmed durable level;
- accidental patterns, ordinary dependencies, source paths, and coding style
  were excluded;
- conflicts remain visible and were not resolved silently;
- no numbered current document was written before proposal approval;
- document boundaries follow durable product areas rather than repository
  structure;
- the initial intent model is small enough to remain useful;
- `.idd/intent/INDEX.md` matches actual current documents;
- all `IDD-NNNN` references are mechanically valid;
- `idd-intent-lint` reports no errors.

## Non-goals

This skill does not:

- prove that the current implementation is correct;
- perform a broad quality or architecture review;
- generate implementation documentation;
- create a full repository inventory as durable project memory;
- preserve every existing behavior;
- automatically lock the current technology stack;
- resolve product conflicts without the owner;
- implement product changes;
- start Factory;
- inspect historical Git state by default.
