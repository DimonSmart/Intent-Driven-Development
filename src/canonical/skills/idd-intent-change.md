# idd-intent-change

Use this skill when the user describes a desired change to durable Product
Intent: behavior, capabilities, interactions, acceptance behavior, defaults,
product constraints, or user-confirmed visual appearance. A visual requirement
may change product truth without changing functional behavior.

This skill updates `.idd/intent/` before implementation.

Formula:

```text
idd-intent-change =
    requested product operation
    + owning intent
    + minimal current-truth update
    + preservation boundary
```

## Rules

- Treat the user request as proposed product intent.
- First find whether the product requirement belongs to an existing current spec,
  including visual requirements owned by an otherwise behavioral spec.
- Prefer updating an existing current spec when the product area already exists.
- Create a new document only through the internal on-demand
  `references/new-intent-document.md` workflow when ownership classification
  requires a new spec, ADR, or spike. Ordinary existing-spec updates do not
  load that reference.
- Do not create a new spec for a local implementation task.
- Do not create a new spec for a small behavioral or visual change inside an
  existing product area; appearance alone is not a new ownership boundary.
- Do not put implementation steps, temporary notes, generated plans, or chat
  history into specs.
- Do not add build or test commands, implementation plans, private classes or
  methods from a proposed solution, source-file lists, dependency-injection
  wiring, migration checklists, or progress status to a spec.
- Verification describes important user scenarios, critical invariants,
  meaningful boundary cases, and justified manual checks. It must not become a
  catalog of test methods, internal classes, private implementation shape, or one
  automated test per specification sentence.
- When a request combines an intent change with implementation work, keep the
  spec implementation-independent and pass the concrete implementation focus to
  the next implementation skill rather than recording it as intent.
- When invoked by Factory Intent Preflight, keep the complete original request
  as authoritative input. Do not replace it with a route summary or generated
  implementation plan.
- Do not archive old specs.
- If behavior or a normative visual property changes inside the same product
  area, edit the existing spec.
- If product area identity changes, use the internal document-creation
  reference after confirming that a distinct new owner is required; do not
  duplicate document-creation instructions in this skill.
- If a document becomes obsolete, duplicated, task-like, process-only, or
  incorrect, delete it.
- For `operation: remove`, delete an owning spec only when no current product
  intent remains in that document.
- Git history preserves previous versions.
- Update the affected Behavior, optional Visual Intent, product constraints,
  Acceptance Criteria and Verification together when the change affects them.
  Do not delete unrelated functional, UX, accessibility, compatibility or
  other visual requirements.
- If the request clearly changes existing Product Intent, update the owning
  spec to the new current truth and mention the superseded requirement in the
  report. When a proposed implementation-only style change might violate an
  Essential visual invariant, ask whether the user intends a product change;
  never silently override the invariant.
- If the request is ambiguous, report the ambiguity and ask for the smallest
  product decision needed.
- Keep the change normative: describe observable product behavior or
  user-confirmed perceptual properties, not patch mechanics.
- Do not treat current implementation as product intent by itself.
- Before editing, identify what changes, what must be preserved, affected
  public contracts, compatibility constraints, and adjacent areas that remain
  out of scope.
- Do not add a mandatory `Preservation Boundary` section to every spec.
  Durable preserved contracts belong in normal Behavior, Acceptance Criteria,
  Constraints, Verification, or Non-Goals sections.

## Operation

Classify the requested product operation as one of:

```text
add
modify
remove
not-applicable
```

This operation is separate from document ownership. Adding behavior or a
visual property can update an existing spec. Removing one visual invariant does
not remove other requirements or the owning spec; delete it only if no current
intent remains.

Use `not-applicable` only when the ownership outcome is
`task-only-no-idd-intent-change`. Do not assign a fake `add`, `modify`, or
`remove` operation to a local implementation task that does not change product
truth.

## Classification

Classify the ownership outcome as one of:

```text
existing-spec-update
new-spec-required
adr-required
spike-required
delete-owning-spec
task-only-no-idd-intent-change
unclear-product-intent
```

Use `existing-spec-update` when an existing current spec already owns the product
area.

Use `new-spec-required` only when no existing current spec owns the product
area and the change describes durable product truth, including a confirmed
visual invariant without a functional behavior change. Create the owning spec
through the internal document-creation workflow only after its ownership check.
Do not create a separate visual spec merely because its requirement is visual.

