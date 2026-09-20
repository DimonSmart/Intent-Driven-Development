# Engineering Guardrails

Engineering Guardrails are optional project-owned durable implementation constraints stored under `.idd/engineering/`.

They are separate from product intent, verification configuration, and Factory state.

```text
Intent
    What the system must do and which product properties must remain true.
Engineering
    Which durable implementation constraints and conventions future implementations must obey.
Verification
    How mechanically obtainable evidence is produced.
Factory
    How one implementation change is organized.
```

The architectural rule is:

```text
semantic decisions -> model
mechanical operations -> deterministic procedure / host capability
```

Do not introduce a Factory runtime merely to support Engineering Rules.

## Intent / Engineering boundary

Use `.idd/intent/` for product behavior, externally observable properties, public/domain/compatibility contracts, product-significant operational constraints, and ADR rationale.

Use `.idd/engineering/` for implementation-only durable constraints that preserve architecture, consistency, maintainability, or engineering conventions.

Ask:

> Could another implementation completely preserve the product contract and still be forbidden by this rule?

If yes, the constraint normally belongs to Engineering.

Examples:

- Consistent keyboard behavior across dialogs is product intent.
- Requiring dialogs to obtain that behavior from a shared Dialog component is Engineering.
- Bounded memory use for large files remains Intent when it is a product or operational contract.
- Application services resolved through the DI container is Engineering.

Do not automatically migrate existing intent between layers.

ADRs may remain in `.idd/intent/` and explain why an Engineering Rule exists. The ADR owns rationale and decision history; the Engineering Rule owns the current normative implementation constraint.

## Optional project layout

Absence of `.idd/engineering/` is valid and produces no warning.

When the directory exists, it contains current rules only:

```text
.idd/engineering/
    README.md
    INDEX.md
    ENG-0001.rule-ui-composition.md
    ENG-0002.rule-services.md
```

There is no Engineering archive. Git stores previous rule versions.

Engineering Rules are not Factory state, verification commands, migration plans, task status, or generated descriptions of the current implementation.

## Rule identity and format

Stable identifiers use:

```text
ENG-NNNN
```

Canonical filenames match:

```text
^ENG-\d{4}\.rule-[a-z0-9][a-z0-9-]*\.md$
```

The first heading must equal the filename stem.

Canonical rule structure:

```markdown
# ENG-0001.rule-ui-composition

## Rule

A concise normative implementation rule.

## Applicability

Conditional

## Applies when

- creating or modifying user-visible UI

## Rationale

Why this durable constraint exists.

## Guidance

Practical implementation constraints and allowed approaches.

## Verification

Properties or evidence that should demonstrate conformance.
Do not put build or test commands here.
```

`Applicability` is exactly one of:

- `Always`
- `Conditional`

A Conditional rule requires a non-empty `## Applies when` section. For Always, `## Applies when` is optional and never changes applicability.

Rules describe current target state, not migration history or implementation progress.

## Engineering index

`.idd/engineering/INDEX.md` is a compact discovery projection. Full rule documents remain normative.

Canonical allocator metadata and columns are:

```markdown
Next ID: ENG-0003
| Rule | Applicability | Applies when | Summary |
| --- | --- | --- | --- |
| ENG-0001 | Always | Every implementation task | No mutable global state |
| ENG-0002 | Conditional | User-visible UI changes | Use shared UI composition |
```

The allocator line has the exact form `^Next ID: ENG-\\d{4}$`. Canonical bootstrap contains `Next ID: ENG-0001`. A valid legacy layer may omit the allocator for read-only consumption. On its first Engineering management mutation, derive `Next ID = max(current ENG IDs) + 1`, or `ENG-0001` when no Rules exist. Once introduced, the allocator is authoritative and deleted IDs are not reused. Do not use Git history to reconstruct pre-allocator deletions.

The `Rule` column contains only stable `ENG-NNNN` IDs.

The index may be used to enumerate Always rules and to discover candidate Conditional rules without loading all rule bodies. Mechanical validation must verify that index Applicability equals document Applicability.

## Engineering management

`idd-engineering-change`, owned by the `idd-intent` plugin, is the standard owner of project Engineering Rule `add`, `modify`, and `remove` mutations.

