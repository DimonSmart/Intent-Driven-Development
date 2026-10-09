# idd-route

Use this skill to classify an IDD-related request and select the smallest safe
end-to-end workflow.

This skill is a read-only classifier. It does not change intent, implementation,
Factory state, or project files, and does not execute the selected workflow.

## Required Reference

Before classifying the request, read:

`references/common-workflows.md`

Treat that document as the canonical source for workflow families, product
operations, requested scope, execution-depth selection, preservation
boundaries, and completion rules.

If the reference is unavailable, report that the installed plugin is incomplete
and do not reconstruct the full routing model from memory.

## Inputs

Accept natural-language user requests. The request may include a product area,
specification, code area, observed mismatch, required result, repository
bootstrap request, or an explicit limit such as "classify only", "update intent
only", or "do not change specs".

Do not require JSON or a special parameter structure.

## Forked Request Contract

This skill may run in an isolated child context. Classify only the request
explicitly supplied with the invocation; do not assume the caller's conversation
history is available. Direct invocation arguments are part of that request.

For automatic invocation, the caller must supply the complete available
original user request, including relevant constraints, prohibitions, requested
scope, and preservation boundaries. Do not replace it with a lossy summary.
For file or source references, pass resolvable references rather than inventing
their contents. Passing input to a child does not imply byte-for-byte identity
or access to unprovided parent context.

If no meaningful request reaches this skill and it cannot be recovered from
available input, do not guess a route. Ask for the missing request or return
an explicit missing-input diagnostic to the caller.

## Context Reading Rules

First classify the request from its wording and the required reference.

Read project context only when needed to determine whether a current owner
exists, product truth changes, the problem is structural, implementation and
intent may diverge, initial intent is missing, or Factory is probably required.

When project context is needed:

1. Read `.idd/intent/README.md`.
2. Read `.idd/intent/INDEX.md`.
3. Read only relevant current `IDD-NNNN` documents.
4. Do not load the whole intent tree.
5. Do not inspect Git history.
6. Do not perform broad code review.
7. Do not change files.

For possible `intent-bootstrap`, perform only the cheap check needed to
distinguish an existing implementation without adequate current intent from an
empty new project or an already documented product. The bootstrap skill owns
broad discovery.

## Classification Fields

Return these semantic fields:

```text
Classification:
- project-initialization
- verification-configuration
- factory-model-update
- intent-bootstrap
- intent-import
- product-change
- engineering-change
- implementation-change
- intent-normalization
- intent-structure-audit
- intent-lint
- intent-drift-audit
- implementation-to-intent
- explicit-skip
- unclear
```

Use `verification-configuration` when the user asks to create or deliberately
update project-owned `.idd/verification.yaml` rules. Do not use it for running
checks, fixing tests, or changing product acceptance criteria.

Use `factory-model-update` when the user asks to refresh, upgrade, downgrade,
or replace the LLMs assigned to Factory effort levels. These levels are
`ExecutionProfile` values, distinct from platform reasoning settings. Do not
route ordinary task execution or a question about models to a model update.

Use `intent-bootstrap` when the repository already contains implementation but
lacks an adequate current IDD product model and the user asks to discover,
reconstruct, or create initial intent from codebase evidence with owner
confirmation.

Use `intent-import` when existing documents or other supplied source material
already express durable knowledge that needs migration into IDD. That knowledge
may contain Product Intent, explicit accepted durable Engineering decisions, or
both. Do not route a new technical choice to import merely because it is written
in the request, and do not route codebase reverse discovery to import merely
because code is a source.

Treat direct requests to create a new product specification, record an ADR, or
investigate an unresolved decision in a spike as `product-change` (or `unclear`
when a required product decision is missing). Route them through
`idd-intent-change`, never a separate public document-creation skill.

Use `engineering-change` when the user explicitly adds, modifies, or removes a durable implementation-only project constraint. Do not route to Engineering merely because current code repeats a pattern; source code alone is not an explicit durable policy decision.

For `product-change` and `engineering-change`, set `Operation` to `add`, `modify`, or `remove` according to the required reference. For every other classification, set `Operation: not-applicable`.

