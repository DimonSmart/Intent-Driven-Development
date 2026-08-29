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

existing source material that already expresses product meaning
    -> idd-intent-import
```

Code may be evidence during both workflows, but import must not become an
implicit reverse-engineering workflow.

### Product Operation

For product truth changes, classify the requested operation separately from
document ownership:

- `add`: introduce behavior, a constraint, an interaction, or a product rule.
- `modify`: change existing behavior, constraints, acceptance criteria, or
  product rules.
- `remove`: remove existing behavior, a capability, a contract, or a product
  rule.

Adding behavior can still update an existing spec. Removing behavior can still
leave the owning spec in place when it contains other current intent.

Bootstrap establishes current product truth; it is not an `add` operation.
Import normalizes source knowledge; it is not an `add` operation.

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
- `intent-only`: perform only intent-side work. Do not implement product code or
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
- `.idd/verification.yaml` is project-owned operational configuration, not product
  intent.
- Git stores history.
- Add, modify, and remove apply only to product truth changes.
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
- Factory may read intent, but must not create or change product intent.
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
-> detect existing implementation without current IDD-NNNN documents
-> offer optional idd-intent-bootstrap
```

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

Use this workflow when existing material already expresses product knowledge:

```text
idd-project-init if needed
-> idd-intent-import
-> structural normalization
-> conflict reporting
-> idd-intent-lint
```

Typical sources include requirements, specifications, ADRs, public contracts,
research notes, product documentation, relevant acceptance tests, and confirmed
operational behavior.

Import is a migration of meaning. It does not automatically treat every old
document or implementation detail as current product truth.

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
intent. Use optional `idd-factory-run` only for coordinated multi-task
implementation, sequencing, temporary planning, review gates, or high-risk
preservation boundaries. Factory remains optional and must not become a
dependency of `idd-intent`.

The original user request defines the Factory Task and is stored unchanged in
`request.md`. The packaged .NET Factory Runtime owns one resumable run under
`.idd/factory/current/`; `state.json` is authoritative and stable work-item
filenames do not encode status. Fresh semantic workers perform decomposition,
implementation, selective checkpoint reviews, bounded replanning, and final
review. Programmatic orchestration runs verification and writes a compact
commit-message handoff under `.idd/factory/results/` before safely clearing
`current/`. Neither directory is product intent, and both are ignored by
default.

Do not start or resume Factory work when requested scope is `route-only` or
`intent-only`. For `implementation-only`, Factory may be used only when current
intent is already sufficient and execution is orchestrated.

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

For `end-to-end`, product changes complete after intent is updated,
implementation is performed, and `idd-code-check-implementation` verifies
changed, removed, and preserved behavior. Implementation-only work completes
after verification proves current intent was preserved. Normalization completes
after semantic movement is checked and `idd-intent-lint` passes.

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
