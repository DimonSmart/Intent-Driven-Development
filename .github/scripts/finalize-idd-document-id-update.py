from pathlib import Path
import re

root = Path('.')


def read(path: str) -> str:
    return (root / path).read_text(encoding='utf-8')


def write(path: str, text: str) -> None:
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding='utf-8')


# Normalize terminology wherever the text refers to current IDD documents.
phrase_replacements = (
    ('numbered current documents', 'current `IDD-NNNN` documents'),
    ('numbered current document', 'current `IDD-NNNN` document'),
    ('current numbered documents', 'current `IDD-NNNN` documents'),
    ('current numbered document', 'current `IDD-NNNN` document'),
    ('numbered current specs', 'current `IDD-NNNN` specs'),
    ('numbered current spec', 'current `IDD-NNNN` spec'),
    ('current numbered specs', 'current `IDD-NNNN` specs'),
    ('current numbered spec', 'current `IDD-NNNN` spec'),
)
for markdown in root.rglob('*.md'):
    if '.git' in markdown.parts:
        continue
    original = markdown.read_text(encoding='utf-8')
    updated = original
    for old, new in phrase_replacements:
        updated = updated.replace(old, new)
    if updated != original:
        markdown.write_text(updated, encoding='utf-8')


write(
    'src/canonical/methodology/numbering.md',
    '''# Document IDs and Numbering

## Why intent documents are numbered

Projects evolve. At the beginning, the full product intent is rarely known.

A namespaced document ID gives each piece of intent a stable and unambiguous
identity while the system changes. Titles and filenames may change, documents
may be deleted or replaced by a new owner, but references by ID remain stable.

The sequence records document creation order without turning specifications
into task logs.

Use one increasing numeric sequence across intent documents directly under
`.idd/intent/`. Every document ID starts with the `IDD-` namespace:

```text
IDD-NNNN
```

The canonical filename is:

```text
.idd/intent/IDD-NNNN.type-short-title.md
```

Examples:

```text
.idd/intent/IDD-0001.spec-initial-product-model.md
.idd/intent/IDD-0002.adr-rendering-architecture.md
.idd/intent/IDD-0003.spike-input-layer-feasibility.md
```

The first Markdown heading must start with the same identifier and document type
as the filename, for example:

```md
# IDD-0001.spec-initial-product-model
```

Use `IDD-NNNN` in normative relations and prose references. A bare four-digit
number such as `0019` is not an IDD document identifier.

When finding the next number, inspect current `IDD-NNNN.type-short-title.md`
files and previously assigned `IDD-NNNN` identifiers in Git history, then use
the maximum `NNNN` value plus one. Do not scan or create an archive directory.
Deleted document numbers are never reused. Do not include lifecycle markers such
as `active` or `retired` in filenames.
''',
)

write(
    'src/canonical/project-files/intent/README.md',
    '''# IDD Intent

This directory contains the current working model of product intent and current
decision/research records.

Read `INDEX.md` first, then read the current `IDD-NNNN` documents directly under
this directory that apply to the change.

Every intent document uses a stable `IDD-NNNN` identifier and the canonical
`IDD-NNNN.type-short-title.md` filename. Its first Markdown heading starts with
the same identifier and document type. Bare numeric document identifiers are not
valid.

Current documents may be specs, ADRs, or active spikes.

Do not treat templates, support files, generated reports, or deleted Git history
as current product intent.

There is no `.idd/intent` archive lifecycle. Deleted or previous document
versions are available through Git history.

A spec document has no lifecycle status: its presence here means it is current.
Do not mark specs as Current, Completed, Deprecated, Retired, or Superseded.
Edit an owning spec in place or migrate its remaining current intent and delete
it. ADR status remains part of ADR decision records; a spike remains only while
its question is active.
''',
)

write(
    'src/canonical/project-files/intent/INDEX.md',
    '''# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent documents.
It is not the source of truth.

Current `IDD-NNNN` documents directly under `.idd/intent/` contain normative
product intent, ADRs, or active spikes.

Git history is the source for deleted or previous document versions.

## Current documents

No current `IDD-NNNN` documents have been created yet.

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
''',
)


# Evaluation fixtures model the same canonical filenames and headings.
fixture_data = {
    'authentication-product': ('authentication', 'Authentication methods.'),
    'checkout-product': ('checkout', 'Checkout completion.'),
    'search-product': ('search', 'Search result interaction.'),
}
for product, (slug, description) in fixture_data.items():
    base = root / 'evals/idd-route/fixtures' / product / '.idd/intent'
    write(
        str(base / 'README.md'),
        '''# Intent

Current `IDD-NNNN` documents are the normative product intent.
''',
    )
    write(
        str(base / 'INDEX.md'),
        f'''# Intent Index

- `IDD-0001.spec-{slug}.md` — {description}
''',
    )
    document = base / f'IDD-0001.spec-{slug}.md'
    text = document.read_text(encoding='utf-8')
    text = re.sub(
        r'^# .*$',
        f'# IDD-0001.spec-{slug}',
        text,
        count=1,
        flags=re.MULTILINE,
    )
    document.write_text(text, encoding='utf-8')


