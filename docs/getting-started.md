# Getting Started

Intent-Driven Development is installed through native Claude Code or Codex plugins.

## Install in Claude Code

Add the marketplace branch as a Git marketplace root:

```bash
git clone --branch marketplace --single-branch https://github.com/DimonSmart/Intent-Driven-Development.git idd-marketplace
claude plugin marketplace add ./idd-marketplace --scope user
```

Verify that the marketplace exposes `idd-core` and `idd-factory`:

```bash
claude plugin list --available --json
```

Install the plugins:

```bash
claude plugin install idd-core@intent-driven-development
claude plugin install idd-factory@intent-driven-development
```

Install `idd-factory` only when you want temporary multi-step implementation orchestration.

## Install in Codex

Add the marketplace branch:

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
```

Verify available plugins:

```bash
codex plugin list --available --json
```

Install the plugins:

```bash
codex plugin add idd-core@intent-driven-development
codex plugin add idd-factory@intent-driven-development
```

Install `idd-core` before `idd-factory`.

## Initialize a Project

In the target repository, invoke:

```text
idd-project-init
```

The skill reads packaged bootstrap files from its own `assets/bootstrap/.idd/intent/` resources and creates missing project-owned files:

```text
.idd/intent/
.idd/plugins.json
```

It never replaces existing files without explicit approval, never copies skills into the project, and never creates `.claude/skills` or `.agents/skills`.

`.idd/plugins.json` is a project-level IDD declaration for people and IDD workflows. It does not install plugins.

## Verify Installation

Claude:

```bash
claude plugin list --json
claude plugin validate artifacts/marketplace
claude plugin validate artifacts/marketplace/plugins/claude/idd-core
claude plugin validate artifacts/marketplace/plugins/claude/idd-factory
```

Codex:

```bash
codex plugin marketplace list
codex plugin list --available --json
codex plugin list --json
```

## Update Plugins

Claude:

```bash
claude plugin marketplace update intent-driven-development
claude plugin update idd-core
claude plugin update idd-factory
```

Codex:

```bash
codex plugin marketplace upgrade
codex plugin remove idd-core
codex plugin remove idd-factory
codex plugin add idd-core@intent-driven-development
codex plugin add idd-factory@intent-driven-development
```

## Remove Marketplace

Claude:

```bash
claude plugin uninstall idd-factory
claude plugin uninstall idd-core
claude plugin marketplace remove intent-driven-development
```

Codex:

```bash
codex plugin remove idd-factory
codex plugin remove idd-core
codex plugin marketplace remove intent-driven-development
```

## Publish a Release

`VERSION` is the only release version source. The pushed release tag must match `v` + `VERSION` exactly.

```powershell
pwsh ./scripts/Check.ps1
$version = (Get-Content -Raw VERSION).Trim()
git tag "v$version"
git push origin "v$version"
```

The workflow runs:

```powershell
pwsh ./scripts/Check.ps1 -Version <tag-version>
```

It publishes the `marketplace` branch only after required native entry paths exist and validators pass or the documented manual Claude validation gate is used.

## Troubleshooting

If Claude cannot find the marketplace, run:

```bash
claude plugin marketplace list
claude plugin marketplace update intent-driven-development
```

If Codex cannot find plugins, run:

```bash
codex plugin marketplace list
codex plugin marketplace upgrade
codex plugin list --available --json
```

If validation fails locally, regenerate and check:

```powershell
pwsh ./scripts/Check.ps1
```
