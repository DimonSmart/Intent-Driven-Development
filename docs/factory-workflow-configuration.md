# Factory configuration

Factory configuration contains deterministic safety budgets only. It does not
describe workflow steps, semantic outcomes, worker capabilities, or review
policy.

The packaged default is `factory.yaml`. A workspace may provide
`.idd/factory.yaml`; its effective hash is pinned into an active run.

```yaml
schemaVersion: 2

limits:
  maxAttemptsPerTask: 4
  maxPlanningCycles: 12
  maxWorkItems: 64
```

- `maxAttemptsPerTask` bounds deterministic retry of one task after failed
  authoritative verification.
- `maxPlanningCycles` bounds the repeated plan/execute loop, including planning
  after final verification failures.
- `maxWorkItems` bounds completed, current, and remaining work.

Unsupported fields and out-of-range values are rejected before execution. The
schema change is intentionally breaking; old semantic-outcome configuration is
not adapted.
