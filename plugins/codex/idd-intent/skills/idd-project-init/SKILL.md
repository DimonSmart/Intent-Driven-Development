---
name: idd-project-init
description: Initialize `.idd/intent` and the IDD plugin declaration without copying skills or installing agent-specific files.
---

# idd-project-init

Use this skill as the only official project initialization workflow for Intent-Driven Development.

## Purpose

Initialize durable product intent storage for a repository that already has the `idd-intent` plugin installed in the user's Coding Agent.

This skill does not copy plugins, copy skills, or create Coding Agent delivery artifacts.

## Behavior

Read bootstrap assets from this skill package:

```text
assets/bootstrap/.idd/intent/
```

When the runtime exposes a skill directory or resource URI, resolve the path relative to this `SKILL.md`. If the runtime only exposes packaged resources by reference, use the equivalent resource reference for `assets/bootstrap/.idd/intent/`.

Create only the project-owned IDD state:

```text
.idd/
.idd/intent/
.idd/plugins.json
```

Create minimal bootstrap intent documents when they are missing:

```text
.idd/intent/README.md
.idd/intent/INDEX.md
.idd/intent/_templates/spec.md
.idd/intent/_templates/adr.md
.idd/intent/_templates/spike.md
```

Write `.idd/plugins.json` as a declaration of the required product-memory plugin, not as a copy of its implementation:

```json
{
  "plugins": [
    "idd-intent"
  ]
}
```

`idd-factory` is a separate optional plugin. Do not add it to `.idd/plugins.json` and do not create `.idd/factory` unless the user explicitly enables Factory workflows.

## Rules

- Copy bootstrap files from `assets/bootstrap/.idd/intent/` without semantic rewriting.
- Never replace an existing file without explicit user approval.
- Do not create agent-specific skill directories in the user project.
- Do not copy plugin skills into the user project.
- Do not create external distribution artifacts or generated delivery files.
- Do not say that `.idd/plugins.json` installs plugins. It is a project-level IDD declaration for people and IDD workflows.
- Do not create `.idd/factory` unless Factory work is explicitly requested.
- Product intent lives only under `.idd/intent`.
- Factory working data, when used, is temporary and belongs under `.idd/factory`.

## Existing Projects

When `.idd/intent` already exists, preserve existing documents. Add only missing bootstrap files.

Normalize legacy declarations as follows:

- replace `idd` with `idd-intent`;
- replace `idd-core` with `idd-intent`;
- preserve `idd-factory` only when it is already declared or the user explicitly enables it;
- remove duplicate plugin names.

A project using only durable product memory should contain:

```json
{
  "plugins": [
    "idd-intent"
  ]
}
```

A project that explicitly uses Factory may contain:

```json
{
  "plugins": [
    "idd-intent",
    "idd-factory"
  ]
}
```

Do not otherwise rewrite existing intent documents during initialization.
