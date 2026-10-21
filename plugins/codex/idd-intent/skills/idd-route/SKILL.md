---
name: idd-route
description: Use a read-only routing step to classify an IDD-related request and select the smallest safe end-to-end workflow before handing off to the appropriate IDD skill.
---

# idd-route

Use this skill to select the smallest safe IDD workflow. Standalone Factory
model-policy requests select their skill directly; all other requests use
the ordinary classification rules.

This skill is a read-only router. It does not change intent, implementation,
Factory state, or project files, and does not execute the selected workflow.

## Required Reference

Before routing the request, read:

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

## Automatic Selection and Route-Only Diagnostics

This is an internal automatic workflow, not a user-facing command. When the user
asks in natural language to classify a request, explain which IDD workflow would
apply, or show the route without execution, the caller may invoke this router
automatically with the complete request. Set `Requested scope: route-only`,
`Current handoff: none`, and stop after the routing result; do not run the
recommended skill, start Factory, or change project files.

A recommended first skill is not permission to invoke it. In particular,
`idd-help`, `idd-project-init`, `idd-verification-configure`,
`idd-glossary-build`, and `idd-skip` are manual-only commands. The caller
must not automatically invoke any of them after routing, even for an
`end-to-end` request. Explain the recommendation and require explicit user
selection of the command or a host-supported direct-invocation equivalent.
Do not treat an automatic routing step as that selection.

## Context Reading Rules

First choose the route from the request wording and the required reference.

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

## Direct Factory Model-Policy Route

Before assigning any `Classification`, check whether the **only** independent
task is to configure or inspect project-owned Factory model policy. Select
`idd-factory-configure` directly as the recommended first skill, not as a
classification value, for standalone requests to:

- Perform initial Factory model setup or change an exact model ID for a
  `codex`/`claude` `economy`, `standard`, or `strong` mapping.
- Change only an existing mapping's reasoning settings (such as
  `reasoningEffort`), or return one mapping to host inheritance.
- Review current model assignments, compare them with available alternatives,
  or make a read-only proposal without writing.
- Refresh/upgrade/downgrade models, replace unavailable models, or select
  cheaper, faster, or stronger candidates.

The direct choice covers initial, exact-edit, review-only, and refresh/selection
requests. The selected `idd-factory-configure` skill, **not** this router,
determines that mode, validates the operation, chooses evidence sources, applies
confirmation gates, and owns any policy write. The router must not research the
model catalog, choose model values, or read, validate, or change
`.idd/execution.yaml` to make a routing decision. It never runs
`idd-factory-configure` in its isolated child context or starts
`idd-factory-run`.

Do **not** use this branch for an explicit `idd-factory-run` request, general
project configuration, Product Intent changes, or unrelated implementation.
Explicit invocation of `idd-factory-configure` bypasses routing altogether.

If a request includes Factory model management **and** another independent
task (Product Intent, Engineering, implementation, audit, Factory execution,
etc.), use the existing **general workflow classification and handoff**.
Preserve **all** requested stages and the complete original intent, including
the model-policy subtask. Route that subtask to `idd-factory-configure` as a
separate authorized stage only when the general workflow requires it. Do not
replace the whole request with configuration or start Factory automatically.
If the primary workflow is unclear, use the existing clarity gate; do not
invent a new classification or choose Factory arbitrarily.

For a standalone Factory model-policy request, select only these result fields:
`Recommended first skill`, `Expected complete workflow`, `Requested scope`,
`Current handoff`, and `Stop after`. Add `Why`, `Handoff`, and user
constraints where useful. Do **not** invent or return `Classification`,
`Operation`, `Clarity`, `Execution depth`, or `Audit scope`, even with
`not-applicable` values; the ordinary fields still apply to other routes.

- Use `Requested scope: route-only` for routing advice: return the
  recommendation, set `Current handoff: none`, and stop after routing.
- Use `Requested scope: end-to-end` for a requested configuration **or**
  read-only review/comparison: the caller may hand the complete request to
  `idd-factory-configure` in the same user request, without a second routing
  confirmation. `end-to-end` is not permission to write. Preserve explicit
  `review-only`/`do not write` and all other authorization limits.
- Do not mechanically map `intent-only` or `implementation-only` to a
  standalone Factory model-policy request. Carry explicit restrictions as
  constraints, or diagnose incompatible scope without expanding permission.
- Preserve all exact selected platforms, profiles, model IDs, reasoning
  settings, inheritance operations, price/quality/latency goals, prohibitions,
  and previously confirmed decisions in the handoff. Do not substitute a
  lossy summary. Exact authorized edits do not require new router confirmation;
  selections and proposals retain the configure skill's confirmation gates.
- `idd-route` is part of `idd-intent`; `idd-factory-configure` requires
  the separate `idd-factory` plugin. Recommending the skill does not prove
  that it is installed. If it is known to be unavailable, diagnose the missing
  plugin and use `Current handoff: none` with a blocking `Stop after`.
  If availability cannot be checked using the caller's existing context,
  state the requirement conditionally, without assuming installation. Do not
  invent a plugin-check API, substitute another skill, or write policy.

## Classification Fields

For requests not handled by the direct Factory model-policy route, return
these ordinary semantic fields:

