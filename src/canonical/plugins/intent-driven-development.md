This project uses Intent-Driven Development.

Current product intent lives in `.idd/intent/`.

Use IDD only when working with durable product intent.

Do not load the whole `.idd/intent/` directory by default. Read
`.idd/intent/README.md`, `.idd/intent/INDEX.md`, then only relevant numbered specs.

If `.idd/intent/GLOSSARY.md` exists and project-specific terminology may affect
interpretation of the current request, read the relevant entries before the
numbered specs. The glossary is optional. Do not create or update it unless the
user explicitly invokes or accepts `idd-glossary-build`.

`idd-project-init`, `idd-intent-bootstrap`, and `idd-intent-import` must read
`references/glossary-integration.md` when that packaged reference is available.

{{skillGuidance}}

Do not put local tasks, temporary implementation notes, generated plans, or chat
history into `.idd/intent/`.

## Optional Glossary

`GLOSSARY.md`, when present, is a project vocabulary support file. It is not a
numbered intent document and does not own product behavior. Other skills may use
it to interpret current intent but must not maintain it automatically.

## Document Lifecycle

Git stores history.

`.idd/intent/` stores current product intent, ADRs, active spikes, and the
optional glossary support file.

There is no `.idd/intent` archive lifecycle.

Do not move obsolete specs to an archive. Delete obsolete, duplicated,
task-like, process-only, or incorrect documents from the working tree.

When product intent evolves inside the same product area, update the existing
spec directly.

When a product area is replaced by a substantially different product area,
delete the old spec and create a new owning spec.

ADRs are decision records. Do not archive superseded ADRs. Mark them as
`Superseded` and create a new ADR for the replacing decision.

Resolved spikes should be deleted after their outcome is captured in a spec or
ADR, unless they remain useful as active research.

{{workflowGuidance}}
