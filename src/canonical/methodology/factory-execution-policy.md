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
with different reasoning settings. An effort field is optional: when a model
is explicit but its effort field is absent, the native host/model selects its
default reasoning behavior for that child. That is not a request to inherit
the parent session's exact effort. An absent profile/platform mapping instead
inherits both the host model choice and its reasoning behavior.

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

## Cost-oriented model and effort selection

Use these initial heuristics for guided configuration of requested mappings and
guided model replacements. Aim for minimum expected cost per successfully
completed task, including repeat attempts, subject to the required capability.
Maximum effort on a cheap model is a starting assumption, not guaranteed savings.
Explicit user settings and constraints take precedence; do not require different
model IDs for different profiles.

| Profile | Model selection goal | Effort selection |
| --- | --- | --- |
| `economy` | Cheap model suitable for simple, bounded tasks | Maximum supported level |
| `standard` | Middle price class suitable for ordinary engineering | Next-to-maximum supported level |
| `strong` | Most capable model suitable for complex tasks | Middle supported level; lower middle when even |

Verify the ordered effort levels accepted for the concrete model through the
active native child-agent platform, using reliable host capabilities and current
platform/vendor information. API support alone does not prove the child-agent
tool can pass the setting. Never infer support from a name, sort labels
alphabetically, or reuse another provider/model's scale. For an ascending list
of `n` verified levels, select these zero-based indices:

- `economy`: `n - 1`;
- `standard`: `max(0, n - 2)`;
- `strong`: `floor((n - 1) / 2)`.

A single supported level serves every profile. For unsupported explicit effort,
unknown allowed values, or unknown ordering, propose `host default (omitted)`
and explain that this delegates to the child platform/model default. Never
invent a value, numeric budget, or provider-independent effort enum.

Resolve the pair once during configuration and present current/proposed exact
model and effort (including omission), rationale and uncertainties in one table.
One Apply/Edit/Reject decision covers the complete interpreted proposal; no
separate effort questionnaire is required. Persist only concrete model IDs and
native values in Codex `reasoningEffort` or Claude `effort`, never relative
selectors such as `maximum`, `maximum-minus-one`, or `middle`.

A guided replacement resolves effort again for the new model. Confirmation of
the displayed pair authorizes its shown effort change or removal; preserve all
unselected mappings. Exact model-only edits instead preserve existing effort,
including omission. Known incompatibility with that preserved value requires
an explicitly approved compatible effort or removal before saving. Effort-only
edits require an existing concrete model mapping; clearing effort removes only
its optional key. Do not retune existing policies without a requested change.
Review-only proposals never write policy.

Runtime passes the saved pair exactly. It never recomputes these heuristics,
benchmarks models, optimizes costs, or switches models based on results.

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
values. A guided model-selection proposal presents model and optional effort
as one decision per selected platform/profile. Recommend an effort only when
current host/vendor evidence supports its compatibility; otherwise disclose
uncertainty and offer omission. The selection heuristics resolve against each
model/platform scale; complexity profiles do not name fixed vendor effort values.
Clearing effort removes only its optional key, not the model.

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
per-child override, do not substitute another model, suppress the effort
setting, or change the profile. Stop with a clear diagnostic and suggest rerunning
`idd-factory-configure`.

The same canonical Factory worker protocol is used for every execution profile.
Profiles affect native child-agent model settings, not worker instructions.
The worker never selects its own model.

## Configuration lifecycle

`idd-factory-configure` is the **single** Factory configuration, model-review,
and lifecycle refresh skill. It owns initial setup, exact assignments, read-only
reviews, alternative selection, and authorized persistence to
`.idd/execution.yaml`. No second update skill or persistence handoff exists.

An absent policy means inheritance, not a prompt to create a policy during
read-only review. Exact, unambiguous user-authorized model IDs do not require
separate approval or autonomous research. Automatically selected or interpreted
alternatives require explicit confirmation; review-only requests never write.
Only an initial configuration with an absent policy and no previously chosen
strategy needs a blocking `default (inherit)` versus `configure` choice.

Preserve unselected profiles/platform mappings. Exact model-only edits preserve
optional reasoning values (including absence); guided replacements resolve a new
pair under the cost-oriented selection rules, confirmed together.
One-model-for-many-profiles is valid. A reasoning-only override without a concrete model is invalid. To inherit one mapping, remove
that mapping; to inherit all profiles, use global `modelStrategy: inherit`.
The final mapping may be an empty `executionProfiles: {}` when the last
explicit override was removed without a request to change the global strategy.

Research alternative models only when needed. Prefer native host capabilities,
platform-provided information, and fresh verifiable vendor documentation.
For exact user assignments, this hierarchy does not override the provided ID.
Never infer vendor model quality, price, latency, reasoning compatibility, or
per-account availability from names. If an exact model-only edit conflicts
with preserved reasoning, block that candidate until explicitly resolved;
unknown compatibility is reported as uncertainty, not a forced fallback.

Before an authorized save, validate every mapping (including inactive-platform
ones), preserve a baseline of the file or its absence, show the relevant diff,
and re-read immediately before writing. Rebase **only** authorized operations
onto a valid concurrent version; get a new decision when the approved result
would change. Stop on unsafe conflicts. Perform a no-op without a file write
when nothing changes; prefer supported atomic persistence and re-read after
saving. Never silently reset malformed input to inheritance.

Policy changes apply at the next worker spawn; the planner's complexity
classification and already running workers are unaffected.
