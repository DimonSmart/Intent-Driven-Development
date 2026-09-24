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
- choose a model during a Factory run;
- modify mappings automatically after failures.

The project configuration is authoritative user policy.

## Read current state

Read `.idd/execution.yaml` when it exists and validate it using the bounded
structural rules in the reference.

A malformed existing explicit policy is a blocking diagnostic. Do not silently
replace it with `inherit`.

If the file is absent, the effective policy is all profiles `inherit`.

## Top-level choice

When the user has not already supplied an unambiguous policy, ask one blocking
single-choice question with this semantic meaning:

```text
How should Factory choose models for worker tasks?

- Use the current model for all tasks
- Configure different models by task complexity
```

Use native structured interaction when the active host exposes it:

- Codex: `request_user_input`;
- Claude Code: `AskUserQuestion`.

Do not reproduce the tool schema in this skill. If structured interaction is
unavailable, ask one concise blocking plain-text question.

## Use current model for all tasks

Persist the explicit intentional choice:

```yaml
version: 1

factory:
  modelStrategy: inherit
```

This means Factory does not pass a Factory-specific model or reasoning override
for `economy`, `standard`, or `strong`.

Do not resolve `inherit` to the current model name and do not copy a session
model ID into the file.

## Configure by task complexity

Use exactly these semantic profiles:

```text
economy
standard
strong
```

First prepare one complete proposed mapping rather than asking three independent
questions by default.

Use the best current information available in this order:

1. native host capability/model catalog;
2. current platform-provided model information;
3. current platform documentation or knowledge available to the agent;
4. concrete model IDs supplied by the user.

The proposal may include optional platform-supported reasoning settings. Do not
hardcode recommended concrete model IDs in IDD source and do not assume a model
is available to this account/workspace merely because it exists. State any
availability uncertainty.

Present the whole proposed policy and obtain one blocking decision:

```text
Use proposed mapping
Edit mapping
Use current model for everything
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

Store only concrete configuration or `inherit`.

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
      claude:
        model: <concrete-model-id>
        effort: <optional-platform-value>
```

A project may contain mappings for more than one supported platform. Missing
profiles and missing active-platform sections inherit host behavior.

Platform-specific reasoning values are optional and remain platform-defined.
Do not invent a canonical list of allowed values.

## Partial policies

Partial override is valid. For example, configuring only `economy` means:

```text
economy  -> explicit mapping
standard -> inherit
strong   -> inherit
```

Do not expand missing mappings merely for symmetry.

The user may intentionally assign the same model to multiple profiles. Preserve
that choice exactly; do not "improve" a `strong` mapping to another model.

## Save

Before writing:

1. show the effective profile-to-model policy when it contains automatic
   interpretation or a proposed mapping;
2. obtain required confirmation;
3. validate the complete document structurally;
4. write `.idd/execution.yaml` atomically when the host permits;
5. re-read the file and report the effective mappings, including inherited
   profiles.

Switching from explicit mappings back to all-inherit replaces the explicit
mapping with `modelStrategy: inherit`.

Do not modify `.idd/factory/current/` while configuring execution policy.
