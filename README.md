# Intent-Driven Development

> **If the codebase disappeared, an AI agent should be able to rebuild the product from the specifications.**
>
> **That only works when specifications contain product truth, not task noise.**

AI can generate code quickly.

Long-term software development fails in a different place: project memory turns into garbage. Specifications get mixed with tasks, generated plans, implementation notes, temporary fixes and old decisions. After a while, nobody knows what is still true and what was just part of yesterday's AI session.

**Intent-Driven Development** is a specification system for long-running AI-assisted development.

Its goal is simple:

> Keep specifications clean enough that any AI coding agent can recreate the product from scratch, using only what actually helps to build the product.

## Why not just Spec-Driven Development?

Spec-Driven Development made the right move: start from a spec.

Intent-Driven Development keeps that idea, but adds a stricter rule:

> **The spec is not a task tracker.**

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

Tasks change.  
Plans change.  
Agents change.  
Product intent should stay stable.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Methodology](docs/methodology.md)
- [Project Internals, Distribution and Release Flow](docs/project-maintenance.md)