Set `Clarity` to `clear`, `ambiguous`, or `research-required`.

A bootstrap request may have `Clarity: clear` even though the product meaning is
not yet known: the requested workflow is clear, and
`idd-intent-bootstrap` contains its own semantic confirmation gates.

Set `Execution depth` to `focused`, `orchestrated`, or `not-applicable`
according to the required reference.

Use `not-applicable` for `intent-bootstrap`, `verification-configuration`, and
`factory-model-update`.
Repository discovery may be broad, but it is intent-side investigation rather
than implementation orchestration and must not start Factory.

For `intent-drift-audit`, also return `Audit scope: focused | project-wide`. Use
`project-wide` only when the user explicitly asks for the whole project, entire
repository, or all current Product Intent. Otherwise use `focused` when a
behavior, intent document, implementation area, or mismatch is identified.
Routing does not load the whole intent tree; project-wide discovery belongs to
`idd-intent-drift-audit`.

For all other classifications, use `Audit scope: not-applicable`.

Set `Requested scope` to one of:

```text
route-only
intent-only
implementation-only
end-to-end
```

Use the narrowest scope that satisfies the user's explicit request. Explicit
limits such as "only", "do not change files", "do not implement", and "do not
change specs" take precedence over the complete workflow normally associated
with the classification.

- `route-only`: describe the route and stop. Do not invoke another skill or
  change files.
- `intent-only`: perform only durable IDD knowledge-side work, including
  initialization, product Intent workflows, Engineering management, audit, lint,
  or normalization as applicable. Do not implement product code or start Factory
  execution.
- `implementation-only`: perform implementation or implementation checking from
  current durable knowledge. Do not change `.idd/intent/*` or
  `.idd/engineering/*`. If the request requires either durable mutation, stop
  and report the required durable workflow instead of expanding scope.
- `end-to-end`: continue through all requested workflow stages, subject to
  clarity gates and execution-depth selection.

A request to understand an existing project and create its initial intent is
normally `intent-only` unless it also explicitly asks for implementation
changes after bootstrap.

A structure audit is normally `intent-only` because it reads the Intent knowledge
model. A drift audit is normally `implementation-only` because it reads Product
Intent plus implementation evidence without mutating either layer. Use
`route-only` when the user asks only for classification.

For `verification-configuration`, use `route-only` only when the user asks for
classification or advice without changing files; otherwise use `end-to-end`.

For `factory-model-update`, use `route-only` only for classification or routing
advice. Use `end-to-end` for a requested model review or update, preserving a
review-only limit so the selected skill returns its proposal without saving.

Do not assign route fields when another explicitly named skill or `idd-skip`
bypasses routing. Those cases are direct skill invocation, not route results.
An explicit `idd-factory-run` still performs its own required Factory Preflight
for a new run; bypassing this router must not bypass Product Intent analysis,
explicit Engineering preparation when required, durable coverage validation, or
the rule that active Factory state is created only afterward.

## First Skill

Use the required reference to select the first skill. This compact table is only
a handoff index:

| Classification | Recommended first skill |
| --- | --- |
| `project-initialization` | `idd-project-init` |
| `verification-configuration` | `idd-verification-configure` |
| `factory-model-update` | `idd-factory-update-effort-models` |
| `intent-bootstrap` | `idd-intent-bootstrap` |
| `intent-import` | `idd-intent-import` |
| `product-change` | `idd-intent-change` |
| `engineering-change` | `idd-engineering-change` |
| `implementation-change` | `idd-code-implement` or Factory |
| `intent-normalization` | `idd-intent-normalize-current` |
| `intent-structure-audit` | `idd-intent-structure-audit` |
| `intent-lint` | `idd-intent-lint` |
| `intent-drift-audit` | `idd-intent-drift-audit` |
| `implementation-to-intent` | `idd-code-update-intent` |
| `explicit-skip` | `idd-skip` |
| `unclear` | `idd-intent-brainstorm` or a spike handoff |

Never select `idd-skip` automatically. Use it only when the user explicitly
refuses IDD for the request.

## Handoff Rules

