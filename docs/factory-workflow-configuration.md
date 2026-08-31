# Factory configuration

Factory configuration is policy-only. It does not describe an execution workflow, step handlers, transitions, or a second DAG.

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
    - documentation
```

## Limits

- `maxAgentAttempts`: per semantic work/refinement attempt budget.
- `maxReplans`: bounded global strategy-replan count.
- `maxCorrectiveCycles`: bounded semantic-review correction count.
- `maxWorkItems`: hard dynamic graph-expansion bound, including runtime-created work/review nodes.

Values must be positive. Budget exhaustion is explicit run state/outcome rather than an implicit retry loop.

## Final review

`finalReview.required` is present for explicit policy provenance but cannot be disabled in this runtime version. Mandatory final integrated semantic review is a durable safety invariant.

## Capability allow-list

`capabilities.allow` contains only registered executable work capabilities. The current runtime recognizes:

- `implementation`
- `research`
- `semantic-review`
- `documentation`

Operational capabilities such as initial decomposition, scoped refinement, and global replan are runtime-owned and are not arbitrary work-item routing choices.

Unknown capabilities, duplicates, empty allow-lists, unsupported schema fields, or attempts to disable mandatory safety invariants are rejected before run execution.

## Deliberately unsupported

Factory configuration cannot define:

- `steps` or `transitions`;
- outcome-to-phase routing;
- arbitrary role or skill names;
- custom handlers or executable scripts;
- a generic workflow/DAG language;
- policies that mutate completed work or weaken strict final verification/final review.

The dynamic task graph lives in run state and evolves through validated runtime graph mutations. Configuration bounds that behavior; it does not duplicate the graph.
