# idd-intent-lint

Use this skill to check mechanical consistency of project-owned `.idd/intent/` and, only when explicitly authorized, conservatively repair unambiguous technical defects. Always perform the existing read-only Mechanical Engineering Validation when `.idd/engineering/` is present.

Formula:

```text
idd-intent-lint = mechanical IDD check | conservative, authorized mechanical fix
```

Use `mode: check` by default; `mode: fix` only for explicit permission to
modify technical Intent documents. Accept an optional document/scope argument.
In a forked Claude context, the caller must forward the **complete original
request**, including mode, scope, exclusions, and prohibitions. Never derive
write consent from the availability of Edit/Write/Bash tools. An explicit
read-only restriction always wins.

Read `references/numbering.md`, `references/document-types.md` and
`references/glossary-integration.md` for normative constraints. Read
`references/engineering-guardrails.md` before validating Engineering.
Use packaged `assets/bootstrap/.idd/intent/` only when restoring missing
standard files in fix mode; do not borrow another skill's assets or read plugin
cache paths.

## Rules

- In `check`, perform no writes, moves, deletes, initialization, temporary
  repository files, shell redirection to files, or script/report generation.
  `Bash` remains read-only in this mode even when the host permits writes.
- In `fix`, make only the minimal, independently verifiable repairs specified
  below. Do not rewrite whole documents as a formatter.
- Do not reorganize specs.
- Do not perform broad semantic review.
- Do not resolve product conflicts.
- Treat `GLOSSARY.md` as optional. Its absence is valid and produces no warning.
- Do not create or update the glossary from lint.
- Always report errors and warnings; in `check` suggest fixes only.
  In `fix` report actual fixes, skipped fixes, changed paths, and the
  final result of mandatory revalidation.
- Absence of `.idd/engineering/` is valid and produces no warning.
- Do not use lint to decide semantic applicability of Conditional Engineering
  Rules.
- Ordinary project lint is limited to project-owned `.idd/intent/`, optional
  `.idd/intent/GLOSSARY.md`, and optional `.idd/engineering/` state. Do not scan
  an installed plugin cache, canonical IDD repository, skill source tree, or IDD
  documentation tree unless the user explicitly asks to validate the IDD
  distribution itself.
- Do not generate a general-purpose validator program solely to perform IDD lint.
  Use bounded repository inspection operations: Read, Glob, Grep/Search, and
  simple bounded host/shell commands when useful.


## Modes, scope and ownership

- `check` is the default for "check", "run lint", "find errors", and
  "check and explain how to fix". It may identify fixable findings but must not
  change any file. It must not create missing bootstrap files.
- `fix` requires "check and fix", "repair mechanical errors", or explicit
  `mode: fix`. Apply eligible changes without repeatedly seeking permission
  for each. "Fix the lint skill" refers to implementation work, **not** a
  project-documents autofix.
- No `dry-run`, snapshot/rollback system, automatic Git commits or
  force/reset/restore/clean operations. Git diff/status are inspection tools
  only. Record new untracked files separately because `git diff` misses them.
- Default scope: project-owned `.idd/intent/`, its direct current numbered
  documents, `README.md`, `INDEX.md`, `_templates/`, and optional
  read-only `GLOSSARY.md`; and read-only `.idd/engineering/` if it exists.
  Do not recursively lint source code, plugin cache, generated marketplace,
  methodology sources or the entire repository. A separately requested
  IDD-distribution audit is a distinct scope, never automatic autofix of
  `src/canonical/`.
- A scoped check includes required INDEX, identity collision, and affected
  relation dependencies. Its `pass` covers only the reported scope.
- If the entire `.idd/intent/` folder is absent, report the need for
  `idd-project-init` without creating it, even in fix mode.
- Never write outside `.idd/intent/`. During a prospective file rename,
  do a bounded *exact* search for old path references elsewhere in the
  project; if such dependencies cannot be safely handled without changing
  outside files, skip the complete rename group.
- `idd-intent-structure-audit` owns semantic structure and conflicts;
  `idd-intent-normalize-current` owns substantive move/split/merge/delete,
  including archives and process-only documents; `idd-intent-change` owns
  product truth; `idd-glossary-build` exclusively owns Glossary writes;
  `idd-engineering-change` owns Engineering writes and allocation;
  `idd-project-init` owns full initialization and agent instructions.
  Findings in these categories remain visible but **diagnostic-only** here.

