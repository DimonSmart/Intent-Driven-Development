# CodingAgent Workflow

Before changing behavior, contracts, architecture, or durable implementation
patterns, inspect `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and the numbered
current documents directly under `.idd/intent/`.

Use only current numbered documents in `.idd/intent/` as normative product intent.
There is no `.idd/intent` archive lifecycle. Do not inspect deleted Git history
unless the user explicitly asks for historical investigation.

When implementation and specification disagree, do not assume the
implementation is the new intent.

If the user explicitly confirms that verified implementation behavior represents
current product intent, update the relevant current specification. Keep
observable behavior and durable architecture, but exclude incidental
implementation details.

## Context Discipline

Do not load the whole specification set unless the task requires it.

Prefer focused specification reads:

- read `.idd/intent/README.md`;
- read `.idd/intent/INDEX.md`;
- read only relevant current numbered documents;
- avoid importing large unrelated context into the main conversation.

Large maintenance operations should produce compact summaries instead of
leaving the full exploration trace in the main conversation.

If the CodingAgent supports isolated, forked, or subagent execution, adapter
authors may use it for heavy specification-maintenance skills.

`idd-intent-normalize-current` may inspect multiple specifications, but it must still be
focused by a concrete topic, source, or target.

It should return a compact reorganization plan:

- found intent;
- proposed target structure;
- source specs to update;
- references to add;
- conflicts requiring a product intent decision.

It should not dump unrelated specification analysis into the main conversation.

Run verification commands that match the repository and the affected behavior.
If generated CodingAgent files exist, regenerate them instead of editing them
manually.
