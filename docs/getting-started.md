# Getting Started

Intent-Driven Development is installed through native Coding Agent plugins.

Supported platforms:

```text
Claude
Codex
```

## Install

Connect the IDD marketplace for your Coding Agent, then install:

```text
idd-core
```

Install this only when you want temporary multi-step execution orchestration:

```text
idd-factory
```

`idd-factory` depends on `idd-core`.

## Initialize A Project

In the target repository, invoke:

```text
idd-project-init
```

This creates the durable project-owned IDD state:

```text
.idd/
.idd/intent/
.idd/plugins.json
```

It also creates minimal bootstrap intent documents under `.idd/intent/`.

## What Is Not Created

IDD plugins do not copy skills into the project. Skills remain in the native plugin cache of the Coding Agent.

## Product Intent

Project product intent lives in:

```text
.idd/intent/
```

Keep durable product behavior, accepted architecture decisions, constraints, and verification rules there. Keep temporary plans, tasks, implementation status, PR notes, and chat summaries elsewhere.

## Factory

Factory workflows are temporary orchestration. When enabled, temporary Factory state belongs under:

```text
.idd/factory/
```

Factory never creates or owns Product Intent. If current intent is missing or insufficient, route to an intent workflow before implementation.

## Local Repository Checks

When changing this repository itself, run:

```powershell
.\scripts\Check.ps1
```

The check builds the generator, generates Claude and Codex marketplaces, validates plugin shape, and verifies reproducibility.
