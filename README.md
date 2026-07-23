# Intent-Driven Development

<p align="center">
  <img src="docs/assets/idd-hero.png" alt="Intent-Driven Development thought experiment: delete the implementation, keep only the intent, and rebuild the product" />
</p>

<p align="center">
  <strong>Durable product intent for disposable implementations.</strong>
</p>

Intent-Driven Development (IDD) is a lightweight alternative to heavyweight Spec-Driven Development workflows for AI-assisted software development.

IDD keeps the current truth about the product and deliberately leaves temporary plans, task lists, statuses, reviews, and implementation attempts out of permanent product documentation. Coding Agents work from the relevant current intent, Git preserves history, and the repository stays easier to understand.

## The Thought Experiment

> **Delete the implementation. Keep only the intent. Can a Coding Agent rebuild the product?**

IDD organizes product knowledge so the answer can move closer to **yes**.

## Why IDD?

**Keep one current source of product truth.**  
Intent documents describe what the product must do now, not every intermediate plan that led there.

**Create fewer permanent artifacts.**  
Plans, task lists, review notes, and execution state remain temporary unless they contain durable product truth.

**Reduce context and token overhead.**  
IDD workflows read the relevant current intent instead of repeatedly loading a growing history of project-management artifacts.

**Let Git own history.**  
Specifications stay current. Git records previous versions, implementation changes, and the path taken to reach them.

Many Spec-Driven workflows preserve specifications, plans, tasks, checklists, reviews, and execution history as repository artifacts. IDD takes a narrower approach: preserve durable product intent, keep implementation work temporary, and use the smallest safe workflow for the current request.

## Try It in a Few Minutes

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

Then describe what you need in natural language. For example:

```text
Use idd-intent-brainstorm to help me clarify a feature that lets users compare two local folders without modifying either side.
```

Or, when current product intent is already clear:

```text
Use idd-code-implement for the folder comparison behavior.
```

That is enough to start. IDD creates only the minimal project-owned intent structure and uses installed skills for clarification, implementation, and verification.

## Choose Your Starting Point

### I already have a project

Import existing documentation, confirmed behavior, requirements, and architectural decisions into a clean current-intent structure.

[Start using IDD in an existing project](docs/existing-project.md)

### I am starting from an idea

Turn an informal product vision into current intent, establish the first product boundaries, and begin implementation without creating a large specification bureaucracy.

[Start a new project with IDD](docs/new-project.md)

### I want practical examples

See common workflows for product changes, implementation-only work, audits, normalization, and verification.

[Browse IDD use cases](docs/using-idd.md)

## Larger Implementation Work

For larger, higher-risk, or naturally multi-stage implementation tasks, install the optional Factory plugin.

Claude Code:

```bash
claude plugin install idd-factory@intent-driven-development
```

Codex:

```bash
codex plugin add idd-factory@intent-driven-development
```

Then give Factory the task once:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Factory carries the requested work through to completion. It may decompose the task when useful, verify intermediate results, review the integrated result, and prepare a concise commit-message handoff.

If execution is unexpectedly interrupted, the current Factory run can be continued without starting again:

```text
Continue the current IDD Factory work.
```

[Learn how Factory works](docs/factory-workflow.md)

## Two Small, Explicit Plugins

```text
idd-intent    durable product memory
idd-factory   optional temporary implementation orchestration
```

`idd-intent` is the standalone core. `idd-factory` is installed only when a task benefits from explicit multi-step execution and independent review.

## Learn More

- [Getting Started](docs/getting-started.md)
- [Existing Project Guide](docs/existing-project.md)
- [New Project Guide](docs/new-project.md)
- [Using IDD](docs/using-idd.md)
- [Factory Workflow](docs/factory-workflow.md)
- [Factory Skills Reference](docs/factory-skills.md)
- [Methodology](docs/methodology.md)

## License

MIT
