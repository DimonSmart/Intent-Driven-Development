---
name: idd-intent-import
description: Migrate supplied durable knowledge into normalized Product Intent and explicit source-owned Engineering Rules, with safe semantic planning, lazy Engineering bootstrap, conflict blocking, and shared mechanical validation.
argument-hint: "[source roots] [--mode propose|apply-safe]"
user-invocable: false
disable-model-invocation: false
---

# idd-intent-import

Migrate supplied durable knowledge into the current IDD state:
Product Intent in `.idd/intent/` and explicit, accepted, implementation-only
Engineering Rules in the optional `.idd/engineering/` layer.

Import **meaning**, not source file layout. The result describes the current
target product, not the history of tasks, migrations, or implementation.

Read `references/document-types.md` for canonical spec/ADR/spike semantics and
structure, and `references/engineering-guardrails.md` for the Intent/Engineering
boundary, Engineering import authority, Rule lifecycle, allocator, Factory guard,
and canonical Mechanical Engineering Validation. These references are normative;
do not reimplement or duplicate their contracts here.

## Modes

```yaml
mode: apply-safe # propose | apply-safe
autoNormalize: true
conflictMode: report-only
allowNewSpecs: true
```

- `propose`: report a complete migration proposal; make **no durable changes**
  to Intent or Engineering.
- `apply-safe` (default): apply only changes established unambiguously by
  authoritative supplied material. Do not invent decisions or silently resolve
  conflicts.
- `autoNormalize`, `conflictMode`, and `allowNewSpecs` retain their existing
  import behavior; default to the values above. A restriction on new specs does
  not permit bypassing required normalization or validation.

Import is not architecture design, reverse engineering, or ordinary Engineering
management. Use `idd-engineering-change` for explicit Rule management outside
source migration, `idd-intent-structure-audit` for broad diagnosis without
import, and `idd-intent-normalize-current` for later focused maintenance.

## 1. Read and classify supplied material

1. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and relevant current
   documents when present. Read only the supplied source roots and bounded
   context needed to interpret them.
2. Identify source conventions, document lifecycle, authoritative decisions,
   and current versus superseded material. Treat legacy status, dates, and
   numbering as evidence, not automatic authority.
3. If `.idd/engineering/` exists, read its README/INDEX and run the canonical
   Mechanical Engineering Validation **before** using its Rules as policy or
   planning Engineering mutations. A malformed or partial layer blocks
   Engineering mutation, not independent safe Product Intent import.
4. Classify **fragments**, not just whole files, into these outcomes:

| Outcome | Import treatment |
| --- | --- |
| Current product behavior, domain/public/API contracts, compatibility, security and operability requirements, acceptance/verification properties | Product Intent: spec, or existing owner |
| Confirmed current product-significant architectural decision | Living ADR or existing architectural owner |
| Active unresolved product/architecture research | Spike or Needs Review; never invent a decision |
| Explicit accepted, durable, implementation-only constraint intended to bind future implementations | Engineering Rule candidate |
| Operational build/test commands | Verification configuration candidate; do not write `.idd/verification.yaml` |
| Tasks, progress, refactors, temporary implementation detail, generated output, chat/history, obsolete or duplicate material | Skip; extract any independently durable requirement first |
| Conflicting or unclear authority/current meaning | Needs Review; do not choose a winner |

Product Intent answers what must remain true if the implementation is rebuilt.
Engineering constrains *how future implementations must be built*. A source
statement can establish a Rule without the literal word "must" when context
clearly establishes a chosen current policy. Suggestions, alternatives, and
research do not. Code, tests, packages, runtime wiring, and repeated patterns
are context, **never authority for creating Engineering policy**.

For Spec Kit-like sources, `spec.md` and `contracts/` commonly contain durable
requirements; `plan.md` and `research.md` may contain accepted decisions or
unresolved research; `tasks.md` and progress checklists are process by default.
Always judge the actual fragment, not the filename.

## 2. Plan normalized Intent and source mapping

Before writing:

1. Group current requirements by **product area**, not original file boundaries.
   Merge related fragments, split mixed-scope or oversized documents, extract
   shared contracts, and update existing semantic owners rather than creating
   duplicates. Do not make one spec per old task.
2. Select canonical `spec`, living `adr`, or active `spike` as defined by
   `references/document-types.md`. Consolidate legacy ADR variants into the
   **one current architectural owner** without preserving obsolete chains.
   Resolved spikes become current specs/ADRs when their results are normative;
   otherwise skip/remove the resolved spike from current Intent.
3. Track enough source-to-target mapping to account for each imported, merged,
   absorbed, skipped, or removed source, and to rewrite meaningful normative
   references. Include source path/identity, disposition, destination or
   semantic owner, and any reason or unresolved decision. This is an analysis
   artifact, **not another required file or JSON schema**.
4. Surface contradictory current behavior, requirements, defaults, or decisions.
   Import independent non-conflicting material; do not silently pick an option.
   If a conflict prevents a coherent normative document, stop that part and
   request the missing product decision.

