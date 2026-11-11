# IDD Routing Core

This is the **normative authority for common route selection, scope, handoff,
and stop conditions**. `common-workflows.md` owns the details *inside*
individual workflows; it is not mandatory reading for every route.
`skill-descriptions.json` separately owns machine-readable `exposure`,
`invocation`, inventory and platform metadata. Do not keep an independent
list of manual-only skills here.

## Decision priority

1. A directly invoked named skill (even `idd-skip`) bypasses the router,
   but not that skill's own gates; never select `idd-skip` automatically.
2. A standalone Factory model-policy request takes the direct configure
   shortcut **before** ordinary classification. If it is one task among
   independent Product/Engineering/code/audit tasks, use ordinary routing.
3. For other requests classify the **original user's requested work**, not
   wrapper instructions governing the router's output or execution. Apply the
   dimensions below, choose the smallest scope, then apply approval, clarity,
   preservation and host availability gates. Evaluate special-purpose gates
   **before** the generic classification-to-first-skill table: an explicitly
   unresolved visual-reference decision uses the visual discovery gate, not
   the generic Product brainstorm gate.
4. `Expected complete workflow` describes the full safe lifecycle, not
   permission to execute it. Only `Current handoff` authorizes the next
   caller-owned step; the router itself is read-only.
5. If required input, authority or a reference is absent, stop and diagnose,
   rather than invent missing constraints, routes or installed capabilities.

## Routing Dimensions

### What Changes

Identify the requested concern before applying ordinary workflow classification.
Standalone Factory model-policy requests select their skill directly:

- `product truth`: desired product behavior, confirmed normative visual
  appearance, constraint, acceptance rule, public contract, or durable
  architecture changes.
- `engineering policy`: an explicit project decision adds, modifies, or removes
  a durable implementation-only Engineering Rule.
- `implementation only`: implement an already-approved Product Intent requirement,
  or change code structure while the normative product intent remains unchanged.
  New behavior in **code** that fulfills an existing approved requirement is not
  by itself a new Product Intent decision.
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
- `Factory model configuration and review`: project-owned Factory model
  mappings or reasoning settings need initial setup, exact edits, inspection,
  comparison, refresh, upgrade, downgrade, or inheritance changes. For this
  standalone concern, select `idd-factory-configure` directly instead of
  creating a classification.
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

When the user asks to **implement an already-approved existing requirement**
(e.g., a named `IDD-NNNN` spec), classify `implementation-change` and select
`idd-code-implement`, unless the user independently requests a change to the
normative product requirement. Implementing approved behavior may change what
code currently does; it does not change what the *product is required to do*.
Do not route through `idd-intent-change` solely because the new implementation
will make that approved behavior observable. If the referenced specification is
missing, contradictory or insufficient to implement, preserve the implementation
classification but block/clarify the missing authority before code; never invent
or silently rewrite Product Intent.

### Product and Engineering Operation

For `product-change`, classify `Operation` as `add`, `modify`, or `remove` and target current product truth under `.idd/intent/`.

For `engineering-change`, use the same operation names independently and target current durable implementation policy under `.idd/engineering/`.

Product operations and Engineering operations do not share semantic ownership. A product constraint must not become Engineering merely because it mentions implementation, and an implementation-only durable convention must not be forced into Intent.

Adding a behavioral or visual product rule can still update an existing spec. Removing one product rule can still leave the owning spec in place when it contains other current intent. Engineering add may become a no-op or an explicit modify when an existing Rule already owns the durable constraint.

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

For new product or Engineering decisions, distinguish a defined outcome from a
mere feature/policy label. A request naming only a capability without stating
what users can do or observe, or naming an Engineering concern without an
enforceable constraint, lacks sufficient normative meaning. Do not supply missing
defaults or acceptance decisions on the user's behalf. In a mixed Product +
Engineering + implementation request, assess **both** durable owners: if either
has an unresolved material decision, use `Clarity: ambiguous` and
`idd-intent-brainstorm` before starting durable mutations. The complete
workflow still retains both owners and implementation after confirmation.
Conversely, an explicit observable contract such as "users can export the
report as CSV" may be `clear` without prescribing implementation details.

