# Getting Started

Intent-Driven Development is distributed as two native plugins:

```text
idd-intent    durable product memory
idd-factory   optional temporary implementation orchestration
```

Install `idd-intent` first. Add `idd-factory` only when a task benefits from multi-step execution and independent review.

## Install for Claude Code

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
claude plugin install idd-intent@intent-driven-development
```

Optional Factory:

```bash
claude plugin install idd-factory@intent-driven-development
```

## Install for Codex

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
codex plugin add idd-intent@intent-driven-development
```

Optional Factory:

```bash
codex plugin add idd-factory@intent-driven-development
```

## Initialize the Repository

Open the target repository and run:

```text
idd-project-init
```

The Coding Agent creates the minimal project-owned IDD structure, records that the repository uses `idd-intent`, and adds one small managed IDD section to the active agent instruction file.

No project-local copy of the plugin or its skills is created.

## Choose the Next Step

For an existing product with documentation, requirements, ADRs, tests, or confirmed behavior:

[Start using IDD in an existing project](existing-project.md)

For a new product or an idea that is not yet precise:

[Start a new project with IDD](new-project.md)

For common intent, implementation, audit, and verification workflows:

[Browse IDD use cases](using-idd.md)

For a large implementation task:

[Use IDD Factory](factory-workflow.md)

## Verify Installation

Claude Code:

```bash
claude plugin list --json
```

Codex:

```bash
codex plugin marketplace list
codex plugin list --json
```

The normal installation contains `idd-intent`. `idd-factory` appears only when installed explicitly.