## Conservative fix policy

Before changing anything, classify every finding as `safe-fix`,
`diagnostic-only`, or `ambiguous`. A safe fix requires an existing canonical
rule, one uniquely determined target state, a purely technical edit, all
dependencies resolvable, and preservation of all user-owned data. Otherwise
report the diagnostic and do not edit. These permissions apply **only** in
`mode: fix`. Continue performing every check below even when there is no
corresponding repair.

### Stable IDs and filenames

- Current documents directly under `.idd/intent/` use
  `^IDD-\d{4}\.(spec|adr|spike)-[a-z0-9][a-z0-9-]*\.md$`.

  Recognize legacy `0001.spec-editor.md` -> `IDD-0001.spec-editor.md`
  only when existing ID/type/slug are unambiguous, including heading and
  INDEX evidence. Change only the missing technical namespace prefix.
- Before renaming, detect conflicting IDs, duplicate claimed identities,
  occupied target paths, and case-insensitive path collisions. Do not pick
  between conflicting claims. If any affected link cannot be preserved,
  skip the *entire* dependent rename group.
- An `IDD-NNNN` is persistent identity: gaps are legal, deleted IDs are
  never reused, and no mass renumbering, new ID allocation, type conversion,
  or rewriting an ID because a slug changed is permitted. New-document ID
  allocation belongs to the owning workflow and `numbering.md`, including
  Git history.
- First H1 must have the same `IDD-NNNN` and `spec|adr|spike` as its file.
  Fix a purely mechanical legacy prefix, or unambiguous heading syntax, only
  when identity is established by independent evidence. Keep user-authored
  heading suffix and additional text; **do not** require entire H1 slug text
  to equal the filename. Conflicting IDs are diagnostic-only.

### INDEX.md: surgical table edits

- Preserve the canonical `Document | Role | Area | Notes | Replaces` schema
  and any extra user columns. The key `Document` contains only plain
  `IDD-NNNN`; the column is **Role**, not Type. Do not regenerate INDEX
  from scratch or reorder existing rows.
- Convert a `Document` cell containing an unambiguous legacy number, current
  filename, relative path, inline-code filename, or Markdown link to the
  corresponding uniquely identified plain ID. Preserve every other cell.
- When a unique existing current document lacks a row, insert one minimally:
  derive Document and Role (`Spec`, `ADR`, `Spike`) from the filename;
  use the established empty/`—` placeholders for other columns. Note
  missing discovery metadata; **never invent** Area, Notes, or Replaces.
- Remove exact duplicate rows only when this loses no associated comments
  or notes. Conflicting duplicate-ID rows remain unchanged with an error.
  A missing document referenced from INDEX is not sufficient grounds for
  deleting its row. Retain author notes and custom columns.
- Fix separators/spacing only inside an unambiguous managed table; do not
  normalize unrelated Markdown. Remove the exact bootstrap placeholder
  "No current `IDD-NNNN` documents have been created yet." only when
  real current documents exist. Do not strip other explanations.
- A provably wrong Role can be adjusted from the unique file's type, but an
  uncertain role or substantive state requires diagnosis. Historical
  "Superseded" prose in Notes alone is not a spec lifecycle marker.
  `Replaces` is a normative ID relation: repair only exact, unambiguous
  ID representation.

### Formal relations, local links, and sections

- In recognized normative fields such as `Related`, `Replaces`,
  `Supersedes`, and `Depends on`, use a unique current `IDD-NNNN`.
  Convert a bare `0007` only *inside* a recognized relation when it maps
  unambiguously to `IDD-0007`. Check missing or multiply-resolved
  targets; do not choose candidates by topic or modify ordinary numbers.
- For a path renamed safely, edit only exact affected local Markdown link
  destinations. Preserve display label, relative form, query, `#anchor`,
  and surrounding formatting. A regular Markdown link stays a link, not a
  plain relation ID. Do not change external URLs, absolute paths outside
  scope, fenced/inline code, quoted history, or arbitrary prose identifiers.
- Validate specs/ADRs/spikes against the **minimal structural contract**
  defined in `references/document-types.md` and the common identity, INDEX
  and relation rules. The headings of packaged or user-owned templates
  are examples, never an independent list of mandatory sections. Accept
  supported equivalent presentation, regardless of heading order or number.
  Validate a single recognized allowed ADR status; do not require a same-named
  `## Decision` or `## Question` heading. In fix mode repair only
  unmistakable mechanical syntax/metadata defects with a unique result.
  Never infer or invent ADR status, architectural decision, research question,
  acceptance criterion, research result, or missing optional section.
  Never insert `None`, `TBD` or empty headings to achieve pass.
  Semantic completeness belongs to the owning workflow, not mechanical lint;
  accepted ADRs must not be semantically rewritten.
