# idd-intent-bootstrap

Use this skill to establish initial current product intent for an existing
implemented project when reliable IDD intent documents do not yet exist.

```text
idd-intent-bootstrap =
    adaptive codebase discovery
    + evidence classification
    + blocking semantic confirmation
    + normalized initial intent
    + lint gate
```

The skill is directly invocable. `idd-project-init` may hand off to it only after
the user explicitly agrees to analyze an existing implementation.

## Purpose

Recover a small, current, owner-confirmed product intent model from an existing
codebase, available documentation, tests, public contracts, observed behavior,
and temporary information supplied by the user.

Implementation is evidence. It is not product intent by itself.

The skill must distinguish:

- observable product behavior from accidental behavior;
- public contracts and compatibility constraints from internal formats;
- intentional durable architecture from replaceable implementation choices;
- current product areas from obsolete, experimental, migration, or internal-tool
  areas;
- confirmed product truth from unresolved or conflicting interpretations.

Nothing becomes a current `IDD-NNNN` document until the user reviews and approves
the proposed semantic model.

## When To Use

Use this skill when:

- meaningful implementation already exists;
- `.idd/intent/` has no adequate current `IDD-NNNN` product model;
- documentation is absent, incomplete, stale, or insufficient;
- the user asks to understand an existing project and reconstruct its initial
  current intent;
- `idd-project-init` detects an existing implementation without current intent
  and the user accepts the bootstrap offer.

The request may limit discovery to selected roots, projects, modules, products,
or applications. Respect explicit include and exclude boundaries.

## When Not To Use

Do not use this skill when:

- the repository is new or effectively empty;
- reliable source specifications already express current product truth and only
  need normalization into IDD; use `idd-intent-import`;
- current IDD intent already owns the area and needs a normal product change; use
  `idd-intent-change`;
- one confirmed implementation behavior is missing from otherwise adequate
  current intent; use `idd-code-update-intent`;
- the request is only to document implementation patterns for Coding Agents;
- the user asks for a broad code-quality, security, or architecture review;
- the user has not authorized creation or modification of current intent.

Do not use bootstrap to replace an established intent tree from implementation.

## Inputs

Accept natural-language input. Useful optional context includes:

- roots or product areas to include;
- generated sources, prototypes, migrations, legacy areas, or tools to exclude;
- a short description of product purpose and users;
- external documentation or related repositories;
- public contracts and compatibility expectations;
- known obsolete or experimental areas;
- technical choices already known to be mandatory, replaceable, or undecided.

Treat this information as temporary evidence for the current run. Persist only
confirmed durable meaning, not the conversation, scan instructions, source
inventory, or temporary notes.

## Structured User Input Protocol

Every decision that blocks discovery scope or normative intent writing must be an
actual user-input request, not a suggestion placed in a completion report.

Use the structured user-question tool exposed by the current host:

- Codex: `request_user_input`;
- Claude Code: `AskUserQuestion`.

Do not reproduce, approximate, or document either tool's JSON schema in this
skill. The runtime defines the tool contract. This skill defines only the
semantic question, stable workflow values, available choices, and the action to
take after each answer.

### When a structured user-question tool is available

For every blocking decision:

1. MUST invoke the host's structured question tool.
2. Prefer one question per call and do not exceed the host's supported limit.
3. Ask a single-choice question unless the workflow explicitly requires multiple
   selections.
4. Provide two or three meaningful, mutually exclusive options.
5. Put the recommended option first and mark it as recommended when the host
   convention supports that label.
6. In Codex, omit `autoResolutionMs`; blocking semantic decisions require an
   explicit answer and must not resolve automatically.
7. Stop and wait immediately after the tool call.
8. Do not emit a final response, continue discovery, or write current intent
   while the answer is pending.
9. Do not convert an unanswered decision into an assumption.

