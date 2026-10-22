# Engineering Guardrails

Engineering Guardrails are an optional IDD layer for durable implementation rules that should remain true across future changes without becoming product intent.

```text
.idd/
    intent/             product truth
    engineering/        durable implementation guardrails, optional
    verification.yaml   operational verification policy, optional
    factory/            temporary Factory state, optional
```

A project without `.idd/engineering/` works exactly as before. `idd-project-init` does not create this directory automatically.

## Intent or Engineering?

Put a constraint in `.idd/intent/` when it defines product behavior, an externally observable property, a public/domain/compatibility contract, or another product-significant property.

Put it in `.idd/engineering/` when it is an implementation-only durable constraint used to preserve architecture, consistency, maintainability, or engineering conventions.

A useful question is:

> Could another implementation completely preserve the product contract and still be forbidden by this rule?

If yes, it usually belongs to Engineering.

For example:

```text
All dialogs have consistent keyboard behavior.
    -> Intent

All dialogs obtain keyboard behavior from the shared Dialog component.
    -> Engineering
```

ADRs may remain in `.idd/intent/` and explain why an Engineering Rule exists.

## Implementation choice is not automatically Engineering policy

Factory may independently choose a compatible internal algorithm, library,
component layout or new-app configuration value, even when the decision
will last years. It needs no explicit "choose yourself" instruction and
does not add an Engineering Rule merely because the implementation works.
An obligation that *all future components* use the same technology
requires an explicit authoritative policy and `idd-engineering-change`.
A technical ADR may explain rationale but does not create policy by itself.

For a new macOS app, Factory can select a consistent `CFBundleIdentifier`
without creating a Rule. Changing a distributed identity or established
permissions, registering an external identifier, accepting license terms,
or choosing a new Developer ID/notarization trust policy may instead
need specific product/policy authority. Delegation cannot waive existing
Intent or Engineering Rules or authorize external commitments.

For example, a Conditional Rule might require special protection of
secrets **when a local database is used**. If Planner leaves database
and non-database approaches open to Worker, it must include the relevant
Conditional Rule ID in `TaskRelatedEngineering` before Worker execution
(or choose/research a narrower approach). A Conditional Rule in the
assignment applies only when its condition holds. Worker may not
discover or edit additional Rule IDs; insufficient coverage is a
blocker returned to Factory for fresh planning, not permission to ignore
the Rule.

## Managing Engineering Rules

Use `idd-engineering-change` for ordinary explicit durable Rule add, modify,
or remove operations. It is the standard mutation owner inside the
`idd-intent` plugin.

`idd-intent-import` has a narrow source-migration exception: when supplied
authoritative material already states an accepted current durable
implementation-only decision, import may create or update the corresponding
Rule without repeating confirmation. Suggestions, alternatives, unresolved
research, and historical statements are not decisions. A current code pattern,
package reference, test, or repeated implementation structure is evidence, not
authority to create policy.

Import never removes a Rule merely because the source omits it. A conflicting
source statement updates an existing semantic owner only when replacement/current
truth is explicit; otherwise the Engineering batch remains unchanged and the
conflict is reported.

One request may contain multiple durable Engineering decisions. The complete semantic batch is planned before files change; independent decisions remain separate Rules, while naturally coherent decisions may share a Rule. The layer is created lazily only when that plan requires the first real add. `idd-project-init` still leaves it absent.

A new Factory entry may invoke normal `idd-engineering-change` during
preflight and during the initial Planner question/answer cycle in
`.idd/factory/preparation/`, while `.idd/factory/current/request.md` does
not exist. The handoff carries the full original request and relevant exact Q/A;
Factory itself does not map ENG owners, decide equivalence, allocate IDs, or
write Rules. A valid first Task batch is prepared BEFORE writing the active
marker last. Pending preparation or an orphaned plan is not active state.
After active `request.md` exists, Engineering mutation is blocked, even
before the first Worker. An Engineering-changing replacement or later active
Planner answer cannot bypass the guard: complete/cancel and start a new
complete request.

