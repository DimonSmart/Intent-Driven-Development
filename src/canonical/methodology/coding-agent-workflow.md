# CodingAgent Workflow

Before changing behavior, contracts, architecture, or durable implementation
patterns, inspect `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and the current
`IDD-NNNN` documents directly under `.idd/intent/`.

Use only current `IDD-NNNN` documents in `.idd/intent/` as normative product
intent. There is no `.idd/intent` archive lifecycle. Do not inspect deleted Git
history unless the user explicitly asks for historical investigation.

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
- read only relevant current `IDD-NNNN` documents;
- avoid importing large unrelated context into the main conversation.

Large maintenance operations should produce compact summaries instead of
leaving the full exploration trace in the main conversation.

If the CodingAgent supports isolated, forked, or subagent execution, adapter
authors may use it for heavy specification-maintenance skills.

`idd-intent-normalize-current` may inspect multiple specifications, but it must
still be focused by a concrete topic, source, or target.

It should return a compact reorganization plan:

- found intent;
- proposed target structure;
- source specs to update;
- references to add;
- conflicts requiring a product intent decision.

It should not dump unrelated specification analysis into the main conversation.

Run verification commands that match the repository and affected behavior.
If generated CodingAgent files exist, regenerate them instead of editing them
manually.

Use IDD skills when a request involves durable product intent, implementation
based on current intent, conformance checking, or Factory orchestration. Use
`idd-code-implement` for one focused implementation change. Use the optional
`idd-factory-run` entry point when temporary multi-task sequencing, explicit
Review checkpoints, or coordinated execution is required; Factory may be
selected automatically and never becomes product intent.

Factory keeps at most one resumable run in `.idd/factory/current/`. Its work
items are a numbered sequence of stable Subtask and optional Review checkpoint
contracts; runtime-owned `state.json` is the only status source. Successful execution
tasks complete without automatic independent review. `idd-factory-review-checkpoint`
runs only for an explicit checkpoint covering one contiguous group of completed
Subtasks. After all work items complete, Factory performs a mandatory
final integrated review, prepares final result artifacts, and moves the complete
run directory to `.idd/factory/results/<timestamp>_<work-slug>/`. The moved
result retains the state, event log, attempts, verification evidence,
commit-message handoff, and other diagnostics, while `.idd/factory/current/`
remains reserved for an active or resumable run.

When Factory decomposition discovers missing durable intent, it creates no task
state. Resolve intent first, then decompose the original request again. Intent
changes never become Factory Subtasks.

When a user asks to continue current Factory work, the packaged runtime validates
and reconciles saved state instead of reconstructing work from conversation
history. A new request must not replace a nonempty current run.

When a request concerns IDD but does not explicitly name a skill, classify it
through the `idd-route` routing model before selecting an intent,
implementation, audit, normalization, import, check, or Factory workflow.

An explicitly named skill bypasses routing. `idd-route` is read-only and selects
the smallest safe workflow. It classifies what changes separately from execution
depth, so Factory selection is independent of whether the product operation is
add, modify, or remove.

When the routed request asks for an actual change, continue from routing into the
recommended next skill in the same user request when possible. Do not ask for a
second message only to confirm the selected route.

Use `idd-skip` only when the user explicitly invokes it for the current request.
Never select `idd-skip` automatically; it is not a project or future-request
setting.
