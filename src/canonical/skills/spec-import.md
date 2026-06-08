# spec-import

Use this skill to import existing product intent into the IDD `.specs/`
structure.

Use it for clean specifications and for dirty sources where specifications,
tasks, plans, outcomes, implementation notes, issue notes, ADRs, research,
checklists, generated output, and stale documents are mixed together.

The import is a migration of meaning:

```text
source documents -> inventory -> classification -> topic map -> target structure -> imported .specs
```

It is not a mechanical conversion from one old file to one new file.

## Rules

- Preserve source meaning. Do not normalize conflicting sources into a fake
  consistent specification.
- Import durable product intent. Drop process noise.
- If imported sources conflict, keep the conflict visible and ask for
  clarification when needed.
- If resolving a conflict requires research, create a spike instead of
  guessing.
- If source status is unclear, do not promote it to current normative intent.
  Mark it as `needs-review` or an archive candidate.
- Do not import backlog items, issue discussion, temporary progress, file
  lists, test output, chat history, or implementation status as normative
  intent.
- A task-like document may contain durable product behavior.
- A spec-like document may contain task steps, temporary status, and generated
  output.
- If imported intent already belongs to an existing current specification,
  update that document instead of creating a duplicate.
- Create new numbered documents only for distinct durable intent.
- Add imported current documents to `INDEX.md`. Add archived documents only
  when they are useful historical context.
- When useful, keep a short source reference in the imported document.

## Source Discovery

Before importing, identify the source methodology and its document conventions.

Look for:

- README or index files;
- templates;
- lifecycle markers;
- status markers;
- document types;
- naming conventions;
- archive/retired folders;
- generated files;
- task, plan, issue, spike, ADR, research, outcome, and implementation
  sections.

Do not import before understanding how the source marks current, obsolete,
draft, task-like, implementation-only, and historical content.

If the source methodology is unknown or inconsistent, classify each document and
section by content instead of trusting filenames or headings.

## Source Families

The source may be organized as one of these families:

1. Worklog-like: mixed specs, tasks, outcomes, progress notes, ADRs, and spikes.
2. Spec Kit / Spec Driven Development-like: feature folders with specs, plans,
   tasks, research, data models, contracts, quickstarts, checklists, and
   implementation notes.
3. Documentation-like: product documentation mixed with setup instructions,
   tutorials, and examples.
4. Issue-derived: requirements copied from issues, discussions, PRs, comments,
   or bug reports.
5. ADR/research-heavy: decisions and investigations mixed with product
   constraints.
6. Unknown markdown dump: no reliable structure. Classify by content.

Use source-specific conventions as hints only. The final decision is based on
whether the fragment expresses durable product intent.

For Spec Kit / Spec Driven Development-like sources:

- `spec.md` may contain durable product intent.
- `plan.md` usually contains implementation approach and should not be imported
  as normative product behavior unless it defines product-level constraints.
- `tasks.md` is backlog/process by default and should not become normative
  intent.
- `research.md` may become ADRs or spikes.
- `data-model.md` may contain durable domain contracts.
- `contracts/` may contain durable API or integration contracts.
- `quickstart.md` is usually user/developer guidance, not normative intent,
  unless it contains acceptance behavior.
- Checklists may contain acceptance or verification rules, but not task status.

## Import Inventory

Create an import inventory before writing target specs.

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
import-archive
convert-to-adr
convert-to-spike
extract-fragments
skip-process-only
skip-generated
needs-review
```

For small imports, summarize the inventory in the response. For large imports,
write an import report.

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
temporary-status
task-step
backlog-item
chat-history
generated-output
test-output
file-list
source-wrapper
```

A task-like document may contain durable product behavior. A spec-like document
may contain task steps, temporary status, and generated output.

Import durable intent. Drop process noise.

## Process Section Handling

Do not import these sections as normative intent by default:

- Plan
- Tasks
- Steps
- Pause points
- Status
- Progress
- Implementation summary
- Files changed
- Commands
- Test output
- Verification output
- Result
- Outcome
- Follow-up
- Chat history
- Generated by
- Work log notes

However, these sections may contain durable product facts.

From process sections, extract only:

- observable current product behavior;
- accepted deviations that define current behavior;
- durable architecture decisions;
- durable verification rules;
- durable non-goals or constraints.

Do not import:

- file paths changed during implementation;
- test counts;
- command output;
- temporary implementation sequence;
- `we added`, `we fixed`, or `we changed` wording;
- local debugging notes;
- future task lists unless they express durable non-goals or known gaps.

Bad import:

- Added `OpenCreateFileDialog.cs`.
- `dotnet test` passed: 869 tests.

Good import:

- The open/create file dialog validates empty paths without closing.
- Existing files opened through the dialog preserve detected encoding until the
  user explicitly changes it.

## Topic Map And Consolidation

Do not mechanically create one target spec per source document when the same
intent is spread across multiple source files.

Before writing target specs, group durable fragments by topic.

Common cross-cutting topics should become shared specifications.

Examples:

- modal dialog behavior;
- reusable text input behavior;
- validation behavior;
- keyboard shortcut conventions;
- mouse interaction rules;
- progress dialog conventions;
- error handling;
- provider compatibility;
- encoding behavior;
- authentication rules;
- background job idempotency;
- API contracts.

Feature-specific behavior should remain in feature specs and reference shared
specs.

If three source files describe different dialogs but repeat the same modal
frame, palette, button bar, focus, and validation rules, create or update a
shared dialog specification and keep only dialog-specific behavior in the
feature specs.

## Target Structure Rules

Create target documents by durable product area, not by source file.

Prefer:

