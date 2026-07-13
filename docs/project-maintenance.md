# Project Maintenance

This page is for contributors and release maintainers. User-facing installation and product guidance belong in the root README, [Getting Started](getting-started.md), and [Using IDD](using-idd.md).

## Distribution Model

IDD publishes native plugin marketplaces for Claude Code and Codex.

The public product is one plugin:

```text
idd
```

The plugin packages two methodological groups of workflows:

- durable intent workflows;
- temporary Factory orchestration workflows.

The groups remain separated in canonical source and behavior, but they are distributed as one product so users do not manage internal implementation modules.

## Repository Layout

```text
release tag               shared marketplace plugin version
src/canonical/            canonical methodology, project intent assets, skills, and plugin model
src/canonical/plugins/    canonical public plugin composition
src/canonical/skills/     platform-neutral skill bodies and metadata
src/canonical/factory/    platform-neutral Factory role prompts
src/adapters/claude/      Claude adapter configuration
src/adapters/codex/       Codex adapter configuration
tools/generate/           canonical model to native marketplace generator
tools/smoke-tests/        marketplace smoke tests
scripts/Check.ps1         local validation entry point
```

`artifacts/marketplace/` is local generated output and is ignored by Git. The main branch contains canonical source, not generated marketplace artifacts.

## Canonical Model

`src/canonical/plugins/plugin-manifest.json` defines the public `idd` plugin and owns:

```text
skills
roles
role references
bootstrap assets
metadata
```

Canonical skills must remain platform-neutral. Adapter-specific behavior belongs in adapter configuration or skill metadata under the relevant adapter key.

## Adapters

The generator uses an `IPlatformAdapter` boundary:

```text
Canonical Model -> IPlatformAdapter -> Native Plugin
```

Core generation logic must not contain Claude- or Codex-specific file layout decisions except through adapter implementations.

The generated marketplace layout is:

```text
.claude-plugin/marketplace.json
.agents/plugins/marketplace.json
plugins/claude/idd
plugins/codex/idd
```

Claude marketplace rename metadata maps the legacy `idd-core` and `idd-factory` plugin names to `idd`.

## Local Validation

Run:

```powershell
pwsh ./scripts/Check.ps1
```

The check:

1. Builds the generator.
2. Builds smoke tests.
3. Generates Claude marketplace output.
4. Generates Codex marketplace output.
5. Verifies generator check mode.
6. Runs smoke tests.
7. Confirms the generated output is stable across repeated generation.

When Claude CLI is available, also validate:

```bash
claude plugin validate artifacts/marketplace
claude plugin validate artifacts/marketplace/plugins/claude/idd
```

## Publication

The release tag is the only release version source. Tags use `vMAJOR.MINOR.PATCH`.

To publish the next patch release:

```powershell
pwsh ./scripts/Check.ps1
./publish-next-version.bat
```

Tag publication runs `.github/workflows/publish-marketplace.yml`.

The workflow:

```text
Checkout
Build Generator
Generate Claude Plugin
Generate Codex Plugin
Validate Claude
Validate Codex
Run Smoke Tests
Publish Marketplace Branch
Create GitHub Release
```

The `marketplace` branch contains only ready-to-consume marketplace output and the public README. It must not contain canonical source, generator source, or maintainer documentation.

## Rules

- Publish one user-facing plugin named `idd`.
- Keep intent and Factory responsibilities separate inside the canonical model.
- Do not add a supported platform without a new adapter and an explicit canonical model update.
- Do not put generated marketplace output in `main`.
- Do not copy skills into user projects.
- Keep product intent in `.idd/intent/`.
- Keep Factory data temporary under `.idd/factory/`.
- Keep release and validation instructions out of the root README.
