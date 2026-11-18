# Common End-to-End Workflows

## Purpose

Detailed platform-independent procedures, shared domain invariants and
workflow-specific completion checks. The normative selection, classification,
requested scope, first skill, current handoff and stop policy lives in
`routing-core.md`, packaged for `idd-route` and `idd-help`.
This document is consulted *selectively* when a specific workflow needs it;
it does not independently redefine route selection.

## Routing Dimensions (owned by Routing Core)

For the authoritative dimensions and selection priorities, consult
`routing-core.md` through the routing skill's packaged reference.
The following sections own workflow-specific procedures, not routing policy.

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
- Factory entry preflight may coordinate required Product Intent preparation
  through normal Intent workflows and explicit Engineering preparation through
  `idd-engineering-change` before active Factory state exists. Factory planners
  and Workers preserve approved normative Product decisions; Planner is
  read-only and only Factory Workers may monotonically refine existing Intent
  owners. Neither may modify Engineering Rules. When Engineering exists, planners select only
  Conditional `TaskRelatedEngineering` IDs, the orchestrator mechanically
  enumerates the current Always set before each worker, and workers do not edit
  Engineering Rules.
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
-> if Factory is explicitly requested or existing Factory state is present, offer idd-factory-configure model policy
-> detect existing implementation without current IDD-NNNN documents
-> offer optional idd-intent-bootstrap
```

Factory model configuration is never offered merely because `idd-factory` is
installed. Treat Factory as relevant to initialization only when the user
explicitly requests Factory workflows or existing Factory-specific project state
such as `.idd/execution.yaml` or `.idd/factory/` demonstrates prior use. When
Factory is relevant and no execution policy exists, initialization offers
current-model inheritance versus fine-grained profile mapping and hands the
choice to `idd-factory-configure`.

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

## Workflow Family: Factory Model Configuration and Review

Use `idd-factory-configure` for initial setup, exact model/reasoning edits,
read-only comparisons, and lifecycle refreshes (cheaper/faster/stronger,
upgrades, downgrades, or unavailable-model replacements):

```text
idd-factory-configure
-> read and validate existing policy or absence
-> recognize exact / initial / review-only / refresh intent
-> research alternatives only if selection is requested
-> propose the smallest authorized edit or read-only report
-> confirm automatically selected mappings
-> re-check baseline, validate and persist or no-op
```

The standalone/mixed Factory policy routing, field format, plugin availability
and scope gates are defined by `routing-core.md`. Actual policy inspection,
confirmation and editing belong to `idd-factory-configure`, as follows.

Only `.idd/execution.yaml` stores project-owned Factory model policy.
Missing policy and absent mappings inherit host settings. A review request
never creates a policy; an exact authorized ID never triggers mandatory
research or repeat confirmation. Preserve untouched profiles, platforms and
reasoning values, including their absence. Automatically recommended or
interpreted models require confirmation. A known reasoning incompatibility
blocks the candidate without silently resetting effort. Malformed input
blocks unauthorized writes; intervening edits must not be lost; no-op does
not rewrite policy. Changes affect the next worker, not an existing worker
or planner classification.

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
-> idd-intent-drift-audit
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

## Workflow Family: Visual Intent Clarification and Maintenance

The read-only visual discovery gate, direct route for confirmed visual intent,
screenshot exception and handoff stop are defined by `routing-core.md`.
Within the discovery workflow, observations and hypotheses lead to user-confirmed
Essential / Preferred / Free / Avoid traits, scopes and exceptions.
Only a separately authorized Intent owner can write those commitments.

Comparison cards and screenshots support discovery but are not normative by
themselves; do not infer all visual traits or dynamic states from an image.
Preserve the source role and its relevant aspect only. The Visual Intent
catalog and Images folder are supporting inputs, never a new intent storage
root or mandatory handoff file.

A saved visual requirement has one product-area owner, regardless of a
`## Visual Intent` heading. `add`, `modify`, and `remove` use ordinary
`idd-intent-change` with the same ownership/INDEX/new-spec gates as behavior.
Scope changes, weaker preferences or removal must also reconcile old
Acceptance Criteria, Verification, exceptions and cross-references without
altering adjacent contracts. If the user requests local styling within
existing intent, use `implementation-change` and current implementation
workflow; a possible conflict with an Essential invariant requires clarification
before implementation, never a silent rewrite of Product Intent.

