# Factory configuration

Factory configuration is policy-only. It does not describe execution steps, handlers, or transitions.

The packaged default is `factory.yaml`. A workspace may provide `.idd/factory.yaml`; the effective configuration hash is pinned into the active run and a change is reported rather than silently adopted.

## Schema

```yaml
schemaVersion: 1
limits:
  maxAgentAttempts: 4
  maxReplans: 3
  maxCorrectiveCycles: 5
  maxWorkItems: 64
finalReview:
  required: true
capabilities:
  allow:
    - implementation
    - research
    - semantic-review
```

## Limits

- `maxAgentAttempts`: per semantic operation attempt budget.
- `maxReplans`: bounded global strategy-replan count.
- `maxCorrectiveCycles`: bounded semantic-review correction count.
- `maxWorkItems`: hard bound for completed, current, and remaining work, including runtime-created tasks.

Values must be positive. Budget exhaustion is explicit run state/outcome rather than an implicit retry loop.

## Final review

`finalReview.required` is present for explicit policy provenance but cannot be disabled in this runtime version. Mandatory final integrated semantic review is a durable safety invariant.

## Capability allow-list

`capabilities.allow` contains only registered executable work capabilities. The current runtime recognizes:

- `implementation`
- `research`
- `semantic-review`

Planning and global replan triggers are runtime-owned and are not arbitrary work-item routing choices.

Unknown capabilities, duplicates, empty allow-lists, unsupported schema fields, or attempts to disable mandatory safety invariants are rejected before run execution.

## Deliberately unsupported

Factory configuration cannot define:

- `steps` or `transitions`;
- outcome-to-phase routing;
- arbitrary role or skill names;
- custom handlers or executable scripts;
- a generic workflow language;
- policies that mutate completed work or weaken strict final verification/final review.

Linear completed/current/remaining state evolves through validated atomic transitions. Configuration bounds that behavior; it does not duplicate the plan.