Use short stable decision keys and short UI headers. Keep each option description
to one sentence explaining the effect of that choice. Stable option values belong
to the workflow description even when a host does not expose those values as
literal tool fields.

### When no structured user-question tool is available

If neither `request_user_input` nor `AskUserQuestion` is available:

1. Ask one concise plain-text question.
2. End the turn immediately after the question.
3. Do not print a textual numbered or bulleted multiple-choice menu.
4. Do not reduce the decision to a generic next-step recommendation.
5. Do not continue with a guessed answer.

### Decisions covered by this protocol

The protocol applies to:

- whole-repository versus selected-area scope;
- whether to bootstrap uncovered areas when partial current intent exists;
- confirmation or correction of the detected product map;
- classification of material technical choices;
- resolution of semantic conflicts;
- approval, revision, or cancellation of the proposed initial intent model.

## Existing Intent Guard

Before discovery:

1. Read `.idd/intent/README.md` and `.idd/intent/INDEX.md`.
2. Inspect current `IDD-NNNN` documents directly under `.idd/intent/`.
3. If an adequate current model exists, stop and recommend the narrower workflow.
4. If only bootstrap support files exist, continue.
5. If a partial current model exists, use the Structured User Input Protocol to
   ask whether only uncovered areas should be bootstrapped. Never replace
   confirmed current intent merely because implementation differs.

## Core Safety Model

Never perform this transformation:

```text
current implementation detail
    -> mandatory product intent
```

Use this transformation:

```text
observed implementation or source evidence
    -> candidate meaning
    -> classification
    -> explicit semantic confirmation
    -> spec, ADR, spike, or omission
```

Classify material findings as one of:

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

- user-visible export behavior may be observable product behavior;
- an externally consumed file schema may be a public contract;
- offline operation may be a compatibility constraint;
- SQLite may be an accepted architecture decision, a replaceable preference, or
  incidental depending on confirmation;
- a private helper name is incidental;
- contradictory README and runtime behavior is a semantic conflict.

## Technical Detail Classification

Do not ask about every dependency. Ask only when the answer changes future
implementation freedom, public compatibility, supported environments,
operability, security, or an intentional architecture boundary.

Typical candidates include language/runtime, framework, persistence provider,
deployment model, operating-system support, network protocol, public file
format, offline behavior, module boundaries, required external service, UI
toolkit, and extension mechanism.

Because structured input should use only two or three options, classify a
technical choice in at most two stages.

First ask one single-choice question:

- decision key: `technical_choice_level`;
- question: `How should this technical choice constrain future implementations?`;
- options:
  - `durable` — **Durable requirement or decision**;
  - `replaceable` — **Replaceable or incidental implementation detail**;
  - `unresolved` — **Unresolved**.

If the user selects `durable` and the distinction matters, ask a second
single-choice question:

- decision key: `technical_choice_kind`;
- question: `What kind of durable constraint is this technical choice?`;
- options:
  - `product_contract` — **Product or compatibility contract**;
  - `architecture_decision` — **Accepted architecture decision**;
  - `unresolved` — **Return to unresolved**.

Map results as follows:

- product or compatibility contract -> spec;
- accepted architecture decision -> ADR;
- replaceable or incidental detail -> omit from current intent;
- unresolved -> active spike only when focused research materially affects the
  product or architecture, otherwise omit and report.

Package presence alone is insufficient evidence that a package belongs in
intent.

## Discovery Strategy

Use adaptive breadth-first discovery followed by focused deep reading.

### Initial repository map

Inspect enough repository structure to identify:

- workspace, solution, package, and project roots;
- monolith, monorepo, multipart, or multi-application structure;
- executable entry points and public libraries;
- CLI commands, UI routes, endpoints, jobs, services, or background processes;
- domain modules and major data flows;
- tests, examples, fixtures, and sample applications;
- persisted or exchanged formats and configuration boundaries;
- integrations and deployment targets;
- README files, docs, ADRs, schemas, and API descriptions;
- generated, vendored, experimental, legacy, migration, or test-only areas.

