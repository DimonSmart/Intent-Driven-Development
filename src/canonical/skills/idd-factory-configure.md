# IDD Factory Configure

Own selection, review, validation and authorized writes of the project model
policy in `.idd/execution.yaml`, including setup, exact edits and model refresh.

Factory Planner and Worker protocols are now installed as internal Skills and
loaded by ordinary native child agents. If asked to install or register the
previous custom Factory agent types, explain that registration is obsolete.
Leave legacy agent definitions, trust, global config and project policy alone
for such a request.

Read `references/factory-execution-policy.md` first for model-policy requests.
Keep this workflow scoped to the policy file, outside Factory runs, planner
classification, child agents, Product Intent and Engineering Rules.

## Interpret the request and authorization

Read the original request, not just a router summary. Direct invocation does
not require `idd-route`. For model-policy requests recognize these modes:

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
- **Refresh/selection:** research options for the requested profiles, platforms
  and criteria, then present an evidence-based proposal for confirmation.

Write only precise user-authorized edits or confirmed proposals. Activation,
an absent file or a worker failure alone grants no write authority. Reuse
unchanged approvals. If scope is ambiguous, ask for the missing decision.

Use the active host platform unless the user names another supported platform.
Factory supports only `economy`, `standard`, `strong`; these describe task
complexity, **not** vendor-specific `reasoningEffort` or `effort`.

## Read and validate existing policy

Read `.idd/execution.yaml`, or record that it is absent, and preserve its
complete contents/absence as the baseline. Validate **all** present mappings
with the referenced structural contract, including mappings for the inactive
platform. A malformed existing policy is a blocking diagnostic, not permission
to reset it or treat it as absent. Repair only with explicit authorization.

Display missing or inherited mappings as `inherit (host)`. Create overrides
only for explicitly requested scope, including when asked to update existing
models and there are no explicit mappings. Never guess or copy the host model.

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

Save an authorized `default`/`inherit` choice as `modelStrategy: inherit`.
For `configure`, use `executionProfiles` only for requested mappings.

## Guided model and reasoning selection

For guided setup and model replacements, aim for the lowest expected cost per
successfully completed task, including retries, while meeting the profile's
capability needs. Use these starting heuristics and honor explicit user choices
and constraints. The same model can serve multiple profiles.

| Profile | Model selection goal | Effort from the model's supported levels |
| --- | --- | --- |
| `economy` | Cheap model suitable for simple tasks | Maximum |
| `standard` | Middle price class suitable for ordinary engineering | Next to maximum |
| `strong` | Most capable model suitable for complex tasks | Middle, using the lower middle on an even scale |

Verify which effort levels the target native child-agent platform accepts for
the chosen model and order them by reasoning intensity. API support alone does
not establish native support. Use the sole supported level if there is just
one. If explicit effort is unsupported or the ordered levels cannot be verified,
explain the reason and propose `host default (omitted)` for that model.

Resolve effort again for a guided replacement. Confirmation of the proposed
pair authorizes the displayed effort change or removal. Save concrete model IDs
and native effort values (`reasoningEffort` for Codex, `effort` for Claude).
Relative selectors, dynamic model aliases and invented effort values are not
configuration values. Retune only the requested mappings. Worker startup
applies the saved pair.

## Exact changes and minimality

Express changes as a set of authorized operations on specific
`platform / profile / field` entries (or an explicitly requested global strategy).
Preserve every other profile, other platform, model, and reasoning field,
including intentionally absent optional fields. Examples:

- An exact model-only replacement preserves the effort field, including its
  absence. If the new model is known to conflict with the existing effort,
  require a compatible model or explicit authorization to change or remove
  effort before saving. Report unknown compatibility as uncertainty.
- Editing only `reasoningEffort` or Claude `effort` requires an existing model
  mapping. Never create a reasoning-only mapping: ask for a concrete model or
  report the operation cannot be represented. An explicit request to clear an
  effort removes only that optional field, retaining the model mapping.
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

Use evidence about quality, cost, latency, reasoning controls and account
availability. A newer release alone does not justify replacing a working model.
Avoid hardcoded recommendations and inferences from model names. If evidence
cannot support a reliable comparison, state the unknowns and ask for concrete
IDs or a bounded alternative.

Show one table with targeted platforms/profiles, current and proposed model/effort
pairs (including omitted effort), rationale, sources and freshness, and any
unverified account availability. Identify unaffected scope briefly. For global
inherit, summarize the resulting strategy. Seek one **Apply**, **Edit**, or
**Reject** decision for the whole interpreted proposal. Show a revised proposal
if it requires new interpretation. Exact edits need no extra confirmation.

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
4. Validate the **complete** result, including inactive-platform mappings,
   against `references/factory-execution-policy.md`.
5. If semantically/structurally unchanged, report **no-op**, without rewriting
   formatting or timestamps. Otherwise use the host's supported safe/atomic
   write if available, preserving unrelated user data and formatting.
   If preservation or safe conflict handling is unavailable, stop with
   diagnostics rather than overwrite changes.
6. Re-read the saved policy; report exact overrides and inherited profiles.
   Updated policy applies only at the **next worker spawn**, without
   replanning. Already running workers and the planner profile do not change.