A Rule exists because the project explicitly decided that a durable implementation constraint must exist. Current code may be evidence, but a repeated implementation pattern is never authority to promote itself into Engineering policy.

```text
explicit durable engineering decision
-> idd-engineering-change
-> .idd/engineering/
-> idd-intent-lint structural validation
```

The workflow resolves semantic ownership before add and resolves modify/remove targets to exactly one current Rule. Semantic equivalence, Intent-versus-Engineering classification, architectural quality, and Conditional applicability remain model decisions; deterministic filename, keyword, embedding, or similarity heuristics must not impersonate them.

The Engineering layer is created lazily only by the first add when `.idd/engineering/` is completely absent, using packaged canonical bootstrap assets. `idd-project-init` does not create it. An existing malformed or partial layer blocks mutation and is never overwritten by bootstrap.

Before any mutation, `.idd/factory/current/request.md` is the canonical active simplified Factory marker. If it exists, block Engineering mutation until that run is completed, cancelled, or explicitly restarted/replanned. The management workflow does not patch `plan.md`, rewrite `TaskRelatedEngineering`, restart Factory, or re-plan automatically. A stale `.idd/factory/current/` directory without `request.md`, or legacy `state.json` alone, is not an active-run marker.

Add consumes exactly current `Next ID`, creates one Rule, advances the allocator, updates INDEX, and validates structure. An equivalent existing Rule is a no-op and does not allocate. Modify preserves stable ID and allocator while synchronizing INDEX projection. Remove deletes the Rule and INDEX row, preserves allocator, and creates no archive or tombstone. Git remains the only history layer.

Other IDD skills may read or validate Engineering, report candidates, and hand explicitly confirmed candidates to `idd-engineering-change`; they do not independently create, modify, or remove Rules.

Engineering management changes durable knowledge, not implementation. If the user's request is end-to-end, implementation may follow through `idd-code-implement` or Factory and then `idd-code-check-implementation`. Existing consumers continue to apply every Always Rule plus semantically relevant Conditional Rules; management does not duplicate applicability logic.

## Mechanical structural validation

When `.idd/engineering/` exists, validate it before using it as a normative source. This validation is deterministic and must not make semantic applicability decisions.

Validate:

1. `README.md` exists.
2. `INDEX.md` exists.
3. No `.idd/engineering/archive` directory exists.
4. Every rule filename matches the canonical regex.
5. Each stable `ENG-NNNN` occurs in exactly one rule document.
6. Every INDEX Rule entry is a plain `ENG-NNNN`, appears once, and resolves to exactly one rule document.
7. Every rule document is represented exactly once in INDEX.
8. The first heading equals the filename stem.
9. Required `## Rule`, `## Applicability`, `## Rationale`, `## Guidance`, and `## Verification` sections exist.
10. Applicability is exactly `Always` or `Conditional`.
11. INDEX Applicability equals document Applicability.
12. Every Conditional rule has a non-empty `## Applies when`.
13. Explicit task/progress lifecycle sections such as `## Task`, `## Tasks`, `## Progress`, `## Implementation progress`, `## Migration status`, or implementation checklists are rejected.
14. Normative Engineering sections do not contain fenced shell/build/test commands. Build and test commands belong in `.idd/verification.yaml`.
15. Allocator metadata is optional only for a legacy read-only layer. If present, exactly one line matches `^Next ID: ENG-\\d{4}$` and its ID is greater than every current `ENG-NNNN`.
16. A management mutation requires allocator metadata: a valid legacy layer receives the migration described above before mutation; malformed, duplicated, or non-monotonic existing allocator metadata is a blocking error.

Malformed, missing, duplicate, ambiguous, or metadata-inconsistent rules are blocking errors for implementation workflows. Do not silently continue without guardrails.

Do not load every complete rule into model context merely to perform this validation when a host or deterministic file operation can check structure directly.

Warnings, not deterministic errors, are appropriate for suspicious implementation leakage such as concrete source filenames, private type names, constructor names, or incidental current layout. Semantic review decides whether such content is truly durable Engineering guidance.

Mechanical validation must not pretend to decide:

- whether a rule belongs in Engineering rather than Intent;
- whether a Conditional rule applies to a task;
- whether Guidance is good architecture;
- whether a rule is fully free of incidental implementation detail.

## Rule discovery and applicability

