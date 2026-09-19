# idd-code-implement

Use this skill when the user asks to implement behavior that is already
specified, when `idd-intent-change` has just updated the relevant spec, or when
the user asks for implementation-only work that must preserve current intent.

Formula:

```text
idd-code-implement = current spec intent + mode + code change + verification
```

## Required Reference

Read `references/project-verification.md` before resolving verification checks
or repository/platform fallback.

Read `references/engineering-guardrails.md` before using an optional
`.idd/engineering/` layer.

## Modes

```text
satisfy-current-intent
preserve-current-intent
```

Use `satisfy-current-intent` after a product change or when implementation must
be brought into conformance with existing current intent.

Use `preserve-current-intent` for refactoring, dependency replacement, internal
architecture cleanup, private type split or merge, implementation algorithm
replacement, internal performance work, or implementation migration without
observable behavior change.

## Rules

- Current `.idd/intent/` documents are the source of product intent.
- Do not implement durable product behavior that is missing from specs.
- If the request changes product behavior and specs are not updated yet, use
  `idd-intent-change` first.
- Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant current specs.
- Do not read the whole `.idd/intent/` directory by default.
- When `.idd/engineering/` exists, validate its structure before code changes,
  enumerate every Always rule mechanically, and select relevant Conditional
  rules semantically from `Applies when`.
- Do not read the whole Engineering rule set by default. Full rule bodies are
  loaded only for all Always rules and selected Conditional rules.
- Missing, duplicate, ambiguous, malformed, or INDEX-inconsistent Engineering
  rules are blocking diagnostics. Do not silently implement without guardrails.
- Never select Conditional Engineering Rules by filename, keyword, path,
  extension, glob, project type, embeddings, or similarity.
- Do not copy implementation plans or temporary notes into specs.
- Prefer the smallest code change that satisfies the relevant acceptance
  criteria.
- In `preserve-current-intent` mode, do not edit intent.
- In `preserve-current-intent` mode, read relevant current intent before
  changing code and treat the preservation boundary as a temporary
  implementation constraint.
- Add or update only the minimal high-value verification needed when existing
  checks are not enough to protect meaningful preserved behavior or regression
  risk.
- If implementation requires a product behavior change, stop
  `preserve-current-intent` and use `idd-intent-change`.
- Do not update intent after the fact to justify an accidental behavior change.
- Before adding a test, check whether the behavior is already covered, whether an
  existing scenario can be extended, whether the logic or risk is non-trivial, and
  whether omitting the test would materially reduce regression detection.
- Prefer a higher-level automated scenario that covers several lower-level details
  over separate tests for each method or specification sentence.
- Use `.idd/verification.yaml` context `direct` when it exists: resolve checks for
  the actual changed paths, run assigned automatic checks, request required
  confirmation, and record IDs, commands, and results. User instructions stay
  `Not verified` until confirmed. Without the file, use and report the
  repository/platform fallback.
- After implementation, perform a focused implementation/spec check using
  `idd-code-check-implementation`.

## Engineering Guardrails

When `.idd/engineering/` is absent, continue exactly as before.

When it exists:

1. Read `.idd/engineering/README.md` and `INDEX.md`.
2. Perform the cheap deterministic structural validation defined in
   `references/engineering-guardrails.md`. Stop on structural errors.
3. Mechanically enumerate all INDEX entries with `Applicability = Always`.
4. Let the model select only semantically relevant Conditional rules from their
   human-readable `Applies when` metadata.
5. Resolve each selected `ENG-NNNN` to exactly one current rule document and
   read the full bodies of all Always plus selected Conditional rules.
6. Treat those rules as normative implementation constraints together with
   relevant product intent and verification policy.

Always rules are enumerated, not semantically selected.

## Workflow

1. Classify mode:

   - `satisfy-current-intent`;
   - `preserve-current-intent`.

2. Read relevant current intent.
3. Resolve optional Engineering Guardrails as defined above.
4. For preserve mode, establish or accept the preservation boundary.
5. Locate implementation and verification areas.
6. Apply the smallest safe implementation change while satisfying relevant
   Intent and Engineering Rules.
7. Add or update only the minimal high-value verification needed for meaningful
   behavior or regression risk.
8. Run relevant verification.
9. Run focused `idd-code-check-implementation`.
10. Report the required implementation result fields.

## Missing Spec Rule

In `satisfy-current-intent`, if the requested durable behavior is not covered by
current specs:

```text
Stop before implementation and use idd-intent-change.
```

Do not silently implement new durable behavior without updating product intent
first.

In `preserve-current-intent`, a missing specification for private
implementation structure is not an error. Preserve mode may proceed when current
intent sufficiently defines the observable behavior and durable contracts that
must remain unchanged.

If the preservation boundary cannot be determined from the request and current
intent, stop and route to `idd-code-check-implementation`,
`idd-intent-brainstorm`, or an intent change workflow instead of making code
changes.

## Report

Use these fields:

```text
Mode:
Specs used as intent:
Engineering rules applied:
Behavior changed:
Behavior preserved:
Public contracts preserved:
Compatibility/data constraints:
Code areas changed:
Tests changed:
Verification result:
Engineering conformance:
Conformance-check result:
Remaining risks:
```

## Removal Implementation

When implementing behavior removal, verify that:

- the removed entry point is no longer available;
- dependent scenarios still work;
- old data and saved settings are handled according to current intent;
- public contracts are removed or changed according to current intent;
- tests for removed behavior are removed or changed instead of remaining as
  false requirements;
- negative verification exists when absence of behavior is a product contract.

## Relationship to Factory

`idd-code-implement` implements one focused behavior from current specs.

It does not create or coordinate Factory runs.
When used from factory execution, the factory task brief is only the local task
scope.
The normative product intent still comes from `.idd/intent/`.

Factory may sequence tasks and reviews, but it must not redefine implementation
rules.

Do not expand a factory task into adjacent work unless required by the relevant
spec.
Report changed files, tests, verification, and concerns back to the factory
workflow.

## Example

If `.idd/intent/IDD-0018.spec-command-history-completion.md` says command completion must
have a neutral default selection, implement that behavior and add only the
high-value verification needed to protect it, then verify the implementation
against IDD-0018.