For an Engineering-only request with a named but underspecified constraint
(e.g., "make logging stricter"), retain `Classification: engineering-change`
and `Clarity: ambiguous`, but select its semantic owner `idd-engineering-change`.
That skill must identify the missing enforceable decision and return `ambiguous`
before any mutation; `idd-intent-brainstorm` clarifies *Product Intent*, not
standalone Engineering policy. Do not silently invent an Engineering Rule.

For mixed Product + Engineering + implementation work, when a new Product
capability is identifiable, keep `Classification: product-change` even if its
behavior or the separate Engineering decision is underspecified. In that
case, `Clarity: ambiguous` and `Recommended first skill: idd-intent-brainstorm`
establish the Product clarity gate; the complete workflow retains the
Engineering owner and authorized implementation. Use `Classification: unclear`
only when the primary concern itself cannot be identified.

### Requested Scope

Classify how much of the workflow the user authorizes in the current request:

- `route-only`: classify and describe the workflow without invoking another
  skill or changing files.
- `intent-only`: perform only durable IDD knowledge-side work, including
  product Intent or Engineering management. Do not implement product code or
  start Factory execution.
- `intent-preserving`: implement/check against the approved Product Intent without changing normative product decisions or `.idd/engineering/*`. Factory Workers alone may add verified monotonic refinements in existing Intent owners; ordinary `idd-code-implement` retains its local restrictions.
- `end-to-end`: continue through all requested workflow stages.

Requested scope is independent from what changes and from execution depth. Use
the narrowest scope that satisfies the **original user's** request; preserve
explicit limits such as "only", "do not implement" and "do not change specs".

Distinguish three different instructions:
- A user asks **which workflow to use**, requests routing advice only, or
  explicitly forbids subsequent handoff: `route-only`.
- A caller, test harness or automatic-routing wrapper says **output only the
  route**, **do not invoke the skill from inside the router**, or **do not
  modify files while classifying**: these govern this read-only router's
  behavior and output. They do **not** reduce the original task's requested
  scope to `route-only` or suppress an otherwise authorized handoff.
- A user requests **read-only work** (audit, lint check, review) rather than
  route advice: route to the appropriate read-only skill; ordinarily
  `end-to-end` means completing that read-only task, not writing files.
  "Do not change files" alone is not "only tell me the route".

For a request limited to creating, importing, changing or normalizing **IDD
knowledge**, use `intent-only` unless code implementation is explicitly
requested. This covers Product Intent, Engineering Rules, and
`implementation-to-intent`. A request to add, change or remove a **requirement
or specification** is knowledge-side work, even when that requirement describes
observable product behavior. Do not infer implementation authorization just
because the user did not expressly forbid code changes. A user asking to "import into IDD" authorizes
knowledge migration and its conflict/validation/lint stages, **not** code
implementation. Keep an unavailable import source as an explicit handoff
blocker without broadening the scope. For implementation-only work preserving
approved Intent/Engineering use `intent-preserving` when requested;
use `end-to-end` for authorized implementation plus its necessary checks.
A refactor with no intended behavior change is `intent-preserving`, including
when the user explicitly forbids modifying specs and Engineering Rules; the
refactor and its verification still run. Implementing a confirmed requirement
already owned by an approved current spec, while leaving normative Product
Intent unchanged, is also `intent-preserving`; implement and verify against
that owner without automatically entering an Intent mutation workflow. A prohibition on spec edits **alone**
does not authorize changing product behavior or establish preservation intent.

Project initialization is `end-to-end` when the user asks to carry out the
complete IDD structure setup: completing the requested setup does not imply
reverse-engineering Product Intent or implementing product code. Do not treat
its lack of application implementation as a reason to select `intent-only`.