Always rules are enumerated, not selected.

For an implementation workflow:

```text
README
-> INDEX
-> mechanically enumerate all Always ENG IDs
-> model selects semantically relevant Conditional ENG IDs from Applies when
-> resolve selected IDs to unique current rule files
-> read full bodies only for Always + selected Conditional rules
```

The model owns semantic applicability for Conditional rules. It must not select them automatically by filename, keyword, source path, changed path, file extension, project type, glob, embedding, similarity score, or another deterministic heuristic.

The host/orchestrator owns mechanical enumeration of Always rules and stable-ID resolution.

Stable resolution is:

```text
ENG-NNNN -> exactly one .idd/engineering/ENG-NNNN.rule-*.md
```

Missing or ambiguous resolution is a blocking diagnostic.

## Direct implementation

`idd-code-implement` applies:

- relevant product intent;
- every Always Engineering Rule;
- model-selected relevant Conditional Engineering Rules;
- the project verification policy.

If the Engineering layer is absent, behavior is unchanged.

If it exists but structural validation fails, implementation stops before code changes.

## Implementation conformance

`idd-code-check-implementation` discovers Engineering Rules with the same policy as direct implementation: every Always rule plus semantically relevant Conditional rules.

Report three dimensions separately:

```text
Product intent conformance
Engineering guardrail conformance
Verification evidence
```

A violation of an Engineering Rule is an Engineering mismatch, not missing product intent.

## Factory planner protocol

The planner may emit, immediately after a Task:

```text
# TaskRelatedEngineering

ENG-0002
ENG-0003
```

This section is optional and contains only planner-selected Conditional rules. It contains only stable `ENG-NNNN` IDs and never contains Always rules.

Planner discovery is:

```text
.idd/engineering/README.md
-> .idd/engineering/INDEX.md
-> candidate Conditional rules
-> relevant full rules only when needed for correct task decomposition
```

The planner may read a particular Always rule when its content is needed to construct a correct task contract, but it does not decide whether the rule applies and does not emit it in `TaskRelatedEngineering`.

## Factory Always propagation

Before every worker execution, the orchestrator/host deterministically re-reads the current Engineering INDEX, validates current metadata, enumerates every `Applicability = Always` ID, and supplies that set separately.

Logical worker input is:

```text
Task
TaskRelatedIntent
TaskRelatedEngineering
AlwaysEngineering
```

`AlwaysEngineering` is not planner output and does not need to be persisted in `plan.md`. Recompute it before each worker so the current rule version and current Always set apply at execution time.

This is a protocol operation, not a reason to add `idd-factory.dll`, an MCP transport, a state machine, a process supervisor, or another Factory runtime.

## Factory worker protocol

Before implementation, the worker reads:

1. all TaskRelatedIntent documents;
2. all TaskRelatedEngineering documents;
3. all AlwaysEngineering documents.

For each Engineering ID, require exactly one current rule file and matching INDEX/document metadata. TaskRelatedEngineering entries must be Conditional. AlwaysEngineering entries must be Always.

If an ID is missing, ambiguous, malformed, or metadata-inconsistent, do not execute the task. Return a short blocking diagnostic.

The worker does not search for additional Conditional rules and does not edit Engineering Rules as part of an implementation task.

## Verification separation

Engineering Rule `## Verification` states the property or evidence required for conformance. It does not contain repository commands.

Operational commands remain in:

```text
.idd/verification.yaml
```

Prefer deterministic verification when a rule can be reliably checked in code, architecture tests, analyzers, or another mechanical check.

## Shared and cross-cutting product intent

Engineering Rules do not replace shared product intent.

Intent discovery metadata in `.idd/intent/INDEX.md` should make shared/cross-cutting product behavior discoverable through `Area`, `Notes`, or equivalent metadata without requiring a schema migration.

Examples include shared UI behavior, keyboard interaction, accessibility, common validation behavior, and product-wide compatibility guarantees.

Factory planners must consider relevant cross-cutting Intent when choosing TaskRelatedIntent.

## Backward compatibility

Projects without `.idd/engineering/` behave exactly as before.

Existing intent is never moved automatically. `.idd/plugins.json` does not require an Engineering plugin. `.idd/verification.yaml` remains separate operational configuration. Factory state remains lightweight Markdown.
