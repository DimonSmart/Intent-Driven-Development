# Factory skills

## Public entry point

`idd-factory-run` performs Intent Preflight and then invokes the packaged
deterministic runtime. The launcher does not schedule semantic work itself.

## Semantic workers

- `idd-factory-decompose-task` is the sole planner. It returns ordered `# Task`
  Markdown sections, or an empty response when nothing remains.
- `idd-factory-execute-subtask` executes one immutable task and returns a
  free-form human-readable report.

There are no research, checkpoint-review, final-review, or standalone replan
skills in the Factory protocol. Research can be included in an ordinary task
contract when it is necessary to make that task coherent; discoveries that
change future work are evaluated by the next planner after batch exhaustion.

Workers never return semantic control JSON. They do not select capabilities,
IDs, retries, corrections, or transitions. Runtime owns materialization,
ordering, verification, retry, recovery, persistence, and finalization.

Each attempt keeps semantic text separate from machine metadata:

```text
invocation.json       runtime-owned invocation identity
planning-output.md    planner-created batch document
semantic-result.md    executor's task-specific report
result.json           runtime-owned provenance and semantic-result path
process-telemetry.json
```
