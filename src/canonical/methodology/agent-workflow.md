# Agent Workflow

Before changing behavior, contracts, architecture, or durable implementation
patterns, inspect `.specs/README.md`, `.specs/INDEX.md`, and the numbered
current documents directly under `.specs/`.

Use only current numbered documents in `.specs/` as normative product intent.
Archived documents are historical context.

When implementation and specification disagree, do not assume the
implementation is the new intent.

If the user explicitly confirms that verified implementation behavior represents
current product intent, update the relevant current specification. Keep
observable behavior and durable architecture, but exclude incidental
implementation details.

Run verification commands that match the repository and the affected behavior.
If generated agent files exist, regenerate them instead of editing them
manually.
