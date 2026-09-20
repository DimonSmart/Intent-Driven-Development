# idd-engineering-change

Standard owner of project Engineering Rule semantic mutations: `add`, `modify`, and `remove`.

Supported operations: `add`, `modify`, and `remove`.

Read `references/engineering-guardrails.md` first. Use existing `idd-intent-lint` for structural validation. Engineering management stays in `idd-intent`; do not create a separate plugin, runtime, MCP server, agent, workflow engine, or Engineering lint skill.

## Boundary

Create or change a Rule only for an explicit durable implementation-only project decision. Current code and repeated patterns are evidence, not authority.

Ask whether another implementation could preserve the product contract and still violate the constraint. Product behavior belongs to Intent, build/test commands to Verification, temporary rules to task/Factory context, and incidental implementation detail nowhere.

Intent <-> Engineering movement requires a separate explicit semantic decision.

## Gates

Before any mutation:

1. If `.idd/factory/current/request.md` exists, return `blocked` and mutate nothing. The current Factory run must be completed, cancelled, or explicitly restarted/replanned after the Engineering decision changes. A directory without `request.md`, or legacy `state.json` alone, is not an active run. Do not patch `plan.md`, rewrite `TaskRelatedEngineering`, restart Factory, or re-plan automatically.
2. If `.idd/engineering/` exists, validate it first. Never bootstrap over a malformed or partial existing layer.
3. For modify/remove, resolve the target to exactly one current Rule by `ENG-NNNN` or unambiguous semantics. Zero matches is missing; multiple plausible matches is `ambiguous`. Never choose by filename or first keyword match.

## Lazy bootstrap and allocator

Only the first add may create a completely absent `.idd/engineering/`. Copy canonical files from `assets/bootstrap/.idd/engineering/`; do not duplicate their text here. `idd-project-init` does not create this layer.

Allocator format is exactly `Next ID: ENG-NNNN`. Canonical bootstrap starts at `ENG-0001`. A valid legacy layer without allocator remains readable; before its first management mutation set Next ID to max current ENG ID + 1, or ENG-0001 when empty. Do not inspect Git history. If an existing allocator is malformed, duplicated, or <= a current ID, block instead of repairing it. After allocator introduction, IDs are never reused.

## Add

Check current Rules for semantic ownership. Equivalent existing Rule => `no-op`, with no allocation or unrelated rewrite. If an existing Rule owns the area and the user explicitly changes its semantics, modify that Rule rather than create a duplicate.

Otherwise re-read INDEX, consume exactly current Next ID, verify it is unused, create one canonical Rule, advance Next ID, update INDEX, and run `idd-intent-lint`.

Semantic equivalence is model work. Do not fake it with deterministic filename, keyword, embedding, or similarity heuristics.

## Modify

Resolve exactly one Rule. Preserve `ENG-NNNN` and normally preserve filename. Update Rule content plus INDEX Applicability, Applies when, and Summary. Never change Next ID. Semantically unchanged modify => `no-op`. If the request turns Engineering into product contract, return `blocked`. Validate afterward.

## Remove

Resolve exactly one Rule, delete its document and INDEX row, preserve Next ID, and validate. Keep README/INDEX when the last Rule is removed. Do not create archive, tombstone, deprecated, or history documents; Git is history. Unambiguously already absent => `no-op`.

## Ownership and implementation

This skill is the standard mutation owner. Other IDD skills may read/validate Engineering, report candidates, and hand off an explicitly confirmed candidate, but they do not mutate Rules themselves.

This skill changes durable knowledge, not implementation. If the same request explicitly includes implementation, continue afterward with `idd-code-implement` or Factory, then `idd-code-check-implementation`. Do not duplicate existing Always + relevant Conditional applicability logic.

Return `success`, `no-op`, `blocked`, or `ambiguous`. Blocked/ambiguous results mutate nothing; no-op never advances the allocator.
