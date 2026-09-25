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
mechanical checks   -> bounded host operations
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

The allocator line has the exact form `^Next ID: ENG-\d{4}$`. Canonical bootstrap contains `Next ID: ENG-0001`. A valid legacy layer may omit the allocator for read-only consumption. On its first Engineering management mutation, derive `Next ID = max(current ENG IDs) + 1`, or `ENG-0001` when no Rules exist. Once introduced, the allocator is authoritative and deleted IDs are not reused. Do not use Git history to reconstruct pre-allocator deletions.

The `Rule` column contains only stable `ENG-NNNN` IDs.

The index may be used to enumerate Always rules and to discover candidate Conditional rules without loading all rule bodies. Mechanical validation must verify that index Applicability equals document Applicability.

## Engineering management

`idd-engineering-change`, owned by the `idd-intent` plugin, is the standard owner of ordinary project Engineering Rule `add`, `modify`, and `remove` mutations.

`idd-intent-import` has one narrow migration exception: it may create or update
Engineering Rules only when supplied import material already contains an
explicit durable current implementation-only decision. Import does not gain
general Engineering management authority and never removes a Rule merely because
the source omits it.

A Rule exists because the project explicitly decided that a durable implementation constraint must exist. Current code may be evidence, but a repeated implementation pattern is never authority to promote itself into Engineering policy.

```text
ordinary explicit Engineering add/modify/remove
-> idd-engineering-change
-> .idd/engineering/

explicit durable Engineering knowledge already present in supplied import sources
-> idd-intent-import
-> .idd/engineering/

both mutation paths
-> Mechanical Engineering Validation
```

The workflow performs semantic planning for the complete request before mutation. One request may contain one or more durable Engineering decisions; the model groups them into the minimum semantically coherent set of Rules, classifies each candidate as new, equivalent, modify, or ambiguous, and resolves modify/remove targets to exactly one current Rule. Semantic equivalence, Intent-versus-Engineering classification, architectural quality, and Conditional applicability remain model decisions; deterministic filename, keyword, embedding, or similarity heuristics must not impersonate them.

The Engineering layer is created lazily only after semantic planning confirms at least one new Rule and `.idd/engineering/` is completely absent, using packaged canonical bootstrap assets. `idd-project-init` does not create it. Planning, ambiguity detection, allocator-capacity checks, and all other pre-mutation gates happen before bootstrap. An existing malformed or partial layer blocks mutation and is never overwritten by bootstrap.

Before any mutation, `.idd/factory/current/request.md` is the canonical active simplified Factory marker. If it exists, block Engineering mutation and return `blocked`. The management workflow does not patch `plan.md`, rewrite `TaskRelatedEngineering`, restart Factory, re-plan automatically, or accept a hidden bypass. A stale `.idd/factory/current/` directory without `request.md`, or legacy `state.json` alone, is not an active-run marker.

A new Factory entry is a valid coordinator only before that marker exists. It
may identify `EngineeringManagementRequired` and invoke normal
`idd-engineering-change` with the complete logical request. Factory itself does
not map decisions to ENG owners, decide semantic equivalence, allocate IDs, edit
INDEX, or write Rules. `success` and `no-op` allow entry preflight to continue;
`ambiguous` and `blocked` stop before active Factory state.

If an explicit replacement request requires Engineering management while an
active marker exists, the replacement is blocked in this iteration before any
durable writes. Complete or cancel the current run, then start the complete
replacement request as a new run. Do not add a guard bypass, temporary
archive/unarchive trick, suspended lifecycle, or automatic cancel/restart.

An add request may create one or more coherent Rules, modify an existing semantic owner, or be a no-op. After the complete batch is planned, only new Rules consume IDs: allocate sequentially from current `Next ID` and set the final allocator to the first unused ID after the batch. Equivalent and modified candidates consume no IDs. If the required allocation would exceed `ENG-9999`, block before any mutation. Modify preserves stable ID while synchronizing INDEX projection. Remove deletes the Rule and INDEX row, preserves the allocator, and creates no archive or tombstone. If any candidate is ambiguous, mutate nothing. Git remains the only history layer.

Other IDD skills may read or validate Engineering and report candidates. Apart
from the bounded source-migration authority of `idd-intent-import`, they do not
independently create, modify, or remove Rules.

Engineering management changes durable knowledge, not implementation. If the user's request is end-to-end, implementation may follow through `idd-code-implement` or Factory and then `idd-code-check-implementation`. Existing consumers continue to apply every Always Rule plus semantically relevant Conditional Rules; management does not duplicate applicability logic.

