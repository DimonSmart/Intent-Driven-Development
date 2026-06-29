# Getting Started

Intent-Driven Development can be installed into another repository through the universal npm wrapper or through the .NET tool.

This repository ships canonical methodology and generated CodingAgent-specific delivery files. The installed CodingAgent files are generated from the canonical source, not edited as the source of truth.

Use npm/npx when you want to install generated CodingAgent files into any project without requiring the .NET SDK:

```bash
npx intent-driven-development list-targets
npx intent-driven-development list-coding-agents
npx intent-driven-development list-packs
npx intent-driven-development init
npx intent-driven-development install --target claude
npx intent-driven-development install --coding-agent codex
npx intent-driven-development install --target codex
npx intent-driven-development install --target copilot
npx intent-driven-development install --target gemini
npx intent-driven-development install --all
```

The default install mode is compact:

```bash
npx intent-driven-development install --target claude --entry minimal
```

Use `--entry none` to install only skills for CodingAgents that support them. Use `--entry full` only as a legacy or debug mode for environments that cannot load skills reliably.

`--target` is the CLI compatibility name for selecting a CodingAgent.

Core IDD is installed by default. It installs the durable `.idd/intent/` intent layer
and the `idd-intent-*` and `idd-code-*` skills.

## Optional Factory Pack

The factory pack adds temporary execution orchestration workflows for
implementing current `.idd/intent/` intent.

Install:

```bash
intent-driven-development install --target codex --pack factory
```

The factory pack automatically includes core. It creates local support files
under `.idd/factory/`; temporary work artifacts are created under
`.idd/factory/work/` only when a factory work plan is created.

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

```bash
npx intent-driven-development init
npx intent-driven-development install --target codex
```

For multiple CodingAgents, install each CodingAgent explicitly or use `--all`:

```bash
npx intent-driven-development install --all
```

Project product intent lives in `.idd/intent/`. Keep durable product decisions there. Keep tasks, temporary plans, pull request notes, chat summaries, and implementation status outside specifications.

## Updating

Install from the current released package again when you want to refresh generated CodingAgent files:

```bash
npx intent-driven-development install --all
```

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