`INDEX.md` carries `Next ID: ENG-NNNN`. Only new Rules consume sequential IDs; modifications, no-ops, and removals consume none. Valid legacy layers without allocator remain readable and acquire `max(current IDs) + 1` only immediately before their first real management mutation. Structural validation uses the shared Mechanical Engineering Validation checklist directly rather than routing Engineering management through another skill.

Engineering management updates durable knowledge only. Implementation follows separately when the same request explicitly asks for code changes.

## Rule format

Rules use stable `ENG-NNNN` identifiers and canonical filenames such as:

```text
ENG-0001.rule-ui-composition.md
```

A full recommended example (not a required list of sections):

```markdown
# ENG-0001.rule-ui-composition

## Rule

Use shared UI composition and design tokens.

## Applicability

Conditional

## Applies when

- creating or modifying user-visible UI

## Rationale

Why this constraint exists.

## Guidance

Implementation guidance and allowed approaches.

## Verification

Properties or evidence that demonstrate conformance.
```

The minimal valid Always Rule contains a non-empty `## Rule` and one
`## Applicability` with value `Always`, along with its file/H1/INDEX
identity. A Conditional Rule additionally needs a non-empty `## Applies when`.
`## Rationale`, `## Guidance` and `## Verification` are optional, and may
not be ambiguously duplicated. Missing optional sections are neither errors
nor warnings; their absence does not waive Rule conformance.

`Applicability` is only `Always` or `Conditional`.

An Always rule applies to every implementation task. A Conditional rule is selected semantically from its human-readable `Applies when` description.

Do not put build or test commands in rule documents. Commands belong in `.idd/verification.yaml`.

## INDEX discovery

`.idd/engineering/INDEX.md` is a compact discovery projection:

```markdown
Next ID: ENG-0003
| Rule | Applicability | Applies when | Summary |
| --- | --- | --- | --- |
| ENG-0001 | Always | Every implementation task | No mutable global state |
| ENG-0010 | Conditional | User-visible UI changes | Use shared UI composition |
```

Read INDEX first. Do not load the complete Engineering tree by default.

If the Engineering layer exists, its filenames, stable IDs, H1, INDEX mapping,
Applicability metadata and *minimal* required structural sections must be
mechanically valid before implementation proceeds. A missing optional section
from the expanded example does not block implementation. Missing or ambiguous rules are blocking diagnostics rather than permission to ignore guardrails.

## Direct implementation

`idd-code-implement` applies:

```text
relevant product intent
+ every Always Engineering Rule
+ semantically relevant Conditional Engineering Rules
+ verification policy
```

Always rules are enumerated mechanically. Conditional rules are selected by the model. IDD deliberately does not use filename, keyword, path, extension, project type, glob, embedding, or similarity heuristics for Conditional applicability.

`idd-intent-drift-audit` uses the same rule selection policy and keeps Engineering conformance separate from Product Intent drift and verification evidence:

```text
Product intent conformance
Engineering guardrail conformance
Verification evidence
```

## Factory integration

The planner may attach only selected Conditional rules:

```text
# Task

Implement Settings dialog.

# TaskRelatedIntent

IDD-0042

# TaskRelatedEngineering

ENG-0010
```

The planner never adds Always rules to `TaskRelatedEngineering`.

Immediately before each worker execution, the orchestrator deterministically enumerates the current Always set and provides it separately:

```text
Task
TaskRelatedIntent
TaskRelatedEngineering
AlwaysEngineering
```

This set is recomputed before every worker execution so the worker receives the current rules.

The worker resolves and reads all supplied Intent and Engineering documents before implementation. It does not search for extra Conditional rules and does not edit Engineering Rules as part of an implementation task.

Engineering support does not add a Factory runtime, MCP transport, workflow state machine, or process supervisor.

## Verification

Engineering Rules may describe evidence that cannot be fully automated. Where a property can be checked reliably, prefer deterministic verification such as architecture tests or analyzers.

A Rule may state what evidence matters, including in an optional
`## Verification` section. `.idd/verification.yaml` states how project commands
obtain that evidence; absence of the optional section does not waive conformance.

## History and lifecycle

Engineering Rules describe current target state. There is no Engineering archive. Git stores previous versions.

Do not automatically generate rules from current code and do not automatically migrate existing Intent into Engineering.