```text
Classification:
- project-initialization
- verification-configuration
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

Route mechanical Intent validation and permitted technical corrections to
`intent-lint`: "check", "validate", or "find errors" means `mode: check`
with no writes; "check and fix", "repair IDs/INDEX/technical references"
means `mode: fix` only with explicit write authorization. Preserve the
original scope, permissions and prohibitions in the handoff. "Fix the lint
skill itself" is an implementation-change request, not permission to
run project `idd-intent-lint` in fix mode. Semantic duplications belong to
`intent-structure-audit`; moves, splits, merges and substantive deletions
belong to `intent-normalization`; changing product behavior belongs to
`product-change`; Glossary changes belong to `idd-glossary-build`,
Engineering changes to `engineering-change`. If the user asks to bring all
documents into the latest format, do not infer authorization for semantic
migrations or blanket formatting: apply only the requested canonical
mechanical fixes and diagnose the rest.

Use `engineering-change` when the user explicitly adds, modifies, or removes a durable implementation-only project constraint. Do not route to Engineering merely because current code repeats a pattern; source code alone is not an explicit durable policy decision.

For `product-change` and `engineering-change`, set `Operation` to `add`, `modify`, or `remove` according to the required reference. For every other classification, set `Operation: not-applicable`.

Set `Clarity` to `clear`, `ambiguous`, or `research-required`.

A bootstrap request may have `Clarity: clear` even though the product meaning is
not yet known: the requested workflow is clear, and
`idd-intent-bootstrap` contains its own semantic confirmation gates.

Set `Execution depth` to `focused`, `orchestrated`, or `not-applicable`
according to the required reference.

Use `not-applicable` for `intent-bootstrap` and
`verification-configuration`. Repository discovery may be broad, but it is
intent-side investigation rather than implementation orchestration and must
not start Factory.

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

Do not assign route fields when another explicitly named skill or `idd-skip`
bypasses routing. Those cases are direct skill invocation, not route results.
An explicit `idd-factory-run` still performs its own required Factory Preflight
for a new run; bypassing this router must not bypass Product Intent analysis,
explicit Engineering preparation when required, durable coverage validation, or
the rule that active Factory state is created only afterward.

## Visual Reference Clarity Gate

When a `product-change` request includes visual references but the user has
not confirmed which visual properties, scope, or strictness should be preserved,
keep `product-change` and recommend the read-only
`idd-intent-visual-brainstorm` discovery gate. Do not invent a top-level
`visual-change` or `visual-intent` classification.

Already confirmed visual Product Intent, including a clear approved handoff
that the user now asks to save, routes **directly** to `idd-intent-change`;
a second brainstorm or approval is unnecessary. A screenshot reporting a bug,
an explicit actionable visual result, or a local styling request preserving
current Intent does not trigger visual discovery. Local styling is an ordinary
`implementation-change` unless it changes durable product truth; when it
might violate an Essential visual invariant, stop for the smallest product
decision rather than silently changing implementation or Intent.
Respect `intent-only` / `implementation-only` / `end-to-end` scope and
never automatically run a separately requested intent write after brainstorm.

## First Skill

For ordinary classified routes, use the required reference to select the first
skill. This compact table is only a handoff index; the standalone Factory
model-policy route above is selected before the table:

| Classification | Recommended first skill |
| --- | --- |
| `project-initialization` | `idd-project-init` |
| `verification-configuration` | `idd-verification-configure` |
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
- For `intent-lint`, pass the complete original request, explicit
  `mode: check|fix`, the requested scope, and read-only restrictions.
  Only an unambiguous user request can authorize `fix`. The router
  remains read-only and must not apply lint fixes itself.
For general classified workflows, pass through the complete original request,
classification fields, requested scope, relevant context, and any temporary
preservation or discovery boundary identified from the required reference.
For direct Factory routing, pass the complete original request and its
constraints instead of nonexistent classification fields. Never replace a
mixed request with a lossy summary.

A single request may contain multiple durable concerns without requiring a new
top-level classification enum. Preserve the primary route fields, but make
`Expected complete workflow` include every required durable stage before
implementation. In particular, a Product change plus explicit Engineering
change plus orchestrated implementation must keep both durable concerns in the
handoff so a new `idd-factory-run` can coordinate them during preflight.
A mixed request such as "change Factory economy model and implement a feature"
must also preserve both tasks and must not be reduced to the direct Factory
model-policy route.

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

Use compact Markdown, not JSON. For ordinary classified workflows, return the
semantic fields below; the direct Factory model-policy route uses the compact
examples following this section:

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

## Direct Factory Model-Policy Output Examples

These are semantic examples, not byte-for-byte templates. Set `Stop after`
based on the actual scope, blockers, and available skills. No artificial
classification, operation, clarity, execution-depth, or audit-scope fields apply.

Standalone authorized model change:

```text
# IDD Route

Recommended first skill: idd-factory-configure
Expected complete workflow: idd-factory-configure
Requested scope: end-to-end
Current handoff: idd-factory-configure
Stop after: requested configuration workflow completes or a required decision blocks it

Handoff: pass the complete original request and its constraints to idd-factory-configure
```

Route recommendation only:

```text
# IDD Route

Recommended first skill: idd-factory-configure
Expected complete workflow: idd-factory-configure
Requested scope: route-only
Current handoff: none
Stop after: routing recommendation
```

For a requested read-only review with `end-to-end`, use the first format but
pass the read-only restriction unchanged; the selected skill must not write.
When `idd-factory` is known to be missing, keep the recommended skill name
but set `Current handoff: none` and stop at the dependency diagnostic.
