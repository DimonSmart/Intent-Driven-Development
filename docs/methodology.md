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

Current means currently accepted as normative, not necessarily already implemented. Implementation may temporarily lag behind accepted intent.

## Decision-Relevant Future Intent

Do not design for an imagined future, but do not ignore known future intent that changes a decision being made now.

Ask:

> Would knowing this future intent materially change the current decision?

If no, do not persist it in current intent. If yes, record the minimum required capability, invariant, or prohibited lock-in—not a speculative future implementation.

## Durable and Temporary Knowledge

```text
product intent       durable product knowledge
project glossary     optional shared vocabulary
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

## Optional Project Glossary

A project may optionally keep `.idd/intent/GLOSSARY.md` for a small amount of shared terminology.

> The glossary contains not all project terms, but only terms whose incorrect interpretation could change the understanding of product intent.

This means that ordinary technical or domain terms used in their ordinary meaning do not belong in the glossary. A term is useful there when the project gives it a special meaning, multiple names denote the same concept, two similar concepts must be distinguished, or translations and legacy names create a real ambiguity risk.

The glossary is absent by default. Its absence is valid and does not make IDD initialization incomplete. It is created or changed only through the manual-only `idd-glossary-build` workflow. Bootstrap and import may detect material candidates, but they ask for explicit consent before handing them to that skill.

Each glossary entry has a canonical term, a short definition, and optionally `Aliases`. Aliases may include synonyms, legacy names, abbreviations, spelling variants, transliterations, and names in other languages. They must all denote the same concept.

The glossary defines vocabulary, not requirements:

```text
What does Aspect mean?          -> GLOSSARY.md
How must the system use Aspect? -> specification
Why was this model chosen?      -> ADR when the decision is durable
```

`GLOSSARY.md` has no `IDD-NNNN` identifier and is not listed in `INDEX.md`.

## Current Truth, Not Historical Archive

IDD documents describe what is true now.

When behavior evolves inside an existing product area, update the current owning document. When an area is replaced, remove the obsolete specification and create the new owner when necessary.

Git owns history. The intent tree should not reproduce Git through status fields, changelogs inside specifications, or retained obsolete documents.

ADRs are the exception because they record durable decisions. When a decision changes, mark the old ADR as superseded and create the replacing ADR.

Resolved spikes should be removed after their durable outcome is captured in a specification or ADR, unless the research itself remains active.

An existing glossary is edited in place through explicit glossary work. If its final approved entry is removed, the file should be deleted rather than retained empty.

## Two Plugins, One Boundary

IDD is distributed as two explicit native plugins:

```text
idd-intent    durable product memory
idd-factory   temporary implementation organization
```

`idd-intent` owns durable product truth and remains fully usable without
Factory.

`idd-factory` owns only lightweight temporary semantic orchestration:

```text
Intent Preflight
-> fresh planner
-> current batch
-> fresh sequential workers
-> fresh planner
-> Question | Done
```

Factory does not package or run a .NET workflow runtime. It has no Factory MCP
transport, process supervisor, retry state machine, authoritative attempt
history, or exact continuation protocol.

The repository is authoritative implementation reality. Temporary continuation
state contains only the original request, remaining current batch, short
completed summaries, exact answers, and optional question/verification failure
artifacts.

Every planner and worker receives a fresh semantic context. Workers share the
repository but do not inherit the root or sibling transcripts. Platforms are
supported only when native agent APIs provide fresh/no-parent-history context,
shared repository access, terminal waiting without model polling, final result
retrieval, and stop/close control.

The planner owns semantic relevance and future work. `TaskRelatedIntent`
contains stable durable-intent IDs; the worker resolves and reads those current
documents itself. There is no `RelevantCompletedWork` reference protocol.
Future tasks are self-contained.

Factory deliberately uses at-least-once execution. An interrupted task may run
again against current repository reality.

Planner `# Question` stops orchestration for one user decision. The outer IDD
workflow decides whether the exact answer changes durable intent, then a fresh
planner resumes.

Planner `# Done` triggers existing project verification when configured.
Failure is bounded input to a fresh planner; success completes the temporary
run.

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
sequencing, temporary planning, migration, compatibility transition, or multiple
independent tasks.

Glossary work is deliberately outside automatic routing. It starts only from an
explicit glossary request or an explicitly accepted bootstrap/import offer.

## How IDD Differs from Broad Spec-Driven Workflows

IDD is still specification-driven in the ordinary sense: implementation follows an explicit product description.

Its distinction is narrower and stricter:

- specifications describe the current product, not the current project;
- implementation work is disposable by default;
- Git owns specification history;
- one current document should own one durable product area;
- optional vocabulary support stays separate from behavioral requirements;
- the methodology is tested by the possibility of rebuilding from intent.

IDD does not attempt to preserve every step that led to the product. It preserves what the product must continue to be.

## Summary

`idd-intent` preserves product memory and may optionally maintain a deliberately small project glossary. `idd-factory` organizes temporary implementation work through fresh native planner/worker contexts, incremental batches, and repository-driven at-least-once convergence. Requests, plans, planner questions and answers, and diagnostic summaries remain temporary, and Git owns product-intent history.
