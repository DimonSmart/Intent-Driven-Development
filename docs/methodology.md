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

`idd-intent` owns the durable side of the methodology. It initializes and maintains `.idd/intent/`, imports or changes current product truth, optionally builds the project glossary, implements from intent, and checks implementation against intent.

`idd-factory` owns temporary execution orchestration. Its packaged .NET runtime
deterministically coordinates bounded-batch planning, sequential execution,
authoritative verification, retries, resume, and finalization.
`idd-factory-run` is only the public launcher.

The separation is visible to the user because the responsibilities have different lifecycles:

- `idd-intent` is the normal standalone installation;
- `idd-factory` is optional and depends on `idd-intent`;
- Factory Runtime and its work items may read intent but must not create or
  silently modify product truth;
- before a new end-to-end run, the Factory launcher distinguishes a missing
  intent document from a genuinely missing product decision and may invoke the
  existing `idd-intent` workflow before runtime creation;
- `INTENT_REQUIRED` before a new run means that a durable decision required for
  safe implementation cannot be determined from the original request and
  current intent, not merely that a corresponding specification file is absent;
- during an existing run, when an exhausted batch leaves the planner unable to
  contract any next task without a user decision, the planner may ask one
  concrete question and runtime pauses with `USER_DECISION_REQUIRED`;
- after the user answers, the outer IDD workflow decides whether the answer
  changes durable intent, then the exact answer is passed back to the same run;
- executors never emit intent, correction, replan, next-work, or user-question
  control outcomes;
- `.idd/factory/current/` holds at most one active or resumable run and
  `.idd/factory/results/` preserves complete successfully finalized run directories for diagnostics and handoff;
- both directories are ignored by default and are never durable product intent.

The current workspace contains immutable `request.md`, authoritative
`state.json`, stable work-item contracts, attempt artifacts, verification
evidence, planning question/answer artifacts when needed, and an append-only
event audit. Explicit state status and revision, not filenames or conversation
history, support validation and safe resume.

Factory's semantic loop is deliberately small:

```text
planner -> ordered batch -> sequential executors -> planner
```

The planner is the only semantic component that creates future work. Runtime
assigns IDs, executes the whole current batch in order, observes actual changed
paths, performs authoritative verification, retries the same immutable task on
ordinary verification failure, and invokes planning again only after the batch
is exhausted. When semantic reassessment finds no remaining work, the planner
returns exactly `# Done`; blank or whitespace-only planner output is malformed.
Runtime mechanically maps validated `# Done` to the existing empty-batch
representation and runs strict final verification. A final verification failure
becomes evidence for another ordinary planning cycle; a success permits
finalization without a mandatory semantic final reviewer.

If planning instead reaches a real user decision boundary, the planner returns
one human-readable question and no tasks. Runtime owns only the durable pause and
resume. It does not infer the answer or decide whether the answer is product
intent. The user may answer and continue, with IDD updating durable intent first
when appropriate, or cancel the run. Exact answers are stored separately from
the immutable original request and become evidence for later planners.

Only after successful final verification does Factory prepare final result
artifacts and move the complete `.idd/factory/current/` directory to
`.idd/factory/results/<timestamp>_<work-slug>/`. The completed result therefore
retains state, request, events, attempts, verification evidence, planning
question/answer artifacts, commit-message handoff, and other run diagnostics;
prior results remain intact.

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

`idd-intent` preserves product memory and may optionally maintain a deliberately small project glossary. `idd-factory` organizes resumable temporary implementation work through bounded planning batches and deterministic runtime control. Requests, task statuses, planner questions and answers, completed-run diagnostics, and commit-message handoffs remain temporary, and Git owns product-intent history.
