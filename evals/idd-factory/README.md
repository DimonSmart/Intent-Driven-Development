# IDD Factory live evaluation

The live Factory evaluation validates the current native-agent architecture
rather than the removed .NET runtime.

## Scenario

`TwoStepCatalog` requires two sequential implementation tasks:

```text
request
-> fresh planner
-> ProductCode task
-> fresh worker A
-> Catalog task
-> fresh worker B
-> fresh planner
-> Done
-> project verification
-> complete
```

The two workers share repository reality but not semantic transcripts.

## Observable contract

The evaluation should establish that:

- requested product behavior is implemented;
- protected durable intent is not modified by workers;
- planning is incremental/current-reality based;
- workers are separate native agents;
- the root agent does not implement the product change instead of workers;
- the generated plugin has no Factory MCP/runtime dependency;
- no model-driven status polling is used;
- final project verification passes.

The trace should contain native child-agent dispatch and no
`factory_run`, `factory_continue`, `factory_status`, or
`idd-factory.dll` transport activity.

Do not emulate the removed deterministic state machine in the eval harness.
