# Project Maintenance

This document describes how this repository is organized, how canonical source becomes CodingAgent-specific output, and how releases are packaged.

## Current Model

Main instruction files are small routers:

```text
CLAUDE.md
AGENTS.md
GEMINI.md
.github/copilot-instructions.md
```

They say when to use IDD, where product intent lives, and which focused skills to load.

Detailed IDD workflows live in skills:

```text
.claude/skills/*
.agents/skills/*
.github/skills/*
```

Project product intent lives in `.idd/intent/`.

Optional factory execution state lives under `.idd/factory/work/` when the
factory pack is installed and used. Factory artifacts are temporary work files,
not specifications.

Do not put full methodology into `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, or `.github/copilot-instructions.md`.

## How CodingAgents Fit In

Codex, Claude, Gemini, GitHub Copilot, and other CodingAgents have different instruction formats. That is a tooling detail.

The project should not make one CodingAgent's format the source of truth.

IDD keeps canonical methodology, compact bootstrap packs, project rules, and skills in source files, then generates CodingAgent-specific output from them.

```text
canonical source -> adapters -> generated CodingAgent files and skills
```

In this repository, `src/canonical/` is authoritative and `generated/` is build output.

If something important changes, update the canonical source and regenerate the CodingAgent files.

## Repository Layout

```text
src/canonical/       canonical methodology, project files, skills, and packs
src/canonical/packs/ compact bootstrap content and pack manifest
src/canonical/factory/roles/ platform-neutral factory role prompts
src/canonical/methodology/ full methodology for skills and project docs
src/canonical/skills/ task-specific workflows
src/adapters/        CodingAgent-specific entry points and adapter capabilities
generated/           generated files for each CodingAgent
tools/generate/      C# generator
tools/idd-tool/      .NET global tool CLI installer
tools/smoke-tests/   smoke tests for generated output
scripts/             local check and release helper scripts
```

Edit files under `src/canonical/` and `src/adapters/`.

Then run:

```powershell
.\scripts\Check.ps1
```

The `generated/` directory is intentionally ignored by git. It is reproducible output from the canonical source and adapters. Do not edit it as product knowledge.

## Canonical Source and Generated Output

`src/canonical/` is the source of truth for Intent-Driven Development.

Canonical files define the method, project `.idd/intent/` files, reusable skills, and instruction packs. CodingAgent-specific adapters may change paths, entry point names, front matter, and supported features, but they must not change the meaning of canonical content.

Generated files are delivery formats for specific CodingAgents. They are not product knowledge, and they should not be edited directly.

## Skill Metadata

`src/canonical/skills/skill-descriptions.json` stores skill descriptions,
invocation policy, and optional adapter-specific metadata.

Missing `invocation` means `auto`: a normal skill the CodingAgent may select
when relevant. `invocation: "manual"` means the workflow is a user-invoked
command only. Manual workflows must not be installed for adapters that cannot
guarantee manual-only invocation.

Canonical skill body remains CodingAgent-neutral.

Adapter-specific behavior, such as Claude Code frontmatter fields, belongs to `adapters.<adapter>.frontmatter`.

Do not put Claude-specific frontmatter directly into canonical skill markdown.

## Packs

Pack membership is defined in `src/canonical/packs/pack-manifest.json`.

`core` is the default pack. It owns `.idd/intent/` project files and the
`idd-intent-*` and `idd-code-*` skills.

`factory` is optional. It depends on core and installs factory skills, role
prompt references, and `.idd/factory/.gitignore`. It must not place work plans,
task briefs, review notes, or logs in `.idd/intent/`.

All public IDD commands use the `idd-` prefix and the format
`idd-<area>-<action>`. This keeps IDD commands grouped in autocomplete and
avoids collisions with project, plugin, or tool commands.

Factory workflows may be selected automatically for temporary multi-task
orchestration, sequencing, review gates, and coordinated implementation. They
must stop when current intent is missing or insufficient and route to intent
skills instead. `idd-skip` is manual-only because automatic selection would
defeat its purpose; it applies only to the current explicitly opted-out request.

Generated bundles may contain all skill-capable CodingAgent files, but installers
copy only the skills and project files selected by packs. Core-only entry
routing must not mention factory skills. Factory-enabled entry text may mention
factory as temporary orchestration and must describe its intent-gap boundary.

## Workflow

The usual maintenance workflow is:

```text
1. Update canonical methodology or adapters.
2. Run the local check.
3. Review generated output.
4. Use generated files with the selected CodingAgent.
5. Keep durable decisions in canonical specs, not in chat.
```

In practice, this means:

```powershell
.\scripts\Check.ps1
```

The check should prove that the generated CodingAgent files are still reproducible and valid enough to use.

## Example: Changing Existing Command Completion Behavior

Request:

```text
Command completion should not accept the first suggestion by default.
The default selected item should mean no completion.
```

Correct routing:

```text
idd-intent-change:
  update .idd/intent/0018.spec-command-history-completion.md

idd-code-implement:
  update command completion behavior and tests

idd-code-check-implementation:
  verify implementation against 0018
```

Do not create a new spec, because command completion is already owned by 0018.

## Distribution

Intent-Driven Development is release-first.

The canonical versioned artifact is the GitHub Release archive. It contains the canonical source, adapters, generated CodingAgent files, manifest, license, README, and checksums for the released content.

Current distribution:

```text
- GitHub Releases for canonical versioned ZIP artifacts
- .NET tool for CLI installation
```

Use the .NET global tool when you want a .NET-native installer command:

```powershell
dotnet tool install --global DimonSmart.IntentDrivenDevelopment.Tool
intent-driven-development list-targets
intent-driven-development list-coding-agents
intent-driven-development list-packs
intent-driven-development install --target codex
```

## Release Flow

Pull requests and pushes to `main` run:

```text
.github/workflows/idd-smoke.yml
```

Release publication follows the tag-based flow:

```powershell
.\publish-next-version.ps1
```

The script runs the local check, creates the next `vMAJOR.MINOR.PATCH` tag, and pushes it.

Then `.github/workflows/publish-package.yml` packs the release archive, checksums, and `DimonSmart.IntentDrivenDevelopment.Tool`. It creates a GitHub Release and publishes the .NET tool package to NuGet.

The project is still evolving. The .NET installer is the supported CLI path, while the GitHub Release ZIP remains the runtime-neutral, versioned artifact. The removed npm distribution may be reconsidered as a separate future design; any future installer should avoid duplicating installation business logic in another language without a strong reason.

Local release packaging uses the same script as CI:

```powershell
.\scripts\Pack-Release.ps1 -Version 1.0.0
```

## Maintenance Rules

There should be one canonical source. Everything else is generated, adapted, or temporary.

`--target` is the CLI compatibility name for selecting a CodingAgent.

The adapters are translation layers. The generated files are delivery formats for specific CodingAgents. The engineer still owns the result.

Migration notes from the older project model are in `src/canonical/methodology/migration-from-copilotinstructions.md` when that file exists in a checked-out version of the repository.
