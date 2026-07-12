# Intent-Driven Development

<p align="center">
  <img src="docs/assets/idd-hero.png" alt="Intent-Driven Development hero image: durable product intent above temporary work artifacts" />
</p>

> Delete the implementation. Keep only the intent. Can a CodingAgent rebuild the product?

Intent-Driven Development is published as a native plugin marketplace for Claude Code and Codex. Durable product intent lives in `.idd/intent/`; plugin skills stay in the agent plugin cache and are not copied into projects.

## Plugins

`idd-core` provides the durable intent workflows and `idd-project-init`.

`idd-factory` provides temporary implementation orchestration. Install `idd-core` first; the dependency is recorded in canonical IDD metadata and in the documentation because the Claude plugin manifest does not add unsupported dependency fields.

## Install in Claude Code

```bash
git clone --branch marketplace --single-branch https://github.com/DimonSmart/Intent-Driven-Development.git idd-marketplace
claude plugin marketplace add ./idd-marketplace --scope user
claude plugin list --available --json
claude plugin install idd-core@intent-driven-development
claude plugin install idd-factory@intent-driven-development
```

## Install in Codex

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
codex plugin list --available --json
codex plugin add idd-core@intent-driven-development
codex plugin add idd-factory@intent-driven-development
```

## Initialize a Project

In the target repository, invoke the installed skill:

```text
idd-project-init
```

It creates only project-owned IDD files:

```text
.idd/intent/
.idd/plugins.json
```

`.idd/plugins.json` is a declaration for people and IDD workflows. It does not install plugins.

## Publish a Release

The release tag is the only release version source. `publish-next-version.bat`
increments the latest `vMAJOR.MINOR.PATCH` tag and publishes the next patch tag.

```bat
publish-next-version.bat
```

The publish workflow validates the tag, generates `artifacts/marketplace`, checks Claude and Codex structure, and publishes the `marketplace` branch with:

```text
.claude-plugin/marketplace.json
.agents/plugins/marketplace.json
plugins/claude/idd-core
plugins/claude/idd-factory
plugins/codex/idd-core
plugins/codex/idd-factory
README.md
```

## Verify Installation

```bash
claude plugin validate artifacts/marketplace
claude plugin validate artifacts/marketplace/plugins/claude/idd-core
claude plugin validate artifacts/marketplace/plugins/claude/idd-factory
codex plugin list --available --json
```

For repository changes, run:

```powershell
pwsh ./scripts/Check.ps1
```

## Documentation

- [Getting Started](https://github.com/DimonSmart/Intent-Driven-Development/blob/main/docs/getting-started.md)
- [Methodology](https://github.com/DimonSmart/Intent-Driven-Development/blob/main/docs/methodology.md)
- [Project Maintenance](https://github.com/DimonSmart/Intent-Driven-Development/blob/main/docs/project-maintenance.md)
