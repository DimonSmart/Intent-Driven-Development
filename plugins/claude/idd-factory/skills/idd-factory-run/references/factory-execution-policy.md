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
- `standard`: ordinary engineering work of normal complexity. This is the
  default profile.
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

Absence of the file means all Factory profiles inherit the host/session/default
model behavior.

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
      claude:
        model: <concrete-model-id>
        effort: <optional-platform-value>
```

Mappings may be partial. A missing profile mapping, or a profile without a
mapping for the active platform, means `inherit`. The same concrete model may be
assigned to more than one profile; this is how a project can deliberately cap
the models Factory is allowed to use.

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
- only canonical profile names `economy`, `standard`, and `strong`;
- platform sections supported by the generated adapter set;
- a non-empty `model` scalar for every explicit platform mapping;
- optional reasoning settings to be non-empty scalars when present;
- no conflicting strategy fields and no unknown required structure.

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

Immediately before each worker spawn, the root agent performs only this
mechanical lookup:

```text
planner ExecutionProfile
-> default missing profile to standard
-> structurally validate .idd/execution.yaml when present
-> select the active-platform mapping for that profile
-> inherit OR exact configured model/settings
-> native child-agent spawn
```

The root agent must not reconsider task complexity, compare models, optimize
cost, substitute a stronger/weaker model, or reinterpret user policy.

If the selected mapping is `inherit`, omit Factory-specific model/reasoning
overrides. If it is explicit, pass exactly the configured model and optional
reasoning setting through the host's native child-agent controls.

If the host rejects the configured model/settings, or cannot honor an explicit
per-child override, do not substitute another model and do not change the
profile. Stop with a clear diagnostic and suggest rerunning
`idd-factory-configure`.

The worker definition remains `idd-factory-execute-subtask` for every profile.
The worker does not read this policy to choose its own model.

## Configuration workflow

`idd-factory-configure` is the only Factory workflow that creates or
deliberately changes `.idd/execution.yaml`.

When proposing a mapping, obtain model information in this order when available:

1. native host capability/model catalog;
2. current platform-provided model information;
3. current platform documentation or knowledge available to the agent;
4. concrete model IDs supplied by the user.

Do not claim account/workspace availability merely because a vendor model exists.
If account-specific availability cannot be verified, say so in the proposal.

Automatically proposed mappings require explicit user confirmation before they
are persisted.
