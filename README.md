# Intent-Driven Development

> Run a simple thought experiment.
>
> Delete the entire implementation.
> Keep only the specifications.
>
> Can a CodingAgent rebuild the product from them?
>
> If the specs contain enough intent to recreate the product, and not enough temporary noise to become a task tracker, they are good specs.

## Why another take on spec-driven development?

Spec-Driven Development made the right move: start from a spec.

But in practice, many spec-driven workflows slowly turn specs into a pile of tasks, test notes, code fragments, bug-fix plans, TODOs, and temporary decisions. That does not make specifications stronger. It makes them harder to use as product intent.

In this project, a specification is the description of the product we want to have, even when the current implementation temporarily differs from that ideal state. Deviations can be tracked separately; they should not rewrite the meaning of the target product.

## What this project provides

This project provides the **Intent-Driven Development** methodology and a set of supporting skills for different CodingAgents such as Codex, Claude, Gemini, and GitHub Copilot.

The core skills help CodingAgents create, review, reconcile, import, implement, and
index specifications without turning them into task logs.

Core IDD is the default install. It owns durable product intent in `.idd/intent/`.

An optional `factory` pack adds temporary execution orchestration for planned
implementation work:

```bash
intent-driven-development install --target codex --pack factory
```

`--target` is the CLI compatibility name for selecting a CodingAgent.

Factory work artifacts live under `.idd/factory/work/`, are ignored by git by
default, and should be deleted after the task unless explicitly kept or
committed. Factory work plans are not specifications; durable product intent
belongs in `.idd/intent/`. Future task-system integrations may use external work
item providers, but no Jira, GitHub Issues, or persistent task database is
implemented now.

## Why not just Spec-Driven Development?

Intent-Driven Development goes further. It keeps the specification pointed at the product, while separating durable product intent from temporary tasks, test work, bug fixes, TODOs, and implementation noise.

That difference matters in long-running AI-assisted development. Spec-driven workflows can drown in their own artifacts. Intent-Driven Development is designed to stay useful after many CodingAgent sessions, many fixes, and many changes of plan.

| Spec-Driven Development | Intent-Driven Development |
| --- | --- |
| Spec, plan and tasks often live too close together | Product intent and temporary work are separated |
| Generated tasks can start looking like truth | Generated artifacts are disposable |
| The workflow is often tied to one tool | Product intent stays independent from CodingAgents |
| Good for starting features | Better for keeping a project coherent over time |

## Core idea

A specification should answer:

> **What product are we building?**

Not:

> What did the CodingAgent do today?

Tasks change. Plans change. CodingAgents change.

Product intent should stay stable.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Methodology](docs/methodology.md)
- [Project Internals, Distribution and Release Flow](docs/project-maintenance.md)
