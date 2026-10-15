# Factory skills

IDD Factory exposes two registered user commands: `idd-factory-run` and
`idd-factory-configure`. Configure can also be selected automatically by the agent;
selection alone never authorizes a write.
Planner and worker are internal canonical protocols owned by `idd-factory-run`.
Automatic Intent workflows remain available to the agent and are hidden from
the Claude Code slash menu. Codex currently has no equivalent documented
visibility switch, so its automatic workflows may remain visible.

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
`ExecutionProfile` through optional `.idd/execution.yaml`. New planner output
requires one canonical profile per task. Existing pending tasks in `plan.md`
without the section use `standard`. An absent policy or missing profile/platform
mapping inherits host settings. Partial mappings are valid; malformed existing
mappings block execution. Explicit overrides are applied exactly.

It does not launch a packaged runtime, use Factory MCP tools, supervise child
processes, poll status, maintain retry budgets, or own a workflow state machine.

## `idd-factory-configure`

The sole Factory model-management command handles initial configuration, exact
model or reasoning edits, targeted inheritance, read-only reviews/comparisons,
and evidence-based refreshes of `economy`, `standard` and `strong`.
It owns validation and authorized persistence of optional `.idd/execution.yaml`.
No other Factory model-update command or persistence entry point is published.

- **Initial setup:** choose once between `default (inherit)` and
  `configure` for explicit partial platform/profile mappings. An absent
  policy already means inheritance; review-only requests do not create one.
- **Exact changes:** user-specified model IDs are applied without autonomous
  substitution or duplicate confirmation. Only requested mappings/settings
  change, including independent Codex and Claude assignments. Returning one
  mapping to inherit removes it; requesting global inherit replaces the strategy.
- **Review-only:** compares the existing policy and candidates, but never
  writes or starts Factory, even when automatically invoked.
- **Refresh:** compare candidates against current host and platform information,
  fresh vendor documentation, and user constraints (cost, quality, latency,
  reasoning). Display current versus proposed mappings, supporting evidence,
  unchanged settings and availability uncertainty. Automatically chosen or
  interpreted mappings require explicit Apply/Edit/Reject confirmation.

Unselected profiles/platforms inherit host settings. The same model may be used
for multiple profiles. Optional `reasoningEffort` (Codex) and `effort` (Claude)
are preserved, including absence; a reasoning-only override without a model is
invalid. Known incompatibility with a preserved setting blocks the proposal
until separately resolved. Unknown availability or compatibility is reported
honestly instead of prompting silent fallback.

Before saving, configure validates the full existing policy, checks for
intervening edits, rebases only authorized operations, and either safely writes
and re-reads or reports no-op/conflict. It never modifies temporary Factory
state. The next worker spawn uses the updated policy, without replanning or
changing a running worker. Model IDs and account availability are never assumed
from model names.

## Internal planner protocol

The planner is not a user-invokable skill. `idd-factory-run` creates a fresh
planner child and supplies the complete packaged
`references/factory-planner.md` protocol.

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

New planner output requires `ExecutionProfile` exactly once for the immediately
preceding task. Existing `plan.md` tasks without it use `standard`. Empty,
repeated, or unknown explicit profiles are invalid. A profile expresses required
execution capability only,
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

## Internal worker protocol

The worker is not a public skill. Before every spawn, `idd-factory-run` reads
and explicitly supplies the complete packaged `references/factory-worker.md`
protocol to a fresh native child agent, together with a separate, single-task
assignment. When the optional Engineering layer exists, it also supplies the
complete `references/engineering-guardrails.md` contract.

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
