# Factory Token Efficiency & Benchmarking

The native-agent Factory exists partly to prevent orchestration overhead from
growing with worker duration.

The key architectural rule is:

```text
worker execution duration
must not
proportionally increase root-agent turns or tokens
```

A long worker should be one native spawn plus a blocking/event-driven wait, not
a chain of short waits and parent model turns.

## Metrics

For a repeatable scenario record at least:

```text
root-agent input tokens
root-agent output tokens
total tokens
root-agent turns
planner invocations
worker invocations
wait-related root turns
wall-clock worker duration
verification result and correctness
```

Also check that worker command logs, large tool results, diffs, and test output
remain inside worker contexts instead of accumulating in the root context.
Workers should receive bounded task packets, not inherited root conversation
history.

## What to compare

The primary comparison is **direct single-agent implementation versus native
Factory**. Both runs must start from the same repository snapshot, receive an
equivalent task, use the same model and reasoning effort, and pass the same
independent verification and correctness assertions. A run that is cheaper but
fails or only partially completes the task is not an improvement.

Record the host and model versions, validation commands, prompt, snapshot,
configuration and run conditions. Compare repeated runs under equivalent
conditions; label measurements taken on different host versions explicitly.

During migration it is also useful to compare the last published runtime-based
Factory with the native-agent Factory on the same:

- repository snapshot;
- task;
- model and reasoning effort;
- project verification;
- correctness assertions.

Do not compare token totals for differing results or verification failures.
The historical architecture is a comparison baseline only. Do not preserve its
runtime code in the current implementation merely to keep the benchmark alive. Historical measurements can be retained as
data; do not restore its MCP server, attempt persistence or process supervision
solely to rerun the old benchmark.

After migration stabilizes, compare native Factory to a direct single-agent
implementation on the same task.

## Regression signals

Treat these as regressions:

- worker duration causes repeated root-agent model turns;
- the root transcript accumulates worker execution logs;
- a worker reads inherited root conversation rather than a bounded packet;
- redundant workers perform verification-only or status-only work;
- planner invocations occur while a current batch still has contractable tasks;
- polling replaces native terminal wait.

Absolute token thresholds are model/runtime dependent. Structural metrics are
usually more stable.

## Practical smoke task

Use a small deterministic task such as the repository's TwoStepCatalog or Bubble
Sort fixture. For an orchestration smoke benchmark, the task should require at least two
worker contexts but remain small enough that extra planner/worker turns are
obvious. Verify product behavior independently before judging token savings.

Record the host/model version alongside results. Token counts from different
host releases are not directly comparable without that context.