Initial bootstrap is normally `intent-only`. It becomes `end-to-end` only when
the user also explicitly requests later implementation work after the initial
intent has been confirmed. Likewise, undecided visual inspiration requests
authorize **read-only visual discovery only**: classify `intent-only` to
express the knowledge-side boundary and stop after discovery. Do not infer
permission to record normative visual Intent or implement a design from
"try", "use this screenshot" or similar exploratory wording.

The complete lifecycle describes what is eventually needed for safe delivery.
It does not grant permission to execute stages outside the requested scope.

Do not assign requested scope when an explicitly named skill or `idd-skip`
bypasses routing.

### Execution Depth

Classify execution depth independently from the product operation and requested
scope. **First decide whether execution depth applies at all.** For
`intent-bootstrap`, including reconstruction and a proposal for user approval,
and for Engineering Rule management without requested code changes, the value
is **always `not-applicable`**. Neither a multi-stage discovery workflow nor
a single Engineering Rule modification is implementation execution.
Do not use `orchestrated` to describe bootstrap's discovery/approval stages,
or `focused` merely because one Engineering Rule is being changed. As a final
field consistency invariant: if `Classification: engineering-change` and the
request is policy-only (`Requested scope: intent-only`), `Execution depth` MUST
be `not-applicable` regardless of the rule count, `Clarity` or operation.
Only after ruling out these knowledge-only categories choose a depth for
eligible Product/implementation work:

- `focused`: one bounded owner/change and localized implementation, without
  complex migration, staged rollout or multiple independent approval gates.
  A single additive public feature does not automatically require orchestration.
- `orchestrated`: multiple subsystems or independent tasks, migration,
  compatibility transition or potentially breaking public contract changes,
  high regression risk, sequenced phases, **multiple independent durable
  Product/Engineering owners with separate review gates**, or major capability
  removal.
- `not-applicable`: routing, initialization, verification configuration,
  bootstrap, imports, audits, lint checks, brainstorms, and knowledge-only
  Engineering Rule management without separately requested implementation.
  Adding or modifying one Engineering Rule is not a `focused` implementation
  just because the policy targets source code.
  An actionable Product requirement may still describe the depth of a later
  implementation stage even when the present scope is `intent-only`.

A broad repository scan does not make bootstrap `orchestrated`. Factory
planner/worker orchestration preserves approved Product decisions and all
Engineering Rules. A new entry may coordinate normal authorized owners in
preflight; only Factory Workers may enrich existing Intent owners with
verified monotonic findings during implementation.

Diff size alone is not enough to choose Factory. Execution depth may describe a
later implementation stage even when the current requested scope stops at
routing or intent work. `orchestrated` does not itself invoke or require the
optional Factory plugin. When an undecided visual discovery is the **only**
authorized step, use `not-applicable` rather than an implementation depth.

## Audit scope, decisive distinctions and safety gates

For an ordinary classified route, return `Classification`, `Operation`,
`Clarity`, `Execution depth`, `Requested scope` and `Audit scope`.
Only `product-change` and `engineering-change` have
`Operation: add | modify | remove`; all other classifications use
`Operation: not-applicable`. Only `intent-drift-audit` uses
`Audit scope: focused | project-wide`: project-wide needs an **explicit**
whole-project conformance request; otherwise use focused. All other routes
use `Audit scope: not-applicable`. These fields are entirely *omitted*
for standalone Factory model-policy routing (no invented placeholders).

- `verification-configuration` means deliberately editing project-owned
  `.idd/verification.yaml`, not running checks, fixing tests or updating
  acceptance criteria. Policy advice is `route-only`; authorized edits are
  `end-to-end`.
- `intent-bootstrap` discovers current product truth from implemented code
  with inadequate current Intent; `intent-import` migrates already-expressed
  durable knowledge, including *explicit* established Engineering decisions.
  Neither is a normal add operation; both have `Execution depth: not-applicable`.
  Mixed initial evidence requires their specific gate, not silent substitution.
