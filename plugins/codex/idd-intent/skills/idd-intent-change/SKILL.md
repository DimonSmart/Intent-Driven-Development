---
name: idd-intent-change
description: Update current `.idd/intent/` product intent for add, modify, and remove product operations, preferring existing specs over new specs.
---

# idd-intent-change

Use this skill when the user describes a desired product behavior change, new
capability, changed interaction rule, changed acceptance behavior, changed
default, or changed product constraint.

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
- First find whether the behavior belongs to an existing current spec.
- Prefer updating an existing current spec when the product area already exists.
- Do not create a new document locally. When the change defines a distinct
  durable product area, ADR, or spike, prepare a semantic handoff to
  `idd-intent-new-document`.
- Do not create a new spec for a local implementation task.
- Do not create a new spec for a small behavior change inside an existing
  feature area.
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
- Do not archive old specs.
- If behavior changes inside the same product area, edit the existing spec.
- If product area identity changes, delegate creation of the new owning spec to
  `idd-intent-new-document`; do not duplicate document-creation logic here.
- If a document becomes obsolete, duplicated, task-like, process-only, or
  incorrect, delete it.
- For `operation: remove`, delete an owning spec only when no current product
  intent remains in that document.
- Git history preserves previous versions.
- Update Behavior, Acceptance Criteria and Verification together when the change
  affects them.
- If the request contradicts current specs and the user clearly asks for the new
  behavior, update the spec to the new intent and mention the superseded
  behavior in the report.
- If the request is ambiguous, report the ambiguity and ask for the smallest
  product decision needed.
- Keep the change normative: describe observable product behavior, not patch
  mechanics.
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

This operation is separate from document ownership. Adding behavior can update
an existing spec. Removing behavior can update part of a spec or delete the
whole owning spec only when that document has no remaining current intent.

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

Use `new-spec-required` only when no existing current spec owns the product area
and the change describes durable product behavior. This classification must be
delegated to `idd-intent-new-document`.

Use `adr-required` when the change is primarily a durable architecture decision;
delegate creation to `idd-intent-new-document`.

Use `spike-required` when the right product or architecture decision requires
research; delegate creation to `idd-intent-new-document`.

Use `delete-owning-spec` for `operation: remove` only when the removed document
does not own any remaining current product intent.

Use `task-only-no-idd-intent-change` when the request is only a local refactor,
cleanup, dependency update, or implementation detail that does not change
durable product intent.

For `task-only-no-idd-intent-change`:

1. Do not change `.idd/intent`.
2. Report that the request is not a product change.
3. Recommend `idd-code-implement(mode: preserve-current-intent)` for focused
   work.
4. Recommend Factory for orchestrated work.
5. Do not force the request into `add`, `modify`, or `remove`.

## Removal Rules

For `operation: remove`:

1. Find the current owner.
2. Find dependent specifications and cross-references.
3. Determine whether the product needs immediate removal, deprecation,
   compatibility transition, or replacement.
4. Remove obsolete behavior from current intent.
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
3. Identify the product area and candidate current specs.
4. Read only relevant current numbered specs.
5. Classify the operation.
6. Classify the ownership outcome.
7. If the ownership outcome is `task-only-no-idd-intent-change`, stop without
   editing `.idd/intent` and report the implementation-work recommendation.
8. If an existing spec owns the area, update that spec instead of creating a
   duplicate.
9. If `new-spec-required`, `adr-required`, or `spike-required`, prepare a
   semantic handoff and invoke `idd-intent-new-document`; do not create the
   document locally.
10. If the change affects behavior, update acceptance criteria.
11. If the change affects testable behavior, update verification.
12. Report:

    - operation;
    - ownership outcome;
    - primary owner;
    - affected specifications;
    - changed behavior;
    - preservation boundary;
    - document changes;
    - recommended implementation depth;
    - recommended next skill.

`existing-spec-update` means this skill updates the current owning document.
The three new-document classifications always hand off creation to
`idd-intent-new-document`, which repeats the ownership check before writing.

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
- read `.idd/intent/0018.spec-command-history-completion.md`;
- update visible-panel command completion behavior;
- update acceptance criteria and manual verification;
- do not create a new spec.
