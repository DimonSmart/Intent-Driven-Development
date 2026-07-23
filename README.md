# Intent-Driven Development

<p align="center">
  <img src="docs/assets/idd-hero.png" alt="Intent-Driven Development thought experiment: delete the implementation, keep only the intent, and rebuild the product" />
</p>

<p align="center">
  <strong>Durable product intent for disposable implementations.</strong>
</p>

## What Is Intent-Driven Development?

Intent-Driven Development (IDD) is a lightweight methodology and plugin toolkit for AI-assisted software development.

IDD turns product specifications into durable working memory for Coding Agents. Instead of accumulating plans, task states, review notes, and obsolete implementation details, it keeps a compact and up-to-date description of what the product must do.

Agents get the context they need to understand, implement, and verify changes. Temporary implementation work remains temporary, while Git preserves history.

> **Delete the implementation. Keep only the intent. Can a Coding Agent rebuild the product?**

IDD organizes product knowledge so the answer can move closer to **yes**.

## Why IDD?

- **One current source of product truth.** Intent documents describe what the product must continue to do.
- **Less workflow debris.** Plans, task states, reviews, and implementation attempts do not accumulate in the repository.
- **Lower context overhead.** Agents read relevant current intent instead of a growing archive of workflow documents.
- **Replaceable implementations.** Product knowledge survives refactoring, tool changes, agent changes, and rewrites.
- **Git owns history.** Specifications stay current; Git records how they and the implementation changed.

## Core Intent Plugin and Optional Factory

### `idd-intent` — durable product memory

`idd-intent` is the core IDD plugin. It helps Coding Agents create, maintain, use, and verify the current product intent.

It is a complete standalone plugin and is sufficient for normal IDD workflows. Start here.

### `idd-factory` — lightweight implementation orchestration

`idd-factory` is an optional plugin for larger tasks that benefit from decomposition, resumable execution, and independent review.

Factory is actively developed with a strong focus on token efficiency. Its primary optimization target is to make structured Factory-driven implementation cost close to equivalent direct Coding Agent commands in token usage — adding control and reliability without turning orchestration into a token multiplier.

The direction is maximum economy: compact state, minimal handoffs, and only as much orchestration as the task actually needs. Install Factory when that additional structure is useful; keep using `idd-intent` alone when a direct workflow is enough.

## Quick Start

Start with the standalone `idd-intent` plugin.

### Claude Code

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
claude plugin install idd-intent@intent-driven-development
```

### Codex

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
codex plugin add idd-intent@intent-driven-development
```

Open the target repository and initialize IDD:

```text
idd-project-init
```

For an existing implementation without current intent documents, initialization can offer the interactive `idd-intent-bootstrap` workflow to analyze the project and propose its initial intent model.

Then describe what you need naturally. For example:

```text
Use idd-intent-brainstorm to help me clarify a feature that lets users compare two local folders without modifying either side.
```

For a complex implementation task, add the optional Factory plugin later:

### Claude Code

```bash
claude plugin install idd-factory@intent-driven-development
```

### Codex

```bash
codex plugin add idd-factory@intent-driven-development
```

## Use Cases

Open [IDD Use Cases](docs/using-idd.md) when you need to decide what to do next. It covers existing and new projects, product changes, implementation-only work, audits, verification, Factory, installation checks, updates, and deliberate IDD bypass.

## Updates and Breaking Changes

### 2026-07-23 — Intent document filename namespace

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

## Documentation

- [IDD Use Cases](docs/using-idd.md)
- [Verify Installation](docs/verify-installation.md)
- [Updating IDD](docs/updating-idd.md)
- [Existing Project Guide](docs/existing-project.md)
- [New Project Guide](docs/new-project.md)
- [Methodology](docs/methodology.md)
- [Factory Workflow](docs/factory-workflow.md)
- [Factory Skills Reference](docs/factory-skills.md)

## License

MIT
