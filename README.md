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

Projects can optionally keep `.idd/verification.md` as their Git-owned operational verification policy. Use `idd-verification-configure` to propose it; the policy keeps repository commands separate from product intent.

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

Questions about IDD itself can be addressed at any time by manually invoking `idd-help`. Use it to ask how the methodology works, why a workflow is chosen, what a skill does, or how a particular IDD rule should be interpreted.

```text
idd-help Why does this change need an intent update?
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

## Update an Existing Installation

Already have `idd-intent` or `idd-factory` installed? Follow [Updating IDD](docs/updating-idd.md) to refresh the marketplace, update or reinstall the plugins, verify the installed versions, and load them in a new session.

Updating the plugin does not automatically migrate project-owned intent documents. Review [Updates and Breaking Changes](docs/updates-and-breaking-changes.md) for repository migration instructions.

## Use Cases

Open [IDD Use Cases](docs/using-idd.md) when you need to decide what to do next. It covers existing and new projects, product changes, implementation-only work, audits, verification, Factory, installation checks, updates, and deliberate IDD bypass.

## Updates and Breaking Changes

Release-specific migration instructions are maintained on the dedicated [Updates and Breaking Changes](docs/updates-and-breaking-changes.md) page.

## Documentation

- [IDD Use Cases](docs/using-idd.md)
- [Verify Installation](docs/verify-installation.md)
- [Updating IDD](docs/updating-idd.md)
- [Updates and Breaking Changes](docs/updates-and-breaking-changes.md)
- [Existing Project Guide](docs/existing-project.md)
- [New Project Guide](docs/new-project.md)
- [Methodology](docs/methodology.md)
- [Factory Workflow](docs/factory-workflow.md)
- [Factory Skills Reference](docs/factory-skills.md)

## License

MIT
