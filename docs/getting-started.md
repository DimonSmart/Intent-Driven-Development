# Getting Started

Intent-Driven Development can be installed into another repository through the universal npm wrapper or through the .NET tool.

Use npm/npx when you want to install generated agent files into any project without requiring the .NET SDK:

```bash
npx intent-driven-development list-targets
npx intent-driven-development init
npx intent-driven-development install --target claude
npx intent-driven-development install --target codex
npx intent-driven-development install --target copilot
npx intent-driven-development install --target gemini
npx intent-driven-development install --all
```

The default install mode is compact:

```bash
npx intent-driven-development install --target claude --entry minimal
```

Use `--entry none` to install only skills for targets that support them. Use `--entry full` only as a legacy or debug mode for environments that cannot load skills reliably.

Use the .NET global tool when you want a .NET-native installer command:

```powershell
dotnet tool install --global DimonSmart.IntentDrivenDevelopment.Tool
intent-driven-development list-targets
intent-driven-development install --target codex
```

## Starting a Project

In a target repository, initialize IDD and install the agent format you use:

```bash
npx intent-driven-development init
npx intent-driven-development install --target codex
```

For multiple agents, install each target explicitly or use `--all`:

```bash
npx intent-driven-development install --all
```

Project product intent lives in `.specs/`. Keep durable product decisions there. Keep tasks, temporary plans, pull request notes, chat summaries, and implementation status outside specifications.

## Updating

Install from the current released package again when you want to refresh generated agent files:

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

The check regenerates target output, verifies that generated files are reproducible, and runs smoke tests.