Use `adr-required` for a confirmed durable architecture decision. Check the
architectural owner first. Update the owning ADR in place with its stable ID
when it covers the decision; create an ADR only for an independent ownerless
decision. Unresolved architecture uses the existing spike/decision workflow.
Ordinary implementation-local choices do not automatically require an ADR.

Use `spike-required` when the right product or architecture decision requires
research; create its investigation record through the internal workflow.

Use `delete-owning-spec` for `operation: remove` only when the removed document
does not own any remaining current product intent.

Use `task-only-no-idd-intent-change` when the request is only a local refactor,
cleanup, dependency update, or styling/implementation choice within current
Product Intent that does not change any durable product requirement.

For `task-only-no-idd-intent-change`:

1. Do not change `.idd/intent`.
2. Report that the request is not a product change.
3. Recommend `idd-code-implement(mode: preserve-current-intent)` for focused
   work.
4. Recommend Factory for orchestrated work.
5. Do not force the request into `add`, `modify`, or `remove`.

## Visual Product Intent: Confirmation and Persistence

Visual Intent is one aspect of Product Intent, not a new storage layer, status
system or mandatory Factory stage. A confirmed visual property can be normative
without a functional behavior change. Use the existing four operations and
ownership outcomes; never create a `visual-change` operation or classification.

When receiving a `Specification-Ready Visual Intent` handoff from
`idd-intent-visual-brainstorm`, use its human-readable decisions, not a new
JSON/YAML contract. For **each** candidate, establish as applicable:

- the exact user-confirmed property and its significance;
- whether it is `Essential`, `Preferred`, `Free`, or `Avoid`;
- product/screen/surface/component/state/theme scope and agreed exceptions;
- the level of obligation, permissible alternatives, and implementation freedom;
- specifically rejected traits, plus any source role (`inspiration`,
  `fidelity-target`, `anti-reference`, `current-state`) and the precise
  source aspect that matters;
- a meaningful perceptual acceptance observation or verification method;
- open questions and contradictions, **separate** from approved requirements.

A screenshot, comparison-card choice, unqualified 'I like this', or design
hypothesis does not approve every feature of the reference. Ask only the
smallest question required for a material missing meaning, scope, strictness,
or blocking conflict. Conversely, if the user approved the proposal or
unambiguously asks to save previously confirmed handoff, persist it without a
repeat discovery/approval round. A new session needs the handoff or an
accessible approved current requirement; do not reconstruct confirmation from
the image.

Interpret category strength in the **written current specification**:

- `Essential`: mandatory within its scope; violations fail conformity.
- `Preferred`: preferred direction with expressly allowed justified
  alternatives, **not** an unconditional hard acceptance gate.
- `Free`: implementation freedom, normally not stored unless it clarifies
  another constraint and prevents over-specification.
- `Avoid`: preserve the agreed degree of prohibition or reluctance; use
  `Non-Goals` only for genuinely excluded product outcomes/approaches.

The text must stand on its own for a Coding Agent with only the repository:
describe scope, relative emphasis, exceptions, alternatives and strength
without requiring card IDs, screenshots, chat history or a UI framework.
Exact colors, shapes or numbers are legitimate *when their exact identity is
explicitly product-significant*; otherwise avoid invented CSS/XAML values,
tokens, private classes and presentation files. Keep independent engineering
conventions in `.idd/engineering/` and retain existing ADR/spike boundaries;
do not migrate old constraints automatically.

References support meaning, never replace it. Do not record OS temp paths,
temporary HTML galleries or expiring URLs. For `fidelity-target`, identify
the specific reproducible aspect, scope, permissible deviations and a durable
resolvable reference (for example a versioned tracked asset). If a precise
image-dependent requirement cannot be independently expressed or checked
without a missing durable reference, **block that precise part** and request
the reference; other confirmed self-contained properties can be saved.

Find the owner by **product area and applicability**, not by a 'Visual'
filename or heading. Reuse the current owning spec; for genuinely ownerless
durable visual truth use the existing `new-spec-required` internal reference.
For cross-area rules, establish one primary owner and align dependent specs
without cloning normative text. Put perceptual constraints in an optional
`## Visual Intent` section or existing appropriate section; interactive
visual states may belong to `## Behavior`. Never require or retrofit a
`## Visual Intent` heading in every spec. Observable mandatory results
belong in Acceptance Criteria and justified checks in Verification.

For `modify` or `remove`, locate and reconcile the previous rule, scoped
exceptions, linked acceptance/verification checks and dependent references.
Replace obsolete current truth, rather than appending a contradictory version.
If Essential becomes Preferred, remove hard pass/fail checks for that property
while retaining meaningful evaluation of the preference. Preserve untouched
behavior, UX, accessibility, compatibility, and visual rules. Git, not the
specification, holds the revision history.

