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

- `spec`: durable observable product behavior, domain contracts, acceptance
  criteria, verification properties, shared behavior, and product-significant
  constraints.
- `adr`: durable architectural decision, its rationale, alternatives, and
  tradeoffs. Preserve the ADR decision lifecycle; accepted ADRs are not
  semantically rewritten. A changed decision gets a new ADR and supersedes the
  old one.
- `spike`: active research, experiment, or hypothesis check before a product
  or architecture decision. A spike does not silently establish the outcome.

Never create a spec for a local task, dependency update, ordinary refactor, or
a small change owned by an existing spec. Do not create a replacement spec
merely to preserve old wording.

## Ownership Check (Mandatory)

1. Read `.idd/intent/README.md`.
2. Read `.idd/intent/INDEX.md`.
3. Read the relevant current `IDD-NNNN` documents directly under
   `.idd/intent/` (not just their index rows).
4. Recheck the owning product area and any shared or adjacent owner against the
   proposed durable change.
5. For `new-spec-required`, if a current spec already owns the area, **do
   not create a new spec**. Return control to the current
   `idd-intent-change` execution to update the existing owner. Do not
   invoke it recursively or emit another skill handoff.
6. For `adr-required`, verify that a distinct architectural decision or
   supersession warrants a new decision record. For `spike-required`, verify
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

1. Use the matching canonical template installed in
   `.idd/intent/_templates/spec.md`, `.idd/intent/_templates/adr.md`, or
   `.idd/intent/_templates/spike.md`. Those
   templates originate from
   `src/canonical/project-files/intent/_templates/`; do not invent a
   parallel template or a new document schema.
2. Create exactly the justified document under `.idd/intent/`, replace the
   template identifier/title consistently in its filename and first heading,
   and populate only current normative content. Never write implementation
   plans, temporary task notes, progress logs, chat history, or private
   implementation shape.
3. For a spec, preserve the canonical Behavior, Acceptance Criteria,
   Verification, related-document and ownership semantics. Verification
   describes important user scenarios, critical invariants, meaningful
   boundary cases, and justified manual checks, not test-method names,
   private classes, or a test per sentence.
4. For an ADR, record context, decision status, alternatives, consequences and
   `Supersedes` relations where relevant. For a spike, retain the question,
   constraints, method, result and recommendation structure; do not fabricate
   research results.
5. Update `.idd/intent/INDEX.md` in the same change. Its `Document` column
   contains the stable plain-text `IDD-NNNN` identifier, not a path,
   filename or Markdown link. Keep its role, area, notes and replacements
   consistent with the new document, and use full `IDD-NNNN` references in
   normative relations. Update affected cross-references only when necessary.
6. Check the result against the established `idd-intent-lint` mechanical
   consistency requirements. Do not relax ownership, numbering, formatting or
   normative relation rules to make the document pass.

## Return To Parent

Return the created file, ID, document type, ownership result, INDEX changes,
related-document changes and any remaining blocking issue to the **current**
`idd-intent-change` execution. That parent remains responsible for final
behavior, acceptance, verification, preservation and (if Factory invoked it)
coverage checks against the unchanged original request.

Do not invoke a child agent, re-enter `idd-intent-change`, or create a new
user-facing skill handoff.
