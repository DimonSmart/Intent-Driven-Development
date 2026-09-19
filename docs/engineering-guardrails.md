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

## Rule format

Rules use stable `ENG-NNNN` identifiers and canonical filenames such as:

```text
ENG-0001.rule-ui-composition.md
```

A rule contains:

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

`Applicability` is only `Always` or `Conditional`.

An Always rule applies to every implementation task. A Conditional rule is selected semantically from its human-readable `Applies when` description.

Do not put build or test commands in rule documents. Commands belong in `.idd/verification.yaml`.

## INDEX discovery

`.idd/engineering/INDEX.md` is a compact discovery projection:

```markdown
| Rule | Applicability | Applies when | Summary |
| --- | --- | --- | --- |
| ENG-0001 | Always | Every implementation task | No mutable global state |
| ENG-0010 | Conditional | User-visible UI changes | Use shared UI composition |
```

Read INDEX first. Do not load the complete Engineering tree by default.

If the Engineering layer exists, its filenames, stable IDs, headings, INDEX mapping, Applicability metadata, and required sections must be mechanically valid before implementation proceeds. Missing or ambiguous rules are blocking diagnostics rather than permission to ignore guardrails.

## Direct implementation

`idd-code-implement` applies:

```text
relevant product intent
+ every Always Engineering Rule
+ semantically relevant Conditional Engineering Rules
+ verification policy
```

Always rules are enumerated mechanically. Conditional rules are selected by the model. IDD deliberately does not use filename, keyword, path, extension, project type, glob, embedding, or similarity heuristics for Conditional applicability.

`idd-code-check-implementation` uses the same rule selection policy and reports separately:

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

The rule states what evidence matters. `.idd/verification.yaml` states how project commands obtain that evidence.

## History and lifecycle

Engineering Rules describe current target state. There is no Engineering archive. Git stores previous versions.

Do not automatically generate rules from current code and do not automatically migrate existing Intent into Engineering.
