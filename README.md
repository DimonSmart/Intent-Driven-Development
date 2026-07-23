# Intent-Driven Development

<p align="center">
  <img src="docs/assets/idd-hero.png" alt="Intent-Driven Development thought experiment: delete the implementation, keep only the intent, and rebuild the product" />
</p>

<p align="center">
  <strong>Durable product intent for disposable implementations.</strong>
</p>

Intent-Driven Development (IDD) is a lightweight alternative to heavyweight Spec-Driven Development workflows for AI-assisted software development.

IDD preserves the current truth about the product. Temporary plans, task lists, statuses, reviews, and implementation attempts stay temporary. Coding Agents work from the relevant intent, while Git preserves history.

## The Thought Experiment

> **Delete the implementation. Keep only the intent. Can a Coding Agent rebuild the product?**

IDD organizes product knowledge so the answer can move closer to **yes**.

## Why IDD?

- **One current source of product truth.** Intent documents describe what the product must do.
- **Fewer permanent artifacts.** Plans, task states, and reviews do not accumulate in the repository.
- **Lower context overhead.** Agents read relevant current intent instead of a growing history of workflow documents.
- **Git owns history.** Specifications stay current; Git records how they and the implementation changed.

## Quick Start

### Claude Code

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
claude plugin install idd-intent@intent-driven-development
```

### Codex

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
codex plugin add idd-intent@intent-driven-development
```

Open the target repository and initialize IDD:

```text
idd-project-init
```

Then describe what you need naturally. For example:

```text
Use idd-intent-brainstorm to help me clarify a feature that lets users compare two local folders without modifying either side.
```

[Installation, initialization, and verification](docs/getting-started.md)

## Choose the Right Workflow

The README stays intentionally small. Continue with the guide that matches the current situation:

- [Existing project](docs/existing-project.md) — import confirmed product knowledge into current intent.
- [New project or idea](docs/new-project.md) — clarify the product and implement the first useful slice.
- [Common use cases](docs/using-idd.md) — find what to do for changes, implementation, audits, verification, updates, and other routine work.
- [Large implementation task](docs/factory-workflow.md) — use optional Factory orchestration and independent review.

## Two Explicit Plugins

```text
idd-intent    durable product memory
idd-factory   optional temporary implementation orchestration
```

`idd-intent` is the standalone core. Install `idd-factory` only when a task benefits from explicit multi-step execution and independent review.

## Documentation

- [Getting Started](docs/getting-started.md)
- [IDD Use Cases](docs/using-idd.md)
- [Methodology](docs/methodology.md)
- [Factory Workflow](docs/factory-workflow.md)
- [Factory Skills Reference](docs/factory-skills.md)

## License

MIT