Exclude normal dependencies, build outputs, generated outputs, and caches unless
they define a public generated contract.

Do not begin by reading every source file. Build the map first, then inspect the
smallest representative set needed to understand product behavior and durable
boundaries.

Do not inspect Git history unless the user explicitly supplies it as evidence or
asks for historical investigation.

## Interactive Workflow

### 1. Establish scope

If scope is not already explicit, use the Structured User Input Protocol:

- decision key: `bootstrap_scope`;
- question: `Which scope should initial intent discovery cover?`;
- options:
  - `whole_repository` — **Whole repository (Recommended)**;
  - `select_areas` — **Select product areas**;
  - `cancel` — **Cancel bootstrap**.

For selected scope, obtain include roots, exclude roots, or a semantic description
of target product areas through another blocking request. Also allow temporary
context, external documents, known obsolete areas, and compatibility constraints.
Do not force additional information when none exists; retain explicit
uncertainty.

### 2. Discover and confirm the project map

Perform the initial repository map and present:

- detected product or application parts;
- likely responsibilities;
- public entry points;
- important source areas;
- excluded generated or non-product areas;
- uncertainties in classification.

Then use the Structured User Input Protocol:

- decision key: `project_map_confirmation`;
- question: `Is this the correct product boundary for intent discovery?`;
- options:
  - `confirm` — **Confirm map (Recommended)**;
  - `revise` — **Revise map**;
  - `cancel` — **Cancel bootstrap**.

If revision is selected, request corrections and repeat confirmation. Do not
create current intent before the boundary is accepted.

Directory boundaries are evidence, not automatic intent-document boundaries.

### 3. Build candidate product intent

For each confirmed product part, investigate:

- purpose and intended users;
- primary user or system scenarios;
- externally observable behavior;
- public entry points and contracts;
- durable domain concepts and invariants;
- compatibility and data-preservation expectations;
- product-defining failure and recovery behavior;
- external integrations;
- observable or confirmed security, privacy, and operational constraints;
- architecture boundaries that may be intentionally durable;
- tests and examples providing behavioral evidence.

Prefer converging evidence. A public API, integration tests, runtime wiring, and
user confirmation together are stronger than one private class.

Track material candidates temporarily using:

```text
candidate statement
classification
supporting evidence
confidence: high | medium | low
confirmation status
proposed target: spec | adr | spike | omit
```

Confidence helps review. It never replaces confirmation. Do not write this
inventory under `.idd/intent/`.

### 4. Ask targeted semantic questions

Ask only high-leverage questions affecting durable meaning. Group related
questions and use the technical classification protocol above.

Examples include:

- whether local/offline operation is required or merely current architecture;
- whether an exported JSON shape is a public contract or internal format;
- whether an unreferenced migration utility is current product, temporary work,
  or obsolete;
- whether C#, .NET, Agent Framework, a database, or a UI toolkit is part of the
  durable contract, an ADR, or replaceable implementation.

### 5. Handle conflicts

A conflict exists when plausible current sources imply different behavior,
contracts, supported environments, defaults, or durable boundaries.

Do not choose silently. State the competing interpretations and strongest
evidence, then request a blocking product decision. Create only non-conflicting
intent while the conflict remains unresolved. Use a spike only when focused
research is genuinely required.

### 6. Propose the initial intent model

Before writing current documents, present a compact proposal. For each proposed
document include:

```text
candidate ID and filename
document type: spec | adr | spike
owning product or decision area
confirmed durable meaning
important evidence
implementation details intentionally excluded
open questions
```

Prefer a small document set. Product areas, shared contracts, durable decisions,
and active research questions determine document boundaries. Folders and project
files do not.

Use the Structured User Input Protocol:

- decision key: `initial_intent_proposal`;
- question: `What should happen with the proposed initial intent model?`;
- options:
  - `apply` — **Apply proposal (Recommended)**;
  - `review` — **Review or edit**;
  - `cancel` — **Cancel without writing**.

If review or edit is selected, obtain corrections, update the proposal, and ask
for approval again. Do not create or modify current `IDD-NNNN` documents before
explicit approval.

### 7. Write confirmed current intent

After approval:

1. Re-read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and current
   `IDD-NNNN` documents.
2. Use existing owners when bootstrapping uncovered areas in a partially
   documented project.
3. Create the minimum required specs, ADRs, and active spikes using current
   templates and `IDD-NNNN` numbering rules.
4. Express observable behavior and durable contracts without private type,
   method, source-file, or dependency-wiring names.
5. Include technologies and libraries only when confirmed as a product contract,
   compatibility constraint, or accepted architecture decision.
6. Keep replaceable preferences and incidental details out of current intent.
7. Update `.idd/intent/INDEX.md`.
8. Run or simulate `idd-intent-lint` and fix every mechanical error.

Do not create a discovery report, source inventory, confirmation transcript, or
scan-state file under `.idd/intent/`. If persistent discovery output is
explicitly requested, place it elsewhere and mark it non-normative.

## Document Selection

Use:

- `spec` for durable behavior, actors, scenarios, domain contracts, public
  contracts, compatibility constraints, acceptance criteria, and verification;
- `adr` for a confirmed durable architecture decision whose rationale,
  alternatives, and consequences matter;
- `spike` for an active unresolved question requiring research before a product
  or architecture decision.

Do not create:

- one spec per project, namespace, directory, class, endpoint, or test;
- specs for ordinary dependencies, coding style, private architecture shape, or
  DI wiring;
- ADRs solely because a framework or library is present;
- spikes for every low-confidence observation;
- task, migration, implementation, or cleanup documents under `.idd/intent/`.

## Relationship To Other Skills

Use `idd-project-init` first when the repository has not been initialized.

Use `idd-intent-import` when source documents already express product knowledge
that can be normalized without reconstructing meaning primarily from code.

Use `idd-intent-brainstorm` to define future direction rather than recover the
current product.

Use `idd-intent-new-document` for a normal focused new owner after bootstrap.
Bootstrap may create its approved initial document set directly.

Use `idd-code-update-intent` for later narrow transfer of explicitly confirmed
implementation behavior into an established model.

Use `idd-code-check-implementation` after bootstrap for a separate conformance
review against the newly confirmed intent.

## Output

Before approval, return discovery and proposal summaries in the conversation,
then request the applicable blocking decision.

After apply, report:

- scope inspected and product parts confirmed;
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
- scope and product boundaries were explicitly confirmed;
- every blocking decision used `request_user_input` in Codex or
  `AskUserQuestion` in Claude Code when available, otherwise a plain-text
  question followed by an immediate stop;
- no tool-call JSON schema was embedded in the skill's question descriptions;
- no final response was emitted while a blocking decision was pending;
- implementation evidence was not treated as product intent automatically;
- user-supplied context was not copied as workflow history;
- technical choices were included only at their confirmed durable level;
- accidental patterns, ordinary dependencies, source paths, and coding style
  were excluded;
- conflicts remained visible and were not resolved silently;
- no current `IDD-NNNN` document was written before proposal approval;
- document boundaries follow durable product areas rather than repository
  structure;
- `.idd/intent/INDEX.md` matches actual current documents;
- all `IDD-NNNN` references are mechanically valid;
- `idd-intent-lint` reports no errors.

## Non-goals

This skill does not:

- prove that the current implementation is correct;
- perform a broad quality or architecture review;
- generate implementation documentation or a durable repository inventory;
- preserve every existing behavior;
- automatically lock the current technology stack;
- resolve product conflicts without the owner;
- implement product changes;
- start Factory;
- inspect historical Git state by default.