- No blanket Markdown formatter, paragraph reflow, section sorting,
  code-block rewriting, archive cleaning, progress-checklist deletion or
  stylistic rephrasing. Diagnostic findings about task/status leakage,
  reports, archives, contradictions and historical content stay in place.

### Restore missing standard bootstrap resources

Only when `.idd/intent/` already exists in an initialized project:

1. Check for `README.md`, `INDEX.md` and
   `_templates/spec.md`, `_templates/adr.md`, `_templates/spike.md`.
2. In fix mode copy **only missing** files from this skill's
   `assets/bootstrap/.idd/intent/`, the exact installed canonical
   version. No handwritten substitutes or dependencies on
   `idd-project-init` resource paths. If assets are missing, report
   an error and skip restoration.
3. If INDEX is missing, copy the canonical scaffold and add minimal rows
   for existing unique documents; preserve its explanatory material.
4. Existing user templates, README and INDEX must never be overwritten
   just because their text differs from canonical. Check a project template
   for usability as a starting point and instructions explicitly violating
   the type contract; do not require canonical optional headings, order,
   field coverage or byte-for-byte equality. A final-document field missing
   from a template may be supplied by the owning creation workflow.
5. An optional missing Glossary or Engineering layer is valid and
   must not be created. Existing Glossary and Engineering are never
   modified by lint, including ID allocator metadata.

### Dependency groups, dirty trees, and failures

- Treat a filename rename, associated first heading, INDEX and every
  in-scope local link update as **one dependent repair group**. Discover
  precise internal/external references and verify uniqueness, target path,
  scope, writable files and compatibility *before* any write. Abort the
  whole group if uncertain. Independent safe edits may still proceed.
- An already dirty working tree is allowed. Read current staged/unstaged
  and untracked content; preserve unrelated modifications. Before each
  edit/rename, confirm the source has not changed since inspection.
  Conflicting concurrent/user changes cause the affected repair to be
  skipped, not overwritten. Do not use broad regex replacement or write
  temporary artifacts under Intent.
- Use minimal changes preserving original encoding, newline form,
  unaffected whitespace, comments, examples, custom metadata and all
  semantic content. A successful second fix on the same tree is a no-op.
- If part of a dependent group fails to write, stop its remaining actions,
  mark that group incomplete, report *exactly* what changed, and diagnose
  remaining defects. No hidden partial success or automatic Git rollback.

## Fix execution protocol

1. Resolve `mode: check|fix` and scope from the complete user request.
2. Inspect project state, canonical references, and available packaged assets.
3. Run **all existing checks** (including read-only Glossary and Engineering),
   collecting errors and warnings for the selected scope.
4. Classify findings; build a stable document identity map and enumerate
   affected exact link/INDEX dependencies.
5. Check file/path collisions, reference resolution, external references,
   dirty-tree preservation, and expected source versions.
6. Group dependent operations and reject any unsafe group.
7. Apply one pass of independent safe groups with minimal edits.
8. Perform **one mandatory re-check** of affected scope/dependencies;
   never initiate an unbounded autofix loop.
9. Report actual fixes, new/deleted/renamed/changed paths, skipped groups,
   remaining warnings/errors, and post-check result.

In `check`, stop after step 3 and report without writing. In `fix`,
`pass` is determined *only* by the re-check, never by planned fixes.
If the post-check cannot complete, return `fail (validation incomplete)`.
Use Read/Glob/Grep/Edit/Write and bounded shell commands, not a CLI,
MCP server, generated validator, runtime, or agent factory.

## Checks

Check that:

- `.idd/intent/README.md` exists;
- `.idd/intent/INDEX.md` exists;
- every `Document` entry in `INDEX.md` is exactly a plain `IDD-NNNN` identifier
  matching `^IDD-\d{4}$`, not a filename, path, inline-code filename, or Markdown
  link;
- every `Document` identifier appears exactly once in `INDEX.md`;
- every `Document` identifier in `INDEX.md` resolves to exactly one current file
  matching `.idd/intent/IDD-NNNN.*.md`;
