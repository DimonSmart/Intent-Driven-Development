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

## One Product Plugin, Two Kinds of Workflow

IDD is distributed as one native plugin named `idd`.

The plugin contains:

- intent workflows that create and maintain durable product memory;
- Factory workflows that coordinate temporary planning, implementation, and review.

This is an internal methodological boundary, not two products the user must install separately.

Factory may consume intent, but it must not create product truth. When Factory discovers missing, contradictory, or insufficient intent, it must stop and route the work to an intent workflow.

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

The specification is product memory. The implementation is replaceable. Plans and statuses are temporary. Git owns history. Coding Agents may change, but durable product intent remains the stable source of truth.
