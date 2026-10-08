# Factory Workflow Configuration

IDD Factory still has no Factory runtime configuration file.

There is no packaged `factory.yaml`, runtime schema, retry budget,
state-transition configuration, capability registry, process-timeout policy, or
Factory MCP transport configuration.

Factory has one separate optional project-owned execution policy:

```text
.idd/execution.yaml
```

This file controls only native child-agent model/reasoning overrides. It is not
Factory runtime state, not workflow configuration, not Product Intent, and not
an Engineering Rule. It survives individual Factory runs and must not be stored
under `.idd/factory/current/`.

## Default and explicit strategies

If `.idd/execution.yaml` is absent, all profiles inherit normal host/session
model behavior. No configuration file needs to be created to run Factory.
If the file exists, it must be structurally valid; malformed or incomplete
configuration blocks execution rather than silently falling back to inheritance.

An explicit intentional all-inherit policy is:

```yaml
version: 1

factory:
  modelStrategy: inherit
```

`inherit` means "do not pass a Factory-specific model override"; it is not a
model ID. In the interactive setup this is the `default` (or `inherit`) answer:
the current Codex or Claude session/host model is used for every worker.

## Execution profiles

Every planner task declares exactly one of:

```text
economy
standard
strong
```

Missing, repeated, or unknown metadata invalidates the affected task.

Profiles express task complexity only. The planner does not know the project's
profile-to-model mapping, and the root agent does not reconsider the planner's
classification.

Fine-grained policy is project-owned and complete for the active platform:

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

Each active platform needs all three mappings. A project may assign the same
model to multiple profiles, including with different reasoning settings.
This explicit strategy cannot mix with `inherit`: each profile has a concrete
mapping, or the project uses global `modelStrategy: inherit` for all three.

## Configuration workflow

Run `idd-factory-configure` to create or change the policy.

For later refreshes, upgrades, or downgrades of the models assigned to effort
levels, use `idd-factory-update-effort-models`. It prepares the change and applies
it through `idd-factory-configure`. Unselected levels, other platform mappings,
and reasoning settings are preserved unless explicitly requested. A review-only
request returns a proposal without saving; an unchanged policy is not rewritten.

For fine-grained configuration the active Coding Agent proposes a complete
mapping from the best current host/platform information available, states when
account-specific availability cannot be verified, and waits for user
confirmation before saving.

The initial question has two answers: `default`/`inherit` for the current
session/host model at every level, or `configure` for a complete three-level
mapping. It never asks for or accepts partial per-level inheritance.

IDD does not ship a hardcoded table such as `economy -> Model X`. Dynamic
requests such as "use the strongest available model" are resolved during
configuration to a concrete ID and confirmed before persistence.

## Runtime application

Immediately before each worker spawn:

```text
ExecutionProfile
-> read .idd/execution.yaml when present
-> if absent, inherit; otherwise validate the existing policy
-> inherit OR exact active-platform mapping
-> native child spawn
```

Absent configuration or explicit `modelStrategy: inherit` omits overrides.
Malformed or incomplete existing configuration blocks execution. If the host rejects a
configured model/settings or cannot honor the override, Factory reports the
problem and suggests reconfiguration; it never silently substitutes another
model or profile.

The worker remains `idd-factory-execute-subtask` for every profile.

Already-created workers keep their model/settings. The next worker reads the
updated policy without requiring a new plan.

## Project verification

`.idd/verification.yaml` remains separate project-owned operational
configuration. It controls verification, not model selection.

Workers run focused task-local checks. After planner `# Done`, Factory applies
configured final project verification when available.

## Temporary state

Project-local Factory continuation data remains under
`.idd/factory/current/` and is intentionally minimal. It is disposable
scheduling/context state and is unrelated to persistent `.idd/execution.yaml`.
