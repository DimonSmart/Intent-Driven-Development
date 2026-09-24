# Common End-to-End Workflows

## Purpose

This document defines platform-independent IDD workflow routing. It describes
how natural-language requests move through initialization, verification
configuration, initial intent bootstrap, import, product intent, implementation,
checking, normalization, and optional Factory orchestration without making the
route itself durable product intent.

## Routing Dimensions

### What Changes

Classify the request by the thing that changes:

- `product truth`: desired product behavior, constraint, acceptance rule,
  public contract, or durable architecture changes.
- `engineering policy`: an explicit project decision adds, modifies, or removes
  a durable implementation-only Engineering Rule.
- `implementation only`: code structure changes while current product intent
  remains unchanged.
- `intent structure`: current intent is moved, split, merged, renamed, or
  cross-referenced without changing product meaning.
- `implementation versus intent`: observed behavior may not match current
  intent.
- `initial product truth discovery`: an existing implementation lacks an
  adequate current intent model and needs interactive discovery and owner
  confirmation.
- `raw imported knowledge`: external or existing product knowledge already
  expressed in documents or other sources needs to be imported into IDD intent.
- `project initialization`: the project needs an `.idd/intent/` structure.
- `project verification configuration`: `.idd/verification.yaml` needs creation or
  deliberate update.
- `unknown`: the request does not provide enough information to choose safely.

Initial product truth discovery and raw imported knowledge are different:

```text
existing implementation + uncertain product meaning
    -> idd-intent-bootstrap

existing source material that already expresses durable product meaning
or explicit durable Engineering decisions
    -> idd-intent-import
```

Raw imported knowledge remains an import route even when the same supplied
source contains both Product Intent and already-decided Engineering policy.
Ordinary new Engineering decisions still route as `engineering-change`.

Code may be evidence during both workflows, but import must not become an
implicit reverse-engineering workflow or derive Engineering policy from current
implementation.

### Product and Engineering Operation

For `product-change`, classify `Operation` as `add`, `modify`, or `remove` and target current product truth under `.idd/intent/`.

For `engineering-change`, use the same operation names independently and target current durable implementation policy under `.idd/engineering/`.

Product operations and Engineering operations do not share semantic ownership. A product constraint must not become Engineering merely because it mentions implementation, and an implementation-only durable convention must not be forced into Intent.

Adding product behavior can still update an existing spec. Removing product behavior can still leave the owning spec in place when it contains other current intent. Engineering add may become a no-op or an explicit modify when an existing Rule already owns the durable constraint.

Bootstrap establishes current product truth; it is not an `add` operation.
Import migrates supplied durable knowledge; it is not an ordinary product or
Engineering `add` operation. It may create/update Engineering Rules only for
explicit decisions already present in supplied import sources.
For classifications other than `product-change` and `engineering-change`, `Operation` is `not-applicable`.

### Request Clarity

Classify clarity as:

- `clear`: the desired workflow or product or implementation outcome is
  actionable.
- `ambiguous`: a product decision is needed before writing intent or code.
- `research-required`: the correct decision depends on investigation that
  should be represented as a spike or focused check.

An explicit request to discover and establish initial intent may be `clear`
even though the discovered product model still requires interactive
confirmation. The bootstrap workflow owns those semantic gates.

### Requested Scope

Classify how much of the workflow the user authorizes in the current request:

- `route-only`: classify and describe the workflow without invoking another
  skill or changing files.
- `intent-only`: perform only durable IDD knowledge-side work, including
  product Intent or Engineering management. Do not implement product code or
  start Factory execution.
- `implementation-only`: implement or check against current intent without
  changing product intent.
- `end-to-end`: continue through all requested workflow stages.

Requested scope is independent from what changes and from execution depth. Use
the narrowest scope that satisfies the explicit request. Explicit limits such
as "only", "do not change files", "do not implement", and "do not change
specs" take precedence over the normal complete lifecycle.

Initial bootstrap is normally `intent-only`. It becomes `end-to-end` only when
the user also explicitly requests later implementation work after the initial
intent has been confirmed.

The complete lifecycle describes what is eventually needed for safe delivery.
It does not grant permission to execute stages outside the requested scope.

Do not assign requested scope when an explicitly named skill or `idd-skip`
bypasses routing.

