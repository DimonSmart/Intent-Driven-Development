# Factory skills

IDD Factory intentionally has only three canonical skills.

## `idd-factory-run`

This is the orchestration entry point.

It performs or reuses Intent Preflight, maintains minimal temporary continuation
state, invokes a fresh planner, invokes fresh workers sequentially, handles one
planner question, and runs configured project verification after planner
`# Done`.

It does not launch a packaged runtime, use Factory MCP tools, supervise child
processes, poll status, maintain retry budgets, or own a workflow state machine.

Required host capabilities are native child spawn, fresh/no-parent-history
context, shared repository access, terminal wait without model polling, final
result retrieval, and stop/close control.

## `idd-factory-decompose-task`

This is the planner.

A fresh planner inspects the original request, repository, relevant current
intent, exact user answers, bounded completed summaries, and the latest bounded
verification failure.

It returns exactly one of:

```text
# Task
<self-contained contract>

# TaskRelatedIntent
IDD-NNNN

# TaskRelatedEngineering
ENG-NNNN

# Question
<one question>

# Done
```

Tasks/Question/Done cannot be mixed. `TaskRelatedIntent` is optional task
metadata and contains stable current intent IDs only. When
`.idd/engineering/` exists, `TaskRelatedEngineering` is optional task metadata
containing only planner-selected Conditional `ENG-NNNN` IDs. The planner never
puts Always rules there.

Planning is incremental. Contract only work knowable now; do not speculate about
later tasks whose contracts depend on unfinished work.

There is no `RelevantCompletedWork` protocol. If a later task needs a semantic
fact not recoverable from repository reality, the next planner embeds only that
fact directly in the self-contained task.

## `idd-factory-execute-subtask`

This is the worker.

Every task runs in a fresh isolated context against the shared current
repository. The worker receives one self-contained task plus optional
`TaskRelatedIntent` IDs, optional Conditional `TaskRelatedEngineering` IDs,
and the current mechanically enumerated `AlwaysEngineering` IDs.

The worker mechanically resolves every selected ID to exactly one current
Intent or Engineering document and reads it itself. Immediately before every
worker execution, the orchestrator recomputes the Always set from the current
Engineering INDEX. The root orchestrator does not load full documents merely to
forward them.

The worker does not select additional Conditional Engineering Rules and does
not edit Engineering Rules.

A worker may be re-run after interruption. It inspects current repository
reality, keeps correct partial work, completes the task, runs focused task-local
checks, and returns only a short semantic result.

Workers do not modify durable intent or Factory scheduling state and do not
decide retries, replanning, completion, or finalization.

## Context isolation

A separate thread is not sufficient when the platform automatically forks the
parent transcript. The adapter must explicitly deliver a fresh semantic context.

Codex generated guidance uses native spawn/wait and explicitly requires history
inheritance to be disabled. Claude is supported only when its native subagent
mechanism can provide the same semantic properties; otherwise Factory is
unsupported on that host rather than emulated through a custom runtime.
