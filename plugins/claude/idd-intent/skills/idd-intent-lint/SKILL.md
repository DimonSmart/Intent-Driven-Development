---
name: idd-intent-lint
description: Run mechanical `.idd/intent/` consistency checks without editing files.
context: fork
agent: Explore
argument-hint: "[optional spec path or scope]"
allowed-tools: Read Glob Grep Bash
---

# idd-intent-lint

Use this skill to perform cheap mechanical validation over `.idd/intent`.

Formula:

```text
idd-intent-lint = cheap mechanical validation, not semantic review
```

Use it when the user asks whether `.idd/intent` is mechanically consistent.

## Rules

- Do not rewrite files.
- Do not reorganize specs.
- Do not perform broad semantic review.
- Do not resolve product conflicts.
- Treat `GLOSSARY.md` as optional. Its absence is valid and produces no warning.
- Do not create or update the glossary from lint.
- Report errors, warnings, and suggested fixes only.

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
- skills do not contain an archive-enabling flag;
- skills do not contain an archive import action;
- skills do not recommend archiving obsolete specs;
- obsolete/task-like/process-only docs are reported as delete candidates, not
  preservation candidates;
- templates and support files are not listed as current specs;
- required sections exist, or missing sections are reported;
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
- ADR files use ADR-like structure;
- spike files are marked as non-normative research or unresolved
  investigation;
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
- an archive directory exists under `.idd/intent`;
- `.idd/intent/import-report.md` exists;
- generated, import, task, progress, or process reports exist under `.idd/intent`;
- `INDEX.md` contains an `Archived` section;
- `INDEX.md` links to deleted document storage;
- any file under `.idd/intent` references `.idd/intent/archive/...`;
- any `Related`, `Replaces`, `Supersedes`, `Depends on`, or similar normative
  relation uses a bare four-digit document number or points to a missing current
  document;
- any skill contains an archive-enabling flag;
- any skill contains an archive import action;
- any skill recommends moving specs to archive;
- docs describe archive as a normal lifecycle;
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

Mechanical lint may flag suspicious wording. It must not claim to have completed
semantic review. Glossary inclusion quality is primarily reviewed by
`idd-glossary-build`; lint only catches cheap structural problems and obvious
scope leakage.

## Output Format

```md
# IDD Intent Lint Report

## Result

pass | fail

## Errors

Problems that should be fixed.

## Warnings

Suspicious structure or wording.

## Suggested fixes

Concrete file-level recommendations.
```

## Examples

User request:

```text
Check whether `.idd/intent` is mechanically consistent.
```

Expected behavior:

- use `idd-intent-lint`;
- check `INDEX.md` ID-only document entries, filenames, document headings, links,
  required sections, relation identifiers, stale `.worklog` references, and the
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

Use `idd-intent-audit` for broad structural diagnostics. Use
`idd-glossary-build` for explicit glossary creation or maintenance.