Distinguish the complete workflow from the current handoff:

- `Expected complete workflow` describes the normal lifecycle needed to finish
  the request safely.
- `Current handoff` recommends what the caller may start in this user request;
  it does not authorize this skill's child agent to execute the handoff.
- `Stop after` defines the requested-scope boundary or clarity gate.

The complete workflow is informative. It is not permission to execute stages
outside `Requested scope`.

Apply these rules:

- For `route-only`, set `Current handoff: none` and stop after returning the
  route.
- For `intent-only`, hand off only to the applicable intent-side skill and stop
  before implementation or Factory execution.
- For `implementation-only`, hand off only to the applicable code or check skill
  and do not modify Product Intent or Engineering.
- For `end-to-end`, the caller continues with the recommended skill in the
  same user request when scope and clarity gates permit. Do not require a
  second user message only to confirm the route. This classifier returns
  the routing decision and never performs that mutation itself.
- When clarity is `ambiguous` or `research-required`, stop at the corresponding
  brainstorm, check, ADR, or spike gate even when the requested scope is
  `end-to-end`. Continue only after the missing decision or evidence exists.
- For `intent-bootstrap`, hand off the original scope and any include, exclude,
  temporary context, or known compatibility information. The bootstrap skill
  must still obtain its own project-boundary and semantic proposal
  confirmations before writing current intent.
- For `factory-model-update`, preserve selected levels, target platform, exact
  model IDs, cost/quality/latency goals, and review-only limits. Continue through
  `idd-factory-update-effort-models -> idd-factory-configure` when application is
  requested. If the Factory plugin is unavailable, report that requirement;
  do not turn this request into product implementation or start a Factory run.

Pass through the complete original request, classification fields, requested
scope, relevant context, and any temporary preservation or discovery boundary
identified from the required reference. Never replace a mixed request with a
lossy summary.

A single request may contain multiple durable concerns without requiring a new
top-level classification enum. Preserve the primary route fields, but make
`Expected complete workflow` include every required durable stage before
implementation. In particular, a Product change plus explicit Engineering
change plus orchestrated implementation must keep both durable concerns in the
handoff so a new `idd-factory-run` can coordinate them during preflight.

Examples:

```text
Does implementation match current intent?
-> Classification: intent-drift-audit
-> Recommended first skill: idd-intent-drift-audit

Audit the whole project for intent drift.
-> Classification: intent-drift-audit
-> Audit scope: project-wide
-> Recommended first skill: idd-intent-drift-audit

Review whether our intent specs should be split or merged.
-> Classification: intent-structure-audit
-> Recommended first skill: idd-intent-structure-audit
```

The route classification is temporary workflow evidence. Do not create route
files, preservation records, discovery reports, Factory state, specs, or code
from this skill.

## Output Format

Use compact Markdown, not JSON:

```md
# IDD Route

Classification: `product-change`
Operation: `modify`
Clarity: `clear`
Execution depth: `focused`
Requested scope: `end-to-end`
Audit scope: `not-applicable`

Recommended first skill: `idd-intent-change`
Expected complete workflow: `idd-intent-change -> idd-code-implement -> idd-intent-drift-audit`
Current handoff: `idd-intent-change`
Stop after: `the requested end-to-end workflow completes, or an intent or verification gate blocks progress`

Preservation boundary:
- Behavior expected to change:
- Behavior expected to remain unchanged:
- Public contracts to preserve:
- Compatibility or data constraints:
- Unresolved preservation questions:

Why:
- Short routing rationale grounded in `references/common-workflows.md`.

Handoff:
- Recommend that the caller invoke the current handoff skill with the original
  request, route fields, and preservation or discovery boundary, or state that
  no handoff is allowed for `route-only`. The caller waits for this routing
  result, applies the scope and clarity gates, and owns any subsequent invocation.
```

For `intent-bootstrap`, use a discovery boundary instead of inventing a product
preservation boundary:

```md
Discovery boundary:
- Repository or product areas included:
- Areas excluded or probably non-product:
- User-provided temporary context:
- Known public or compatibility contracts:
- Unresolved scope questions:
```
