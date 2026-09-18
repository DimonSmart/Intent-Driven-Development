# Factory Workflow Configuration

IDD Factory no longer has a Factory runtime configuration file.

There is no packaged `factory.yaml`, runtime schema, retry budget,
state-transition configuration, capability registry, process timeout policy, or
Factory MCP transport configuration.

Factory behavior is defined by:

1. the canonical Factory skills;
2. current durable intent under `.idd/intent/`;
3. the host adapter's native child-agent capabilities;
4. optional project verification in `.idd/verification.yaml`.

## Project verification

`.idd/verification.yaml` remains project-owned operational configuration. It is
not Factory state and it is not product intent.

Workers run focused task-local checks. After planner `# Done`, Factory applies
the configured final project verification when available. A failure is reduced
to a bounded diagnostic for a fresh planner.

See [Project Verification](../src/canonical/methodology/project-verification.md).

## Adapter capabilities

Factory does not configure a custom workflow engine per adapter. An adapter is
supported only if the host can natively provide:

```text
spawn child
fresh/no-parent-history context
shared repository
terminal wait without model polling
final child result
stop/close child
```

Platform-specific API names and sandbox choices belong in generated adapter
guidance, not in canonical Factory configuration.

## Temporary state

Project-local Factory continuation data lives under `.idd/factory/current/`
and is intentionally minimal. It is not configured through YAML and is safe to
discard after cancellation/completion once diagnostic history is no longer
needed.
