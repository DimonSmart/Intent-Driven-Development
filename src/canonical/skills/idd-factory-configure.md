# IDD Factory Configure

Configure the project-owned Factory execution policy in `.idd/execution.yaml`.
This skill is reusable: use it during initial Factory enablement or later to
change, simplify, or refresh model mappings.

Read `references/factory-execution-policy.md` before editing the policy.

## Scope

This workflow owns only Factory model-selection policy. It does not:

- change Product Intent or Engineering Rules;
- edit Factory temporary state under `.idd/factory/current/`;
- create a workflow runtime, resolver service, process supervisor, or MCP
  orchestration layer;
- perform per-worker model selection during a Factory run;
- modify mappings automatically after failures.

The project configuration is authoritative user policy.

## Model-update handoff

`idd-factory-update-effort-models` may hand off a complete proposal, the original
request, the baseline policy contents or absence, and user authorization. Load
that context and finish configuration in the same request. Preserve unselected
profiles, other platform sections, and existing reasoning settings, including
omitted settings, unless their change is explicitly requested.

Reuse an already confirmed proposal. Exact, unambiguous instructions to apply
specified model replacements authorize those replacements; do not ask the same
decision again. Automatically selected or interpreted replacements still need
confirmation. A review-only request must not write the policy.

Compare the current file with the supplied baseline before applying. If it has
changed, rebase the requested replacements onto current policy and show the
revised proposal. Obtain a new decision when the previously authorized result
would change. Never save a stale snapshot over intervening edits.

## Read current state

Read `.idd/execution.yaml` when it exists and validate it using the bounded
structural rules in the reference. Retain its contents or absence as the
proposal baseline; a handed-off baseline must also be checked against this read.

A malformed existing policy is a blocking diagnostic. Show the diagnostic and
repair it as part of configuration; do not silently replace its strategy.

If the file is absent, the effective policy is `inherit` for every profile;
Factory can run without creating the file. When the user invokes this workflow
to configure a policy, save either explicit `inherit` or the requested partial
or full mapping.

## Top-level choice

When the user has not already supplied an unambiguous policy, ask one blocking
single-choice question with these answer values and meanings:

```text
Which LLM strategy should Factory use for worker tasks?

- default (inherit) — use the current session/host model for all tasks
- configure — explicitly configure models for selected economy, standard, or strong profiles
```

Use native structured interaction when the active host exposes it:

- Codex: `request_user_input`;
- Claude Code: `AskUserQuestion`.

Do not reproduce the tool schema in this skill. If structured interaction is
unavailable, ask one concise blocking plain-text question.

## `default` / `inherit`: use the current model for all tasks

Persist the explicit intentional choice:

```yaml
version: 1

factory:
  modelStrategy: inherit
```

This means Factory does not pass a Factory-specific model or reasoning override
for `economy`, `standard`, or `strong`.

Do not resolve `inherit` to the current model name and do not copy a session
model ID into the file. `default` and `inherit` are equivalent user answers;
persist only `modelStrategy: inherit`.

## `configure`: configure selected task-complexity levels

Use exactly these semantic profiles:

```text
economy
standard
strong
```

Prepare one proposed policy for the requested scope. Do not require model IDs
for unselected profiles or platforms. Partial mappings are valid; absent
mappings inherit host model/reasoning settings. Preserve unselected existing
mappings and intentional absence. A request to return one profile/platform to
inheritance removes that mapping; do not persist `default`, `inherit`, a null,
or a blank model as a placeholder. Clean up an empty profile section if needed.
To return every profile/platform to inheritance, use `modelStrategy: inherit`.

Use the best current information available in this order:

1. native host capability/model catalog;
2. current platform-provided model information;
3. current platform documentation or knowledge available to the agent;
4. concrete model IDs supplied by the user.

The proposal may include optional platform-supported reasoning settings. Do not
hardcode recommended concrete model IDs in IDD source and do not assume a model
is available to this account/workspace merely because it exists. State any
availability uncertainty.

When model selection or interpretation is needed, present the whole proposed
policy and obtain one blocking decision. Reuse a complete already-authorized
handoff instead of asking again:

```text
Use proposed mapping
Edit mapping
Use default (inherit) for everything
```

Never save an automatically proposed mapping before explicit confirmation.

If the user chooses edit, accept either structured edits or a natural-language
policy such as using a cheaper model only for simple tasks and one specified
model for the other profiles. Resolve dynamic descriptions during this
configuration turn to concrete model IDs. Show the resulting concrete mapping
before saving whenever interpretation was needed.

Do not persist runtime aliases such as:

```text
cheapest
best
strongest
latest
recommended
```

## Platform sections

Store concrete overrides only where requested, or global `modelStrategy: inherit`.

Conceptually:

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

A project may contain mappings for more than one supported platform. A missing
profile or missing active-platform mapping means inherit host settings. A
Codex-only policy remains valid for Claude without requiring Claude model IDs.
Validate the structure of every present mapping. A present platform mapping
without a non-empty model is malformed; it is not the same as absence.

Platform-specific reasoning values are optional and remain platform-defined.
Do not invent a canonical list of allowed values.

The user may intentionally assign the same model to multiple profiles. Preserve
that choice exactly; do not "improve" a `strong` mapping to another model.

## Save

Before writing:

1. show a table with profile, current model/reasoning, and proposed
   model/reasoning when updating a mapping;
2. obtain required confirmation;
3. re-read and compare the file with the proposal baseline; resolve intervening
   changes as described above, then validate the complete document structurally;
4. write `.idd/execution.yaml` atomically when the host permits;
5. re-read the file and report explicit mappings and inherited profiles, or the explicit
   `inherit` strategy.

If the final policy is unchanged, report a no-op without rewriting the file.

Switching from explicit mappings back to all-inherit replaces the explicit
mapping with `modelStrategy: inherit`.

Do not modify `.idd/factory/current/` while configuring execution policy.
