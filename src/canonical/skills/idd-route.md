# idd-route

Use this skill to classify an IDD-related request and select the smallest safe
end-to-end workflow.

This skill is read-only. It does not change intent, implementation, Factory
state, or project files.

## Required Reference

Before classifying the request, read:

`references/common-workflows.md`

Treat that document as the canonical source for workflow families, product
operations, execution-depth selection, preservation boundaries, and completion
rules.

If the reference is unavailable, report that the installed plugin is incomplete
and do not reconstruct the full routing model from memory.

## Inputs

Accept natural-language user requests. The request may include a product area,
specification, code area, observed mismatch, or required result. Do not require
JSON or a special parameter structure.

## Context Reading Rules

First classify the request from its wording and the required reference.

Read project context only when needed to determine whether a current owner
exists, product truth changes, the problem is structural, implementation and
intent may diverge, or Factory is probably required.

When project context is needed:

1. Read `.idd/intent/README.md`.
2. Read `.idd/intent/INDEX.md`.
3. Read only relevant current numbered documents.
4. Do not load the whole intent tree.
5. Do not inspect Git history.
6. Do not perform broad code review.
7. Do not change files.

## Classification Fields

Return these semantic fields:

```text
Classification:
- project-initialization
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

For `product-change`, set `Operation` to `add`, `modify`, or `remove` according
to the required reference. For every other classification, set
`Operation: not-applicable`.

Set `Clarity` to `clear`, `ambiguous`, or `research-required`.

Set `Execution depth` to `focused`, `orchestrated`, or `not-applicable`
according to the required reference.

## First Skill

Use the required reference to select the first skill. This compact table is only
a handoff index:

| Classification | Recommended first skill |
| --- | --- |
| `project-initialization` | `idd-project-init` |
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

After routing a real request to a write-oriented workflow, continue with the
recommended skill in the same user request when the Coding Agent can do so.
Do not require a second user message only to confirm the route.

Pass through the original request, classification fields, relevant context, and
any temporary preservation boundary identified from the required reference.

The route classification is temporary workflow evidence. Do not create route
files, preservation records, Factory Work Plans, specs, or code from this skill.

## Output Format

Use compact Markdown, not JSON:

```md
# IDD Route

Classification: `product-change`
Operation: `modify`
Clarity: `clear`
Execution depth: `focused`

Recommended first skill: `idd-intent-change`
Expected workflow: `idd-intent-change -> idd-code-implement -> idd-code-check-implementation`

Preservation boundary:
- Behavior expected to change:
- Behavior expected to remain unchanged:
- Public contracts to preserve:
- Compatibility or data constraints:
- Unresolved preservation questions:

Why:
- Short routing rationale grounded in `references/common-workflows.md`.

Handoff:
- Invoke the recommended first skill with the original request and the
  preservation boundary.
```