# New-document creation must allocate and write the namespaced ID explicitly.
path = 'src/canonical/skills/idd-intent-new-document.md'
text = read(path)
text = text.replace(
    '- Every new document uses a stable `IDD-NNNN` identifier and the canonical\n'
    '  `IDD-NNNN.type-short-title.md` filename.\n'
    '- Never create a bare `NNNN.type-short-title.md` filename.\n',
    '- Every new document uses a stable `IDD-NNNN` identifier and the canonical\n'
    '  `IDD-NNNN.type-short-title.md` filename.\n'
    '- The first Markdown heading starts with the same `IDD-NNNN` identifier and\n'
    '  document type as the filename.\n'
    '- Never create a bare `NNNN.type-short-title.md` filename or bare numeric\n'
    '  normative relation.\n',
)
text = re.sub(
    r'6\. Find the next number by scanning current `IDD-NNNN` documents directly under\n'
    r'   `\.idd/intent/`\. Do not scan or create an archive directory\. Deleted document\n'
    r'   numbers are not reused\.\n'
    r'7\. Create the document from the matching template\.\n'
    r'8\. Update `INDEX\.md` when a numbered document is added\.',
    '6. Find the next number by inspecting current filenames matching\n'
    '   `IDD-NNNN.type-short-title.md` and previously assigned `IDD-NNNN` identifiers\n'
    '   in Git history. Use the maximum `NNNN` value plus one. Do not scan or create an\n'
    '   archive directory. Deleted document numbers are not reused.\n'
    '7. Create the document from the matching template. Use the same `IDD-NNNN`\n'
    '   identifier and document type in the filename and first Markdown heading.\n'
    '8. Update `INDEX.md` when an `IDD-NNNN` document is added.',
    text,
)
write(path, text)


# Lint must discover and reject the old convention, not merely document it.
path = 'src/canonical/skills/idd-intent-lint.md'
text = read(path)
text = text.replace(
    '- every current spec listed in `INDEX.md` exists;\n'
    '- every current numbered spec under `.idd/intent/` is listed in `INDEX.md`;\n',
    '- every current document listed in `INDEX.md` exists;\n'
    '- every current `IDD-NNNN` document under `.idd/intent/` is listed in\n'
    '  `INDEX.md`;\n'
    '- every current intent filename matches\n'
    '  `^IDD-\\d{4}\\.(spec|adr|spike)-[a-z0-9][a-z0-9-]*\\.md$`;\n'
    '- no current intent document uses the legacy bare numeric filename format\n'
    '  `^\\d{4}\\.(spec|adr|spike)-`;\n'
    '- the `IDD-NNNN` identifier and document type in the first Markdown heading\n'
    '  match the filename;\n',
)
text = text.replace(
    '- `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar numeric\n'
    '  relation references point to existing current numbered docs;\n',
    '- `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar normative\n'
    '  relations use `IDD-NNNN` identifiers and point to existing current\n'
    '  documents;\n'
    '- normative relations do not use bare four-digit document numbers;\n',
)
text = text.replace(
    '- any numeric `Related`, `Replaces`, `Supersedes`, `Depends on`, or similar\n'
    '  relation points to a missing current numbered doc;\n',
    '- any `Related`, `Replaces`, `Supersedes`, `Depends on`, or similar normative\n'
    '  relation uses a bare four-digit document number or points to a missing\n'
    '  current document;\n',
)
text = text.replace(
    '- check `INDEX.md`, files, links, required sections, and stale `.worklog`\n'
    '  references;\n',
    '- check `INDEX.md`, filenames, document headings, links, required sections,\n'
    '  relation identifiers, and stale `.worklog` references;\n',
)
write(path, text)


# Polish import rules and state the canonical identity consistently.
path = 'src/canonical/skills/idd-intent-import.md'
text = read(path)
text = text.replace(
    '- all `IDD-NNNN` `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar\n'
    '  references point to existing current documents;\n',
    '- all `Related`, `Replaces`, `Supersedes`, `Depends on`, and similar\n'
    '  normative relations use `IDD-NNNN` identifiers and point to existing\n'
    '  current documents;\n',
)
write(path, text)


# Remaining canonical prose must not use the legacy terminology or filename form.
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

old_paths = [
    str(path)
    for path in root.rglob('*.md')
    if re.match(r'^\d{4}\.(spec|adr|spike)-', path.name)
]
if old_paths:
    raise RuntimeError(f'Legacy intent filenames remain: {old_paths}')

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
for required_path, markers in required.items():
    content = read(required_path)
    for marker in markers:
        if marker not in content:
            raise RuntimeError(f'Missing {marker!r} in {required_path}')