- New specs/ADRs/product spikes are `product-change` through
  `idd-intent-change` or an ambiguity gate. A durable Engineering rule must
  be an explicit authorized decision; recurring code patterns are evidence,
  not authority.
- `intent-lint` is `mode: check` unless unambiguous **mechanical** repair
  is expressly allowed, then `mode: fix`. Changing the lint skill's code is
  `implementation-change`. Semantic duplicates and ownership are
  `intent-structure-audit`; substantial moves/splits/deletion are
  `intent-normalization`; Glossary is manual-only. Generic "make docs
  current" is not broad permission for semantic migration.
- A bug with explicit expected behavior and localized implementation defect
  may go straight to focused `idd-code-implement` and verification, even
  without numbered Intent. Drift audit is needed when intent must establish
  correct behavior, a public/durable contract may change, the behavior may
  be deliberately different, or conformance is explicitly requested. If the
  requested product behavior differs, update its durable owner first.
- A `product-change` with visual references but unconfirmed visual meaning,
  scope or strictness must first use read-only
  `idd-intent-visual-brainstorm` to establish Essential/Preferred/Free/Avoid
  traits and exceptions, then **stop**. This specialized gate **takes
  precedence** over generic `idd-intent-brainstorm` when the specific
  outstanding question is which traits of a visual reference to preserve,
  including a request to add a new page using a screenshot as inspiration.
  The generic Product brainstorm applies to unresolved product goals or
  behavior, not in place of this visual-trait clarification. Set `Requested scope: intent-only`
  and `Execution depth: not-applicable` for this discovery-only task,
  regardless of whether the request says "use" or "create" a visual style.
  Do not claim `end-to-end` permission for later stages. Later Intent writing
  is separately authorized. Already-confirmed visual Intent (including an
  approved "save it" handoff) goes straight to `idd-intent-change`.
  Screenshots of bugs,
  actionable fidelity targets and local styling that preserves current
  Intent do not automatically require brainstorm. Stop for a Product
  decision if local styling risks an Essential invariant.
- `route-only` executes nothing, `intent-only` stops before code/Factory,
  and `intent-preserving` allows no new normative Product or Engineering
  decision. **Only Factory Workers** may apply verified monotonic refinements
  to *existing Intent owners*; ordinary implementers and Planner cannot.
  `end-to-end` retains every required owner approval and safety gate.
- Manual-only skills may be recommended but never automatically invoked:
  consult installed invocation metadata/`skill-descriptions.json`.
  Direct skill invocation is user selection, not the result of this router.
  The router never executes an advised skill or any Factory preflight.
  Engineering-only ambiguity stays with `idd-engineering-change`: its own
  semantic planning gate asks for clarification before changing any Rules.
- `idd-factory-run` owns mandatory preflight under the existing
  `intent-preflight.md`. The router must not inspect Factory state broadly,
  duplicate preflight, or change policy. Missing known Factory installation
  makes `Current handoff: none` with a dependency explanation; unknown
  availability remains conditional. No substitute runner is authorized.

## Direct Factory Model-Policy Route

**Before ordinary classification**, when the sole independent task concerns
project-owned Factory model policy, recommend `idd-factory-configure` directly:
initial configuration, exact `codex`/`claude` `economy | standard | strong`
model ID or reasoning-settings edits, return to host inheritance, read-only
assignment review/comparison/proposal, or evidence-based model refresh,
upgrade, downgrade, cost/latency/quality selection or replacement.

The selected skill owns mode selection, model evidence, validation,
confirmation and any write. Router must not inspect or edit
`.idd/execution.yaml`, research models, start Factory or execute the
recommended skill. An explicit invocation of `idd-factory-configure` bypasses
routing. Do not take this shortcut for `idd-factory-run`, unrelated project
configuration, Product Intent, Engineering, audits or implementation.

