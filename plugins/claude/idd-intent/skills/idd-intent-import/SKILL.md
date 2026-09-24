---
name: idd-intent-import
description: Migrate supplied durable knowledge into normalized Product Intent and explicit source-owned Engineering Rules, with safe semantic planning, lazy Engineering bootstrap, conflict blocking, and shared mechanical validation.
argument-hint: "[source roots] [--mode propose|apply-safe]"
---

# idd-intent-import

Use this skill to migrate supplied durable knowledge into normalized IDD state:
Product Intent under `.idd/intent/` and explicit durable implementation-only
Engineering decisions under the optional `.idd/engineering/` layer.

Formula:

```text
idd-intent-import =
    classify supplied durable knowledge
    + import Product Intent
    + import explicit durable Engineering decisions
    + normalize
    + mechanical validation
```

Use it when old `.worklog` content, GitHub Spec Kit folders, issue/task docs,
ADRs, research notes, implementation notes, or other sources must become a
coherent current product intent document set.

Import is a migration of meaning, not a mechanical conversion from old files to
new files. Source files are evidence. They are not the desired target structure.

Import is not complete until the resulting Intent state is mechanically
consistent and, when Engineering state is read or changed, the Engineering layer
passes the canonical Mechanical Engineering Validation.

For successful apply-safe import, the expected final state is:

