# Intent-Driven Development

<p align="center">
  <img src="docs/assets/idd-hero.png" alt="Intent-Driven Development thought experiment: delete the implementation, keep only the intent, and rebuild the product" />
</p>

<p align="center">
  <strong>Durable product intent for disposable implementations.</strong>
</p>

Intent-Driven Development (IDD) is a lightweight, opinionated alternative to heavyweight spec-driven workflows for AI-assisted software development.

IDD keeps the current truth about the product separate from plans, tasks, statuses, reviews, and implementation details. Change the Coding Agent. Replace the architecture. Rebuild from scratch. The product intent remains.

## The Thought Experiment

> **Delete the implementation. Keep only the intent. Can a Coding Agent rebuild the product?**

IDD organizes product knowledge so the answer can move closer to **yes**.

The goal is not to make specifications executable or eliminate engineering judgment. The goal is to preserve enough durable product truth that the implementation can be replaced without losing what the product is supposed to be.

## Two Explicit Plugins

```text
idd-intent    durable product memory
idd-factory   temporary implementation organization
```

`idd-intent` is the standalone core of the methodology. It initializes `.idd/intent/`, maintains current product truth, implements from intent, and verifies implementation against intent.

`idd-factory` is optional. It adds temporary planning, role orchestration, task review, and final review under `.idd/factory/`. Factory consumes intent but must not create product truth. Install `idd-intent` first.

## Why IDD?

**Keep product truth, not project debris.**  
Specifications describe the current product—not the history of how it was built.

**Treat implementation as replaceable.**  
Code, libraries, architecture, and even the Coding Agent may change.

**Keep temporary work temporary.**  
Plans, tasks, statuses, reviews, and failed implementation attempts do not become permanent product documentation.

**Let Git own history.**  
IDD documents describe what is true now. Git records what used to be true.

## Install

### Claude Code

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
claude plugin install idd-intent@intent-driven-development
```

Optional Factory:

```bash
claude plugin install idd-factory@intent-driven-development
```

### Codex

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
codex plugin add idd-intent@intent-driven-development
```

Optional Factory:

```bash
codex plugin add idd-factory@intent-driven-development
```

## Start

Open the target repository and invoke:

```text
idd-project-init
```

Then ask IDD to import an existing product, describe a new one, change current product intent, implement from intent, or verify that the implementation still matches it.

Factory is deliberately absent from the default installation. Add it only when the implementation needs explicit multi-step planning and review orchestration.

## Learn More

- [Getting Started](docs/getting-started.md)
- [Using IDD](docs/using-idd.md)
- [Methodology](docs/methodology.md)
- [Project Maintenance](docs/project-maintenance.md)

## License

MIT