If model policy is **one of several independent tasks**, use the general
classification in this Core and the relevant detailed workflow sections. Keep the model-policy stage and every
other authorized stage in the complete workflow and handoff. An unclear
primary workflow uses the normal clarity gate, not an invented type.

Standalone model-policy output contains ONLY `Recommended first skill`,
`Expected complete workflow`, `Requested scope`, `Current handoff`,
`Stop after` and, where useful, `Why`, `Handoff`, and constraints.
It has NO `Classification`, `Operation`, `Clarity`, `Execution depth`
or `Audit scope` fields, including fictitious not-applicable values.

For routing advice use `route-only` and no handoff. For requested
configuration **or read-only review** use `end-to-end` and hand off to
`idd-factory-configure` when available; that scope does not itself permit
writing. Do not mechanically assign `intent-only`/`intent-preserving`
to a standalone configuration task: carry exact restrictions as constraints
or diagnose incompatible scope. No second router confirmation is required
for an already authorized exact edit; model selection retains configure's
own approval gates.

Preserve exact platform, profile, model ID, reasoning settings, inheritance,
review-only/write prohibition, goals and prior user decisions in the original
request handoff; never substitute a summary.

`idd-factory-configure` is in the separate `idd-factory` plugin.
A recommendation does not establish installation. If known unavailable,
keep the recommended skill but set `Current handoff: none` and stop with
the dependency diagnostic. If unknown, state availability conditionally in the free-text rationale or
handoff instructions, not as a claimed installed capability. Keep
`Current handoff` as a **single canonical skill ID** if conditionally allowed,
or `none` if a required dependency is known to be missing; do not append
"if available" or other explanatory text to this structured field.
Never fabricate an availability API, substitute another skill or write policy
from the router.

## First Skill

For an **advice-only** request asking which IDD workflow handles a specific
operation, identify its actual capability owner before applying the ordinary
classification-to-skill index. Consult registered skill metadata when the
matching workflow is manual-only. A manual-only skill may be **recommended**
without being invoked; do not substitute a generic Product Intent change just
because the question uses verbs such as "create" or "update".

In particular, questions about which workflow creates, reviews, or updates
the optional project glossary must recommend `idd-glossary-build`, not
`idd-intent-change`. For "which workflow?" / "advise only" requests set
`Requested scope: route-only`, `Current handoff: none`, and stop after the
recommendation. Do not invoke a manual-only command on the user's behalf.
Keep the ordinary route output fields; this recommendation does not create
a new classification or authorize glossary changes.

For ordinary classified routes, use the following first-skill index **only
after** evaluating special-purpose gates (including the unconfirmed
visual-reference gate) and explicit Engineering ownership. The standalone
Factory model-policy route above is selected before the table. In particular,
the generic `product-change` entry does **not** override the mandatory
`idd-intent-visual-brainstorm` first step when visual traits remain undecided:

| Classification | Recommended first skill |
| --- | --- |
| `project-initialization` | `idd-project-init` |
| `verification-configuration` | `idd-verification-configure` |
| `intent-bootstrap` | `idd-intent-bootstrap` |
| `intent-import` | `idd-intent-import` |
| `product-change` | `idd-intent-change` |
| `engineering-change` | `idd-engineering-change` |
| `implementation-change` | `idd-code-implement` or Factory |
| `intent-normalization` | `idd-intent-normalize-current` |
| `intent-structure-audit` | `idd-intent-structure-audit` |
| `intent-lint` | `idd-intent-lint` |
| `intent-drift-audit` | `idd-intent-drift-audit` |
| `implementation-to-intent` | `idd-code-update-intent` |
| `explicit-skip` | `idd-skip` |
| `unclear` | `idd-intent-brainstorm` or a spike handoff |

Never select `idd-skip` automatically. Use it only when the user explicitly
refuses IDD for the request.

## Complete workflows and the current boundary

The following are **minimal expected safe lifecycle patterns**, not an
authorization to run every stage. Specialized checks and owner-level semantics
remain in the detailed workflow sections indexed below.

