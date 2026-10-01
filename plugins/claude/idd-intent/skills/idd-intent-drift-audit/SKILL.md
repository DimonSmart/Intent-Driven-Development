---
name: idd-intent-drift-audit
description: Audit whether current implementation behavior conforms to durable Product Intent in `.idd/intent/` for an explicitly defined focused or project-wide scope.
context: fork
agent: Explore
argument-hint: "[audit scope: feature, intent ID, implementation area, failure, or project-wide]"
allowed-tools: Read Glob Grep Bash
---

# idd-intent-drift-audit

Use this read-only skill to audit whether current implementation behavior still
conforms to current durable Product Intent in `.idd/intent/`.

Keep three independent dimensions:

```text
Product Intent conformance
Engineering Rule conformance
Verification evidence
```

Product Intent is normative. Implementation and verification are evidence.
The skill never silently promotes implementation into product truth and never
changes Product Intent, Engineering Rules, or implementation.

## Required Reference

Read `references/project-verification.md` before resolving verification checks
or repository/platform fallback.

Read `references/engineering-guardrails.md` before using an optional
`.idd/engineering/` layer.

## Explicit Audit Scope

`idd-intent-drift-audit` always works with an explicitly defined audit scope.

Focused scopes may identify a feature or behavior, an `IDD-NNNN` document, an
implementation area, or a failure/mismatch:

```text
editor search/replace behavior
IDD-0021
src/Auth
GET /api/users
OTP retry failure
```

Explicit project-wide scopes are also valid:

```text
project-wide
whole project
entire repository
all current product intent
```

A focused scope must not be silently widened to project-wide.

### Direct invocation without scope

If the user directly invokes this skill and scope cannot be determined from the
request, ask:

```text
Please specify the drift audit scope:
a feature or behavior, an IDD-NNNN intent document, an implementation area,
a failure/mismatch, or project-wide.
```

Do not start discovery until the scope is explicit.

### Invocation from another workflow

A caller such as `idd-code-implement` may pass an explicit derived focused
scope. In that case do not ask the user to repeat it. The caller scope should
cover changed implementation, affected current requirements, preservation
boundary, relevant Engineering Rules, and any applicable compatibility or
removed-behavior boundary.

## Rules

- Use only current `IDD-NNNN` documents directly under `.idd/intent/` as normative
  product intent.
- When `.idd/engineering/` exists, structurally validate it first, include all
  mechanically enumerated Always rules, and semantically select relevant
  Conditional rules from `Applies when`.
- Do not select Conditional Engineering Rules by filename, keyword, path,
  extension, glob, project type, embeddings, or similarity.
- Classify an Engineering Rule violation as an Engineering mismatch, not as
  missing product specification or product-intent mismatch.
- There is no `.idd/intent` archive lifecycle.
- Do not inspect deleted Git history unless the user explicitly asks for
  historical investigation.
- Do not treat implementation as product intent by itself.
- Do not update specifications unless the user explicitly confirms that the
  implementation represents current product intent.
- Do not change code unless the user explicitly asks for implementation changes.
- Do not classify every difference as a bug.
- Do not classify every implementation behavior as missing intent. `missing-intent` is only for durable observable product behavior, public or compatibility contracts, product-significant constraints, user-visible scenarios, or other durable externally meaningful behavior without a current Product Intent owner.
- Preserve the distinction between:

  - implementation drift;
  - missing verification;
  - unclear intent;
  - missing Product Intent ownership;
  - possible or confirmed intent change;
  - intentional non-goal.

- If Product Intent and implementation disagree, report the drift and propose
  the smallest safe next step.
- If a preservation boundary is provided, check changed behavior, removed
  behavior, preserved behavior, public contracts, compatibility or data
  constraints, and verification evidence.
- If intent is unclear, ask for confirmation or recommend a spike.
- If meaningful behavior or regression risk lacks adequate verification,
  recommend only the minimal high-value tests or checks needed to protect it.
- If current Product Intent is clear and implementation violates it, classify the
  issue as `implementation-drift`.
- If implementation behavior may represent a desired change, classify it as
  `possible-intent-change`, not as current product truth.

## Workflow

### Focused drift audit

1. Resolve the explicit focused scope.
2. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and the relevant current
   numbered Product Intent documents.
3. Determine the current intent owner for the scoped behavior.
4. Resolve applicable Engineering Rules using the policy below.
5. Gather focused implementation evidence: code, tests, runtime behavior, logs,
   build output, or the user-provided mismatch when relevant.
