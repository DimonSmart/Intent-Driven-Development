# Project Maintenance

This repository is the canonical source for the IDD plugin marketplaces.

## Current Model

IDD publishes native plugin marketplaces for:

```text
Claude
Codex
```

There are two logical plugins:

```text
idd-core
idd-factory
```

`idd-core` contains the durable intent workflows and `idd-project-init`. `idd-factory` depends on `idd-core` and contains temporary execution orchestration workflows.

## Repository Layout

```text
release tag               shared marketplace plugin version
src/canonical/            canonical methodology, project intent assets, skills, and plugins
src/canonical/plugins/    canonical plugin model
src/canonical/skills/     platform-neutral skill bodies and metadata
src/canonical/factory/    platform-neutral Factory role prompts
src/adapters/claude/      Claude adapter configuration
src/adapters/codex/       Codex adapter configuration
tools/generate/           canonical model to native marketplace generator
tools/smoke-tests/        marketplace smoke tests
scripts/Check.ps1         local validation entry point
```

`artifacts/marketplace/` is local generated output and is ignored by git. The main branch should contain canonical source, not generated marketplace artifacts.

## Canonical Model

`src/canonical/plugins/plugin-manifest.json` defines:

```text
plugins
skills
roles
dependencies
assets
metadata
```

Canonical skills must stay platform-neutral. Adapter-specific behavior belongs in adapter configuration or skill metadata under the relevant adapter key.

## Adapters

The generator uses an `IPlatformAdapter` boundary:

```text
Canonical Model -> IPlatformAdapter -> Native Plugin
```

Core generation logic should not contain Claude or Codex file layout decisions except through adapter implementations.

## Workflow

Use this maintenance loop:

```powershell
.\scripts\Check.ps1
```

The check:

```text
1. Builds the generator.
2. Builds smoke tests.
3. Generates Claude marketplace output.
4. Generates Codex marketplace output.
5. Verifies generator check mode.
6. Runs smoke tests.
```

## Publication

Tag publication runs `.github/workflows/publish-marketplace.yml`.

The workflow:

```text
Checkout
Build Generator
Generate Claude Plugins
Generate Codex Plugins
Validate Claude
Validate Codex
Smoke Tests
Publish Marketplace Branch
GitHub Release
```

The `marketplace` branch contains only ready-to-consume marketplace output. It must not contain canonical source, generator source, or documentation source.

## Rules

- Do not add new supported platforms without a new adapter and an explicit canonical model update.
- Do not put generated marketplace output in main.
- Do not copy skills into user projects.
- Do not create compatibility wrappers for removed distribution paths.
- Keep product intent in `.idd/intent/`.
- Keep Factory data temporary under `.idd/factory/`.