| Classification | Complete lifecycle and decisive stop |
| --- | --- |
| `project-initialization` | `idd-project-init`; optional `idd-intent-bootstrap` only with consent when implementation exists without current Intent; optionally Factory model setup only if Factory requested or project state shows earlier Factory use. Initialization-only ends after init. |
| `verification-configuration` | `idd-verification-configure` review/edit/no-op; no test execution implied. |
| `intent-bootstrap` | `idd-project-init` if needed -> `idd-intent-bootstrap` discovery -> explicit boundary and semantic approval -> write current Intent -> `idd-intent-lint`; optional later scoped drift audit. Block before unapproved writes. |
| `intent-import` | After verifying supplied sources: `idd-project-init` if needed -> `idd-intent-import` inventories and classifies existing Product / explicit Engineering knowledge -> identifies conflict/owner gates before mutations -> applies only safe changes -> directly performs Mechanical Intent Validation and, when applicable, Mechanical Engineering Validation -> reports blocked decisions and validation. No reverse-discovery or separate mandatory `idd-intent-lint` invocation. |
| `product-change` | Clarity/visual discovery if needed -> `idd-intent-change` (add/modify/remove) -> `idd-code-implement` or optional Factory when implementation authorized -> focused `idd-intent-drift-audit`. Stop before code for `intent-only`. |
| `engineering-change` | `idd-engineering-change` -> direct Mechanical Engineering Validation; implementation/checks only if independently requested. Active Factory blocks Engineering mutation. |
| `implementation-change` | `idd-code-implement` preserving current Intent, or optional `idd-factory-run` for orchestrated execution -> verification -> applicable scoped conformance. Stop at missing durable decision/approved preservation boundary. |
| `intent-normalization` | Optional structure audit -> targeted `idd-intent-normalize-current` proposal/confirmation/apply -> `idd-intent-lint`; preserve Product meaning. |
| `intent-structure-audit` | `idd-intent-structure-audit` read-only; no automatic normalization. |
| `intent-lint` | `idd-intent-lint` check, or explicitly approved mechanical fix -> recheck; no semantic mutations. |
| `intent-drift-audit` | `idd-intent-drift-audit` read-only with explicit focused or project-wide scope; no automatic implementation. |
| `implementation-to-intent` | `idd-code-update-intent` only for user-confirmed behavior, with owner's semantic checks; code alone is not authority. |
| `explicit-skip` | No automatic route or handoff. Only direct explicit `idd-skip` invocation bypasses IDD. |
| `unclear` | `idd-intent-brainstorm` or focused spike/research; stop at unresolved owner decision. |

Mixed Product + Engineering + implementation requests keep *both* durable
owners before implementation and normally have `Execution depth: orchestrated`
because their distinct semantic owners and approval gates must be sequenced.
If the decisions are underspecified, route first to `idd-intent-brainstorm`
as a clarity gate, **without** discarding the Engineering owner or later code
stages. Mixed Factory model-policy + code requests keep
the separate `idd-factory-configure` stage and the implementation stages.
Large new tasks may have Factory coordinate the authorized owners during its
own preflight, but only those owners can mutate durable decisions.

`Recommended first skill` and `Current handoff` are structured IDs: use exactly
one registered `idd-*` skill name, or `none` for `Current handoff`. Put availability
conditions, blockers and explanations in `Why`, `Stop after`, or `Handoff`,
never inside an ID field.

`Current handoff` is the next user-authorized action after scope, clarity,
manual-only, availability and owner gates; use `none` for `route-only`,
missing required input/reference/dependency, or a blocker. The caller may
continue an already-authorized `end-to-end` lifecycle without *new routing*
approval but must respect subsequent semantic approvals.

