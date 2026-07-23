# Updates and Breaking Changes

This page records IDD changes that require action in repositories that already use the toolkit.

Updating the installed plugins and migrating project-owned files are separate operations. Follow [Updating IDD](updating-idd.md) to refresh `idd-intent` and `idd-factory`. Then apply any relevant migration instructions below. Plugin updates do not automatically rewrite a repository's `.idd/intent/` directory.

## 2026-07-23 — Intent document filename namespace

Intent document identifiers and filenames now use the `IDD-` prefix: `IDD-0001.spec-example.md` instead of `0001.spec-example.md`. The namespace makes document IDs unambiguous in prose, search results, links, logs, and automated repository scans.

This is a breaking change. IDD does not provide an automatic migration or compatibility with the old bare numeric format. Update an existing `.idd/intent/` directory by running the prompt below with a Coding Agent.

<details>
<summary>Prompt: update intent document numbering and internal links</summary>

```text
Update this repository's `.idd/intent/` document identifiers to the current IDD naming convention.

Required result:
- Rename every current intent document from `NNNN.type-short-title.md` to `IDD-NNNN.type-short-title.md`.
- Preserve each existing four-digit number, document type, slug, content, and product meaning.
- Update each renamed document heading so its identifier starts with the same `IDD-NNNN` value.
- Update `.idd/intent/INDEX.md` to use the new filenames and identifiers.
- Update all internal references, including Related, Replaces, Supersedes, Depends on, prose references, and Markdown links, from bare `NNNN` identifiers or old filenames to `IDD-NNNN` identifiers or the corresponding new filenames.
- Treat `IDD-NNNN` as the stable document ID. Do not renumber documents.
- Do not add aliases, redirect files, fallback parsing, migration code, or compatibility with the old naming convention.
- Do not modify unrelated numbers such as versions, dates, issue numbers, task sequence numbers, ports, or quantities.
- Verify that no current intent filename uses the old `NNNN.type-short-title.md` format and no normative internal relation uses a bare four-digit document number.
- Run or simulate `idd-intent-lint` and fix all mechanical errors caused by the rename.

Report the renamed files, rewritten references, and verification result.
```

</details>
