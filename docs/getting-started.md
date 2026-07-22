# Getting Started

Intent-Driven Development is distributed as two native plugins for Claude Code and Codex:

```text
idd-intent    durable product memory
idd-factory   temporary implementation organization
```

`idd-intent` is standalone and is the default installation. `idd-factory` is optional and requires `idd-intent`.

## Install in Claude Code

Add the marketplace:

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
```

Install durable product-memory workflows:

```bash
claude plugin install idd-intent@intent-driven-development
```

Install Factory only when temporary multi-step implementation orchestration is needed:

```bash
claude plugin install idd-factory@intent-driven-development
```

## Install in Codex

Add the marketplace:

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
```

Install durable product-memory workflows:

```bash
codex plugin add idd-intent@intent-driven-development
```

Optional Factory:

```bash
codex plugin add idd-factory@intent-driven-development
```

## Initialize a Project

With `idd-intent` installed, invoke in the target repository:

```text
idd-project-init
```

The workflow is performed directly by the Coding Agent. No generator, CLI helper, installation hook, or application runtime writes project agent instructions.

The skill creates the project-owned durable state:

```text
.idd/
.idd/intent/
.idd/plugins.json
```

It also creates or updates exactly one repository-root Coding Agent instruction file:

```text
Codex        AGENTS.md
Claude Code  CLAUDE.md
```

The file receives one minimal managed IDD section stating that the project uses Intent-Driven Development, that `.idd/intent/` is the current product truth, and that installed IDD skills should be used for intent, implementation, and verification workflows.

Existing unrelated instructions are preserved. If the file already contains a managed IDD block or clearly IDD-specific instructions, `idd-project-init` updates and consolidates them instead of appending a second section. Re-running initialization is idempotent.

The skill adds minimal bootstrap intent documents when they are missing. It does not copy plugin skills into the repository and does not create agent-specific skill directories.

The default `.idd/plugins.json` declaration is:

```json
{
  "plugins": [
    "idd-intent"
  ]
}
```

This file is project metadata for people and IDD workflows; it does not install plugins.

When Factory is explicitly enabled, the declaration may become:

```json
{
  "plugins": [
    "idd-intent",
    "idd-factory"
  ]
}
```

Factory working data belongs under `.idd/factory/` and remains temporary.

## Choose a First Intent Workflow

For an existing product:

```text
idd-intent-import
```

For a new product area or an unclear request:

```text
idd-intent-brainstorm
```

For a requested product change:

```text
idd-intent-change
```

To implement current intent:

```text
idd-code-implement
```

To check implementation against intent:

```text
idd-code-check-implementation
```

## Use Factory Deliberately

After installing `idd-factory`, use its temporary workflow when a change needs explicit planning and review stages:

```text
idd-factory-create-work-plan
idd-factory-execute-work-plan
idd-factory-review-work-result
idd-factory-finish-work
```

Factory may consume intent, but it must not invent or silently modify product truth. Missing or contradictory intent must be resolved through `idd-intent` workflows.

See [Using IDD](using-idd.md) for common workflows and example prompts.

## Verify Installation

Claude Code:

```bash
claude plugin list --json
```

Codex:

```bash
codex plugin marketplace list
codex plugin list --json
```

The default installation should contain `idd-intent`. `idd-factory` appears only when explicitly installed.

## Update

Claude Code:

```bash
claude plugin marketplace update intent-driven-development
claude plugin update idd-intent
```

When Factory is installed:

```bash
claude plugin update idd-factory
```

Codex:

```bash
codex plugin marketplace upgrade
codex plugin remove idd-intent
codex plugin add idd-intent@intent-driven-development
```

When Factory is installed:

```bash
codex plugin remove idd-factory
codex plugin add idd-factory@intent-driven-development
```

## Migrate from Earlier Plugin Names

Earlier releases used `idd-core`, then briefly published a unified `idd` plugin. The durable plugin is now named `idd-intent`.

Claude Code marketplace rename metadata maps both `idd-core` and `idd` to `idd-intent`. For a manual migration:

```bash
claude plugin uninstall idd
claude plugin uninstall idd-core
claude plugin install idd-intent@intent-driven-development
```

Keep or install Factory only when needed:

```bash
claude plugin install idd-factory@intent-driven-development
```

Codex manual migration:

```bash
codex plugin remove idd
codex plugin remove idd-core
codex plugin add idd-intent@intent-driven-development
```

Optional Factory:

```bash
codex plugin add idd-factory@intent-driven-development
```

Normalize project declarations by replacing `idd` or `idd-core` with `idd-intent`. Preserve `idd-factory` only in projects that intentionally use Factory.

## Remove

Claude Code:

```bash
claude plugin uninstall idd-factory
claude plugin uninstall idd-intent
claude plugin marketplace remove intent-driven-development
```

Codex:

```bash
codex plugin remove idd-factory
codex plugin remove idd-intent
codex plugin marketplace remove intent-driven-development
```

## Troubleshooting

If Claude Code cannot find the plugins:

```bash
claude plugin marketplace list
claude plugin marketplace update intent-driven-development
```

If Codex cannot find the plugins:

```bash
codex plugin marketplace list
codex plugin marketplace upgrade
codex plugin list --available --json
```

For methodology questions, see [Methodology](methodology.md). Repository generation, validation, and release instructions belong in [Project Maintenance](project-maintenance.md).