In particular, `intent-import` requires actual source content in the original
request or an accessible, resolvable supplied document. A reference to a
"supplied", "attached" or previously discussed source that is not actually
available is **not** an import input. Keep `Classification: intent-import`,
`Requested scope: intent-only` when only IDD migration was requested, and
the full future lifecycle; set `Current handoff: none` and `Stop after` to
the missing-source blocker. Do not hand off to the import skill to discover
or invent material that the user has not supplied.

`Stop after` is the narrowest requested completion point or earliest blocker
(uncertainty, permission, research, dependency, verification). An Engineering-
only request has no invented implementation step. Read-only Factory
model-policy review can legitimately use `end-to-end` with **no writes**.

A temporary **Preservation boundary** records changed/unchanged behavior,
public contracts, compatibility/data constraints and unresolved questions.
A **Discovery boundary** for bootstrap records included/excluded product
areas, provided temporary context, known contracts and unresolved scope.
Neither becomes a separate durable Intent document. Always pass the full
original request, every independent task and prohibition to the caller;
a route summary never replaces the request.

## Selective detailed-workflow index

Read the mandatory Core first. Search for exact headings below, then read
**only the relevant complete sections and their subheadings**, plus explicitly
required linked sections. Do not preload all detailed workflows to decide
which one to load. Multiple independent tasks require the *union* of their
relevant sections, preserving stage order.

| Stable heading in `common-workflows.md` | Select when |
| --- | --- |
| `## Shared Invariants` | Cross-layer Engineering / Factory invariant is needed beyond basic route safety |
| `## Workflow Family: Project Initialization` | Conditional bootstrap offer, existing Factory state, project initialization edge cases |
| `## Workflow Family: Verification Configuration` | Verification setup versus running/repairing checks is still unclear |
| `## Workflow Family: Factory Model Configuration and Review` | Complex/mixed model configuration, refresh or owner approval; not necessary for standalone simple routing |
| `## Workflow Family: Initial Intent Bootstrap` | Bootstrap boundaries, mixed bootstrap/import, initial product discovery gates |
| `## Workflow Family: Visual Intent Clarification and Maintenance` | Unconfirmed visual reference, complex visual scope, protected Essential traits |
| `## Workflow Family: Intent Import` | Mixed Product/Engineering sources, ambiguity or authority conflicts |
| `## Workflow Family: Product Change` | Nontrivial add/modify/remove, public compatibility, ADR, migration; includes `### Add Product Requirement`, `### Modify Product Requirement`, `### Remove Product Requirement` |
| `## Workflow Family: Engineering Management` | Existing Engineering owner, mixed policy/implementation, active Factory |
| `## Workflow Family: Implementation Change` | Missing preservation authority or specialized implementation/refactor; includes `### Refactor While Preserving Behavior` |
| `## Workflow Family: Intent Maintenance` | Lint fix vs normalization, semantic movement, special scope |
| `## Audit and Verification Separation` | Distinguishing lint, structure, drift or verification requires more detail |
| `## Bug and Mismatch Entry Points` | Unclear expected bug behavior, intentional change, missing Intent |
| `## Focused and Orchestrated Execution` | Complex Factory phases, preflight, owner handoff / sequencing |
| `## Preservation And Discovery Boundaries` | Detailed boundary beyond the compact route handoff |
| `## Workflow Completion Rules` | Specialized completion/verification and mixed workflow stopping |

When route and authority are clear, a standard request needs **only Core**.
Do not read a detailed section solely to confirm a fully determined standard
route (including a clear whole-project drift audit or a localized refactor).
Do not inspect a downstream skill's complete SKILL.md merely to classify a
route; the next skill owns its own implementation details. When a detailed
section is needed, read only its exact heading-delimited bounds, not a rounded
line range extending into an adjacent section.
If the host cannot partially read a detailed section, it may load the
whole `common-workflows.md` only when details are actually required;
record that as full-read fallback. Never evade a gate to save tokens.

Missing Core means an incomplete installation: stop. If a required detailed
section/reference is missing, inaccessible or corrupt, report its exact
name as a blocker. Do not replace its contents with remembered rules or
broaden authorized scope.
