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
product intent       durable product behavior and product-significant contracts
engineering rules    optional durable implementation guardrails
project glossary     optional shared vocabulary
verification policy  optional operational evidence commands
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
accepted architecture decisions and rationale
product-significant constraints
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
implementation-only durable conventions that belong in Engineering
```

## Technical autonomy versus durable policy

IDD distinguishes what the product must do (Intent), which implementation
constraints must bind *future* work (Engineering), and which technical
means are locally chosen for the current Task. Factory may choose a
compatible library, algorithm, internal structure or new application's
configuration value without special permission, even when that choice
lasts for years. Ordinary implementation choices do not become Rules
automatically.

By contrast, requiring the chosen library across all future tasks,
changing a shipped identity or altering distribution/trust policy needs
appropriate authority and the existing durable owner workflow. All
existing Rules remain mandatory. A Planner leaving implementation
alternatives to Worker must semantically include relevant Conditional
Rules, or narrow/research the approach before execution. Worker cannot
discover additional Conditional rules. Technical facts are established
through research, not by asking users to guess them.

## Optional Engineering Guardrails

A project may optionally keep durable implementation constraints in
`.idd/engineering/`. This layer is separate from product intent.

Use Intent for externally observable behavior, public/domain/compatibility
contracts, and product-significant operational properties. Use Engineering when
another implementation could preserve the complete product contract but would
still be forbidden by a durable architecture, consistency, maintainability, or
engineering convention.

For example:

```text
Dialogs have consistent keyboard behavior.              -> Intent
Dialogs obtain keyboard behavior from shared Dialog UI. -> Engineering
```

Engineering Rules use stable `ENG-NNNN` identifiers. `Always` rules apply to
every implementation task and are enumerated mechanically. `Conditional`
rules are selected semantically from their human-readable `Applies when`
metadata. IDD does not use filename, keyword, path, file-extension, project-type,
embedding, or similarity heuristics to determine Conditional applicability.

The Engineering INDEX is read first for cheap discovery. Full rule documents are
loaded only for all Always rules and the relevant Conditional rules. Malformed
or ambiguous Engineering structure blocks implementation rather than being
silently ignored.

Rule verification sections describe required evidence or properties; operational
commands stay in `.idd/verification.yaml`.

The layer is optional and is not created automatically by
`idd-project-init`. `idd-engineering-change` owns ordinary Rule
add/modify/remove operations. `idd-intent-import` may lazily create or update
Rules only when migrating explicit durable Engineering decisions already present
in supplied import sources; it does not infer policy from implementation
reality. Git owns Engineering history and there is no Engineering archive.

See [Engineering Guardrails](engineering-guardrails.md) for the exact format and
direct/Factory protocol.

## Audit and Verification Responsibilities

IDD keeps four checks separate:

```text
idd-intent-lint              mechanical Intent validity
idd-intent-structure-audit   semantic Intent organization
idd-intent-drift-audit       implementation / Intent conformance
.idd/verification.yaml       project-specific verification evidence
```

Mechanical Intent lint defaults to `mode: check` (no writes). An explicitly
authorized `mode: fix` repairs only unambiguous technical faults in
project-owned Intent: stable filename prefixes, first headings, INDEX
identifiers and roles, exact local references, managed formatting, and
missing canonical README/INDEX/templates when the project is initialized.
After one repair pass it re-checks and reports remaining errors and actual
changed paths. No automatic Git commit, `dry-run`, or rollback is performed.
Stable IDs are never reassigned. Semantic reorganization remains in
`idd-intent-normalize-current`, glossary edits in `idd-glossary-build`,
and Engineering edits in `idd-engineering-change`. Both lint modes treat
Glossary and Engineering as read-only.

Drift audit is read-only and requires an explicit focused or project-wide
scope. Project-wide audit starts from Intent ownership and is not generic
code-quality review.

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

ADRs are also current knowledge, not an exception to Git-owned history. A
living ADR describes the currently accepted architectural decision for its
area, its rationale, relevant experimental findings and consequences. Update
its owning ADR in place when the approach changes while the architectural
responsibility remains the same; keep the ID. Create a new ADR only for a
genuinely independent decision. Remove obsolete, historical-only ADRs after
transferring useful knowledge and updating INDEX and current references.
Do not use ADR lifecycle statuses or supersession chains.

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

`idd-factory` owns lightweight entry coordination plus temporary semantic
orchestration:

```text
complete logical request
-> Factory Preflight
   -> Product Intent preparation through normal Intent workflows when required
   -> explicit Engineering preparation through idd-engineering-change when required
   -> durable coverage validation
-> persist preparation -> fresh initial planner (inactive)
   -> Question: exact answer -> authorized owners -> fresh planner
   -> Done: final verification without activation
   -> Tasks: validate first batch -> create active Factory state LAST
-> current batch
-> fresh sequential workers
-> fresh planner
-> Question | Done
```

The entry workflow coordinates existing durable owners before active state.
An initial Planner can ask while `current/request.md` is absent; exact Q/A
are saved in preparation for continuation between sessions, and authorized
durable owners run before renewed coverage and a fresh Planner. The first
valid Task batch, not preflight alone, allows writing `current/request.md`
LAST after its plan, immutable baseline, provenance and answers. Factory
Planner remains read-only. Only Factory Worker may refine existing Intent
monotonically; neither independently changes approved Product decisions or
Engineering Rules.

Factory does not package or run a .NET workflow runtime. It has no Factory MCP
transport, process supervisor, retry state machine, authoritative attempt
history, or exact continuation protocol.

The repository is authoritative implementation reality. Temporary continuation
state contains the original request and exact Q/A in
`.idd/factory/preparation/` before activation and the original request,
remaining batch, short completed summaries and exact answers in `current/`
after activation. Only `current/request.md` denotes an active run.
Pending preparation can be cancelled without undoing durable writes.

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

Planner `# Question` stops orchestration for one user decision. Before
activation, answers are classified against the complete base request and the
original scope. A local choice creates no Engineering Rule; authorized
Engineering changes use `idd-engineering-change`, with Product Intent
updates afterward when both are required. After activation, a new durable
Engineering decision cannot be applied while the active marker exists;
the run stays paused until completed/cancelled and restarted with the decision.

Planner `# Done` uses existing final project verification when configured,
even before the first Worker. Pre-activation success completes without an
active run; failures provide bounded diagnostics to a fresh Planner, and
repeated unchanged failure or `Not verified` is not a success.

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
