---
name: idd-factory-configure
description: "Configure and review project-owned Factory model policy in .idd/execution.yaml: initial setup, exact edits, read-only comparisons, and evidence-based model refresh, with authorization before selected changes are saved."
---

# IDD Factory Configure

Manage project-owned Factory model-selection policy in `.idd/execution.yaml` throughout
the project lifecycle: initial setup, exact edits, read-only review, and
evidence-based model refresh. This is the **only** skill that validates and
writes Factory model policy; no separate model-update command or writer exists.

**First** read `references/factory-execution-policy.md`. Do not read or change
`.idd/factory/current/`, Product Intent, Engineering Rules, the Factory runtime,
planner classification, or existing child agents. Configuration is operational
policy, not durable product or engineering intent.

## Interpret the request and authorization

Read the original user request, not merely a router summary. Direct invocation
of this skill does not require `idd-route`. Recognize these modes:

- **Initial setup:** Factory is being explicitly configured, the policy is absent,
  and no strategy was chosen previously. Ask one blocking strategy choice below.
  Reuse an answer already obtained by `idd-project-init` or by the user.
- **Exact edit:** user specifies concrete model IDs and/or explicit reasoning
  edits, platforms and profiles, or requests inheritance. Apply only those
  unambiguous authorized operations; do not research alternatives or ask for
  redundant confirmation. A request to apply an exact ID is authorization for
  that edit even if its availability cannot be verified.
- **Review-only:** read, compare, or recommend without writing, including
  "propose but do not change." Return findings/proposals only; do not create
  `.idd/execution.yaml`, request write permission, or start Factory.
- **Refresh/selection:** cheaper economy, faster standard, stronger strong,
  upgrade/downgrade, outdated model review, or comparing available options.
  Research only the requested platforms/profiles/criteria, propose changes with
  evidence, and obtain explicit approval before writing. A newer release alone
  is not a reason to change a working assignment.

Automatic activation alone does not authorize a policy write. An absent file,
an observed worker failure, and an agent-selected recommendation do not authorize
a change. **A review-only request must not write the policy.** Reuse already
confirmed, unchanged choices instead of asking again. If scope is ambiguous,
request the specific missing decision; never silently expand scope.

Use the active host platform unless the user names another supported platform.
Factory supports only `economy`, `standard`, `strong`; these describe task
complexity, **not** vendor-specific `reasoningEffort` or `effort`.

## Read and validate existing policy

Read `.idd/execution.yaml`, or record that it is absent, and preserve its
complete contents/absence as the baseline. Validate **all** present mappings
with the referenced structural contract, including mappings for the inactive
platform. A malformed existing policy is a blocking diagnostic, not permission
to reset it or treat it as absent. Repair only with explicit authorization.

Missing policy means `inherit (host)` for every platform/profile, with no
concrete model ID known or implied. Global `modelStrategy: inherit` has the same
effective inheritance. Do not claim to know the actual host model or copy it
into mappings. Review-only use must not create the file.

## Initial strategy choice

Only when actual first-time configuration requires a strategy choice that the
user has not already made, ask one blocking question:

```text
Which LLM strategy should Factory use for worker tasks?

- default (inherit) — use the current session/host model for all tasks
- configure — explicitly configure models for selected economy, standard, or strong profiles
```

Use native interaction when available: Codex `request_user_input`, Claude Code
`AskUserQuestion`; otherwise use a concise plain-text question. No second
question when the answer came from `idd-project-init`.

An explicitly selected `default`/`inherit` may be saved, when authorized, as:

```yaml
version: 1
factory:
  modelStrategy: inherit
```

For `configure`, create `executionProfiles` only for requested platform/profile
mappings. All missing mappings inherit host behavior. Do not require a model
for every profile, install-time defaults, or an automatic configuration merely
because the policy is absent.

## Exact changes and minimality

Express changes as a set of authorized operations on specific
`platform / profile / field` entries (or an explicitly requested global strategy).
Preserve every other profile, other platform, model, and reasoning field,
including intentionally absent optional fields. Examples:

- Replacing `economy.codex.model` preserves its `reasoningEffort` if present,
  every `claude` mapping, and every unselected profile.
- Editing only `reasoningEffort` or Claude `effort` requires an existing model
  mapping. Never create a reasoning-only mapping: ask for a concrete model or
  report the operation cannot be represented.
- Returning one mapping to inheritance **removes** the corresponding platform
  mapping and removes an empty profile section. Never persist `model: inherit`,
  `model: default`, null, or blank.
- Explicitly returning **all** Factory profiles to inheritance writes global
  `modelStrategy: inherit` and removes `executionProfiles`.
- Removing the final mapping without a global-strategy request may leave valid
  `executionProfiles: {}` instead; do not silently change strategies.