## Removal Rules

For `operation: remove`:

1. Find the current owner.
2. Find dependent specifications and cross-references.
3. Determine whether the product needs immediate removal, deprecation,
   compatibility transition, or replacement.
4. Remove obsolete behavior or visual constraints from current intent and
   reconcile scope-specific exceptions and related acceptance/verification.
5. Preserve remaining compatibility requirements.
6. Preserve a durable non-goal only when the removed behavior defines an
   intentional product boundary.
7. Delete the whole owning spec only if no current intent remains in it.
8. Do not archive removed specs.
9. Update `INDEX.md` when the document set changes.
10. Recommend `idd-intent-lint` when the document set changes.

## Workflow

1. Read `.idd/intent/README.md`.
2. Read `.idd/intent/INDEX.md`.
3. Identify the product or architectural area and candidate current owners.
4. Read relevant current specs, ADRs and related references.
5. Identify the requested current-truth change, its scope and confirmation;
   for visual handoff apply the Visual Product Intent gate above.
6. Classify the operation (`add`, `modify`, `remove`, `not-applicable`).
7. Classify the ownership outcome by product area and applicability.
8. If `task-only-no-idd-intent-change`, stop without editing
   `.idd/intent` and report the implementation recommendation. Do not
   mistake local styling that preserves the current visual contract for a
   Product Intent change.
9. For `existing-spec-update`, update the owning spec. For `adr-required`,
   update an existing architectural owner directly, keeping its ID, or create
   a new ADR only for an independent ownerless decision. Preserve useful
   findings and remove obsolete normative claims. Never infer acceptance from
   a code/ADR discrepancy. For `spike-required`, investigate the question
   without creating a speculative ADR.
10. If `new-spec-required`, `spike-required`, or independent ownerless
    `adr-required`, read `references/new-intent-document.md` lazily and follow its complete
    internal creation workflow inside this same execution. Recheck ownership;
    if the owner already exists, return to step 9, not a recursive invocation.
    Preserve ordinary IDs, templates, `INDEX.md`, and lint invariants.
    Use the already confirmed Product Intent, without a child agent or additional confirmation solely for document creation.
11. Reconcile old requirements, scope exceptions, related specs, applicable
    Acceptance Criteria and Verification. Do not harden `Preferred`/`Free`
    into unconditional acceptance tests; do not leave obsolete checks after
    `modify` or `remove`.
12. Check the normative wording independently of any visual gallery, chat
    history, framework or reference-specific shorthand. Only make a durable
    image-dependent fidelity claim when the source remains resolvable.
13. When invoked by Factory Intent Preflight, validate the resulting current
    intent against the unchanged original request: requested behavior or
    visual requirements, material non-goals, safety/durability and compatibility
    boundaries. Stop for a genuinely missing durable decision.
14. Report:

    - operation;
    - ownership outcome;
    - primary owner and affected specifications;
    - changed behavioral/visual product truth, strength, scope and exceptions;
    - preservation boundary (including untouched adjacent contracts);
    - document changes and synchronized Acceptance Criteria/Verification;
    - blocked/unknown requirements, if any;
    - recommended implementation depth and next skill;
    - preflight coverage as `Covered` or `MissingIntentDecision` when
      Factory Intent Preflight was requested.

`existing-spec-update` means this skill updates the current owning document.
The three new-document classifications use the lazy internal reference
`references/new-intent-document.md`, which repeats the ownership check before
writing. `idd-intent-change` owns final semantic consistency for both
existing and newly created documents. A request to save intent only stops
here after validation, without implementation or Factory.

## Example

Bad request wording:

```text
Add ModalDialogHost to SearchDialog constructor and run dotnet test.
```

Good specification wording:

```text
Search dialogs participate in the shared modal composition lifecycle and retain
their state across viewport changes.
```

User request:

```text
When command-line completion is visible, Enter should not automatically accept
the first history suggestion. The default selected item should mean "no
completion"; Enter should execute the typed command unchanged. A real suggestion
is accepted only after the user explicitly selects it with keyboard or mouse.
```

Expected behavior:

- classify as `existing-spec-update`;
- read `.idd/intent/IDD-0018.spec-command-history-completion.md`;
- update visible-panel command completion behavior;
- update acceptance criteria and manual verification;
- do not create a new spec.
