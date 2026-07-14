# idd-code-implement

Use this skill when the user asks to implement behavior that is already
specified, when `idd-intent-change` has just updated the relevant spec, or when
the user asks for implementation-only work that must preserve current intent.

Formula:

```text
idd-code-implement = current spec intent + mode + code change + verification
```

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
- Do not copy implementation plans or temporary notes into specs.
- Prefer the smallest code change that satisfies the relevant acceptance
  criteria.
- In `preserve-current-intent` mode, do not edit intent.
- In `preserve-current-intent` mode, read relevant current intent before
  changing code and treat the preservation boundary as a temporary
  implementation constraint.
- Add or update verification when existing checks are not enough to prove the
  preserved behavior.
- If implementation requires a product behavior change, stop
  `preserve-current-intent` and use `idd-intent-change`.
- Do not update intent after the fact to justify an accidental behavior change.
- Add or update tests when the behavior can be tested.
- Run relevant verification.
- After implementation, perform a focused implementation/spec check using
  `idd-code-check-implementation`.

## Workflow

1. Identify the relevant spec and acceptance criteria.
2. Locate the implementation area.
3. Locate existing tests for the behavior.
4. Implement the smallest change that satisfies the spec.
5. Add or update tests.
6. Run relevant verification.
7. Run or recommend focused `idd-code-check-implementation`.
8. Report:

   - specs used as intent;
   - code areas changed;
   - tests added or updated;
   - verification result;
   - remaining risks or missing coverage.

## Missing Spec Rule

If the requested behavior is not covered by current specs:

```text
Stop before implementation and use idd-intent-change.
```

Do not silently implement new durable behavior without updating product intent
first.

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

It does not create or execute Factory Work Plans.
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

If `.idd/intent/0018.spec-command-history-completion.md` says command completion must
have a neutral default selection, implement that behavior in command completion
code and tests, then verify the implementation against spec 0018.
