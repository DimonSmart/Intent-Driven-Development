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

**Before ordinary classification**, when the sole independent task concerns
project-owned Factory model policy, recommend `idd-factory-configure` directly:
initial configuration, exact `codex`/`claude` `economy | standard | strong`
model ID or reasoning-settings edits, return to host inheritance, read-only
assignment review/comparison/proposal, or evidence-based model refresh,
upgrade, downgrade, cost/latency/quality selection or replacement.

The selected skill owns mode selection, model evidence, validation,
confirmation and any write. Router must not inspect or edit
`.idd/execution.yaml`, research models, start Factory or execute the
recommended skill. An explicit invocation of `idd-factory-configure` bypasses
routing. Do not take this shortcut for `idd-factory-run`, unrelated project
configuration, Product Intent, Engineering, audits or implementation.

If model policy is **one of several independent tasks**, use the general
classification in `Routing Dimensions` and `Workflow Families` of
`references/common-workflows.md`. Keep the model-policy stage and every
other authorized stage in the complete workflow and handoff. An unclear
primary workflow uses the normal clarity gate, not an invented type.

Standalone model-policy output contains ONLY `Recommended first skill`,
`Expected complete workflow`, `Requested scope`, `Current handoff`,
`Stop after` and, where useful, `Why`, `Handoff`, and constraints.
It has NO `Classification`, `Operation`, `Clarity`, `Execution depth`
or `Audit scope` fields, including fictitious not-applicable values.

For routing advice use `route-only` and no handoff. For requested
configuration **or read-only review** use `end-to-end` and hand off to
`idd-factory-configure` when available; that scope does not itself permit
writing. Do not mechanically assign `intent-only`/`intent-preserving`
to a standalone configuration task: carry exact restrictions as constraints
or diagnose incompatible scope. No second router confirmation is required
for an already authorized exact edit; model selection retains configure's
own approval gates.

Preserve exact platform, profile, model ID, reasoning settings, inheritance,
review-only/write prohibition, goals and prior user decisions in the original
request handoff; never substitute a summary.

`idd-factory-configure` is in the separate `idd-factory` plugin.
A recommendation does not establish installation. If known unavailable,
keep the recommended skill but set `Current handoff: none` and stop with
the dependency diagnostic. If unknown, state availability conditionally,
not as a claimed installed capability. Never fabricate an availability API,
substitute another skill or write policy from the router.


## Apply Common Classification

For every route except the standalone Factory model-policy shortcut, classify
the supplied request using the REQUIRED `references/common-workflows.md`:
`Routing Dimensions` (`What Changes`, `Product and Engineering Operation`,
`Request Clarity`, `Requested Scope`, `Execution Depth`), `Shared
Invariants`, the applicable `Workflow Family`, and `Complete Workflow
and Current Handoff`. That reference defines the classification values,
add/modify/remove semantics, focused/orchestrated criteria, scope limits and
allowed Factory-only monotonic refinements. Do not redefine them here.

Return `Classification`, `Operation`, `Clarity`, `Execution depth`,
`Requested scope`, and `Audit scope` for every ordinary classified route.
Use `Operation: not-applicable` except for `product-change` and
`engineering-change`, where choose `add | modify | remove` from the
required reference. Use `Audit scope: not-applicable` except for
`intent-drift-audit`: `project-wide` only on explicit whole-project intent
audit; otherwise `focused` for a specified area or mismatch. The audit
skill, not this router, performs broad discovery.

Apply these specific decision gates while using the common classification:

- `verification-configuration` applies to deliberate creation/change of
  project-owned `.idd/verification.yaml`, not running checks or changing
  acceptance criteria. Configuration advice alone is `route-only`;
  actual configuration is `end-to-end`.
- `intent-bootstrap` applies to reconstruction of current Product Intent
  from existing implementation lacking adequate documentation, with its
  owner-confirmation gates. `intent-import` migrates supplied already
  expressed durable knowledge (including explicit accepted Engineering
  decisions), not reverse discovery from code or a new technical choice.
  Both have `Execution depth: not-applicable` even if discovery is broad.
- Explicit new specs, ADRs and product spikes go through
  `product-change` / `idd-intent-change` (or an ambiguity gate), not
  a new public document-creation skill. A durable Engineering policy change
  is `engineering-change` only when explicitly authoritative; a code
  pattern alone does not establish a Rule.
- `intent-lint`: "check"/"validate" means read-only `mode: check`;
  explicitly "check and fix"/unambiguous mechanical repair permits
  `mode: fix` only within authorized scope. Fixing the lint skill's CODE
  is `implementation-change`. Semantic duplicates go to
  `intent-structure-audit`; substantive splits/moves/deletions to
  `intent-normalization`; Glossary to the manual-only
  `idd-glossary-build`. Do not infer blanket semantic migration from
  "bring documents up to date".
- A bootstrap or import request may need its own later semantic approvals
  even when routing `Clarity` is `clear`. If the request is
  `ambiguous` or `research-required`, hand off to the appropriate
  discovery/spike gate and stop before unauthorized work.
- Select the narrowest `Requested scope` from the common reference,
  honoring explicit prohibitions. In particular `route-only` never
  executes, `intent-only` stops before implementation/Factory, and
  `intent-preserving` never permits a new normative Product/Engineering
  decision. An explicit ordinary `idd-factory-run` invocation bypasses
  this router but not its own mandatory entry Preflight.
- An explicitly invoked other skill (including `idd-skip`) bypasses
  routing entirely; do not invent route fields for that direct invocation.

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
Respect `intent-only` / `intent-preserving` / `end-to-end` scope and
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

Use `Complete Workflow and Current Handoff` and `Workflow Completion Rules`
in `references/common-workflows.md` to determine the complete lifecycle
and the current user-authorized boundary. Return all three distinctly:

- `Expected complete workflow`: the full safe lifecycle, including later
  stages that are not authorized *now*.
- `Current handoff`: the recommended next action permitted *now*;
  `none` for `route-only` or a blocking/dependency diagnostic.
- `Stop after`: the requested-scope limit, clarification/research gate,
  missing plugin, verification failure or successful authorized completion.

The router does NOT invoke its recommendation. The caller may continue within
an authorized `end-to-end` scope without requiring another routing approval,
subject to clarity and manual-only gates. Do not silently expand any scope.

Pass the **complete original request**, all independent tasks, explicit
prohibitions, the actual classification fields, and a relevant temporary
preservation/discovery boundary. No generated route summary replaces this
input. For bootstrap preserve inclusion/exclusion and known compatibility
context; that owner still obtains its own confirmations. For lint include
`mode: check | fix` and the exact write authorization. For
`intent-preserving` retain the approved Product/Engineering boundary.

A mixed Product + Engineering + implementation request keeps both durable
owners in the expected lifecycle before implementation. A mixed model-policy
+ implementation request keeps the `idd-factory-configure` stage as well;
do not collapse it to a standalone policy route. The common workflow controls
the sequence, and `idd-factory-run` performs its own mandatory Preflight
even after a direct invocation.

Examples of distinct routing decisions (not execution):

```text
"Does the code match this requirement?" -> intent-drift-audit, focused
"Audit all current Product Intent for drift" -> intent-drift-audit, project-wide
"Should these specs be split?" -> intent-structure-audit
```

Route results and boundaries are temporary evidence. This skill does not
create route documents, Factory state, Product Intent, Engineering or code.

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
