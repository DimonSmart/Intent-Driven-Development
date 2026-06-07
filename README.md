# Intent-Driven Development

## The thought experiment

Delete the entire implementation.

Keep only the specifications.

Can an AI coding agent rebuild the product from them?

If the specs contain only what helps recreate the product, and nothing that is just temporary task noise, they are good specs.

## Why another take on spec-driven development?

Spec-Driven Development made the right move: start from a spec.

But in practice, many spec-driven workflows slowly turn specs into a pile of tasks, test notes, code fragments, bug-fix plans, TODOs, and temporary decisions. That does not make specifications stronger. It makes them harder to use as product intent.

In this project, a specification is the description of the product we want to have, even when the current implementation temporarily differs from that ideal state. Deviations can be tracked separately; they should not rewrite the meaning of the target product.

## What this project provides

This project provides the **Intent-Driven Development** methodology and a set of supporting skills for different AI coding agents.

The skills help agents create, review, reconcile, import, and index specifications without turning them into task logs.

## Why not just Spec-Driven Development?

Intent-Driven Development goes further. It keeps the specification pointed at the product, while separating durable product intent from temporary tasks, test work, bug fixes, TODOs, and implementation noise.

That difference matters in long-running AI-assisted development. Spec-driven workflows can drown in their own artifacts. Intent-Driven Development is designed to stay useful after many agent sessions, many fixes, and many changes of plan.

| Spec-Driven Development | Intent-Driven Development |
| --- | --- |
| Spec, plan and tasks often live too close together | Product intent and temporary work are separated |
| Generated tasks can start looking like truth | Generated artifacts are disposable |
| The workflow is often tied to one tool | Product intent stays independent from AI agents |
| Good for starting features | Better for keeping a project coherent over time |

## Core idea

A specification should answer:

> **What product are we building?**

Not:

> What did the agent do today?

Tasks change. Plans change. Agents change.

Product intent should stay stable.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Methodology](docs/methodology.md)
- [Project Internals, Distribution and Release Flow](docs/project-maintenance.md)
