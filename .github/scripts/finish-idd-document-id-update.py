from pathlib import Path
import re

root = Path('.')


def write(path: str, content: str) -> None:
    (root / path).write_text(content, encoding='utf-8')


write(
    'src/canonical/skills/idd-intent-new-document.md',
    '''# idd-intent-new-document

Use this skill to create a new owning specification, ADR, or spike when no
existing current document owns the product area or decision.

## Input

The request may explicitly specify the document type:

```text
type: spec | adr | spike
```

Use the requested type when it matches the change. If the type is not
specified, infer it from the change. If the requested type conflicts with IDD
rules, state the mismatch and use the correct document type.

This skill accepts a semantic handoff from `idd-intent-change`,
`idd-intent-brainstorm`, or Factory. The handoff includes document type,
product or decision area, durable intent, why current documents are not valid
owners, related documents, acceptance or decision context, and open questions.

## Rules

- Do not use this skill for changing behavior already covered by an existing
  current spec.
- Use `idd-intent-change` for user-requested changes to existing product behavior.
- Use `idd-intent-new-document` only when a new durable product area, ADR, or spike is
  needed.
- Do not create a spec for task-level changes.
- Do not create a spec for an ordinary dependency update.
- Create a spec only for durable product intent.
- Create an ADR for durable architectural decisions.
- Create a spike for research before a decision.
- Do not create replacement specs only to preserve old wording.
- If the product area is the same, update the existing spec.
- If the product area identity changes, create the new owning spec only after
  this skill's ownership check confirms that no current document owns it.
- Every new document uses a stable `IDD-NNNN` identifier and the canonical
  `IDD-NNNN.type-short-title.md` filename.
- The first Markdown heading starts with the same `IDD-NNNN` identifier and
  document type as the filename.
- Never create a bare `NNNN.type-short-title.md` filename or a bare numeric
  normative relation.
- Git history preserves the deleted document.
- If the requested type does not match the change, do not follow it blindly.
  State the mismatch and use the correct IDD document type.
- Verification sections describe important user scenarios, critical invariants,
  meaningful boundary cases, and justified manual checks. They must not list
  individual test methods, internal classes, private implementation shape, or
  require one automated test per specification sentence.

## Document Type

- `spec` - durable product behavior, domain contracts, acceptance criteria,
  verification rules, shared behavior.
- `adr` - durable architectural decision where rationale, alternatives, and
  tradeoffs matter.
- `spike` - research, experiment, or hypothesis check before committing to
  product or architecture intent.

## Workflow

1. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and relevant current
   `IDD-NNNN` documents directly under `.idd/intent/`.
2. Determine the document type from the explicit input or from the change.
3. Before creating a new document, search `INDEX.md` and relevant current specs
   for an existing owner of the product area.
4. If an owner exists, stop and use `idd-intent-change`.
5. If current intent already exists, update the existing current document
   instead of creating a duplicate.
6. Find the next number by inspecting current filenames matching
   `IDD-NNNN.type-short-title.md` and previously assigned `IDD-NNNN` identifiers
   in Git history. Use the maximum `NNNN` value plus one. Do not scan or create an
   archive directory. Deleted document numbers are not reused.
7. Create the document from the matching template. Use the same `IDD-NNNN`
   identifier and document type in the filename and first Markdown heading.
8. Update `INDEX.md` when an `IDD-NNNN` document is added.
9. Keep the document normative. Do not add local task notes.
''',
)

write(
    'src/canonical/skills/idd-intent-lint.md',
    '''# idd-intent-lint

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
- Report errors, warnings, and suggested fixes only.

## Checks

Check that:

- `.idd/intent/README.md` exists;
- `.idd/intent/INDEX.md` exists;
- every current document listed in `INDEX.md` exists;
- every current `IDD-NNNN` document under `.idd/intent/` is listed in
  `INDEX.md`;
- every current intent filename matches
  `^IDD-\\d{4}\\.(spec|adr|spike)-[a-z0-9][a-z0-9-]*\\.md$`;
- no current intent document uses the legacy bare numeric filename format
  `^\\d{4}\\.(spec|adr|spike)-`;
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
- templates/support docs are not listed as current specs;
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

`idd-intent-lint` must fail if:

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
  checklist, or migration status.

Warn, without failing automatically, when a normative spec contains source file
names such as `.cs`, `.java`, `.ts`, or `.py`; private-style identifiers;
method-call notation; constructor names; dependency-injection registration
instructions; test method names; CLI commands; or terms such as `remaining`,
`finish`, `complete migration`, or `follow-up implementation`. Public APIs and
durable architecture types may legitimately match these patterns.

Do not apply implementation-leakage checks to clearly non-normative sections
named `Source history`, `Migration source`, `Provenance`, `Imported from`, or
`Historical note`; those sections must not state current requirements.

Mechanical lint may flag suspicious wording. It must not claim to have completed
semantic review.

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
- check `INDEX.md`, filenames, document headings, links, required sections,
  relation identifiers, and stale `.worklog` references;
- report pass/fail and warnings;
- do not edit files.

## Non-goals

Do not use this skill to:

- rewrite specs;
- import source material;
- reorganize product areas;
- decide whether product behavior is correct;
- perform implementation conformance checks.

Use `idd-intent-audit` for broad structural diagnostics.
''',
)

# Final strict validation. README intentionally contains the old format inside
# the user-run breaking-change prompt, so it is excluded from this one scan.
legacy_filename = re.compile(r'(?<!IDD-)\b\d{4}\.(?:spec|adr|spike)-')
legacy_terms = ('current numbered', 'numbered current')
legacy_files: list[str] = []
legacy_wording: list[str] = []
for markdown in root.rglob('*.md'):
    if '.git' in markdown.parts:
        continue
    content = markdown.read_text(encoding='utf-8')
    if markdown != Path('README.md') and legacy_filename.search(content):
        legacy_files.append(str(markdown))
    if any(term in content for term in legacy_terms):
        legacy_wording.append(str(markdown))
if legacy_files:
    raise RuntimeError(f'Legacy filename references remain: {legacy_files}')
if legacy_wording:
    raise RuntimeError(f'Legacy current-document wording remains: {legacy_wording}')

legacy_paths = [
    str(path)
    for path in root.rglob('*.md')
    if re.match(r'^\d{4}\.(spec|adr|spike)-', path.name)
]
if legacy_paths:
    raise RuntimeError(f'Legacy intent filenames remain: {legacy_paths}')

required = {
    'README.md': ('## Updates and Breaking Changes', '<details>', '2026-07-23'),
    'src/canonical/methodology/numbering.md': (
        'IDD-NNNN.type-short-title.md',
        'Git history',
        'first Markdown heading',
    ),
    'src/canonical/skills/idd-intent-lint.md': (
        'legacy bare numeric filename format',
        'bare four-digit document numbers',
        'match the filename',
    ),
    'src/canonical/skills/idd-intent-new-document.md': (
        'maximum `NNNN` value plus one',
        'previously assigned `IDD-NNNN` identifiers',
        'first Markdown heading',
    ),
}
for path, markers in required.items():
    content = (root / path).read_text(encoding='utf-8')
    for marker in markers:
        if marker not in content:
            raise RuntimeError(f'Missing {marker!r} in {path}')
