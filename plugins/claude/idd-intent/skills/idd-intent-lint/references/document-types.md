# Document Types

The canonical type contract defines *semantic obligations* (information that
must be conveyed) separately from *minimal structural requirements* needed
for reliable identity, lifecycle, discovery, and normative reference resolution.
The existence, order or number of headings in canonical or customized templates
never creates validation requirements. Equivalent readable presentation is
acceptable unless an existing mechanical consumer requires a specific field.
Mechanical lint cannot establish semantic completeness by counting headings.

For every current `spec`, `adr`, and `spike`, retain canonical
`IDD-NNNN.type-short-title.md` identity, a matching first H1 and one plain ID
entry in `INDEX.md`, plus valid normative `IDD-NNNN` relations. These
requirements support stable resolution and discovery. Type-specific lifecycle
and structural rules follow.

## spec

`spec` describes what the system should be. It is not a task list.

**Semantic contract:** express current Product Intent, including behavior,
acceptance and verification properties, boundaries and constraints where they
matter to the product. An empty or placeholder-only spec is not semantically
sufficient.

**Mechanical contract:** common identity, H1, INDEX and normative relations.
No universal H2 heading is required. `Intent`, `Related Specifications`,
`Behavior`, `Product-Significant Architecture And Constraints`, `Non-Goals`,
`Acceptance Criteria`, `Verification` and `Visual Intent` are recommended
or contextual headings, not universal structural requirements. A spec has no
lifecycle-status field; bans on task/progress content, implementation detail and
operational commands remain in force.

Use `spec` when the change affects product behavior, domain contracts,
product-significant architecture or operational constraints, compatibility
expectations, non-goals, acceptance criteria, verification rules, or shared
behavior.

A durable technical constraint is not automatically product intent. If another
implementation could preserve the complete product contract while still being
forbidden by the constraint, the constraint normally belongs in the optional
`.idd/engineering/` layer. Do not migrate existing intent automatically merely
because it now fits this classification better.

A spec document has no lifecycle status. Its presence in the current intent
directory means that it is current. Do not mark a spec as `Current`,
`Completed`, `Deprecated`, `Retired`, or `Superseded`. Edit the owning spec in
place when its product area remains current; migrate remaining current intent
and delete it when obsolete. Git history is the only history of spec revisions.

When appearance or interaction cues are product-significant, the current
owning specification may include an **optional** `## Visual Intent` section.
Record user-confirmed properties, relative visual relationships, scope,
implementation freedom, and deliberate things to avoid. Reference images
are supporting evidence by default, not a replacement for normative text.
Do not introduce this section where visual appearance is irrelevant.

Shared specification is a normal `spec`. Common rendering, input, validation,
or dialog behavior should be a separate specification when multiple product
areas depend on it.

## adr

`adr` is a **living record of the currently accepted architectural decision**
for a stable architectural area. It is not an immutable event or a versioned
decision history. Its presence among current `.idd/intent/` documents means
that its decision is normative; no lifecycle status is used.

**Semantic contract:** explain the current architectural choice, its context,
rationale, significant alternatives, relevant findings from experiments, and
consequences or tradeoffs. The ADR must be self-contained without requiring
deleted documents or Git history. Preserve former experimental findings only
when they explain the current decision or prevent known errors; do not retain
a chronology of implementation iterations. Do not invent reasons or results.

**Mechanical contract:** the common stable ID, filename, first H1, unique INDEX
entry and valid normative references. No universal H2 heading is required.
`Context`, `Decision`, `Alternatives Considered`, `Experiments and Findings`,
and `Consequences` are example headings only. Do not require lifecycle
`Status`, `Supersedes`, `Superseded By`, or history-oriented `Replaces`.

When the decision changes within the same architectural responsibility, update
the owning ADR directly and retain its ID, even if the technical approach
changes. Create a new ADR only for a genuinely independent architectural
decision without an existing owner. Ordinary implementation-local decisions
need no ADR. An unresolved choice belongs in a spike or the existing decision
workflow, not a `Proposed` ADR; rejected alternatives can be included in the
current ADR's rationale but not as separate `Rejected` ADRs.

When an architectural area disappears, loses durable relevance or duplicates
another ADR, use an authorized semantic workflow to transfer useful knowledge
to the appropriate current owner, reconcile current normative references and
INDEX, and delete obsolete records. Retain the ID of the best architectural
owner, not necessarily the newest number. Deleted IDs are not reused; Git
preserves revisions. Do not create archives or tombstones.

An ADR explains architecture but does not replace product specs, Engineering
Rules, or implementation reality. An accepted target decision may differ from
current code: establish whether to change the implementation or the decision
before editing the ADR.

## spike

`spike` describes an experiment, research task, or hypothesis check before a
product or architecture decision.

**Semantic contract:** a spike must express an active uncertainty, question or
hypothesis. Describe constraints and evaluation method where relevant. Results
and recommendations belong in the document only as they become available;
an active unresolved spike can legitimately have neither. Do not invent
`TBD` results to match a template.

**Mechanical contract:** common identity, H1, INDEX and relation invariants
apply. No universal H2 (`Question`, `Constraints`, `Method`, `Result`,
`Recommendation`) is required. A missing research question is a semantic
workflow concern, not an H2-counting rule.

A spike is active research only while the question is unresolved. When resolved,
move durable product behavior into a spec, move durable architecture decisions
into an ADR, and delete the spike unless it remains useful as active research.

## Optional glossary support file

`GLOSSARY.md` is not an IDD document type. It is an optional unnumbered support
file for project-specific vocabulary.

The glossary contains not all project terms, but only terms whose incorrect
interpretation could change the understanding of product intent. It does not own
behavior, constraints, decisions, acceptance criteria, or verification rules.

Do not assign it an `IDD-NNNN` identifier and do not list it in `INDEX.md`. Its
absence is valid and means that the project does not use a managed glossary.
Create or change it only through the explicit manual-only
`idd-glossary-build` workflow.