- every current `IDD-NNNN` document under `.idd/intent/` is listed exactly once in
  `INDEX.md` by its stable identifier;
- every current intent filename matches
  `^IDD-\d{4}\.(spec|adr|spike)-[a-z0-9][a-z0-9-]*\.md$`;
- no current intent document uses the legacy bare numeric filename format
  `^\d{4}\.(spec|adr|spike)-`;
- the `IDD-NNNN` identifier and document type in the first Markdown heading
  match the filename;
- `.idd/intent` has no archive directory;
- `.idd/intent/import-report.md` does not exist;
- generated, import, task, progress, or process reports are not stored under
  `.idd/intent`;
- `INDEX.md` has no `Archived` section;
- no current spec links to deleted document storage;
- no file under `.idd/intent` references `.idd/intent/archive/...`;
- obsolete/task-like/process-only docs are reported as delete candidates, not
  preservation candidates;
- templates and support files are not listed as current specs;
- only genuinely mandatory structural fields of the document type exist and
  are unambiguous; absence of optional template headings is neither error nor
  warning;
- `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar normative
  relations use `IDD-NNNN` identifiers and point to existing current documents;
- normative relations do not use bare four-digit document numbers;
- Related Specifications links point to existing files or valid external
  references;
- specs do not contain obvious stale `.worklog` references except in
  source/history sections;
- specs do not contain task/progress/status language in normative sections;
- specs do not contain generated chat transcripts;
- specs do not contain obvious contradiction markers such as "supported" in
  Scope and "not implemented" in Non-goals for the same feature;
- ADR files have a single recognized status value from `Proposed`,
  `Accepted`, `Superseded`, `Rejected` without conflicts or unselected
  template choices; the decision itself is assessed by the owning semantic
  workflow, not inferred from a `## Decision` heading;
- spikes represent active non-normative research or unresolved
  investigation; missing `## Question`, `## Result` or
  `## Recommendation` headings are not themselves mechanical defects;
- normative spec sections do not contain fenced build/test shell commands,
  explicit task/progress sections, implementation checklists, migration status,
  or a spec lifecycle status.

When `.idd/intent/GLOSSARY.md` exists, also check that:

- the first heading is exactly `# Project Glossary`;
- the file has no `IDD-NNNN` identifier and is not listed in `INDEX.md`;
- every entry uses a unique non-empty `## <canonical term>` heading,
  case-insensitively;
- every entry contains a non-empty definition before the next entry;
- the only entry metadata field is an optional single `- Aliases:` line;
- an alias is not assigned to more than one canonical term,
  case-insensitively;
- the file does not contain acceptance criteria, verification sections, task or
  progress sections, implementation checklists, or lifecycle status;
- the file is not empty and contains at least one term entry.

`idd-intent-lint` must fail if:

- a `Document` entry in `INDEX.md` is not exactly a plain `IDD-NNNN` identifier,
  including when it contains a canonical filename, a path, or a Markdown link;
- the same `Document` identifier appears more than once in `INDEX.md`;
- an `INDEX.md` document identifier resolves to zero or multiple current
  `.idd/intent/IDD-NNNN.*.md` files;
- a current `IDD-NNNN` document is missing from `INDEX.md` or is represented there
  by something other than its stable identifier;
- a current intent filename does not use the canonical
  `IDD-NNNN.type-short-title.md` format;
- a current intent filename uses the legacy bare numeric format;
- a document heading uses a missing, malformed, or filename-mismatched
  `IDD-NNNN` identifier;
- an ADR has no recognized single allowed status, has conflicting status
  values, or retains a template choice list instead of one selected status;
- an archive directory exists under `.idd/intent`;
- `.idd/intent/import-report.md` exists;
- generated, import, task, progress, or process reports exist under `.idd/intent`;
- `INDEX.md` contains an `Archived` section;
- `INDEX.md` links to deleted document storage;
- any file under `.idd/intent` references `.idd/intent/archive/...`;
- any `Related`, `Replaces`, `Supersedes`, `Depends on`, or similar normative
  relation uses a bare four-digit document number or points to a missing current
  document;
- an ordinary spec contains `Status: Current`, `Status: Superseded`,
  `Superseded by`, or another explicit lifecycle status;
- `INDEX.md` models ordinary specs as `Current`, `Completed`, `Superseded`, or
  another lifecycle status;