Preserve user-provided and external **original** reference documents (including
those under `doc/` or `docs/`): do not rewrite, delete, move, or rename them
without explicit authorization. Link to originals as evidence, but place every
accepted requirement in an IDD owner; originals alone are not normative.
An explicitly authorized newer official edition may replace an earlier one.

Skipping obsolete material means **not importing it**. Cleanup or deletion of
obsolete, duplicate, task-like, or historical documents applies to generated or
current `.idd/intent/` state where safe, **not** to preserved external originals.
Never create `.idd/intent/archive`: Git is the history layer.

## 3. Plan Engineering atomically

Import has only the bounded Engineering migration authority described in
`references/engineering-guardrails.md`. It may create or update a Rule from an
**explicit current durable decision already supplied**; ordinary add/modify/
remove requests belong to `idd-engineering-change`. Do not invoke that skill
as an executable subroutine.

For every authoritative Engineering decision, group coherent policy, then
classify against existing Rules as:

- `new`: create only if no current semantic owner exists;
- `equivalent`: no-op, no ID consumed;
- `modify-existing`: replace/update exactly one established owner and preserve
  its `ENG-NNNN` identifier **only** with clear current-truth/replacement authority;
- `ambiguous`: report; apply **no Engineering mutations** in this invocation.

Suggestions, unresolved alternatives, and historical context are review/skipped
material, not mutating candidates. A different statement in a source does not
automatically override an existing Rule. Absence from the source never removes
an existing Rule; explicit removals that cannot be represented as an
unambiguous owner update are Engineering management candidates to report.

Complete semantic grouping, owner matching, ambiguity checks, and all safety
checks **before any Engineering write**. Follow the canonical Engineering
contract for existing-layer validation, consecutive ID allocation, `ENG-9999`
capacity, lazy packaged bootstrap, legacy allocator migration, and INDEX
projection. Never bootstrap an absent layer for a no-op, review-only, or
Product-Intent-only import; never bootstrap over a partial layer.

If `.idd/factory/current/request.md` exists, **do not mutate Engineering**.
Do not change Factory state, plan, or `TaskRelatedEngineering`, and do not
restart/replan Factory. Independent safe Intent may still be imported; report
the Engineering portion as blocked and the result as partial.

## 4. Apply and validate

In `propose`, stop before any durable write and return the proposal and
blockers. In `apply-safe`:

1. Apply only safe current Product Intent and, separately, the entire safe
   Engineering batch if allowed. Never resolve product or Engineering ambiguity
   merely to make validation pass.
2. Use the canonical `IDD-NNNN.type-short-title.md` filenames, matching H1
   identities, and semantic document shapes from `references/document-types.md`.
   Do not generate empty headings, status fields, invented evidence, or
   placeholders just to resemble a legacy template.
3. Regenerate `.idd/intent/INDEX.md` from actual current Intent documents.
   The `Document` column contains **plain `IDD-NNNN` identifiers only**,
   exactly once per current document; no paths, filenames, Markdown links,
   missing/duplicate entries, process reports, or archive section.
4. Resolve all normative `Related`, `Depends on`, `Replaces`, `Supersedes`,
   and equivalent relations to **existing current `IDD-NNNN` targets** using
   the source mapping. Rewrite absorbed-source references where meaning remains;
   omit historical-only references; report unresolvable product-significant
   links instead of leaving dangling relations.
5. Remove obsolete/process-only/duplicate material from the **target Intent
   state** when safe. Ensure `.idd/intent/archive` and process/import reports
   are absent under `.idd/intent/` or `.idd/engineering/`. Do not delete external
   source documents or create a default `docs/import-report.md`.
6. Regenerate the Engineering INDEX projection only when applicable. Do not
   change `.idd/verification.yaml`, even when test commands were discovered.
7. Run Intent mechanical validation directly, and run the canonical Mechanical
   Engineering Validation whenever Engineering existed and was read or changed.
   Fix mechanically safe errors caused by the import; do not silently repair
   pre-existing malformed Engineering or make semantic decisions to silence
   validation. Report any remaining blockers.
8. Run relevant repository checks.

Do not invoke `idd-intent-lint` or `idd-engineering-change` as executable
validation subroutines or generate a validator program, classifier, temporary
shell/PowerShell/Python/C# script, MCP server, or other validation subsystem.
Use bounded repository/host operations for mechanical checks and model
judgment for semantics.

## 5. Report

Return a concise import report **in the response**, not as a normative file.
Include source roots/methodology and material source-to-target transformations,
and identify:

- imported/updated Product Intent;
- new/updated Engineering Rules and equivalent no-ops;
- blocked Engineering changes and semantic questions requiring review;
- verification configuration candidates;
- skipped process, obsolete, generated, or historical material;
- mechanical validation results and repository checks.

Mark partial success explicitly when safe Product Intent was applied but
Engineering could not be changed. A fully successful import has mechanically
consistent current Intent; if Engineering was read or changed, its canonical
Mechanical Engineering Validation must also pass. Semantic ambiguity may be
reported as review without inventing policy or silently changing it.

The final state contains only current durable Intent and accepted Engineering
policy, not migration history.
