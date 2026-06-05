# Intent-Driven Development

Intent-Driven Development is an AI-assisted development method where a living
specification guides implementation without replacing engineering judgment.

In AI development, the key skill is no longer just writing code, but describing
intent precisely enough that both humans and AI agents can act on it.

IDD keeps durable product intent in `.specs/` and generates agent-specific
instruction files from one canonical source. Codex, Claude, Gemini, and GitHub
Copilot are peer target formats, not wrappers around each other.

## Repository Layout

```text
src/canonical/    canonical methodology, project files, skills, and packs
src/adapters/     target-specific entry points and skill front matter
generated/        generated files for each AI coding agent system
tools/generate/   C# generator
tools/smoke-tests/ smoke tests for generated output
scripts/          local check and release helper scripts
```

Edit files under `src/canonical/` and `src/adapters/`, then run:

```powershell
.\scripts\Check.ps1
```

`generated/` is intentionally ignored by git. It is reproducible output from the
canonical source and adapters.

## Release

Pull requests and pushes to `main` run `.github/workflows/idd-smoke.yml`.

Release publication follows the tag-based flow:

```powershell
.\publish-next-version.ps1
```

The script runs the local check, creates the next `vMAJOR.MINOR.PATCH` tag, and
pushes it. `.github/workflows/publish-package.yml` packs
`DimonSmart.IntentDrivenDevelopment`, creates a GitHub Release, and publishes
the package to NuGet.

## Non-Goals

- Do not update `CopilotInstructions`.
- Do not create a pull request back to `CopilotInstructions`.
- Do not build Claude or Gemini on top of Codex `AGENTS.md`.
- Do not store agent-specific instruction copies as source of truth.
- Do not use legacy terminology in canonical methodology or skills.
- Do not turn specifications into a task tracker.

Migration notes from the older project model are in
`src/canonical/methodology/migration-from-copilotinstructions.md`.
