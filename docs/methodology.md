# Methodology

Intent-Driven Development keeps product memory separate from temporary work.

The specification is not executable magic. It does not replace architecture, code review, testing, or human responsibility. It is a stable description of what the product should become.

## Intent

Intent means stable product truth that future implementations must preserve.

A task says what to do next. Intent says what must remain true after the task is done.

Ask:

```text
If we delete the implementation, can we rebuild the product from these files?
```

If yes, the specification is useful. If no, it is probably a task list, local note, or chat summary.

## Mental Model

```text
product intent       durable product knowledge
plugin skills        reusable workflow knowledge
implementation       code, tests, scripts, and concrete changes
temporary work       plans, tasks, status, reviews, and chat
```

Product intent should survive tool changes, agent changes, and implementation attempts.

## What Goes Into Intent

Good specification content:

```text
product behavior
user scenarios
domain contracts
accepted architecture decisions
important constraints
non-goals
acceptance criteria
verification rules
```

Temporary content belongs elsewhere:

```text
tasks
implementation plans
status notes
review notes
chat summaries
local scratch files
agent delivery files
```

## Document Lifecycle

`.idd/intent/` stores current product intent, active ADRs, and active spikes.

When product intent evolves inside the same area, update the existing owning document. When an area is replaced, remove the obsolete document and create a new owner.

ADRs are decision records. If a durable decision changes, mark the old ADR as superseded and create the replacing ADR.

Resolved spikes should be removed after their outcome is captured in a spec or ADR, unless they remain active research.

## Plugin Delivery

IDD workflows are distributed as native plugins. Skills are knowledge artifacts installed in the Coding Agent's plugin cache.

User projects keep only their product memory and plugin declarations:

```text
.idd/intent/
.idd/plugins.json
```

Agent-specific plugin files are delivery artifacts, not product knowledge.

## Factory

Factory is temporary execution orchestration. It may coordinate planning, implementation, review, and finish workflows, but it must not create Product Intent.

When Factory discovers missing or insufficient intent, it must stop and route to an intent workflow.

## Summary

The specification is product memory. Plugins distribute workflow knowledge. Adapters translate canonical workflow knowledge into native agent formats. Temporary work stays disposable.