- Switching from absent policy or global inherit to selected explicit overrides
  uses **only** `executionProfiles`; never mix the two strategy shapes.

A concrete model ID can be shared by several profiles. Do not substitute an
agent-preferred model for an exact requested ID or reject an otherwise valid ID
merely because native model availability is unknown. State uncertainty rather
than inventing model-validation results.

## Refresh, research, and comparison

When the request actually requires selecting alternatives, prefer:

1. the active host's native model catalog and validation capabilities;
2. current platform information and models available to the user;
3. fresh vendor documentation, verified with available tools;
4. user information as supplementary evidence.

This precedence is for **researching alternatives**, not overriding exact user
instructions. Consider target complexity, quality, cost, latency, available
reasoning controls, and user constraints. Explain which source was used and
how current it is. Never infer these properties or account availability from
the model's spelling. IDD must not hardcode a table of recommended concrete IDs
or require distinct models for different profiles. Do not persist the aliases
`cheapest`, `best`, `strongest`, `latest`, or `recommended` as model IDs.

An inherited current value is displayed as `inherit (host)`, not a guessed
model. A request to "update existing models" with no explicit mappings does
not authorize creating overrides: explain the inherited state and propose
concrete options only for an explicitly chosen scope. If current evidence is
insufficient for reliable comparison or host compatibility, state the unknowns
and request concrete IDs or a bounded alternative rather than inventing a winner.

Preserve existing optional reasoning settings during model replacements.
If a candidate is **known** to be incompatible with a preserved
`reasoningEffort`/`effort`, show the conflict and block that candidate until
a compatible model or a separately authorized reasoning edit is selected.
Unknown compatibility is uncertainty, not proof of incompatibility. A host
rejection at spawn is the authoritative error: do not silently switch models,
drop reasoning, or revert to inherit in response.

For review or proposals involving existing mappings, show a comparison with at
least platform/profile, current model, proposed model, previous/proposed reasoning,
and rationale. Identify targeted and unchanged scope, recommendation sources
and freshness, unverified account availability, and confirmation required.
For global inherit, a short resulting-strategy explanation is sufficient.

Ask one explicit decision for automatically chosen or ambiguously interpreted
changes: **Apply**, **Edit**, or **Reject**. An edited proposal requiring
interpretation must be presented again; do not ask again for an unchanged
previously approved decision. An exact unambiguous user-authored mapping needs
no further approval. If rejected or review-only, stop with no writes.

## Validate and persist authorized changes safely

1. Record the baseline (including absence versus presence), validate all
   existing sections, and derive only operations authorized by the user.
2. Prepare the minimal candidate, show the comparison/diff and collect any
   required approval. Do not write in review-only, rejected, or unapproved modes.
3. **Immediately before saving**, re-read the file and compare with the
   baseline, including absent-to-present and present-to-absent transitions.
   If changed, revalidate and rebase only the authorized operations onto the
   new current policy; never overwrite intervening edits with a stale snapshot.
   If changes conflict or alter an already approved result, show the revised
   proposal and seek a new decision. Stop rather than risk loss of user edits.
4. Validate the **complete** resulting document: `version: 1`; exactly one
   `factory.modelStrategy: inherit` or `factory.executionProfiles` mapping;
   only supported profiles and `codex`/`claude` platforms; valid nonempty
   explicit `model`; optional nonempty `reasoningEffort` for Codex or
   `effort` for Claude. No malformed inactive-platform mapping, mixed
   strategy, placeholder alias, or invented effort-value allowlist.
5. If semantically/structurally unchanged, report **no-op**, without rewriting
   formatting or timestamps. Otherwise use the host's supported safe/atomic
   write if available, preserving unrelated user data and formatting.
   If preservation or safe conflict handling is unavailable, stop with
   diagnostics rather than overwrite changes.
6. Re-read the saved policy; report exact overrides and inherited profiles.
   Updated policy applies only at the **next worker spawn**, without
   replanning. Already running workers and the planner profile do not change.

The only project-owned model policy is optional `.idd/execution.yaml`.
Do not modify `.idd/factory/current/`, run Factory, spawn workers, migrate
valid policies, or introduce a second persistence service. Git provides
configuration history.

## Codex Factory model configuration

Use native `request_user_input` for blocking configuration
choices when it is available; do not reproduce its tool schema.
Prefer a native host model/capability catalog when Codex exposes
one, then current platform-provided model information or current
documentation/knowledge, and finally concrete model IDs supplied
by the user. Do not claim account-specific availability unless
the host can verify it.

Persist Codex mappings under the `codex` platform key with a
concrete `model` and optional `reasoningEffort`. Never embed a
recommended concrete model ID in generated guidance, and never
save an automatically proposed mapping before user confirmation.
