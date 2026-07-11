# Getting Started

Intent-Driven Development can be installed into another repository with the .NET tool or obtained as a versioned GitHub Release ZIP.

This repository ships canonical methodology and generated CodingAgent-specific delivery files. The installed CodingAgent files are generated from the canonical source, not edited as the source of truth.

Use the .NET global tool for CLI installation:

```powershell
dotnet tool install --global DimonSmart.IntentDrivenDevelopment.Tool
intent-driven-development list-coding-agents
intent-driven-development list-packs
intent-driven-development init
intent-driven-development install --target codex
```

The tool requires the .NET runtime/SDK supported by its tool package. The installed methodology and generated files remain CodingAgent-neutral.

Core IDD is installed by default. It installs the durable `.idd/intent/` intent layer
and the `idd-intent-*` and `idd-code-*` skills.

All public IDD commands use the `idd-` prefix and the format
`idd-<area>-<action>`. This keeps IDD commands grouped in autocomplete and
avoids collisions with project, plugin, or tool commands.

## Optional Factory Pack

The factory pack adds temporary execution orchestration workflows for
implementing current `.idd/intent/` intent.

Factory workflows may be selected automatically for temporary multi-task
orchestration, sequencing, task reviews, or final review. Use
`idd-code-implement` for one focused change. `idd-skip` is manual-only and
applies only when explicitly invoked for the current request; never select it
automatically.

Install:

```bash
intent-driven-development install --target claude --pack factory
```

The factory pack automatically includes core. It creates local support files
under `.idd/factory/`; temporary work artifacts are created under
`.idd/factory/work/` only when a factory work plan is created.
Factory remains optional. It never replaces intent workflows and must stop when
current intent is missing or insufficient.

`.idd/factory/work/` is ignored by git by default. Factory work plans are not
product specifications and must not be reused automatically for unrelated later
tasks. Durable product intent belongs in `.idd/intent/`.

Future task-system integration may use an external Work Item Provider or Task
Backend. The current implementation uses temporary local markdown files only.

Use the .NET global tool when you want a .NET-native installer command:

```powershell
dotnet tool install --global DimonSmart.IntentDrivenDevelopment.Tool
intent-driven-development list-targets
intent-driven-development list-coding-agents
intent-driven-development list-packs
intent-driven-development install --target codex
```

## Starting a Project

In a target repository, initialize IDD and install the CodingAgent format you use:

```powershell
intent-driven-development init
intent-driven-development install --target codex
```

For multiple CodingAgents, install each CodingAgent explicitly or use `--all`:

```powershell
intent-driven-development install --all
```

Project product intent lives in `.idd/intent/`. Keep durable product decisions there. Keep tasks, temporary plans, pull request notes, chat summaries, and implementation status outside specifications.

## Updating

If you use the .NET tool, update it first:

```powershell
dotnet tool update --global DimonSmart.IntentDrivenDevelopment.Tool
intent-driven-development install --target codex
```

## Local Repository Checks

When changing this repository itself, run the local check:

```powershell
.\scripts\Check.ps1
```

The check regenerates CodingAgent output, verifies that generated files are reproducible, and runs smoke tests.
