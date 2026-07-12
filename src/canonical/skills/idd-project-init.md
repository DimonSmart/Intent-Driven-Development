# idd-project-init

Use this skill as the only official project initialization workflow for
Intent-Driven Development.

## Purpose

Initialize durable product intent storage for a repository that already has IDD
plugins installed in the user's Coding Agent.

This skill does not copy plugins, copy skills, or create Coding Agent delivery
artifacts.

## Behavior

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

Write `.idd/plugins.json` as a declaration of required IDD plugins, not as a
copy of their implementation:

```json
{
  "plugins": [
    "idd-core"
  ]
}
```

Add `idd-factory` only when the user explicitly asks to enable Factory
workflows:

```json
{
  "plugins": [
    "idd-core",
    "idd-factory"
  ]
}
```

## Rules

- Do not create agent-specific skill directories in the user project.
- Do not copy plugin skills into the user project.
- Do not create external distribution artifacts or generated delivery files.
- Do not create `.idd/factory` unless Factory is explicitly enabled.
- Product intent lives only under `.idd/intent`.
- Factory working data, when enabled, is temporary and belongs under
  `.idd/factory`.

## Existing Projects

When `.idd/intent` already exists, preserve existing documents. Add only missing
bootstrap files and update `.idd/plugins.json` if plugin declarations are absent
or incomplete.
