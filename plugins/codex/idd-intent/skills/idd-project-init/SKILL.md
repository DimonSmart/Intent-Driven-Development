---
name: idd-project-init
description: Initialize `.idd/intent` and the IDD plugin declaration without copying skills or installing agent-specific files.
---

# idd-project-init

Use this skill as the only official project initialization workflow for Intent-Driven Development.

## Purpose

Initialize durable product intent storage for a repository that already has the `idd-intent` plugin installed in the user's Coding Agent.

The workflow also makes the repository's use of IDD visible to the active Coding Agent by creating or updating exactly one root instruction file:

```text
Codex        AGENTS.md
Claude Code  CLAUDE.md
```

The agent performing this workflow must edit that file directly. Do not implement this behavior through generator code, a CLI helper, an installation hook, or runtime application code.

This skill does not copy plugins or copy skills into the repository.

## Behavior

### 1. Resolve the Coding Agent instruction file

Use the repository-root instruction file for the active platform:

- Codex: `AGENTS.md`;
- Claude Code: `CLAUDE.md`.

Create or update only the file for the active Coding Agent. Do not create both files merely for symmetry.

Read the complete existing file before editing it.

### 2. Create project-owned IDD state

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

### 3. Maintain one minimal IDD instruction block

The root Coding Agent instruction file must contain exactly one managed IDD block with this content:

```markdown
<!-- idd:project:start -->
## Intent-Driven Development

This project uses Intent-Driven Development (IDD). Treat `.idd/intent/` as the current product truth and use the installed IDD skills when changing intent, implementing behavior, or verifying the implementation.
<!-- idd:project:end -->
```

Apply these rules:

- If the instruction file does not exist, create it with only the managed IDD block.
- If a managed IDD block already exists, replace it in place with the canonical block above.
- If more than one managed IDD block exists, keep one canonical block at the position of the first block and remove the duplicates.
- If no managed markers exist but the file already contains clearly IDD-specific instructions, consolidate those instructions into the canonical managed block instead of appending a second IDD section.
- Treat text as clearly IDD-specific when it explicitly names Intent-Driven Development, defines an IDD workflow, or directs the agent to `.idd/intent/`. Do not remove unrelated text merely because it contains the letters `IDD` as part of another term.
- Preserve all unrelated instructions, headings, comments, formatting, and ordering.
- When adding the block to an existing file, append it with a normal blank-line separation unless replacing an existing IDD section in place preserves the document better.
- Do not add detailed workflow documentation, skill catalogs, Factory instructions, implementation plans, or duplicated methodology text to the instruction file.
- Re-running `idd-project-init` must leave the instruction file semantically unchanged and must never create a second IDD section.

## Rules

- Copy bootstrap files from `assets/bootstrap/.idd/intent/` without semantic rewriting.
- Never replace an existing project file wholesale. Initialization authorizes only adding missing bootstrap files, normalizing `.idd/plugins.json`, and creating or updating the single managed IDD block in the active Coding Agent instruction file.
- Do not create agent-specific skill directories in the user project.
- Do not copy plugin skills into the user project.
- Do not create generated plugin delivery artifacts. The root `AGENTS.md` or `CLAUDE.md` instruction file is project-owned and is intentionally maintained by the agent.
- Do not implement instruction-file installation through program code.
- Do not say that `.idd/plugins.json` installs plugins. It is a project-level IDD declaration for people and IDD workflows.
- Do not create `.idd/factory` unless Factory work is explicitly requested.
- Product intent lives only under `.idd/intent`.
- Factory working data, when used, is temporary and belongs under `.idd/factory`.

## Existing Projects

When `.idd/intent` already exists, preserve existing documents. Add only missing bootstrap files.

Always inspect and normalize the active Coding Agent instruction file so that it contains exactly one minimal managed IDD block while preserving unrelated instructions.

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
