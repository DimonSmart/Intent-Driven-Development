# Factory skills

IDD Factory has four canonical skills. Three participate in a run; the fourth
configures persistent project execution policy.

## `idd-factory-run`

This is the orchestration entry point.

For a new run it performs Factory Preflight: materialize the complete logical
request, analyze Product Intent, hand explicit durable Engineering decisions to
`idd-engineering-change` when required, apply required Product Intent through
normal Intent workflows, validate durable coverage, and only then create active
Factory state. It then maintains minimal temporary continuation state, invokes a
fresh planner and sequential fresh workers, handles one planner question, and
runs configured project verification after planner `# Done`.

Immediately before each worker spawn it mechanically maps the task's
`ExecutionProfile` through `.idd/execution.yaml`. Missing profile metadata
means `standard`; missing policy/mapping means `inherit`. Explicit mappings
are applied exactly and are never semantically "improved" by the root agent.

It does not launch a packaged runtime, use Factory MCP tools, supervise child
processes, poll status, maintain retry budgets, or own a workflow state machine.

## `idd-factory-configure`

This reusable manual workflow owns `.idd/execution.yaml`.

It supports:

- explicit `modelStrategy: inherit` for using the current host model everywhere;
- concrete per-platform mappings for `economy`, `standard`, and `strong`;
- partial mappings, with missing entries inheriting host behavior;
- optional platform-specific reasoning settings;
- later reconfiguration or return to all-inherit.

When fine-grained configuration is requested, the active Coding Agent first
proposes one complete mapping from currently available information, clearly
states any account-availability uncertainty, and asks for confirmation. IDD
source does not contain a built-in table of recommended concrete models.

Dynamic aliases such as `cheapest`, `best`, `latest`, or `strongest` are
resolved during configuration to concrete model IDs; they are not persisted as
runtime identifiers.

## `idd-factory-decompose-task`

This is the planner.

A fresh planner inspects the original request, repository, relevant current
intent, exact user answers, bounded completed summaries, and the latest bounded
verification failure. It does not read `.idd/execution.yaml`.

A task may contain:

```text
# Task
<self-contained contract>

# ExecutionProfile
economy | standard | strong

# TaskRelatedIntent
IDD-NNNN

# TaskRelatedEngineering
ENG-NNNN
```

`ExecutionProfile` is optional and belongs to the immediately preceding task.
Its absence means `standard`. It expresses required execution capability only,
not a concrete model or cost policy.

Tasks/Question/Done cannot be mixed. `TaskRelatedIntent` is optional task
metadata and contains stable current intent IDs only. When
`.idd/engineering/` exists, `TaskRelatedEngineering` contains only
planner-selected Conditional `ENG-NNNN` IDs. The planner never puts Always
rules there.

Planning is incremental. Contract only implementation/research work knowable
now; do not speculate about later tasks whose contracts depend on unfinished
work. The planner never creates a task to mutate Product Intent or Engineering
Rules.

## `idd-factory-execute-subtask`

This is the worker.

Every task runs in a fresh isolated context against the shared current
repository. The root agent has already selected the model/context before the
worker begins. The worker never reads `.idd/execution.yaml`, changes the
execution profile, or chooses another model.

The worker receives one self-contained task plus optional `TaskRelatedIntent`
IDs, optional Conditional `TaskRelatedEngineering` IDs, and the current
mechanically enumerated `AlwaysEngineering` IDs. It resolves and reads those
documents itself, performs the implementation, runs focused task-local checks,
and returns a short semantic result. It never mutates `.idd/intent/*` or
`.idd/engineering/*`.

## Context isolation

A separate thread is not sufficient when the platform automatically forks the
parent transcript. The adapter must explicitly deliver a fresh semantic context.

Codex and Claude generated guidance describe how to apply an already-authorized
active-platform model override through native child-agent controls. Platform
adapters do not contain profile-to-concrete-model recommendations.
