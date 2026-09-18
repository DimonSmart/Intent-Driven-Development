# Factory Token Efficiency

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
```

Also check that worker command logs, large tool results, diffs, and test output
remain inside worker contexts instead of accumulating in the root context.

## What to compare

During migration it is useful to compare the last published runtime-based
Factory with the native-agent Factory on the same:

- repository snapshot;
- task;
- model and reasoning effort;
- project verification;
- correctness assertions.

The historical architecture is a comparison baseline only. Do not preserve its
runtime code in the current implementation merely to keep the benchmark alive.

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
Sort fixture. The task should be large enough to require at least two worker
contexts but small enough that unexpected extra planner/worker turns are
obvious.

Record the host/model version alongside results. Token counts from different
host releases are not directly comparable without that context.
