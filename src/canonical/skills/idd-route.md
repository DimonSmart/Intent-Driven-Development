# idd-route

Use this skill to classify an IDD-related request and select the smallest safe
end-to-end workflow.

This skill is read-only. It does not change intent, implementation, Factory
state, or project files.

## Inputs

Accept natural-language user requests. The request may include a product area,
specification, code area, observed mismatch, or required result. Do not require
JSON or a special parameter structure.

## Context Reading Rules

First classify the request from its wording.

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

## Classification

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

For `product-change`, set:

```text
Operation:
- add
- modify
- remove
```

For all other classifications, set:

```text
Operation: not-applicable
```

Set clarity as:

```text
Clarity:
- clear
- ambiguous
- research-required
```

Set execution depth as:

```text
Execution depth:
- focused
- orchestrated
- not-applicable
```

## Execution Depth

Choose `focused` when the change has one main product owner, localized
implementation, no complex migration, no dependent phases, no multiple review
gates, and can be handled safely by one focused implementation workflow.

Choose `orchestrated` when the request involves multiple subsystems, multiple
independent implementation tasks, data or settings migration, compatibility
transition, public contract changes, high regression risk, ordered stages,
multiple roles or review gates, major capability removal, or architecture work
that cannot be safely executed as one focused change.

Diff size alone is not a sufficient reason to choose Factory.

## Product Change Routing

Treat adding, modifying, and removing behavior as variants of one
`product-change` workflow:

```text
idd-intent-change(operation: add|modify|remove)
-> idd-code-implement or Factory
-> idd-code-check-implementation
```

Do not decide document ownership from the operation alone. `idd-intent-change`
separately determines whether the outcome is an existing-spec update, new spec,
ADR, spike, owning spec deletion, or unclear intent.

## Implementation Change Routing

For refactoring, dependency replacement, internal cleanup, private type
movement, algorithm replacement, performance work, or migration with no
observable behavior change:

```text
read relevant intent
-> idd-code-implement(mode: preserve-current-intent) or Factory
-> idd-code-check-implementation
```

If the request requires product behavior to change, route to
`idd-intent-change` instead of implementation-only work.

## Intent Maintenance Routing

Use `idd-intent-audit` for broad or unclear structural problems.

Use `idd-intent-normalize-current` for focused normalization that does not
change product meaning, followed by `idd-intent-lint`.

Use `idd-intent-lint` for mechanical consistency checks.

## Additional Routes

- Project initialization: `idd-project-init`.
- Import existing product knowledge: `idd-intent-import`.
- Unclear desired product behavior: `idd-intent-brainstorm`.
- Possible implementation/spec mismatch: `idd-code-check-implementation`.
- Confirmed implementation behavior should become intent:
  `idd-code-update-intent`.
- Explicit refusal of IDD for this request: `idd-skip`.

Never select `idd-skip` automatically.

## Bug Routing

Route bug reports by implementation relationship to current intent:

- Clear intent and violating implementation: start with
  `idd-code-check-implementation`.
- Implementation matches current intent but the user wants different behavior:
  route to `idd-intent-change(operation: modify)`.
- Correct intent is unclear: start with `idd-code-check-implementation`, then
  route to `idd-intent-brainstorm` or a spike.

Bug is not a separate top-level workflow.

## Preservation Boundary

Identify a temporary preservation boundary:

```text
Behavior expected to change:
Behavior expected to remain unchanged:
Public contracts to preserve:
Compatibility or data constraints:
Unresolved preservation questions:
```

Do not invent product truth. If the boundary cannot be determined from current
intent or the user request, mark it unresolved and choose
`idd-intent-brainstorm`, `idd-code-check-implementation`, or a spike instead of
direct implementation.

Do not save the preservation boundary as a standalone `.idd/intent/` document.
Durable preserved contracts belong in ordinary acceptance criteria,
constraints, behavior, verification, or non-goals of the owning current spec.

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
- Short routing rationale.

Handoff:
- Invoke the recommended first skill with the original request and the
  preservation boundary.
```

## Handoff Rules

After routing a real request to a write-oriented workflow, continue with the
recommended skill in the same user request when the Coding Agent can do so.
Do not require a second user message only to confirm the route.

The route classification is temporary workflow evidence. Do not create route
files, preservation records, Factory Work Plans, specs, or code from this skill.