## Workflow Family: Intent Import

Use this workflow when existing supplied material already expresses durable
knowledge. Core owns the required-source handoff gate; this section describes
the internal migration lifecycle once a source is available:

```text
check that supplied source content is accessible
-> idd-project-init if needed
-> idd-intent-import
   -> inventory and classify Product Intent + explicit Engineering decisions
   -> resolve semantic owners and identify conflicts/blocked decisions
      before any unsafe mutation
   -> normalize and apply only safe Product/Engineering changes
   -> direct Mechanical Intent Validation and, where applicable,
      direct Mechanical Engineering Validation
   -> report conflicts, blocked changes, validation and review material
```

Conflict detection is a **pre-mutation semantic gate**; the report of any
remaining conflicts is produced after validation. The importer itself performs
Mechanical Intent Validation and applicable Mechanical Engineering Validation.
Do not add a second executable `idd-intent-lint` handoff merely to validate
the imported result; the import skill's own contract owns these checks.

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

## Workflow Family: Product Clarification and Brainstorm

These are optional **read-only** discovery stages, not new mandatory Factory steps or durable document types. Routing Core selects a semantic owner:

- idd-intent-clarify narrows an incomplete or over-specified Product requirement using available relevant Intent and targeted code/tests, adaptive customer discovery and symmetrical simplification. Its outcome is Clarification Required, Clarification Proposal, or Specification-Ready Intent with explicit confirmation status.
- idd-intent-brainstorm investigates alternatives and opportunities using bounded read-only sources, problem reframing, divergent ideas, counterarguments and optional convergence. The Brainstorm Decision Brief is temporary and may end without a winner.
- idd-intent-visual-brainstorm remains responsible for unconfirmed visual-reference traits.

Neither Clarify nor Brainstorm updates .idd/intent/, creates ADRs/spikes or starts implementation. User-confirmed actionable results may pass to idd-intent-change; under-specified selections to Clarify; technical uncertainties to the existing spike workflow. Preserve confirmed decisions, unanswered questions, original restrictions and all independent Engineering/implementation stages during handoff. Do not repeat established research or force a choice when the user asks to continue exploring.

## Workflow Family: Product Change

Product changes use `idd-intent-change` first. The ownership outcome is decided
there: existing spec update, new spec required, ADR required, spike required,
delete owning spec, or unclear product intent. An ADR-required decision reuses
an existing architectural owner in place when available. Creation is reserved
for a confirmed independent decision; unresolved choices use the existing
spike/decision workflow.

### Add Product Requirement

```text
idd-intent-clarify if the Product behavior is unclear
-> idd-intent-change(operation: add)
-> update existing owner or create new document internally in idd-intent-change
-> idd-code-implement or Factory
-> idd-intent-drift-audit
```

### Modify Product Requirement

```text
find current owner
-> idd-intent-change(operation: modify)
-> identify changed and preserved behavior
-> idd-code-implement or Factory
-> idd-intent-drift-audit
```

### Remove Product Requirement

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
complete lifecycle without starting implementation. This also applies to a
request solely to persist approved Visual Intent; do not start UI work or
Factory. For a visual change without a functional behavior change, retain
product-change routing and existing operation names. For `route-only`, do not
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
-> idd-intent-drift-audit
```

For one new large end-to-end request that simultaneously changes Product Intent,
contains explicit durable Engineering decisions, and requires orchestrated
implementation, `idd-factory-run` may coordinate those existing durable owners
during entry preflight. It passes the complete logical request to
`idd-engineering-change`, performs any required Product Intent mutation through
normal Intent workflows, validates durable coverage, and creates active Factory
state only after a fresh initial Planner yields a validated executable Task batch.
Before activation its questions may lead to further authorized durable-owner
work and renewed coverage validation; a `# Done` with successful final
verification needs no active run. Factory does not duplicate Engineering ownership,
equivalence, allocator, or mutation logic.

