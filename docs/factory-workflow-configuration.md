# Factory Workflow Configuration

IDD Factory still has no Factory runtime configuration file.

There is no packaged `factory.yaml`, runtime schema, retry budget,
state-transition configuration, capability registry, process-timeout policy, or
Factory MCP transport configuration.

Factory now has one separate optional project-owned execution policy:

```text
.idd/execution.yaml
```

This file controls only native child-agent model/reasoning overrides. It is not
Factory runtime state, not workflow configuration, not Product Intent, and not
an Engineering Rule. It survives individual Factory runs and must not be stored
under `.idd/factory/current/`.

## Default behavior

If `.idd/execution.yaml` is absent, all profiles inherit normal
host/session/default model behavior. Existing projects therefore continue to
behave as before.

An explicit intentional all-inherit policy is:

```yaml
version: 1

factory:
  modelStrategy: inherit
```

`inherit` means "do not pass a Factory-specific model override"; it is not a
model ID.

## Execution profiles

Planner tasks may optionally declare:

```text
economy
standard
strong
```

Missing metadata means `standard`.

Profiles express task complexity only. The planner does not know the project's
profile-to-model mapping, and the root agent does not reconsider the planner's
classification.

Fine-grained policy is project-owned and may be partial:

```yaml
version: 1

factory:
  executionProfiles:
    economy:
      codex:
        model: <concrete-model-id>
        reasoningEffort: <optional-platform-value>
    strong:
      claude:
        model: <concrete-model-id>
        effort: <optional-platform-value>
```

Missing profile/platform mappings inherit. A project may assign the same model
to multiple profiles to cap what Factory is allowed to use.

## Configuration workflow

Run `idd-factory-configure` to create or change the policy.

For fine-grained configuration the active Coding Agent proposes a complete
mapping from the best current host/platform information available, states when
account-specific availability cannot be verified, and waits for user
confirmation before saving.

IDD does not ship a hardcoded table such as `economy -> Model X`. Dynamic
requests such as "use the strongest available model" are resolved during
configuration to a concrete ID and confirmed before persistence.

## Runtime application

Immediately before each worker spawn:

```text
ExecutionProfile
-> validate .idd/execution.yaml
-> exact active-platform lookup
-> inherit OR configured model/settings
-> native child spawn
```

Malformed explicit configuration blocks execution. If the host rejects a
configured model/settings or cannot honor the override, Factory reports the
problem and suggests reconfiguration; it never silently substitutes another
model or profile.

The worker remains `idd-factory-execute-subtask` for every profile.

## Project verification

`.idd/verification.yaml` remains separate project-owned operational
configuration. It controls verification, not model selection.

Workers run focused task-local checks. After planner `# Done`, Factory applies
configured final project verification when available.

## Temporary state

Project-local Factory continuation data remains under
`.idd/factory/current/` and is intentionally minimal. It is disposable
scheduling/context state and is unrelated to persistent `.idd/execution.yaml`.
