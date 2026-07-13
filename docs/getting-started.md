# Getting Started

Intent-Driven Development is distributed as one native plugin named `idd` for Claude Code and Codex.

## Install in Claude Code

Add the marketplace:

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
```

Install IDD:

```bash
claude plugin install idd@intent-driven-development
```

## Install in Codex

Add the marketplace:

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
```

Install IDD:

```bash
codex plugin add idd@intent-driven-development
```

## Initialize a Project

In the target repository, invoke:

```text
idd-project-init
```

The skill creates only project-owned IDD state:

```text
.idd/
.idd/intent/
.idd/plugins.json
```

It adds minimal bootstrap intent documents when they are missing. It does not copy plugin skills into the repository and does not create agent-specific skill directories.

`.idd/plugins.json` declares that the project uses the `idd` plugin. It is project metadata for people and IDD workflows; it does not install the plugin.

## Choose a First Workflow

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

For explicit temporary multi-step orchestration:

```text
idd-factory-create-work-plan
idd-factory-execute-work-plan
idd-factory-review-work-result
idd-factory-finish-work
```

Factory workflows are included in the same `idd` plugin. Their plans, tasks, reviews, and status are temporary and must not become product intent.

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

The installed plugin should be named `idd` and come from the `intent-driven-development` marketplace.

## Update

Claude Code:

```bash
claude plugin marketplace update intent-driven-development
claude plugin update idd
```

Codex:

```bash
codex plugin marketplace upgrade
codex plugin remove idd
codex plugin add idd@intent-driven-development
```

## Migrate from the Split Plugins

Older releases exposed `idd-core` and `idd-factory` separately. The marketplace now publishes one plugin named `idd`.

Claude Code marketplace rename metadata maps both old plugin names to `idd`. When a manual migration is needed, remove the old plugins and install `idd`:

```bash
claude plugin uninstall idd-factory
claude plugin uninstall idd-core
claude plugin install idd@intent-driven-development
```

Codex:

```bash
codex plugin remove idd-factory
codex plugin remove idd-core
codex plugin add idd@intent-driven-development
```

Update project declarations so `.idd/plugins.json` contains:

```json
{
  "plugins": [
    "idd"
  ]
}
```

## Remove

Claude Code:

```bash
claude plugin uninstall idd
claude plugin marketplace remove intent-driven-development
```

Codex:

```bash
codex plugin remove idd
codex plugin marketplace remove intent-driven-development
```

## Troubleshooting

If Claude Code cannot find IDD:

```bash
claude plugin marketplace list
claude plugin marketplace update intent-driven-development
```

If Codex cannot find IDD:

```bash
codex plugin marketplace list
codex plugin marketplace upgrade
codex plugin list --available --json
```

For methodology questions, see [Methodology](methodology.md). Repository generation, validation, and release instructions belong in [Project Maintenance](project-maintenance.md).