The Engineering mutation happens before active Factory state is created. An
already-active Factory run marked by `.idd/factory/current/request.md` blocks
Engineering mutation; management does not rewrite or re-plan active Factory
state automatically.

For `intent-only`, stop after Engineering management and direct Mechanical Engineering Validation. For `route-only`, do not start the management workflow.

## Workflow Family: Implementation Change

### Refactor While Preserving Behavior

```text
read relevant intent
-> identify preservation boundary
-> idd-code-implement(mode: preserve-current-intent) or Factory
-> verification
-> idd-intent-drift-audit
```

If implementation work reveals that product behavior must change, stop the
implementation workflow and route to `idd-intent-change`. Do not silently
expand an `intent-preserving` request into an intent change.

If adequate current intent is missing for the affected product area, stop and
route to bootstrap, import, or the applicable intent workflow. Do not infer a
preservation boundary solely from code.

## Workflow Family: Intent Maintenance

### Mechanical Intent check and repair

```text
Check IDD formal consistency -> idd-intent-lint mode: check (default, read-only)
Check and explicitly fix mechanical IDD errors -> idd-intent-lint mode: fix
```

Lint preserves all existing checks and reports remaining mechanical errors
after repair. Only uniquely determined technical repairs (stable filename
prefixes and H1, INDEX Document/Role cells, local links and formal relations,
missing canonical project bootstrap files, and managed heading/separator
syntax) can be fixed. There is no `dry-run` or automatic commit/rollback:
use `git diff` and `git status` to inspect the actual work. Scope is
project-owned `.idd/intent/` by default; optional Glossary and Engineering
validation remains read-only in both modes. A missing `.idd/intent/` belongs
to `idd-project-init`.

Lint never deletes obsolete documents, process reports or archives, makes
Product Intent decisions, edits `GLOSSARY.md`, or changes Engineering Rules
and allocator state. It reports those findings and hands them to their owning
semantic workflow. Existing write-workflows remain responsible for the
mechanical correctness of their own output without requiring a separate
user invocation or recursively dispatching lint.

### Normalize Current Intent

```text
idd-intent-structure-audit if the problem is broad
-> choose a concrete focus
-> idd-intent-normalize-current --mode propose
-> check for semantic movement
-> idd-intent-normalize-current --mode apply
-> idd-intent-lint
```

Mechanical filename, INDEX, H1, and normative-reference corrections are
owned by `idd-intent-lint` in `mode: fix`, not by a normalization pass.
Substantive deletion of archive/process-only documents, ownership remapping,
and split/merge/move remain deliberate semantic normalization.

Normalization may change ownership, location, grouping, references, and
document boundaries. It must not change product behavior, constraints,
exceptions, acceptance criteria, compatibility contracts, or non-goals.

## Audit and Verification Separation

Keep these responsibilities distinct:

```text
Intent mechanical consistency
    -> idd-intent-lint

Intent semantic organization
    -> idd-intent-structure-audit

Intent / implementation conformance
    -> idd-intent-drift-audit

Implementation technical verification
    -> .idd/verification.yaml
       consumed by implementation / Factory workflows
```

`idd-intent-drift-audit` is read-only and always uses an explicit audit scope.
The scope may be focused or explicitly project-wide. Project-wide audit remains
intent-driven and is not generic repository code review.

## Bug and Mismatch Entry Points

A concrete bug-fix request does not automatically require an intent or
conformance pass before implementation. When the user reports an observed
failure and asks to fix it, first determine whether the expected behavior is
already clear from the request and focused implementation context.

- If expected behavior is clear, the defect is localized, and there is no sign
  that the fix changes product truth, a public contract, compatibility, or a
  durable architecture boundary: investigate the implementation directly,
  apply `idd-code-implement` or an equivalent focused code fix, and run relevant
  verification. Do not run `idd-intent-drift-audit` first merely because
  the request is a bug report.
- Use `idd-intent-drift-audit` before implementation when current intent
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

Use focused implementation when one bounded pass can safely preserve
current Product Intent. Use optional `idd-factory-run` when multiple
tasks, sequencing, significant preservation risks or staged
implementation call for native Planner/Worker orchestration.
The intent plugin never requires Factory to be installed.

Factory entry uses a complete self-contained logical request and a
**Preflight before active implementation**. For orientation:

```text
complete original request + explicit scope
  -> analyze Product Intent and explicit durable Engineering concerns
  -> reject request-level blockers before writes
  -> independent new-run Git baseline + preparation checkpoints
  -> Engineering owner (when required), then Product Intent owner
  -> validate coverage of both durable layers and full request
  -> fresh isolated initial Planner while INACTIVE
       Question -> save one question + exact Q/A, renew preflight as needed
       Done     -> final verification, finish without activating
       Tasks    -> validate batch, prepare plan/answers/baseline/provenance
  -> write current/request.md LAST [ACTIVE]
  -> sequential native Workers + fresh isolated Planner
  -> final project verification + Intent Refinement Review
```

Factory's Product assessment distinguishes `AlreadyCovered`,
`ExplicitIntentChange`, `MissingIntentDecision`, and
`ImplementationOnly`. Its Engineering entry assessment distinguishes
`NoEngineeringManagementNeeded`, `EngineeringManagementRequired`,
and `EngineeringDecisionMissing`; Engineering Rule equivalence and
changes belong to `idd-engineering-change`, not Factory. Ordinary
unchosen implementation techniques do not create missing durable
decisions. Root checks host support and both registered native roles
before durable owner/Factory writes.

Missing durable Product/Engineering decisions, conflicts and unauthorized
scope expansion block before `preparation/` and owner mutations.
Analyze Product first, resolve an explicit Engineering owner second,
mutate authorized Product Intent third, then validate complete durable
coverage. An Engineering owner result of `blocked` or `ambiguous`
cannot activate Factory. Already authorized owner writes are not
automatically rolled back after interruption.

`current/request.md` alone marks an active run. Saved preparation is
recoverable without assuming an earlier write failed; active continuation
does not recreate the initial baseline. Explicit replacement must
preserve existing active state until successful preflight and cannot
change Engineering while that run is active. Partial activation,
unknown provenance or an interrupted child writer calls for conservative
recovery, not fabricated permission or automatic rollback. The
detailed baseline, request identity, provenance, recovery and initial
Planner protocol is defined in the canonical `intent-preflight.md`
document, packaged as a reference for `idd-factory-run`, **not**
for `idd-help` or `idd-route`. The diagram above provides
independent orientation for Help.

Each active cycle plans remaining work, runs one fresh registered Worker
at a time and re-plans in an isolated child context; workers share
repository state but not transcripts. Planner Tasks identify existing
relevant Conditional Engineering Rules; Root supplies the current
Always set before Workers. New Tasks require exactly one
`ExecutionProfile` (`economy`, `standard`, `strong`). Legacy
pending Tasks without a profile use `standard`; invalid explicit
profiles fail closed. Optional `.idd/execution.yaml` maps profiles
to native model settings; absent mappings inherit host settings,
invalid mappings block. Planner never chooses the model.

Factory is at-least-once: a recovering Worker inspects the actual
repository and converges from partial progress. `# Done` after
activation triggers final verification, and failures go back to a
fresh Planner with bounded evidence. A question stops until answered;
a new durable Engineering decision cannot be applied inside an
active run. The orchestration is not a new runtime or transaction.
Scope remains binding throughout: `route-only` and
`intent-only` cannot start Factory; `intent-preserving`
cannot mutate normative decisions, although authorized Factory
Workers may monotonically refine existing Intent owners.

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

## Complete Workflow and Current Handoff (owned by Routing Core)

See `routing-core.md` for the authoritative expected lifecycle, permitted
handoff, requested-scope and stop distinction. The following section documents
workflow-specific completion checks.

## Workflow Completion Rules

Complete the current request when all stages inside its requested scope have
finished and their applicable checks pass. Do not claim that the complete
product lifecycle has finished when the request intentionally stopped at
routing or intent work.

Verification configuration completes after the confirmed policy is written, or
a deliberate review concludes that no change is required.

For `end-to-end`, product changes complete after intent is updated and coverage
is validated against the materialized logical request, implementation is
performed, and a focused `idd-intent-drift-audit` with explicit caller-provided
scope verifies changed, removed, and preserved behavior. Engineering-only management completes after the requested
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