- no `.idd/intent/archive`;
- no process-only import reports under `.idd/intent`;
- all current `IDD-NNNN` documents are listed in `.idd/intent/INDEX.md`;
- every `INDEX.md` `Document` entry uses only the stable `IDD-NNNN` identifier;
- all current documents listed in `.idd/intent/INDEX.md` exist;
- all `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar
  normative relations use `IDD-NNNN` identifiers and point to existing
  current documents;
- imported current specs, ADRs, and active spikes follow the current document
  shape;
- Intent mechanical validation has no errors;
- when Engineering exists and was read or changed, canonical Mechanical Engineering Validation has no errors.

Warnings may remain only for genuinely semantic ambiguity. Mechanical errors
must be fixed before finishing the import.

## Default Modes

```yaml
mode: apply-safe
autoNormalize: true
conflictMode: report-only
allowNewSpecs: true
```

Supported modes:

```yaml
mode: propose | apply-safe
autoNormalize: true
conflictMode: report-only
allowNewSpecs: true
```

`apply-safe` may apply safe durable-state changes whose meaning is already
established by supplied authoritative material. It must not resolve product or
Engineering conflicts, choose among technical alternatives, or invent new
decisions.

## Durable knowledge model

Import answers two independent questions:

```text
What supplied material is current Product Intent?
What explicit current implementation-only durable decisions must survive removal of the source?
```

Product Intent and Engineering remain separate durable layers. Import is a
migration workflow, not a technology-selection, architecture-design, or
reverse-engineering workflow.

A supplied source authorizes migration of decisions it actually states. It does
not turn suggestions into decisions, authorize choosing between alternatives,
or authorize inferring policy from repository implementation.

### Engineering authority

An Engineering Rule is importable only when the supplied material explicitly
expresses an accepted current target decision that is implementation-only,
durable, intended to constrain future implementations, and sufficiently
unambiguous to write as a canonical Rule.

Typical authoritative sources include requirements/specification documents,
architecture documents, ADRs, normative technical-design sections, documented
engineering conventions, migration/design documents that explicitly describe
the target design, and user-supplied text that states an accepted decision.

Repository code, package references, tests, runtime wiring, solution structure,
and repeated implementation patterns may provide context but are never authority
for automatically creating Engineering policy.

Accepted wording does not require the literal word `must`. Statements such as
"We use MudBlazor for UI", "The geometry engine is Clipper2", "SVG is normalized
into polygons before geometry processing", or "STL generation happens
client-side" are importable when context clearly presents them as chosen current
target design.

Suggestions, examples, unresolved alternatives, research without a conclusion,
and historical statements are not current Engineering policy. Classify them as
review/skipped material rather than inventing a decision.

### Engineering mutation authority

`idd-engineering-change` remains the standard owner of ordinary explicit
Engineering add/modify/remove requests.

`idd-intent-import` has one narrow mutation exception: while migrating explicit
durable Engineering knowledge already present in supplied import sources it may
create Rules, update the single existing semantic owner, or record an equivalent
Rule as a no-op.

Import never treats absence from the source as a remove request. If supplied
material explicitly calls for removing a current Rule and the change cannot be
expressed as an unambiguous update of its current semantic owner, report an
Engineering change candidate for ordinary management instead of deleting the
Rule.

Do not invoke `idd-engineering-change` as an executable subroutine.

### Propose and apply-safe

In `propose`, mutate neither `.idd/intent/` nor `.idd/engineering/`. Report
proposed Product Intent, proposed Engineering Rules/updates, Engineering no-ops,
Needs Review, skipped material, and verification-configuration candidates.

In `apply-safe`, safe Product Intent and unambiguous supplied Engineering
decisions may be written. Never auto-resolve semantic conflicts, competing
technologies, unresolved research, material Intent/Engineering ambiguity,
ambiguous Engineering ownership, or uncertainty about whether supplied material
supersedes current Engineering policy.

### Existing Engineering state and semantic atomicity

If `.idd/engineering/` exists, read its README and INDEX and apply the canonical
Mechanical Engineering Validation from
`references/engineering-guardrails.md` before planning Engineering mutations.
Structural errors block only the Engineering portion; never repair a malformed
layer heuristically or bootstrap over a partial layer.

For every explicit supplied decision classify the Engineering action as
`new`, `equivalent`, `modify-existing`, or `ambiguous`. Preserve a stable
`ENG-NNNN` when modifying its unique semantic owner. Equivalent decisions are
no-ops and consume no ID.

Complete grouping, ownership resolution, ambiguity detection, new-ID counting,
allocator/capacity checks, and Factory safety checks before any Engineering
mutation. If any real explicit Engineering mutation candidate is ambiguous,
mutate no Engineering Rules in this invocation. Unconfirmed suggestions or
research are not mutation candidates and therefore do not block an otherwise
safe Engineering batch.

A conflicting source statement does not silently supersede an existing current
Rule. Modification is safe only when the source/context clearly establishes
replacement/current-truth semantics.

If the Engineering layer is absent, materialize the packaged canonical bootstrap
only immediately before the first real new Rule after semantic planning has
succeeded. Do not create `.idd/engineering/` for no-op, review-only, or
Product-Intent-only imports.

Use the canonical allocator semantics: new Rules consume consecutive IDs,
equivalent/modified Rules consume none, valid legacy layers gain an allocator
only immediately before a real mutation, and any allocation beyond
`ENG-9999` blocks before files change.

### Factory safety and partial import

`.idd/factory/current/request.md` is the active-run marker. When it exists,
Engineering mutation is blocked. Do not alter Factory state, plan, or
`TaskRelatedEngineering`, and do not restart/re-plan automatically.

Independent safe Product Intent may still be imported. The report must clearly
state that the result is partial and that Engineering knowledge was not applied.

### Verification configuration

Operational commands such as `dotnet test`, `npm test`, or `pytest` are
neither Product Intent nor Engineering Rules. Report them as verification
configuration candidates when useful. `idd-intent-import` never changes
`.idd/verification.yaml`; ownership remains with
`idd-verification-configure`.

## Current spec test

Current spec documents describe target product state, not the history of work.

A spec answers:

```text
If the implementation is deleted but the intent documents remain, can the product be rebuilt?
```

Therefore current specs may contain:

- product behavior;
- user scenarios;
- domain contracts;
- architecture and technical constraints when they are product-significant,
  public, domain, compatibility, security, or operability contracts;
- compatibility requirements;
- non-goals;
- acceptance criteria;
- verification rules.

A durable implementation-only constraint is not automatically product intent.
Classify it separately. When supplied authoritative material already states an
explicit durable Engineering decision, migrate that decision directly under the
narrow import authority above; no repeated user confirmation is required.
Suggestions, alternatives, code-derived patterns, and unresolved material remain
non-authoritative.

Current specs must not contain:

- local tasks;
- temporary implementation notes;
- progress logs;
- chat history;
- one-off cleanup notes;
- plans that do not define product behavior;
- source-specific wrapper text from imported methodologies.

Task, refactor, cleanup, progress, and status notes are not current product
specs unless a fragment defines durable product behavior.

## Structural Normalization

Do not preserve source file boundaries by default. Source files are evidence,
not the desired target structure. The target structure must follow durable
product intent areas.

Before writing files, build a normalized target structure and look for:

- oversized specs that must be split;
- tiny specs that should be merged into an existing area;
- mixed-scope specs that describe unrelated product areas;
- repeated common models that should become shared specs;
- semantic conflicts that require a product decision;
- task/refactor/cleanup notes that should not be current product specs;
- ADR-worthy architectural decisions;
- spike-worthy unresolved research;
- obsolete or source-specific wrapper text;
- duplicated behavior across current specs.

Typical product areas include:

- product overview;
- panels;
- command line;
- file operations;
- viewer;
- editor;
- shared text format / encoding / BOM / EOL;
- UI controls / dialogs;
- providers / virtual file systems;
- rendering / console viewport;
- settings;
- architecture decisions;
- spikes / unresolved research.

This is not a fixed enum. Prefer areas that match the actual product.

## Required Behavior

1. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and relevant current
   Intent documents when they exist.
2. If `.idd/engineering/` exists, read its README/INDEX and perform canonical
   Mechanical Engineering Validation before using it as current policy.
3. Read only the supplied source files/directories plus bounded context needed to
   interpret them.
4. Classify source fragments independently as Product Intent, explicit durable
   Engineering decision, Engineering rationale, unconfirmed candidate,
   alternative/research/history, verification configuration, or process/noise.
5. Apply the Intent/Engineering boundary: product-visible/public/domain/
   compatibility/security/operability contracts belong to Intent;
   implementation-only durable policy belongs to Engineering.
6. Treat code, package references, tests, runtime wiring, and repeated patterns
   as evidence only, never Engineering authority.
7. Build the normalized Intent target structure and source-to-target remap before
   writing Intent.
8. Build the complete Engineering semantic plan before any Engineering mutation:
   group coherent decisions, resolve semantic owners, classify
   new/equivalent/modify/ambiguous, determine IDs, and check capacity/safety.
9. Do not treat suggestion/research/history as a mutating Engineering candidate.
10. If any real Engineering candidate is ambiguous, mutate no Engineering Rules
    in this invocation.
11. In `propose`, write no durable state.
12. In `apply-safe`, write only unambiguous Product Intent and explicit supplied
    Engineering decisions allowed by this workflow.
13. Preserve existing `ENG-NNNN` for an unambiguous semantic-owner update;
    equivalent decisions are no-op.
14. Never remove an existing Rule merely because the imported source omits it.
15. Lazily bootstrap Engineering only before the first real new Rule and only
    from packaged canonical assets.
16. Never mutate `.idd/engineering/` while
    `.idd/factory/current/request.md` exists.
17. Never mutate `.idd/verification.yaml`; report operational commands as
    configuration candidates instead.
18. Regenerate Intent INDEX and Engineering INDEX projections as applicable.
19. Apply Intent mechanical validation and, whenever Engineering was read or
    changed, canonical Mechanical Engineering Validation directly.
20. Return a report that makes additions, updates, no-ops, blocked changes,
    review items, verification candidates, skipped material, and partial results
    explicit.

Do not import task/progress/status material as current specs. Do not preserve
source file boundaries automatically. Do not generate a validator program or
semantic classifier. Semantic classification and ownership are model work;
mechanical checks use bounded host/repository operations.

## Source Triage

Identify the source methodology and conventions before importing. Look for
README or index files, templates, lifecycle markers, document types, generated
files, task sections, ADRs, spikes, research, and implementation
sections.

Use source-specific conventions as hints only. Classify each document and
section by whether it expresses durable product intent.

For GitHub Spec Kit / Spec Driven Development-like sources:

- `spec.md` may contain durable product intent.
- `plan.md` usually contains implementation approach; import only
  product-level constraints.
- `tasks.md` is process by default and should not become current intent.
- `research.md` may become ADRs or spikes.
- `data-model.md` may contain durable domain contracts.
- `contracts/` may contain durable API or integration contracts.
- `quickstart.md` is usually guidance, not normative intent, unless it defines
  acceptance behavior.
- Checklists may contain acceptance or verification rules, but not task status.

## Import Inventory

Create an import inventory before writing target documents.

For each source, track:

```text
source path
detected type
detected lifecycle/status
main product area
import action
reason
target document
review notes
```

Possible import actions:

```text
import-current
convert-to-adr
convert-to-spike
extract-fragments
import-engineering-rule
merge-into-engineering-rule
update-engineering-rule
engineering-no-op
needs-engineering-review
engineering-blocked
skip-process-only
skip-generated
delete-obsolete
delete-duplicate
needs-review
```

## Fragment Classification

Classify sections and paragraphs, not only files.

Fragment categories:

```text
durable-current-intent
durable-obsolete-intent
architecture-rationale
uncertainty-or-research-question
acceptance-or-verification-rule
user-visible-behavior
domain-contract
product-defining-technical-constraint
implementation-note
durable-verification-property
verification-command
durable-architecture-boundary
explicit-durable-engineering-decision
engineering-rationale
engineering-candidate-unconfirmed
technical-alternative
unresolved-technical-research
historical-engineering-context
temporary-implementation-note
implementation-structure
public-contract
private-code-detail
migration-step
source-scan-instruction
temporary-status
task-step
backlog-item
chat-history
generated-output
test-output
file-list
source-wrapper
```

Import durable intent. Drop process noise.

Do not import obsolete or process-only source material into an archive. Skip or
delete source material that has no current product intent. Preserve old
versions only through Git history.

Never create `.idd/intent/archive`.
Never move obsolete imported documents into `.idd/intent/archive`.
Never preserve skipped, obsolete, duplicate, process-only, task-like, or
historical source files as archived specs.
Git history is the archive.

## Source-to-target Remap

Before writing final documents, build a source-to-target mapping for every
imported, skipped, merged, deleted, absorbed, or converted source document.

Track at least:

```text
source id/path
source title
source detected type
action
target document if any
absorbed-by document if any
reason
reference rewrite rule
```

Possible actions:

```text
import-current
convert-to-adr
convert-to-spike
extract-fragments
merge-into-existing
absorb-into-new
skip-process-only
skip-generated
delete-obsolete
delete-duplicate
needs-review
```

Rules:

- An `IDD-NNNN` relation may be written only if the referenced target document
  exists in current `.idd/intent`.
- If source document A was absorbed by target document B, references to A must
  be rewritten to B when the relation is still meaningful.
- If source document A was skipped as process-only, duplicate, obsolete,
  generated, or historical-only, references to A must be removed or rewritten as
  source history, not kept as normative `IDD-NNNN` references.
- If the correct remap cannot be inferred safely, do not leave a broken
  reference. Report the unresolved mapping as a blocking import issue.

## Conflict Handling

A conflict exists when two current or possibly-current fragments define
different behavior, constraints, APIs, defaults, compatibility rules, or
non-goals.

Example:

```text
Scope says feature X is supported.
Non-goals says feature X must not be implemented.
```

Do not choose one side silently. Instead:

- create or import only non-conflicting durable intent;
- report the conflict;
- add an explicit unresolved decision section when the target location is clear;
- recommend an ADR, spike, or product decision;
- avoid hiding the conflict inside rewritten prose.

If the conflict blocks a coherent normative spec, stop and ask for a product
decision.

## Normalized Writing Rules

Create target documents by durable product area, not by source file.

Prefer:

- one shared spec for common reusable behavior;
- feature specs for user-visible capabilities;
- ADRs for durable architectural decisions;
- spikes for active unresolved questions.

Avoid:

- one imported spec per old task;
- one imported spec per old implementation step;
- duplicate specs for the same behavior;
- specs named after temporary work items;
- specs that describe how the migration was performed.

Create current spec documents only for durable current product intent. Create
adr documents only for durable decision records. Create spike documents only for
active unresolved research.

For example, this source mixes operational commands with a concrete
implementation constraint:

````md
Run:

```bash
dotnet build
dotnet test
```

UiCompositionHost must be created in Bootstrap and passed to every dialog
constructor.
````

Do not turn either fragment into product intent automatically. Build/test
commands belong in verification configuration. Constructor wiring is incidental
unless the supplied authoritative material itself establishes it as a durable
Engineering decision; only in that case may import migrate it as a Rule.

If the same source also states a product-visible requirement such as consistent
dialog and overlay redraw behavior across viewport changes, import that product
requirement and its durable verification property into Intent.

Imported current documents must use current IDD document shapes. Do not preserve
legacy section layout when the document becomes current normative intent.

Every target document must use the canonical `IDD-NNNN.type-short-title.md`
filename and the same `IDD-NNNN` identifier in its heading. Bare `NNNN`
filenames and normative references are invalid.

Minimum shape for `spec` documents:

```md
# IDD-NNNN.spec-short-title

