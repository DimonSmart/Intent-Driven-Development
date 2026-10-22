# Internal Intent Document Creation Workflow

This is an internal, on-demand document-creation workflow owned and executed
inside `idd-intent-change`. It is not a public skill, separate agent, or
another route. Read and execute this complete reference only after classifying
the ownership outcome as `new-spec-required`, `adr-required`, or
`spike-required`. For an existing owner, stay in the parent workflow without
loading this reference.

## Input From idd-intent-change

Use the already-established Product Intent decision and the complete original
request. The parent supplies the operation and ownership classification,
requested or inferred document type, product or decision area, durable intent,
reason a current document is not the owner, related documents, acceptance or
decision context, preservation boundaries, and any open questions.

Do not ask for a new confirmation solely because creation is internal.
If a durable product or architectural decision remains unresolved, block and
return the smallest missing decision to the parent. Do not silently fill gaps.

## Document Type

The request may explicitly say `type: spec | adr | spike`. Respect that type
only if it fits the current IDD document semantics. If it conflicts with those
semantics, explain the mismatch and use the appropriate type; block if the
choice requires a missing durable decision.

- `spec`: durable observable product behavior **or confirmed visual product
  properties** (including appearance without a functional behavior change),
  domain contracts, acceptance criteria, verification properties, shared
  behavior, and product-significant constraints.
- `adr`: a confirmed current architectural decision, its rationale,
  alternatives and tradeoffs. When an ADR already owns this architectural area,
  update it directly instead of entering the creation workflow.
- `spike`: active research, experiment, or hypothesis check before a product
  or architecture decision. A spike does not silently establish the outcome.

Never create a spec for a local task, dependency update, ordinary refactor,
local styling within current Product Intent, or a small behavioral **or
visual** change owned by an existing spec. A visual label is not an owner. Do not create a replacement spec
merely to preserve old wording.

## Ownership Check (Mandatory)

1. Read `.idd/intent/README.md`.
2. Read `.idd/intent/INDEX.md`.
3. Read the relevant current `IDD-NNNN` documents directly under
   `.idd/intent/` (not just their index rows).
4. Recheck the owning product area and any shared or adjacent owner against the
   proposed durable behavioral or visual change; do not select ownership by
   a document's name or whether it already has a `## Visual Intent` section.
5. For `new-spec-required`, if a current spec already owns the area, **do
   not create a new spec**. Return control to the current
   `idd-intent-change` execution to update the existing owner. Do not
   invoke it recursively or emit another skill handoff.
6. For `adr-required`, verify that a confirmed independent architectural
   decision has no existing ADR owner. If an ADR owns the area, return to the
   parent to edit it in place with the same ID. A revision is not a new owner. For `spike-required`, verify
   that a genuine unresolved research question warrants a spike, rather than
   duplicating an existing active investigation. An existing behavior-owning
   spec alone does not prohibit a justified ADR or spike.
7. If ownership or the durable decision cannot be resolved safely, stop and
   return the blocking question to the parent. Do not allocate an ID.

## Stable ID Allocation

Use the shared `IDD-NNNN` sequence for specs, ADRs, and spikes. The canonical
filename is `IDD-NNNN.type-short-title.md`. The first Markdown heading must
start with the same `IDD-NNNN.type-short-title`. Never use a bare
`NNNN.type-short-title.md` or bare numeric normative relation.

Before allocating an ID, inspect both:

- all current `.idd/intent/IDD-NNNN.*.md` filenames;
- all historically allocated `IDD-NNNN` identifiers in accessible Git
  history, **including deleted documents** (not only files in HEAD).

Allocate `max(previously allocated NNNN) + 1`, retaining the established
zero-padded numbering. Do not reuse deleted IDs; do not inspect or create an
archive directory. If available history cannot establish a trustworthy next
ID, stop and request resolution rather than guessing or choosing a potentially
conflicting number.

## Create And Normalize

1. Use the matching project template installed in
   `.idd/intent/_templates/spec.md`, `.idd/intent/_templates/adr.md`, or
   `.idd/intent/_templates/spike.md` **as a starting point**. These are
   initially installed from `src/canonical/project-files/intent/_templates/`.
   Project-owned edits may omit or rearrange optional sections. Neither
   canonical nor custom templates are validation schemas. Do not invent a
   parallel schema or reject a usable project template merely because it
   omits a field required in the final document.
2. Create exactly the justified document under `.idd/intent/`, set the
   canonical identifier/type in its filename and first heading, and populate
   only current normative content. Use semantically relevant sections or
   another readable structure; neither copy every template heading nor pad
   missing sections with `None`, `TBD` or invented facts. Never write
   implementation plans, temporary task notes, progress logs, chat history,
   or private implementation shape.
3. For a spec, express relevant Behavior, Acceptance Criteria,
   Verification, related-document and ownership semantics wherever readable;
   none requires a same-named H2. Add optional `## Visual Intent` only where
   relevant. Preserve confirmed visual scope, obligation strength, allowed
   alternatives and exceptions. Verification describes important user scenarios,
   critical invariants, meaningful boundary cases and justified manual checks,
   not test-method names, private classes or a test per sentence.
4. For an ADR, record the confirmed current decision, context, rationale,
   alternatives, consequences and useful experimental findings where known.
   Do not write status or supersession metadata and do not require specific
   H2 headings. If the architecture is unresolved, return the missing decision
   to the parent. For an active spike, preserve
   the question and known constraints/method, and include results and
   recommendations only when known. Do not fabricate research outcomes.
5. Update `.idd/intent/INDEX.md` in the same change. Its `Document` column
   contains the stable plain-text `IDD-NNNN` identifier, not a path,
   filename or Markdown link. Keep its role, area and notes consistent
   with the new document while preserving unrelated custom columns, and use full `IDD-NNNN` references in
   normative relations. Update affected cross-references only when necessary.
6. Check the result as a document against `idd-intent-lint` and the
   `references/document-types.md` type contract, never for heading-by-heading
   template equality. Ensure mandatory structural identity, INDEX and current normative relations
   without inventing status, supersession links or optional headings. Do not relax any
   invariant to make the document pass. Review semantic completeness separately.

## Return To Parent

Return the created file, ID, document type, ownership result, INDEX changes,
related-document changes and any remaining blocking issue to the **current**
`idd-intent-change` execution. That parent remains responsible for final
behavior, acceptance, verification, preservation and (if Factory invoked it)
coverage checks against the unchanged original request.

Do not invoke a child agent, re-enter `idd-intent-change`, or create a new
user-facing skill handoff.
