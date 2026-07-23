# Intent-Driven Development Methodology

Intent-Driven Development keeps product memory separate from temporary work.

It is a lightweight, opinionated response to a common failure mode of spec-driven workflows: specifications gradually accumulate plans, statuses, implementation notes, obsolete alternatives, and historical debris until the current product truth is difficult to find.

IDD draws a stricter boundary.

## The Thought Experiment

```text
Delete the implementation.
Keep only the intent.
Can a Coding Agent rebuild the product?
```

This is a test of the repository's product memory, not a claim that specifications can replace engineering.

A useful body of intent should make reconstruction possible in principle while still leaving implementation choices, architecture work, testing, review, and human responsibility to the engineering process.

## Intent Is Current Product Truth

Intent is stable product knowledge that future implementations must preserve.

A task says what to do next. Intent says what must remain true after the task is finished.

Good intent answers questions such as:

- What behavior does the user rely on?
- What domain rules must hold?
- Which constraints and non-goals are deliberate?
- Which architecture decisions are durable?
- How can the behavior be accepted or verified?

Intent does not need to prescribe every class, command, library, or implementation step.

## Durable and Temporary Knowledge

```text
product intent       durable product knowledge
plugin workflows     reusable methodology knowledge
implementation       replaceable code, tests, and architecture
temporary work       plans, tasks, status, reviews, and chat
```

Product intent should survive tool changes, agent changes, refactoring, failed implementation attempts, and complete rewrites.

Temporary work exists to complete one change. It should be removable when that work is finished.

## What Belongs in Intent

Keep:

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

Keep elsewhere or discard:

```text
tasks
implementation plans
status notes
review notes
chat summaries
local scratch files
agent delivery files
commands tied only to the current toolchain
```

## Current Truth, Not Historical Archive

IDD documents describe what is true now.

When behavior evolves inside an existing product area, update the current owning document. When an area is replaced, remove the obsolete specification and create the new owner when necessary.

Git owns history. The intent tree should not reproduce Git through status fields, changelogs inside specifications, or retained obsolete documents.

ADRs are the exception because they record durable decisions. When a decision changes, mark the old ADR as superseded and create the replacing ADR.

Resolved spikes should be removed after their durable outcome is captured in a specification or ADR, unless the research itself remains active.

## Two Plugins, One Boundary

IDD is distributed as two explicit native plugins:

```text
idd-intent    durable product memory
idd-factory   temporary implementation organization
```

`idd-intent` owns the durable side of the methodology. It initializes and maintains `.idd/intent/`, imports or changes current product truth, implements from intent, and checks implementation against intent.

`idd-factory` owns temporary execution orchestration. Its `idd-factory-run`
entry point decomposes a request, coordinates sequential tasks, performs
independent task and final reviews, supports session-independent resume, and
prepares a commit-message handoff under `.idd/factory/`.

The separation is visible to the user because the responsibilities have different lifecycles:

- `idd-intent` is the normal standalone installation;
- `idd-factory` is optional and depends on `idd-intent`;
- Factory may read intent but must not create or silently modify product truth;
- when Factory discovers missing, contradictory, or insufficient intent, it must stop and route the work to an `idd-intent` workflow;
- `.idd/factory/current/` holds at most one active run and
  `.idd/factory/results/` holds compact commit-message handoffs;
- both directories are ignored by default and are never durable product intent.

The current workspace contains `request.md` and a flat, gap-free sequence of
task files. A task filename is `<sequence>-<slug>.<status>.md`; its suffix is the
only status source. The supported states are `ready`, `active`, `completed`,
and `blocked`, with at most one active or blocked task. This filesystem state,
not conversation history, lets a later session validate and resume safely.

Each completed task passes an independent review. Final review findings create
a new corrective task instead of reopening completed history. Only after final
approval does Factory create
`.idd/factory/results/<work-slug>/commit-message.md`; it then clears the
contents of `current/` and leaves prior results intact.

## Routing Model

IDD routes natural-language requests across two dimensions:

```text
what changes x execution depth
```

What changes determines the workflow family: product truth, implementation
only, intent structure, implementation versus intent, raw imported knowledge,
project initialization, or unknown. Product truth changes are further classified
as `add`, `modify`, or `remove`.

Execution depth is independent: a change can be focused or orchestrated
regardless of the product operation. Focused work uses the smallest direct
workflow. Orchestrated work may use optional Factory when implementation needs
sequencing, temporary planning, review gates, migration, compatibility
transition, or multiple independent tasks.

## How IDD Differs from Broad Spec-Driven Workflows

IDD is still specification-driven in the ordinary sense: implementation follows an explicit product description.

Its distinction is narrower and stricter:

- specifications describe the current product, not the current project;
- implementation work is disposable by default;
- Git owns specification history;
- one current document should own one durable product area;
- the methodology is tested by the possibility of rebuilding from intent.

IDD does not attempt to preserve every step that led to the product. It preserves what the product must continue to be.

## Summary

`idd-intent` preserves product memory. `idd-factory` organizes resumable temporary implementation work. Requests, task statuses, reviews, and commit-message handoffs remain temporary, and Git owns history.