### Execution Depth

Classify execution depth independently from the product operation and requested
scope:

- `focused`: one primary product owner, localized implementation, no complex
  migration, no staged rollout, and no multiple review gates.
- `orchestrated`: multiple subsystems, independent implementation tasks,
  migration, compatibility transition, public contract change, high regression
  risk, sequenced phases, multiple roles, review gates, or major capability
  removal.
- `not-applicable`: routing, initialization, verification configuration,
  bootstrap, imports, audits, lint checks, brainstorms, and pure intent reads
  that do not execute implementation.

A broad repository scan does not make bootstrap `orchestrated`. Factory
orchestration is for implementation work and must not be used to create or
change product intent.

Diff size alone is not enough to choose Factory. Execution depth may describe a
later implementation stage even when the current requested scope stops at
routing or intent work.

## Shared Invariants

- Current `IDD-NNNN` documents directly under `.idd/intent/` are normative
  product intent.
- Optional `.idd/engineering/` contains current durable implementation
  guardrails, not product intent. Its absence is valid.
- Every Always Engineering Rule applies to implementation work. Relevant
  Conditional rules are selected semantically from `Applies when`; deterministic
  filename, keyword, path, extension, project-type, embedding, or similarity
  heuristics do not decide applicability.
- `.idd/verification.yaml` is project-owned operational configuration, not product
  intent or Engineering Rules.
- Git stores history.
- Product `add`, `modify`, and `remove` mutate only `.idd/intent/`.
- Ordinary Engineering `add`, `modify`, and `remove` target
  `.idd/engineering/` and are owned by `idd-engineering-change`.
- `idd-intent-import` may create or update Rules only while migrating explicit
  durable Engineering knowledge already present in supplied import sources.
- Other IDD skills may read or validate Engineering and may report or hand off
  explicitly confirmed candidates, but they do not independently mutate Rules.
- Repeated implementation patterns are evidence, not authority for creating
  durable Engineering policy.
- Implementation-only refactoring does not change product truth.
- Intent normalization does not change product meaning.
- Implementation evidence is not product intent by itself.
- Bootstrap findings remain temporary evidence until the user confirms the
  semantic proposal.
- Bootstrap must separate product behavior, public contracts, durable
  architecture decisions, replaceable technical preferences, incidental
  details, unknowns, and conflicts.
- No current numbered document is written by bootstrap before explicit proposal
  approval.
- Import uses existing product knowledge as evidence and does not reconstruct
  requirements primarily from code.
- Factory planners and workers may read intent, but must not create or change
  product intent. When Engineering exists, planners select only Conditional
  `TaskRelatedEngineering` IDs, the orchestrator mechanically enumerates the
  current Always set before each worker, and workers do not edit Engineering
  Rules. An end-to-end Factory run completes the separate Intent Preflight
  before native-agent orchestration starts.
- Plans, route classifications, preservation records, discovery reports,
  confirmation transcripts, and review notes are temporary workflow evidence.
- Obsolete ordinary specs are deleted, not archived.
- A new spec is created only when no current owner exists.
- Every executed workflow stage is checked against current intent where
  applicable.
- An expected complete workflow must never be interpreted as permission to
  exceed the current requested scope.

## Workflow Family: Project Initialization

```text
idd-project-init
-> create minimal project-owned IDD state
-> maintain one managed agent-instruction block
-> if Factory is explicitly enabled, offer idd-factory-configure model policy
-> detect existing implementation without current IDD-NNNN documents
-> offer optional idd-intent-bootstrap
```

Factory model configuration is never offered for an `idd-intent`-only
project. When Factory is explicitly enabled and no execution policy exists,
initialization offers current-model inheritance versus fine-grained profile
mapping and hands the choice to `idd-factory-configure`.

The bootstrap offer requires explicit user consent.

Initialization completes successfully when the user declines bootstrap. The
initialization skill itself must not infer or write current numbered intent.

Do not offer bootstrap for an empty new product, a project that already has
current numbered intent, or an explicit initialization-only request.

## Workflow Family: Verification Configuration

Use this workflow to create or deliberately update project-owned verification
rules:

```text
idd-verification-configure
```

Do not use it to run checks, fix failing tests, or define product acceptance
criteria.