6. Use verification evidence when it materially helps determine conformance.
7. Compare implementation evidence separately with Product Intent and applicable
   Engineering Rules.
8. Classify findings and assign Evidence Scope.
9. Recommend the smallest next workflow.
10. Change nothing.

### Project-wide drift audit

Project-wide is a first-class explicit scope, but it is not a generic code
review. Start from Intent and build an intent-driven audit map.

Use breadth-first discovery:

1. Read `.idd/intent/README.md` and `.idd/intent/INDEX.md`.
2. Enumerate the complete current `IDD-NNNN` document set and ownership metadata.
3. On the first pass, use headings, Intent, Scope/Behavior, Related specs,
   Acceptance Criteria, and other information needed to build audit areas.
4. Map each current intent area to expected observable behavior or durable
   contracts, likely implementation evidence, and useful verification evidence.
5. Read full intent documents and implementation evidence area by area rather
   than loading the complete repository into one context.
6. Resolve applicable Engineering Rules separately for each audit area when
   needed.
7. Compare each area and record findings.
8. Separately identify durable observable implementation behavior for which no
   current Product Intent owner can be found.

The analysis direction is:

```text
Intent
-> expected observable behavior / durable contract
-> implementation evidence
-> verification evidence
-> comparison
```

Do not begin with random source scanning and search for a specification only
after something interesting is found.

Do not report `missing-intent` for private helpers, ordinary dependency usage,
internal refactoring structure, local naming conventions, private method
behavior, or incidental architecture shape unless that detail is itself Product
Intent or an applicable Engineering Rule.

A large project-wide audit may use multiple internal passes or sub-agents when
the host supports them, but the final result remains one `Intent Drift Audit`.

### Engineering Rule applicability

When `.idd/engineering/` exists, preserve the shared applicability algorithm:

```text
README
-> INDEX
-> mechanically enumerate every Always rule
-> semantically select relevant Conditional rules from Applies when
-> resolve selected ENG-NNNN IDs uniquely
-> read the full bodies of all Always plus selected Conditional rules
```

Do not introduce filename, keyword, path, extension, glob, project-type,
embedding, similarity, or other deterministic heuristics for Conditional
selection. For project-wide audits, determine relevant Conditional rules
separately for each audit area.

### Verification use

When repository commands are materially useful for a finding, resolve
`.idd/verification.yaml` using `references/project-verification.md`. Run only
checks allowed by that policy. Project-wide scope does not by itself authorize
running every repository check. Verification evidence is interpreted relative
to Product Intent; this skill does not create a verification engine.

## Output Format

Use this structure:

```md
# Intent Drift Audit

## Scope

Explicit audit scope and whether it is focused or project-wide.

## Relevant Current Intent

Current Product Intent used as normative evidence.

## Relevant Engineering Guardrails

Applicable Always rules and semantically selected Conditional rules, or
`None (Engineering layer absent)`.

## Audit Map

For project-wide audits, summarize intent ownership and implementation evidence.
For focused audits this section may be compact.

## Findings

### 1. Finding title

Classification: `matches-intent | implementation-drift | engineering-match | engineering-mismatch | missing-verification | missing-intent | unclear-intent | possible-intent-change | non-goal-or-out-of-scope`

Scope: `current-requirement | changed-requirement | preserved-requirement | removed-behavior | compatibility-boundary | unowned-behavior`

Evidence:
- Product Intent evidence:
- Engineering Rule evidence:
- Implementation evidence:
- Verification evidence:

Explanation:

Recommended next step:

## Summary

### Product Intent conformance
- Matches:
- Drift:
- Missing intent:
- Unclear / possible intent changes:

### Engineering Rule conformance
- Matches:
- Mismatches:

### Verification evidence
- Evidence present:
- Missing verification:

### Recommended actions
```

## Classification Rules

## Evidence Scope Rules

Use `current-requirement` for an ordinary check of current intent that is not
tied to a specific change.

Use `changed-requirement` for a requirement changed by the current work.

Use `preserved-requirement` for a requirement from a preservation boundary.

Use `removed-behavior` to verify behavior that should no longer exist.

Use `compatibility-boundary` for migration, legacy data, or compatibility
contracts.

Use `unowned-behavior` when implementation contains observable durable behavior
for which no current owning intent exists.

Do not use `current-requirement` for `missing-intent` when no current requirement exists. Use `unowned-behavior`.

When no change context is provided and a current requirement exists, use:

```text
Scope: current-requirement
```

### `matches-intent`

Use when implementation behavior satisfies current Product Intent.

Example:

```text
The spec requires session expiration after 30 days. The implementation expires
sessions after 30 days.
```

### `implementation-drift`

Use when current Product Intent is clear and implementation violates it.
Also use this classification when preserved behavior regresses during a change.

Example:

```text
The spec says mouse wheel scrolls the table. The implementation changes row
selection instead.
```

Recommended next step:

```text
Fix implementation or tests. Do not update the spec unless the user confirms the
implementation is the intended behavior.
```

### `engineering-match`

Use when implementation satisfies an applicable Engineering Rule.

### `engineering-mismatch`

Use when implementation violates an applicable `ENG-NNNN` rule. Cite that
rule and keep the finding in the Engineering conformance dimension. Do not
reclassify it as `missing-intent` or `implementation-drift` when Product Intent
does not own that implementation detail.

Example:

```text
ENG-0012 requires application services to be resolved through the DI container.
The implementation directly constructs another application service.

Classification: engineering-mismatch
```

### `missing-verification`

Use when implementation appears to satisfy spec, but an important user scenario,
critical invariant, meaningful boundary case, or real regression risk lacks
adequate evidence.

Do not classify `missing-verification` merely because a method or Product Intent
sentence lacks a dedicated test, when the behavior is trivial, or when a
higher-level automated scenario already covers the risk.

Example:

```text
The code appears to support OTP, but no test covers OTP failure retry behavior.
```

Recommended next step:

```text
Add or update only verification that proves meaningful behavior or regression
risk not already covered by a higher-level automated check.
```

### `missing-intent`

Use when implementation contains durable observable product behavior for which no
current Product Intent owner exists.

Use this classification with:

```text
Scope: unowned-behavior
```

Example:

```text
Password reset is implemented, but no current specification owns or describes
password-reset behavior.

Classification: missing-intent
Scope: unowned-behavior
```

Recommended next step:

```text
Ask whether this behavior is intended product intent. If the user describes a
desired behavior, use idd-intent-change. If the user confirms existing implementation
as intent, use idd-code-update-intent. If the decision is unclear, use brainstorm
or create a spike.
```

### `unclear-intent`

Use when the spec is ambiguous or incomplete.

Example:

```text
The spec says "mouse support" but does not define whether wheel changes
selection or scroll position.
```

Recommended next step:

```text
Ask for product decision or create a spike.
```

### `possible-intent-change`

Use when implementation differs from spec and may represent a deliberate product
change, but the user has not confirmed it.

Example:

```text
The spec says sessions expire after 30 days. Implementation uses 7 days. This
may be a security-driven product change, but it is not confirmed.
```

Recommended next step:

```text
Ask for explicit confirmation before updating specs.
```

### `non-goal-or-out-of-scope`

Use when the checked behavior is explicitly excluded or outside current spec
scope.

Example:

```text
The spec explicitly excludes offline mode. Missing offline behavior is not an
implementation bug.
```

## Examples

Focused:

```text
Use idd-intent-drift-audit to audit editor search/replace behavior against current intent.
```

Project-wide:

```text
Use idd-intent-drift-audit project-wide.
```

Direct invocation without a resolvable scope:

```text
Run idd-intent-drift-audit.
```

Ask for the audit scope rather than assuming focused or project-wide.

## Relationship To Other Skills

Use `idd-intent-change` when the user describes desired future product behavior before
implementation or wants to change current behavior.

Use `idd-code-implement` when current specs are clear and implementation should be
changed to match them.

Use `idd-intent-new-document` when durable product intent needs a new spec, ADR, or
spike.

Use `idd-code-update-intent` only when the user explicitly confirms
that verified implementation behavior represents current product intent.

Use `idd-intent-normalize-current` when existing intent should be moved to a better
location without changing meaning.

Use `idd-intent-drift-audit` when the task is to compare implementation with current Product Intent.

## Routing After Findings

- If current spec is clear and implementation violates it, recommend
  `idd-code-implement`.
- If user wants to change current behavior, recommend `idd-intent-change` before code
  changes.
- If implementation contains desired behavior not yet specified, recommend
  `idd-code-update-intent` only after explicit user confirmation.
- If product intent is missing and user describes desired behavior, recommend
  `idd-intent-change`, not `idd-intent-new-document`, unless no existing spec owns the area.

## Non-Goals

This skill does not:

- fix code automatically;
- update specs automatically;
- create new product intent from implementation;
- review code quality in general;
- perform generic code-quality, style, performance, or refactoring review;
- replace tests;
- replace code review;
- silently widen a focused audit to project-wide;
- inspect deleted history as current intent.