- one shared spec for common reusable behavior;
- feature specs for user-visible capabilities;
- ADRs for durable architectural decisions;
- spikes for unresolved questions;
- archive files for useful obsolete intent.

Avoid:

- one imported spec per old task;
- one imported spec per old implementation step;
- duplicate specs for the same behavior;
- specs named after temporary work items;
- specs that describe how the migration was performed.

## Conflict Handling During Import

A conflict exists when two current or possibly-current fragments define
different product behavior, constraints, APIs, defaults, compatibility rules, or
non-goals.

Do not resolve conflicts by choosing the newer, longer, cleaner, or more
detailed source automatically.

Record conflicts in one of these ways:

- inline `## Open Conflict` section in the affected target spec;
- a spike when research is required;
- the import report when the target location is not yet clear.

If the conflict blocks writing a coherent normative spec, stop and ask for a
product intent decision.

## Obsolete And Historical Intent

Archive obsolete but useful normative intent.

Skip obsolete process notes.

Do not import retired, archived, deprecated, superseded, or replaced documents
as current intent unless a current source explicitly revives them.

If an old document explains why a current rule exists, preserve it as:

- ADR rationale;
- source reference;
- archived context.

## Import Report

For non-trivial imports, create an import report.

The report should include:

- source roots inspected;
- source methodology detected;
- source files skipped and why;
- source files imported and target documents;
- fragments extracted from task/process documents;
- conflicts found;
- obsolete documents archived;
- documents requiring human review;
- shared topics consolidated;
- source-to-target mapping.

The report is not normative product intent. Place it outside current numbered
specs, for example `.specs/import-report.md` or
`.specs/archive/import-report-YYYYMMDD.md`.

## Workflow

1. Read the target IDD guidance:
   - `.specs/README.md`
   - `.specs/INDEX.md`
   - relevant existing `.specs/` documents
2. Discover the source methodology:
   - README/index
   - templates
   - lifecycle/status/type conventions
   - archive/generated/task conventions
3. Build an import inventory:
   - list source documents
   - classify document type and lifecycle
   - identify product area
   - choose preliminary import action
4. Classify fragments inside candidate documents:
   - durable intent
   - architecture rationale
   - uncertainty/research
   - verification rules
   - process noise
   - generated output
   - conflicts
5. Build a topic map:
   - group fragments by product area and concept
   - detect cross-cutting topics
   - detect duplicates
   - detect conflicts
   - detect obsolete intent
6. Propose or infer target structure:
   - update existing specs when suitable
   - create new numbered specs for distinct durable intent
   - create shared specs for cross-cutting behavior
   - create ADRs for durable decisions
   - create spikes for unresolved uncertainty
   - archive historical intent
7. Import current durable intent:
   - rewrite into IDD spec template sections
   - remove source wrapper text and process noise
   - preserve source meaning
   - keep source references when useful
8. Consolidate shared intent:
   - move common rules into shared specs
   - keep feature-specific behavior in feature specs
   - replace duplicated local wording with references
9. Handle conflicts:
   - do not silently choose one side
   - record conflict in the target spec or import report
   - ask for clarification when needed
10. Update `.specs/INDEX.md`.
11. Write or update import report for non-trivial imports.
12. Run relevant verification if the repository has verification commands.

## Import Quality Gate

Before finishing, check:

- No task steps were imported as product requirements.
- No implementation status was imported as normative intent.
- No file lists or test output were imported.
- Durable behavior from task-like documents was not lost.
- Cross-cutting topics were consolidated instead of duplicated.
- Existing `.specs/` documents were updated when appropriate.
- Conflicts are visible.
- Unclear status is marked as `needs-review`.
- `.specs/INDEX.md` is updated.
- Import report exists for non-trivial imports.
- The resulting specs describe the target product, not the history of work.

## Examples

### Task-like source

Source:

```md
Type: task
Status: done

Goal:
Make F7 create folder dialog look like Far Manager.

Done when:
- title is `Make folder`
- prompt is `Create the folder:`
- buttons are `{ OK }` and `[ Cancel ]`

Outcome:
- Added CreateFolderDialog.cs
- dotnet test passed
```

Import:

- Do not import Type, Status, Outcome file list, or test output.
- Extract durable behavior:
  - F7 opens a create-folder dialog.
  - Dialog title is `Make folder`.
  - Prompt is `Create the folder:`.
  - Buttons are `{ OK }` and `[ Cancel ]`.

### Spec Kit-like source

Source folder:

```text
feature-x/
- spec.md
- plan.md
- tasks.md
- research.md
- contracts/api.yaml
```

Import:

- Import durable behavior from `spec.md`.
- Import durable API contract from `contracts/api.yaml`.
- Convert architectural decisions from `research.md` into ADR or rationale.
- Skip `tasks.md` as process.
- Use `plan.md` only for product-defining constraints, not implementation
  steps.

### Consolidation across several files

Source:

- `copy-dialog.md` describes modal frame, palette, button bar.
- `create-file-dialog.md` repeats modal frame, palette, button bar.
- `delete-progress-dialog.md` repeats modal frame and shadow.

Import:

- Create shared console modal dialog spec.
- Move common frame/palette/button behavior there.
- Keep copy/create/delete-specific behavior in feature specs.
- Add references from feature specs to shared spec.

## Non-goals

Do not use this skill for:

- full quality review of all existing specs when import was not requested;
- rewriting specifications just to make them nicer;
- deriving requirements from code;
- automatically resolving product conflicts;
- moving tasks into `.specs/`;
- creating a project plan;
- creating an implementation backlog.

Use `spec-reorganize` for reorganization after import. During import, this skill
may still perform primary consolidation when source intent is clearly spread
across multiple files.