## Intent

Describe the durable product intent.

## Related Specifications

List related specs, ADRs, or spikes that define adjacent, shared, or dependent
intent.

## Behavior

Describe observable behavior and domain contracts.

## Product-Significant Architecture And Constraints

Describe architecture boundaries and technical constraints only when they are
part of product behavior, a public/domain/compatibility contract, security,
operability, or another product-significant property.

Do not put implementation-only durable conventions here merely because future
implementations should follow them. Those belong in the optional Engineering
layer. This import workflow may migrate them there only when supplied source
material already establishes an explicit durable decision.

Do not include private class names, private methods, file names, constructor
signatures, dependency-wiring steps, temporary workarounds, migration steps, or
current code structure.

## Non-goals

List behavior or scope that is intentionally excluded.

## Acceptance Criteria

List conditions that must hold for the specification to be satisfied.

## Verification

Describe durable verification properties and evidence required to establish
correctness. State what must be verified, not the local command used to run
verification.

Do not include build commands, test-runner commands, CI commands, test class
names, temporary source scans, or step-by-step execution instructions.

Do not turn Verification into a catalog of test methods, internal classes,
private implementation shape, or one automated test per specification
sentence. Prefer important user scenarios, critical invariants, meaningful
boundary cases, and justified manual checks.
```

Minimum shape for `adr` documents:

```md
# IDD-NNNN.adr-short-title