## Workflow Family: Initial Intent Bootstrap

Use this workflow when a meaningful implementation exists but reliable current
intent does not.

```text
idd-project-init if needed
-> idd-intent-bootstrap
-> confirm repository and product-part boundaries
-> discover candidate product behavior and contracts
-> classify technical choices and implementation details
-> resolve or expose conflicts
-> present proposed initial specs, ADRs, and active spikes
-> explicit user approval
-> write the minimum current intent model
-> idd-intent-lint
```

Optional follow-up:

```text
-> idd-code-check-implementation
```

The optional conformance check is not permission to modify code.

Bootstrap uses adaptive breadth-first repository discovery followed by focused
reading. It must not begin by copying the repository structure into intent.

The user may provide temporary information such as:

- product purpose;
- project roots or modules to include;
- legacy, generated, experimental, or migration areas to exclude;
- external documents;
- known compatibility contracts;
- whether a language, framework, database, protocol, deployment model, or other
  technical choice is durable, replaceable, incidental, or unresolved.

Persist only confirmed durable meaning. Do not save the discovery inventory,
source map, conversation, confidence scores, or temporary instructions as
current product intent.

When a technical choice could constrain future implementations, classify it as
one of:

```text
durable product or compatibility contract
accepted architecture decision
replaceable implementation preference
incidental current detail
unresolved
```

Do not ask about every dependency. Ask only when the answer changes product
behavior, public compatibility, supported environments, security, operability,
or an intentional architecture boundary.

A conflict between plausible current sources must remain visible until the user
decides or an active spike resolves it. Never choose the implementation over
documentation, tests, or owner input automatically.

Bootstrap completes only when:

- project boundaries are confirmed;
- the semantic proposal is explicitly approved;
- the minimum current intent documents are written;
- `INDEX.md` matches the actual document set;
- `idd-intent-lint` reports no errors.

If the user cancels before approval, no current numbered intent is created and
the workflow still returns the discovery summary as temporary output.

## Workflow Family: Intent Import

Use this workflow when existing supplied material already expresses durable
knowledge:

```text
idd-project-init if needed
-> idd-intent-import
-> classify Product Intent + explicit Engineering knowledge
-> normalize + direct mechanical validation
-> report conflicts, blocked changes, and review material
```

Typical sources include requirements, specifications, ADRs, public contracts,
normative technical-design material, documented engineering conventions,
research notes, product documentation, relevant acceptance tests, and confirmed
operational behavior.

Import is a migration of meaning. It may create or update Engineering Rules only
for explicit durable decisions already established by the supplied source. It
does not choose a new technical solution and does not automatically treat every
old document or implementation detail as current product truth.

When the main task is to infer what an undocumented implemented product is,
route to bootstrap rather than import.

## Workflow Family: Product Change

Product changes use `idd-intent-change` first. The ownership outcome is decided
there: existing spec update, new spec required, ADR required, spike required,
delete owning spec, or unclear product intent.

### Add Behavior

```text
brainstorm if the request is unclear
-> idd-intent-change(operation: add)
-> existing owner or new document handoff
-> idd-code-implement or Factory
-> idd-code-check-implementation
```

### Modify Behavior

```text
find current owner
-> idd-intent-change(operation: modify)
-> identify changed and preserved behavior
-> idd-code-implement or Factory
-> idd-code-check-implementation
```

### Remove Behavior

```text
find owner and dependent scenarios
-> idd-intent-change(operation: remove)
-> identify immediate removal, transition, or replacement
-> remove obsolete intent
-> remove or change implementation
-> verify removed behavior is absent
-> verify dependent scenarios remain preserved
-> run idd-intent-lint when the document set changes
```

When removing behavior, check references from other specs, shared contracts,
public APIs, data formats, saved settings, migration or deprecation
requirements, remaining intent in the owning document, and whether a durable
non-goal is needed.

For `intent-only`, stop after the intent-side stage and report the remaining
complete lifecycle without starting implementation. For `route-only`, do not
start the product-change workflow at all.

## Workflow Family: Engineering Management

Use this workflow only for an explicit durable implementation-only project decision:

```text
explicit durable engineering decision
-> idd-engineering-change(operation: add | modify | remove)
-> direct Mechanical Engineering Validation
```

