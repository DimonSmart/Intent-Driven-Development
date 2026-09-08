# Factory configuration

Factory configuration contains deterministic safety budgets only. It does not
describe workflow steps, semantic outcomes, worker capabilities, or review
policy.

The packaged default is `factory.yaml`. A workspace may provide
`.idd/factory.yaml`; its effective hash is pinned into an active run.

```yaml
schemaVersion: 3

limits:
  maxAttemptsPerTask: 4
  maxPlanningCycles: 12
  maxWorkItems: 64
  semanticCommandTimeout: 10m
```

- `maxAttemptsPerTask` bounds deterministic retry of one task after failed
  authoritative verification or an incomplete/timed-out worker command.
- `maxPlanningCycles` bounds the repeated plan/execute loop, including planning
  after final verification failures.
- `maxWorkItems` bounds completed, current, and remaining work.
- `semanticCommandTimeout` bounds each shell command observed in a semantic
  worker's event stream. It accepts `s`, `m`, or `h` suffixes and must be between
  one second and one hour. The effective value is recorded in attempt telemetry.

Unsupported fields and out-of-range values are rejected before execution. The
schema change is intentionally breaking; old semantic-outcome configuration is
not adapted.