## Status

Proposed | Accepted | Superseded | Rejected

## Context

Describe the decision context.

## Decision

Describe the chosen decision.

## Alternatives Considered

List alternatives and why they were not chosen.

## Consequences

Describe accepted tradeoffs and follow-up constraints.

## Supersedes

Reference the superseded ADR when this ADR replaces an earlier decision.
```

Minimum shape for `spike` documents:

```md
# IDD-NNNN.spike-short-title

A spike is active research only while the question is unresolved. When resolved,
move durable product behavior into a spec, move durable architecture decisions
into an ADR, and delete the spike unless it remains useful as active research.

## Question

State the uncertainty or hypothesis being tested.

## Constraints

List constraints for the investigation.

## Method

Describe how the spike is evaluated.

## Result

Record what was learned.

## Recommendation

State the proposed follow-up.
```

If a spike already has `Result` or `Recommendation` and is no longer active
research, import must choose one of these outcomes:

- convert the result into a spec or adr if it defines current product intent;
- delete or skip it if it is only historical research;
- keep it as a spike only if the question is still active and unresolved.

## Relation Normalization

After writing documents, scan all current specs, ADRs, and spikes for `IDD-NNNN`
references.

For each reference:

1. Check whether the target file exists.
2. If missing, consult the source-to-target map.
3. Rewrite to the current target when safe.
4. Remove historical-only references.
5. Stop and report a blocking ambiguity only when the relation is
   product-relevant but cannot be safely mapped.

Do not finish with a relation such as:

```text
Related: IDD-0026
```

when `IDD-0026` does not exist.

It is valid to rewrite the relation when the source remap proves the target:

```text
Related: IDD-0027
```

It is also valid to preserve traceability as non-normative history:

```text
Source history: extracted from legacy .worklog/0026.
```

Source history is not a normative relation.

## Index Regeneration

Regenerate `.idd/intent/INDEX.md` from actual current `IDD-NNNN` documents after
import. Never leave placeholder index content. Never rely on the source index as
the final target index.

The index references document identity, not storage filenames. Its `Document`
column must contain only stable `IDD-NNNN` identifiers as plain text. Resolve an
identifier to the unique current `.idd/intent/IDD-NNNN.*.md` file when opening
the document.

Minimum structure:

```md
# IDD Intent Index

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | Spec | Product overview | ... | — |
| IDD-0002 | ADR | Rendering | ... | — |
| IDD-0003 | Spike | Input layer | ... | — |
```

Rules:

- every `Document` entry must match exactly `IDD-NNNN`;
- filenames, paths, inline-code filenames, and Markdown links must not be used in
  the `Document` column;
- every current `IDD-NNNN` document under `.idd/intent/` must be listed exactly
  once by its stable identifier;
- every listed identifier must resolve to exactly one current
  `.idd/intent/IDD-NNNN.*.md` file;
- process reports, templates, README files, and support docs must not be listed
  as current specs;
- there must be no Archived section.

## Workflow

1. Read current Intent state.
2. Read and mechanically validate existing Engineering state when present.
3. Read the supplied source roots and discover source methodology/lifecycle.
4. Build the import inventory.
5. Classify fragments into Product Intent, explicit Engineering decisions,
   Engineering rationale, review/research/history, verification configuration,
   and process/noise.
6. Build the Intent source-to-target remap and normalized product-area model.
7. Group explicit Engineering decisions into the minimum semantically coherent
   Rules.
8. Match those groups to current Engineering Rules and classify each as
   new/equivalent/modify-existing/ambiguous.
9. Complete Engineering semantic atomicity checks, allocator/capacity planning,
   Factory guard, and lazy-bootstrap decision before mutation.
10. In `propose`, stop before durable mutation and return the full proposal.
11. In `apply-safe`, write safe Product Intent.
12. If the Engineering batch is safe and Factory is inactive, materialize
    bootstrap only if needed, then apply new/modify/no-op Engineering actions.
13. If Engineering is blocked or ambiguous, leave all Engineering files
    unchanged while allowing semantically independent safe Product Intent.
14. Regenerate affected INDEX projections.
15. Run Intent mechanical validation directly.
16. If Engineering existed or was changed, apply canonical Mechanical
    Engineering Validation directly from `references/engineering-guardrails.md`.
17. Return the import report; do not persist it inside either durable layer.
18. Run relevant repository checks.

Do not invoke `idd-engineering-change` or `idd-intent-lint` as an executable
Engineering-validation subroutine. Do not add Python, PowerShell, C#, shell, MCP,
or other validator subsystems.

## Post-import Cleanup

Before finishing:

1. Delete `.idd/intent/archive` if it exists.
2. Remove import/process reports from `.idd/intent` and
   `.idd/engineering/`.
3. Regenerate `.idd/intent/INDEX.md` from current Intent documents.
4. Validate and rewrite Intent relations through the source-to-target map.
5. Normalize current specs, ADRs, and spikes to current section shapes.
6. Reclassify resolved spikes.
7. Remove or merge task-like/process-only/duplicate/historical-only Intent docs.
8. Ensure Engineering additions/updates use the canonical Rule format and INDEX
   projection from `references/engineering-guardrails.md`.
9. Preserve allocator semantics: no-op/modify do not consume IDs, and blocked
   batches do not mutate allocator state.
10. Apply Intent mechanical validation.
11. Apply Mechanical Engineering Validation when Engineering exists and was read
    or changed.
12. Continue fixing only mechanical errors introduced by safe mutations; never
    resolve semantic ambiguity merely to make validation pass.

## Import Report

Do not create `.idd/intent/import-report.md`.

Import reports are process output, not product intent. They must not be stored
inside `.idd/intent`.

Prefer returning the import report in the assistant response. If a persistent
report is explicitly needed, write it outside `.idd/intent`, for example:

- `.intent-driven-development/import-report.md`;
- `docs/import-report.md`.

The report must not recommend creating `.idd/intent/archive` or link to
`.idd/intent/archive/...`.

Include these sections (use `None` when empty):

- Imported Product Intent;
- Imported Engineering Rules;
- Updated Existing Engineering Rules;
- Engineering No-ops;
- Blocked Engineering Changes;
- Needs Review;
- Verification Configuration Candidates;
- Skipped Process / Temporary Material;
- Mechanical Validation.

Also identify source roots/methodology and enough source-to-target mapping or
reasoning to explain non-obvious normalization. Make partial success explicit
when Product Intent was applied but Engineering was blocked.

The report is not normative product intent.

## Quality Gate

Before finishing, check:

- No task steps were imported as product requirements.
- No progress/status notes were imported as normative intent.
- No implementation-only cleanup/refactor notes became current specs.
- No file lists, generated output, test output, or chat transcripts were
  imported.
- Durable behavior from task-like documents was not lost.
- Source boundaries were not preserved by default.
- Mixed-scope sources were split.
- Small related sources were consolidated.
- Cross-cutting topics were extracted to shared specs.
- Existing specs were updated when appropriate.
- Conflicts are visible and unresolved.
- ADR-worthy decisions and spike-worthy research are separated.
- `.idd/intent/INDEX.md` is regenerated from actual current `IDD-NNNN` documents
  using ID-only `Document` entries.
- `IDD-NNNN` relations point only to existing current documents.
- no process/import report remains under `.idd/intent`.
- no `.idd/intent/archive` directory exists.
- imported specs, ADRs, and active spikes use current document shapes.
- resolved spikes are converted, removed, or justified as still-active
  research.
- Intent mechanical validation has no errors.
- Explicit supplied Engineering decisions are migrated without repeated
  confirmation when authority and meaning are clear.
- Suggestions, alternatives, historical statements, and unresolved research do
  not become current Rules.
- Repository implementation does not become Engineering authority.
- Equivalent Rules are no-op; unambiguous replacements preserve stable IDs;
  ambiguous real Engineering candidates mutate no Engineering state.
- Engineering bootstrap is lazy and packaged, allocator/capacity semantics are
  preserved, and Factory active-run safety is enforced.
- `.idd/verification.yaml` is unchanged.
- Mechanical Engineering Validation passes whenever the Engineering layer was
  read or changed.
- The resulting specs describe target product state, not work history.

## Examples

### Import mixed old material

Input:

```text
Old `.worklog` contains:
- one large MVP document;
- separate notes about viewer/editor encoding;
- cleanup task about removing unused fields;
- conflicting copy behavior about Append.
```

Expected import behavior:

- split the MVP document into product overview and area specs;
- extract shared text encoding/BOM/EOL behavior into a dedicated spec;
- do not import the cleanup task as a current product spec;
- report the Append conflict as a product decision;
- update `.idd/intent/INDEX.md`.

### Import legacy `.worklog` with broken historical references

Input:

- old `.worklog` contains numbered documents;
- some documents were archived or obsolete;
- several current documents use `Related` / `Replaces` links to documents that
  are not imported;
- source has old section layouts;
- source contains resolved spikes and task-like cleanup notes.

Expected import behavior:

- do not create `.idd/intent/archive`;
- do not create `.idd/intent/import-report.md`;
- build source-to-target mapping;
- rewrite references to current targets when documents were absorbed or merged;
- remove historical-only relations;
- regenerate `.idd/intent/INDEX.md`;
- normalize specs, ADRs, and active spikes to current shapes;
- convert or remove resolved spikes;
- finish with no `idd-intent-lint` errors.

### Spec Kit-like source

Input:

```text
feature-x/
- spec.md
- plan.md
- tasks.md
- research.md
- contracts/api.yaml
```

Expected import behavior:

- import durable behavior from `spec.md`;
- import durable API contracts from `contracts/api.yaml`;
- convert durable architecture decisions from `research.md` into ADR material;
- convert unresolved research into a spike;
- skip `tasks.md` as process;
- use `plan.md` only for product-defining constraints.

## Non-goals

Do not use this skill for:

- full quality review of all existing specs when import was not requested;
- broad diagnostics of current `.idd/intent` structure without import;
- rewriting specifications just to make them nicer;
- deriving requirements from code;
- automatically resolving product conflicts;
- moving tasks into `.idd/intent/`;
- creating a project plan or implementation backlog.

Use `idd-intent-audit` for broad structural diagnostics without edits. Use
`idd-intent-normalize-current` only for later maintenance of an existing `.idd/intent`
tree, not as a required manual cleanup after import.