`idd-engineering-change` is the standard mutation owner for ordinary explicit
Engineering management requests. One request may contain one or more durable
decisions; it plans the complete semantic batch before mutation, groups decisions
into coherent Rules, and allocates IDs only to new Rules. It may lazily create
the Engineering layer only after planning confirms a real add, but
`idd-project-init` does not create it. The workflow does not infer Rules from
current code patterns. The separate bounded `idd-intent-import` exception
applies only while migrating explicit durable decisions already present in
supplied import sources.

When the same user request also asks to change implementation, the complete lifecycle may continue:

```text
idd-engineering-change
-> idd-code-implement or Factory
-> idd-code-check-implementation
```

The Engineering mutation happens first. An active Factory run marked by `.idd/factory/current/request.md` blocks the mutation; management does not rewrite or re-plan active Factory state automatically.

For `intent-only`, stop after Engineering management and direct Mechanical Engineering Validation. For `route-only`, do not start the management workflow.

## Workflow Family: Implementation Change

### Refactor While Preserving Behavior

```text
read relevant intent
-> identify preservation boundary
-> idd-code-implement(mode: preserve-current-intent) or Factory
-> verification
-> idd-code-check-implementation
```

If implementation work reveals that product behavior must change, stop the
implementation workflow and route to `idd-intent-change`. Do not silently
expand an `implementation-only` request into an intent change.

If adequate current intent is missing for the affected product area, stop and
route to bootstrap, import, or the applicable intent workflow. Do not infer a
preservation boundary solely from code.

## Workflow Family: Intent Maintenance

### Normalize Current Intent

```text
idd-intent-audit if the problem is broad
-> choose a concrete focus
-> idd-intent-normalize-current --mode propose
-> check for semantic movement
-> idd-intent-normalize-current --mode apply
-> idd-intent-lint
```

Normalization may change ownership, location, grouping, references, and
document boundaries. It must not change product behavior, constraints,
exceptions, acceptance criteria, compatibility contracts, or non-goals.

## Bug and Mismatch Entry Points

A concrete bug-fix request does not automatically require an intent or
conformance pass before implementation. When the user reports an observed
failure and asks to fix it, first determine whether the expected behavior is
already clear from the request and focused implementation context.

- If expected behavior is clear, the defect is localized, and there is no sign
  that the fix changes product truth, a public contract, compatibility, or a
  durable architecture boundary: investigate the implementation directly,
  apply `idd-code-implement` or an equivalent focused code fix, and run relevant
  verification. Do not run `idd-code-check-implementation` first merely because
  the request is a bug report.
- Use `idd-code-check-implementation` before implementation when current intent
  is actually needed to determine what correct behavior is, when expected
  behavior is unclear, when the implementation may represent a deliberate
  product change, when the fix touches a public or durable contract, or when the
  user explicitly asks for conformance checking.
- If implementation matches current intent but the user wants different
  behavior: `idd-intent-change(operation: modify)`, then implementation, then
  check.
- If no adequate current intent exists and the requested work would establish or
  change durable product behavior: use `idd-intent-bootstrap` when broad initial
  discovery is needed, or `idd-code-update-intent` only for a narrow behavior
  the user already confirms. A narrow implementation bug whose expected behavior
  is explicitly supplied by the user does not require bootstrap solely because
  numbered intent is absent.

A bug is not a separate top-level workflow family. Route through intent or
conformance workflows only when they are needed to decide or protect durable
product meaning; otherwise prefer the smallest focused implementation workflow.

## Focused and Orchestrated Execution

Use focused execution when one implementation pass can safely satisfy current
intent. Use optional `idd-factory-run` for coordinated multi-task
implementation, sequencing, temporary planning, or high-risk preservation
boundaries. Factory remains optional and must not become a dependency of
`idd-intent`.

The logical request is materialized self-contained and stored in
`.idd/factory/current/request.md`. Before a new run, apply Intent Preflight:

```text
logical request + requested scope + relevant current intent
-> AlreadyCovered | ExplicitIntentChange | MissingIntentDecision | ImplementationOnly
-> optional intent update
-> coverage validation
-> native-agent Factory orchestration
```

The Factory semantic loop is intentionally small:

```text
fresh planner
-> current batch
-> fresh sequential worker per task
-> fresh planner
```

Each planner and worker receives a fresh isolated semantic context rather than
the root transcript. Workers share the repository but do not share transcripts.
The repository is authoritative implementation reality.

Planner tasks may optionally classify required execution capability as
`economy`, `standard`, or `strong`; missing metadata means `standard`. The
planner never reads model policy. Immediately before each worker spawn, the root
agent mechanically maps the profile through optional project-owned
`.idd/execution.yaml` and either inherits host behavior or applies the exact
configured native model/settings. It never substitutes another model or
reclassifies the task.

Temporary continuation state is limited to the original request, remaining
current batch, short completed summaries, exact user answers, and optional
question/verification-failure artifacts. There is no Factory-owned workflow
state machine, process supervisor, MCP transport, retry budget, or exact
continuation protocol.

Factory uses at-least-once execution. An interrupted task may execute again; a
fresh worker inspects current repository reality and converges from whatever
correct partial work already exists.

When the planner returns `# Done`, run configured project verification. A
bounded verification failure becomes input to a new fresh planner. A successful
verification completes Factory. Planner `# Question` persists one concrete
question and stops until the user answers; durable-intent handling remains an
ordinary outer IDD workflow.

Do not start or resume implementation when requested scope is `route-only` or
`intent-only`. With `implementation-only`, Factory can run only when current
intent is already sufficient.

## Preservation And Discovery Boundaries

Implementation and product-change workflows should identify temporary
preservation evidence:

- Behavior expected to change.
- Behavior expected to remain unchanged.
- Public contracts to preserve.
- Compatibility or data constraints.
- Unresolved preservation questions.

Bootstrap should identify a temporary discovery boundary instead:

- Repository or product areas included.
- Areas excluded or probably non-product.
- User-provided temporary context.
- Known public or compatibility contracts.
- Unresolved scope questions.

Neither boundary is saved as a standalone `.idd/intent/` document. Durable
preserved behavior belongs in ordinary behavior, acceptance criteria,
constraints, verification, or non-goals of the owning current spec. Durable
bootstrap findings belong in confirmed specs or ADRs only after proposal
approval.

## Complete Workflow and Current Handoff

Routing must distinguish:

- the `Expected complete workflow`, which describes the safe lifecycle;
- the `Current handoff`, which is allowed in the current request;
- the `Stop after` boundary, which follows requested scope and clarity gates.

For `route-only`, the current handoff is none. For `intent-only`, stop before
implementation. For `implementation-only`, do not modify intent. For
`end-to-end`, continue through the complete workflow unless ambiguity, required
research, missing intent, verification failure, or another safety gate blocks
progress.

A bootstrap handoff carries scope and temporary discovery context, but the
bootstrap skill must still ask for product-boundary confirmation and semantic
proposal approval.

## Workflow Completion Rules

Complete the current request when all stages inside its requested scope have
finished and their applicable checks pass. Do not claim that the complete
product lifecycle has finished when the request intentionally stopped at
routing or intent work.

Verification configuration completes after the confirmed policy is written, or
a deliberate review concludes that no change is required.

For `end-to-end`, product changes complete after intent is updated and coverage
is validated against the materialized logical request, implementation is
performed, and `idd-code-check-implementation` verifies changed, removed, and
preserved behavior. Engineering-only management completes after the requested
mutation or semantic no-op and clean structural validation; it does not imply
repository implementation changes. End-to-end Engineering requests continue to
implementation only when that implementation work was explicitly requested.
Implementation-only work completes after verification proves
current intent was preserved. Normalization completes after semantic movement
is checked and `idd-intent-lint` passes.

Initialization completes after project-owned state and the managed instruction
block are correct, even when bootstrap is declined.

Bootstrap apply completes after semantic approval, current intent creation, and
a clean lint result. A discovery-only or cancelled bootstrap must not claim that
initial current intent was established.

Import completes after normalized current intent is written and lint passes.

If `idd-intent-lint` reports errors, the bootstrap, import, or normalization
workflow is not complete. Fix the errors or report them explicitly as unresolved
blockers. Do not present those workflows as completed while mechanical
consistency errors remain. Warnings may remain only when they do not indicate
mechanical inconsistency and are explicitly reflected in the report.
