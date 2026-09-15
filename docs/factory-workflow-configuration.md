# Factory configuration

Factory configuration contains deterministic safety budgets only. It does not
describe workflow steps, semantic outcomes, worker capabilities, or review
policy.

The packaged default is `factory.yaml`. A workspace may provide
`.idd/factory.yaml`; its effective hash is pinned into an active run.

```yaml
schemaVersion: 4

limits:
  maxAttemptsPerTask: 4
  maxTechnicalRestartsPerTask: 1
  maxPlanningCycles: 12
  maxWorkItems: 64
  semanticCommandTimeout: 10m
```

- `maxAttemptsPerTask` bounds semantic implementation attempts for one work item.
  The initial implementation is semantic attempt 1. A new semantic attempt is
  consumed only when authoritative task verification returns an unexpected
  failure and the same immutable work item must be implemented again.
- `maxTechnicalRestartsPerTask` bounds restartable execution-layer failures for
  one work item independently of the semantic budget. The default is `1`; the
  accepted range is `0..3`, where `0` disables automatic Technical Restart.
  The counter is cumulative for the work item and does not reset after a
  Semantic Retry.
- `maxPlanningCycles` bounds the repeated plan/execute loop, including planning
  after final verification failures.
- `maxWorkItems` bounds completed, current, and remaining work.
- `semanticCommandTimeout` bounds each shell command observed in a semantic
  worker's event stream. It accepts `s`, `m`, or `h` suffixes and must be between
  one second and one hour. The effective value is recorded in attempt telemetry.

`AGENT_COMMAND_TIMEOUT`, `AGENT_COMMAND_INCOMPLETE`, and a transport failure
without an already observed complete trusted result are restartable technical
failures for implementation execution. They may start a new executor invocation
with a new `AttemptId`, but they keep the same semantic-attempt number and do not
consume `maxAttemptsPerTask`. Exhausting the independent technical budget stops
the run with `TECHNICAL_RESTART_BUDGET_EXHAUSTED`.

`factory_retry` extends only the semantic attempt budget. It does not change the
technical restart budget or counter.

Unsupported fields and out-of-range values are rejected before execution. Schema
4 is intentionally breaking because the second budget is required explicitly;
older strict configuration schemas are not silently reinterpreted.
