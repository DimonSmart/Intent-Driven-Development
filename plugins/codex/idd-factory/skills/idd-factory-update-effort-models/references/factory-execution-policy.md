# Factory Execution Policy

Factory execution policy is project-owned operational configuration for choosing
native child-agent model overrides. It is not Product Intent, an Engineering
Rule, Factory temporary state, or a Factory workflow/runtime configuration.

## Execution profiles

The canonical profiles are:

```text
economy
standard
strong
```

They describe required execution capability only.

- `economy`: simple, well-bounded, mostly mechanical work with limited
  reasoning.
- `standard`: ordinary engineering work of normal complexity.
- `strong`: work that needs materially stronger reasoning, such as architecture
  changes, multi-cause debugging, concurrency/lifecycle analysis, or substantial
  interacting constraints and uncertainty.

Profiles never name a vendor model. The planner classifies task complexity
without reading model policy, model availability, price, or reasoning settings.

## Project-owned configuration

The optional file is:

```text
.idd/execution.yaml
```

Absence of this file means that all profiles inherit host/session/default
model and reasoning behavior. Factory can run without configuring a policy.
When the file exists, it must be valid; malformed existing configuration is a
blocking diagnostic, not permission to inherit.

An explicit all-inherit policy is:

```yaml
version: 1

factory:
  modelStrategy: inherit
```

`inherit` is not a model identifier. It means that Factory supplies no
Factory-specific model or reasoning override for the child agent.

Fine-grained configuration uses profile mappings:

```yaml
version: 1

factory:
  executionProfiles:
    economy:
      codex:
        model: <concrete-model-id>
        reasoningEffort: <optional-platform-value>
    standard:
      codex:
        model: <concrete-model-id>
    strong:
      codex:
        model: <concrete-model-id>
        reasoningEffort: <optional-platform-value>
```

Partial mappings are valid. A missing profile or missing active-platform mapping
means inherit host model/reasoning settings for that task. A Codex-only mapping
does not require a Claude mapping; Claude inherits when its mapping is absent.
The same concrete model may be assigned to more than one profile, including
with different reasoning settings.

The two strategies do not mix. `modelStrategy: inherit` applies globally to all
three profiles. `executionProfiles` may override only selected profiles and
platforms. Per-profile inheritance is represented by an absent mapping, not by
a `default` or `inherit` model ID or a blank mapping.

For example, this valid partial policy overrides only Codex `strong`:

```yaml
version: 1
factory:
  executionProfiles:
    strong:
      codex:
        model: <concrete-model-id>
```

Codex `economy` and `standard`, and every Claude profile, inherit host settings.

Do not persist dynamic aliases such as `cheapest`, `best`, `strongest`,
`latest`, or `recommended` as runtime model identifiers. Resolve such user
requests during configuration to a concrete model ID, obtain confirmation, and
persist the concrete result.

## Structural validation

Before applying an explicit policy, perform bounded structural validation.

Require:

- `version: 1`;
- exactly one Factory strategy shape: either `modelStrategy: inherit` or
  `executionProfiles`;
- `executionProfiles` and each present profile/platform section are mappings;
- only canonical profile names `economy`, `standard`, and `strong`;
- platform sections supported by the generated adapter set;
- a non-empty `model` scalar for every explicit platform mapping;
- optional reasoning settings to be non-empty scalars when present;
- no conflicting strategy fields, unknown profiles, unknown platforms, or
  unknown required structure;
- validate every explicit mapping, including mappings for inactive platforms.

An absent mapping is valid inheritance. A present null, scalar, or list where a
mapping is required, or a platform mapping with a missing/blank `model`, is
malformed and blocks execution. An empty `executionProfiles` mapping supplies
no overrides. Never fill absent mappings with the current session model ID.

For the current adapters, Codex mappings use `model` and optional
`reasoningEffort`; Claude mappings use `model` and optional `effort`.
Canonical IDD does not define concrete model IDs or enumerate allowed effort
values.

Do not silently convert malformed explicit policy to `inherit`. Return a clear
diagnostic and stop before spawning the affected worker.

Do not attempt semantic existence checks for a configured model unless the host
offers a reliable native validation capability. Native host rejection is the
authoritative execution-time validation.

## Runtime lookup

Immediately before each worker spawn, the root agent checks for the optional
policy, re-reads and validates it when present, and performs this mechanical
lookup:

```text
planner ExecutionProfile
-> when reading an existing plan.md, missing ExecutionProfile means standard
-> reject empty, repeated, or unknown explicit profiles
-> if .idd/execution.yaml is absent, inherit host model/reasoning settings
-> otherwise validate .idd/execution.yaml and select the active-platform mapping
-> missing profile/platform mapping means inherit
-> inherit OR exact configured model/settings
-> native child-agent spawn
```

New planner output must contain exactly one canonical `# ExecutionProfile` per
task. Validate that output before saving it to `plan.md`. Existing pending tasks
without the section remain valid and use `standard`; do not require migration,
rewrite the plan, or reclassify those tasks to continue an active run.

The root agent must not reconsider task complexity, compare models, optimize
cost, substitute a stronger/weaker model, or reinterpret user policy.

If the selected mapping is `inherit`, omit Factory-specific model/reasoning
overrides. If it is explicit, pass exactly the configured model and optional
reasoning setting through the host's native child-agent controls.

An absent policy, an explicit `inherit` policy, or an absent selected mapping
omits overrides. A policy
update after this lookup does not alter the already-created worker; the next
worker reads the current file (or observes its absence) without a new plan.

If the host rejects the configured model/settings, or cannot honor an explicit
per-child override, do not substitute another model and do not change the
profile. Stop with a clear diagnostic and suggest rerunning
`idd-factory-configure`.

The same canonical Factory worker protocol is used for every execution profile.
Profiles affect native child-agent model settings, not worker instructions.
The worker never selects its own model.

## Configuration workflow

`idd-factory-configure` is the only Factory workflow that creates or
deliberately changes `.idd/execution.yaml`.

`idd-factory-update-effort-models` is the lifecycle entry point for refreshing,
upgrading, downgrading, or replacing the models assigned to those profiles. It
prepares a proposal and hands it to `idd-factory-configure` with the original
request, policy baseline, and user authorization. It does not introduce a second
policy writer or change planner classification. Preserve unaffected mappings,
other platform sections, and reasoning settings unless explicitly requested.

The configuration owner compares current policy with the proposal baseline
before saving. Intervening edits require a revised proposal, with a new decision
when the authorized result changes. An unchanged policy is not rewritten. Exact
user-authorized replacements or an already confirmed proposal do not require
the same decision again; review-only requests never authorize a write.

When proposing a mapping, obtain model information in this order when available:

1. native host capability/model catalog;
2. current platform-provided model information;
3. current platform documentation or knowledge available to the agent;
4. concrete model IDs supplied by the user.

Do not claim account/workspace availability merely because a vendor model exists.
If account-specific availability cannot be verified, say so in the proposal.

Automatically proposed mappings require explicit user confirmation before they
are persisted.