- a normative spec section contains a fenced shell block with a build or test
  command such as `dotnet build`, `dotnet test`, `cargo test`, `mvn test`,
  `gradle test`, or `pytest`;
- a normative spec contains an explicit task/progress section, implementation
  checklist, or migration status;
- `GLOSSARY.md` is listed as a current document in `INDEX.md`;
- `GLOSSARY.md` has a malformed heading, duplicate canonical terms, duplicate
  aliases assigned to different terms, an empty entry, unsupported entry
  metadata, or no term entries.

Warn, without failing automatically, when a normative spec contains source file
names such as `.cs`, `.java`, `.ts`, or `.py`; private-style identifiers;
method-call notation; constructor names; dependency-injection registration
instructions; test method names; CLI commands; or terms such as `remaining`,
`finish`, `complete migration`, or `follow-up implementation`. Public APIs and
durable architecture types may legitimately match these patterns.

When a glossary exists, warn without failing automatically when:

- a definition is long enough to look like a specification section;
- entries contain normative words such as `must`, `shall`, acceptance language,
  or verification requirements;
- a term appears to be an ordinary technical term used in its ordinary meaning;
- an entry appears to document a private implementation identifier;
- aliases look like distinct concepts rather than equivalent names.

Do not apply implementation-leakage checks to clearly non-normative sections
named `Source history`, `Migration source`, `Provenance`, `Imported from`, or
`Historical note`; those sections must not state current requirements.

Mechanical lint may flag suspicious wording with existing safe heuristics.
It must not warn merely because a template heading is absent, or claim to have
completed semantic review. A mechanical pass says nothing about whether a spec
expresses Product Intent, an ADR records a decision, or a spike has a question. Glossary inclusion quality is primarily reviewed by
`idd-glossary-build`; lint only catches cheap structural problems and obvious
scope leakage.

## Engineering layer

When `.idd/engineering/` exists, apply the `Mechanical Engineering Validation` defined in `references/engineering-guardrails.md`.

Absence of the Engineering layer is valid. Engineering validation is read-only in **both** modes. A valid legacy layer without allocator remains readable; lint does not migrate, create, repair, or advance allocator state. Lint does not make semantic Engineering decisions such as ownership, Intent-versus-Engineering classification, or Conditional applicability.

Warn, without failing automatically, when a Rule contains concrete source filenames, private type names, constructor names, or other text that may be incidental implementation detail rather than durable guidance.

Use the canonical reference directly instead of copying its structural checklist into this skill.

## Output Format

Report `Mode` (`check` or `fix`), the precise `Scope`, and
`Result: pass | fail`. A pass means no blocking mechanical errors
**within that scope**; it does not prove semantic product correctness.

For `check`, show Errors, Warnings and Suggested fixes, with no Fixed
section and no writes. For `fix`, show Fixed (actual completed repairs),
Errors, Warnings, Skipped fixes (with concrete reasons) and Changed files
(including separate created/untracked, removed, and renamed paths).
Unresolved errors remain errors after a skipped fix.

Example:

```md
# IDD Mechanical Lint Report

Mode: fix
Scope: .idd/intent/
Result: fail

## Fixed
- Renamed 0003.spec-editor.md -> IDD-0003.spec-editor.md
- Updated heading, INDEX row, and two links

## Errors
- IDD-0008: conflicting duplicate INDEX rows

## Warnings
- IDD-0011: possible process wording in normative section

## Skipped fixes
- IDD-0008: rows have differing Notes; author decision required

## Changed files
- .idd/intent/IDD-0003.spec-editor.md (renamed)
- .idd/intent/INDEX.md
```

Always report the result of the actual last validation. Never claim that
a partly-applied dependent group succeeded or that an unrun live check passed.

## Examples

User request:

```text
Check whether `.idd/intent` is mechanically consistent.
```

Expected behavior:

- use `idd-intent-lint`;
- check `INDEX.md` ID-only document entries, filenames, document headings, links,
  required structural fields (not template headings), relation identifiers,
  stale `.worklog` references, and the
  optional glossary when present;
- report pass/fail and warnings;
- do not edit files.

## Non-goals

Do not use this skill to:

- rewrite specs;
- import source material;
- reorganize product areas;
- decide whether product behavior is correct;
- build, expand, or prune the project glossary;
- perform implementation conformance checks.

Use `idd-intent-structure-audit` for broad structural diagnostics. Use
`idd-glossary-build` for explicit glossary creation or maintenance.
