# idd-route

Use this skill to classify an IDD-related request and select the smallest safe
end-to-end workflow.

This skill is read-only. It does not change intent, implementation, Factory
state, or project files.

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
- intent-bootstrap
- intent-import
- product-change
- implementation-change
- intent-normalization
- intent-audit
- intent-lint
- implementation-intent-check
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

Use `intent-import` when existing documents or other source material already
express product knowledge that needs normalization into IDD. Do not route
codebase reverse discovery to import merely because code is a source.

For `product-change`, set `Operation` to `add`, `modify`, or `remove` according
to the required reference. For every other classification, set
`Operation: not-applicable`.

Set `Clarity` to `clear`, `ambiguous`, or `research-required`.

A bootstrap request may have `Clarity: clear` even though the product meaning is
not yet known: the requested workflow is clear, and
`idd-intent-bootstrap` contains its own semantic confirmation gates.

Set `Execution depth` to `focused`, `orchestrated`, or `not-applicable`
according to the required reference.

Use `not-applicable` for `intent-bootstrap` and `verification-configuration`.
Repository discovery may be broad, but it is intent-side investigation rather
than implementation orchestration and must not start Factory.

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
- `intent-only`: perform only intent-side work, including initialization,
  bootstrap, import, brainstorm, audit, lint, change, new-document, or
  normalization as applicable. Do not implement product code or start Factory
  execution.
- `implementation-only`: perform implementation or implementation checking from
  current intent. Do not change product intent. If current intent is missing,
  unclear, or wrong, stop and report the required intent workflow instead of
  expanding scope.
- `end-to-end`: continue through all requested workflow stages, subject to
  clarity gates and execution-depth selection.

A request to understand an existing project and create its initial intent is
normally `intent-only` unless it also explicitly asks for implementation
changes after bootstrap.

For `verification-configuration`, use `route-only` only when the user asks for
classification or advice without changing files; otherwise use `end-to-end`.

Do not assign route fields when another explicitly named skill or `idd-skip`
bypasses routing. Those cases are direct skill invocation, not route results.

## First Skill

Use the required reference to select the first skill. This compact table is only
a handoff index:

| Classification | Recommended first skill |
| --- | --- |
| `project-initialization` | `idd-project-init` |
| `verification-configuration` | `idd-verification-configure` |
| `intent-bootstrap` | `idd-intent-bootstrap` |
| `intent-import` | `idd-intent-import` |
| `product-change` | `idd-intent-change` |
| `implementation-change` | `idd-code-implement` or Factory |
| `intent-normalization` | `idd-intent-normalize-current` |
| `intent-audit` | `idd-intent-audit` |
| `intent-lint` | `idd-intent-lint` |
| `implementation-intent-check` | `idd-code-check-implementation` |
| `implementation-to-intent` | `idd-code-update-intent` |
| `explicit-skip` | `idd-skip` |
| `unclear` | `idd-intent-brainstorm` or a spike handoff |

Never select `idd-skip` automatically. Use it only when the user explicitly
refuses IDD for the request.

## Handoff Rules

Distinguish the complete workflow from the current handoff:

- `Expected complete workflow` describes the normal lifecycle needed to finish
  the request safely.
- `Current handoff` describes what may start in this user request.
- `Stop after` defines the requested-scope boundary or clarity gate.

The complete workflow is informative. It is not permission to execute stages
outside `Requested scope`.

Apply these rules:

- For `route-only`, set `Current handoff: none` and stop after returning the
  route.
- For `intent-only`, hand off only to the applicable intent-side skill and stop
  before implementation or Factory execution.
- For `implementation-only`, hand off only to the applicable code or check skill
  and do not modify intent.
- For `end-to-end`, continue with the recommended skill in the same user request
  when the Coding Agent can do so. Do not require a second user message only to
  confirm the route.
- When clarity is `ambiguous` or `research-required`, stop at the corresponding
  brainstorm, check, ADR, or spike gate even when the requested scope is
  `end-to-end`. Continue only after the missing decision or evidence exists.
- For `intent-bootstrap`, hand off the original scope and any include, exclude,
  temporary context, or known compatibility information. The bootstrap skill
  must still obtain its own project-boundary and semantic proposal
  confirmations before writing current intent.

Pass through the original request, classification fields, requested scope,
relevant context, and any temporary preservation or discovery boundary
identified from the required reference.

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

Recommended first skill: `idd-intent-change`
Expected complete workflow: `idd-intent-change -> idd-code-implement -> idd-code-check-implementation`
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
- Invoke the current handoff skill with the original request, route fields, and
  preservation or discovery boundary, or state that no handoff is allowed for
  `route-only`.
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