## Engineering import migration

When `idd-intent-import` receives authoritative source material, it may migrate
only decisions already established by that source. Code structure, package
references, tests, runtime wiring, and repeated implementation patterns remain
evidence rather than authority.

Import classifies explicit decision groups as new, equivalent, modify-existing,
or ambiguous against current Rules. Equivalent decisions are no-op. An
unambiguous replacement preserves the semantic owner's stable `ENG-NNNN`.
Conflicting current policy is not superseded unless supplied context clearly
establishes replacement/current-truth semantics. Any real ambiguous Engineering
candidate makes the entire Engineering mutation batch atomic-no-change, although
independent Product Intent may still be imported.

Unconfirmed suggestions, alternatives, unresolved research, and historical
statements are review/skipped material, not mutating candidates. Import does not
perform ordinary Rule removal and does not modify `.idd/verification.yaml`.

The same pre-mutation invariants apply to import: validate an existing layer,
plan the full batch, check the Factory `request.md` guard and allocator
capacity, lazily materialize canonical packaged bootstrap only before a real new
Rule, migrate legacy allocator metadata only immediately before real mutation,
and validate the final layer mechanically.

## Mechanical Engineering Validation

When `.idd/engineering/` exists, validate it before using it as a normative source and after every Engineering management mutation. This section is the single canonical checklist for Engineering structural validation. Consumers may state when to run it or which subset is relevant, but must not maintain another full copy.

Mechanical Engineering Validation uses bounded repository/host operations only:

```text
read
list/glob
search/grep
exact string/regex match
count
compare
```

A short single-purpose host or shell command is acceptable when useful. Do not generate a general-purpose validator program, temporary `.ps1`, `.py`, `.sh`, `.cs`, validator executable, MCP validator, workflow engine, or equivalent inline multi-step program solely to reimplement IDD lint.

Validate:

1. `.idd/engineering/README.md` exists.
2. `.idd/engineering/INDEX.md` exists.
3. `.idd/engineering/archive` does not exist.
4. Every current Rule filename matches `^ENG-\d{4}\.rule-[a-z0-9][a-z0-9-]*\.md$`.
5. A stable ID is derived from the canonical Rule filename. Every current `ENG-NNNN` resolves to exactly one Rule document; arbitrary mentions of an ID inside prose do not count as extra documents.
6. Every INDEX `Rule` cell is exactly a plain `ENG-NNNN` matching `^ENG-\d{4}$`, appears exactly once, and resolves to exactly one current Rule document. Every current Rule document is represented exactly once in INDEX.
7. The first Markdown heading is exactly `# <filename stem>`.
8. Each Rule contains exactly one unambiguous structural section for `## Rule`, `## Applicability`, `## Rationale`, `## Guidance`, and `## Verification`.
9. Document Applicability is exactly `Always` or `Conditional`, and INDEX Applicability matches it.
10. Every Conditional Rule has a non-empty `## Applies when` section. For Always, that section is optional.
11. Explicit lifecycle sections such as `## Task`, `## Tasks`, `## Progress`, `## Implementation progress`, and `## Migration status`, plus implementation checklists in normative Engineering content, are rejected. Do not try to infer from ordinary prose whether it merely resembles a task.
12. Normative Engineering sections do not contain fenced shell/build/test command blocks. Operational commands belong in `.idd/verification.yaml`; `## Verification` describes required properties or evidence.
13. Allocator metadata may be absent only in a valid legacy read-only layer. If present, exactly one line matches `^Next ID: ENG-\d{4}$` and its ID is strictly greater than every current Rule ID.
14. Before a real management mutation, a valid legacy layer without allocator receives `max(existing ENG IDs) + 1`, or `ENG-0001` when no Rules exist. This migration occurs only after semantic planning and only when mutation will actually happen. A malformed, duplicate, or non-monotonic existing allocator is blocking and is not repaired automatically.
15. A mutation that would require an ID beyond `ENG-9999` is blocked before files are changed.

Malformed, missing, duplicate, ambiguous, or metadata-inconsistent rules are blocking errors for implementation workflows. Management reports success only after post-mutation validation passes.

Mechanical validation is structural only. It must not decide whether a rule belongs in Engineering rather than Intent, whether a Conditional rule applies to a task, whether Guidance is good architecture, or whether ordinary prose contains incidental implementation detail.

Warnings, not deterministic errors, are appropriate for suspicious implementation leakage such as concrete source filenames, private type names, constructor names, or incidental current layout. Semantic review decides whether such content is truly durable Engineering guidance.

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
