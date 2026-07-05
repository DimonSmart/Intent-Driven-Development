# Intent-Driven Development

<p align="center">
  <img src="docs/assets/idd-hero.png" alt="Intent-Driven Development hero image: durable product intent above temporary work artifacts" />
</p>

> Run a thought experiment.
>
> Delete the implementation.  
> Keep only the intent.  
> Can a CodingAgent rebuild the product?

Intent-Driven Development is a methodology for keeping durable product intent in the repository while keeping temporary work artifacts disposable.

Product intent is product memory. Tasks, TODOs, generated plans, PR notes, chat summaries, and temporary implementation notes are not product truth.

## The Problem

AI coding tools generate code, plans, checklists, summaries, and instruction files quickly. Over time, those artifacts can start pretending to be the source of truth.

IDD exists to prevent that. The implementation can change. The tools can change. The CodingAgent can change. The durable product intent should remain stable.

## What IDD Is

IDD treats specifications and ADRs as durable product memory. They describe what the product is supposed to become and what future implementations must preserve.

Temporary work belongs elsewhere: tasks, pull requests, chats, generated plans, and local implementation notes can guide the current change, but they do not define the product.

Core IDD is the default install. It creates the durable intent layer in `.idd/intent/`. Optional execution orchestration, such as the factory pack, is for temporary work and must not become the canonical specification. Factory workflows are manual-only commands; installing factory does not authorize automatic factory routing.

All public IDD commands use the `idd-` prefix and the format `idd-<area>-<action>`.
This keeps IDD commands grouped in autocomplete and avoids collisions with project, plugin, or tool commands.

## Why It Is Different

| Ordinary spec-driven workflow | Intent-Driven Development |
| --- | --- |
| Specs often mix with plans, tasks, and status notes | Product intent stays separate from temporary work |
| Generated artifacts can look authoritative | Generated artifacts stay disposable |
| Tool-specific files can drift into product memory | CodingAgent files are delivery/adaptation formats |
| A workflow may depend on one CodingAgent | The source of truth remains tool-independent |

IDD is not just another spec-driven workflow. It is stricter about separating product memory from task history.

## Who It Is For

IDD is useful for long-lived projects, multiple CodingAgents, repeated implementation sessions, architectural rules that should survive resets, and product knowledge that should not live only in chat history.

It is less useful for throwaway experiments where the code and the decisions will both be discarded.

## Start Here

- [Getting Started](docs/getting-started.md) - install IDD, choose a CodingAgent target, and initialize a project.
- [Methodology](docs/methodology.md) - understand durable product intent, temporary work artifacts, and how IDD differs from spec-driven workflows.
- [Project Maintenance](docs/project-maintenance.md) - maintain this repository, generated CodingAgent files, packs, checks, and releases.

The engineer still owns the result. IDD keeps the product memory clear enough for humans and CodingAgents to use it.
