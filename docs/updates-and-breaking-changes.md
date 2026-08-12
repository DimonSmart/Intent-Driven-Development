# Updates and Breaking Changes

This page records IDD changes that require action in repositories that already use the toolkit.

Updating the installed plugins and migrating project-owned files are separate operations. Follow [Updating IDD](updating-idd.md) to refresh `idd-intent` and `idd-factory`. Then apply any relevant migration instructions below. Plugin updates do not automatically rewrite a repository's `.idd/intent/` directory.

## 2026-08-11 — Programmatic Factory Runtime

Factory orchestration is now owned by the packaged .NET 10 runtime. Active runs
use authoritative `.idd/factory/current/state.json`, stable work-item filenames,
versioned worker results, and a pinned workflow hash. The former LLM step
coordinator and LLM finalizer are removed.

Legacy active runs containing `.ready.md`, `.active.md`, `.completed.md`, or
`.blocked.md` work items are not migrated. Finish such a run with the previous
Factory version, or cancel it and start a new run after updating the plugin.
Existing `.idd/factory/results/` directories remain valid and are not changed.

## 2026-07-31 — Factory Task and Subtask terminology

Factory now reserves `Task` for the complete user-requested unit of work and
uses `Subtask` for each decomposed executable unit. Existing persisted Factory
state remains readable: `request.md`, `run-context.md`, work-item filenames,
and their content-based type detection are unchanged.

Update manual invocations and generated integrations using this migration map:

- `idd-factory-decompose-work` → `idd-factory-decompose-task`
- `idd-factory-execute-task` → `idd-factory-execute-subtask`
- `idd-factory-review-work-result` → `idd-factory-review-task`
- `idd-factory-finish-work` → `idd-factory-finalize-run`

`idd-factory-review-task` formerly meant checkpoint review and now means final
Task review. It has no runtime alias because routing the old name would be
ambiguous; use `idd-factory-review-checkpoint` for a Review checkpoint.

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
- Update `.idd/intent/INDEX.md` document identifiers from `NNNN` to `IDD-NNNN` while preserving the ID-only index representation. The `Document` column must contain only the stable `IDD-NNNN` identifier, not filenames, paths, or Markdown links.
- Rewrite normative relations and prose document references from bare `NNNN` identifiers to stable `IDD-NNNN` identifiers.
- When an existing Markdown link actually targets an intent file, update only its link target from the old filename to the corresponding `IDD-NNNN.type-short-title.md` filename. Do not introduce a new filename-based Markdown link where the source used a document identifier.
- Treat `IDD-NNNN` as the stable document ID. Do not renumber documents.
- Do not add aliases, redirect files, fallback parsing, migration code, or compatibility with the old naming convention.
- Do not modify unrelated numbers such as versions, dates, issue numbers, task sequence numbers, ports, or quantities.
- Verify that no current intent filename uses the old `NNNN.type-short-title.md` format, no normative internal relation uses a bare four-digit document number, and every `INDEX.md` Document entry is exactly an `IDD-NNNN` identifier.
- Run or simulate `idd-intent-lint` and fix all mechanical errors caused by the rename.

Report the renamed files, rewritten references, and verification result.
```

</details>
