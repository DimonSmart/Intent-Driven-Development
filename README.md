# Intent-Driven Development

<p align="center">
  <img src="docs/assets/idd-hero.png" alt="Intent-Driven Development hero image: durable product intent above temporary work artifacts" />
</p>

> Delete the implementation. Keep only the intent. Can a CodingAgent rebuild the product?

Intent-Driven Development is a plugin marketplace source for Claude Code and Codex. It keeps durable product intent in `.idd/intent/` and delivers the workflows as native agent plugins.

The repository is the canonical source for methodology, skills, plugin metadata, adapters, and marketplace publication.

## What IDD Is

IDD treats specifications and ADRs as durable product memory. Tasks, plans, PR notes, chat summaries, and local implementation notes are temporary work, not product truth.

The plugin model is:

```text
Canonical methodology
        |
        v
Canonical skills
        |
        v
Canonical plugin model
        |
        v
IPlatformAdapter
        |
        +-- Claude native plugin
        +-- Codex native plugin
```

There are two logical plugins:

```text
idd-core
idd-factory
```

`idd-core` owns durable intent workflows and the `idd-project-init` entry point. `idd-factory` depends on `idd-core` and provides temporary execution orchestration. Factory work never becomes product intent.

## User Workflow

Users connect the IDD marketplace for their Coding Agent, install `idd-core`, optionally install `idd-factory`, then run:

```text
idd-project-init
```

The project receives only durable IDD state:

```text
.idd/intent/
.idd/plugins.json
```

Skills remain inside the agent plugin cache. They are not copied into user repositories.

## Repository Shape

```text
src/canonical/              canonical methodology, skills, assets, and plugins
src/canonical/plugins/      canonical plugin model
src/adapters/claude/        Claude adapter input
src/adapters/codex/         Codex adapter input
tools/generate/             marketplace generator
tools/smoke-tests/          marketplace validation
artifacts/marketplace/      local generated output, ignored by git
```

## Start Here

- [Getting Started](docs/getting-started.md) - connect a marketplace, install plugins, and initialize project intent.
- [Methodology](docs/methodology.md) - understand durable product intent and temporary work.
- [Project Maintenance](docs/project-maintenance.md) - maintain canonical source, adapters, generation, and marketplace publication.

The engineer still owns the result. IDD keeps the product memory clear enough for humans and Coding Agents to use it.
