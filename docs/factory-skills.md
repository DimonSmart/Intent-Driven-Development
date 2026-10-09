# Factory skills

IDD Factory exposes three registered user commands: `idd-factory-run`,
`idd-factory-configure`, and `idd-factory-update-effort-models`.
`idd-factory-run` and `idd-factory-update-effort-models` can also be selected
by the agent automatically. `idd-factory-configure` remains manual-only.
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
`ExecutionProfile` through optional `.idd/execution.yaml`. Each task must
provide one canonical profile. If the file is absent, all profiles inherit
host behavior; an existing policy must explicitly select `inherit` or be
complete for the active platform. Explicit mappings are applied exactly and
are never semantically "improved" by the root agent.

It does not launch a packaged runtime, use Factory MCP tools, supervise child
processes, poll status, maintain retry budgets, or own a workflow state machine.

## `idd-factory-configure`

This reusable manual workflow owns `.idd/execution.yaml`.

It supports:

- explicit `modelStrategy: inherit` for using the current host model everywhere;
- concrete per-platform mappings for `economy`, `standard`, and `strong`;
- complete active-platform mappings, or an explicit `inherit` strategy;
- optional platform-specific reasoning settings;
- later reconfiguration or return to all-inherit.

When fine-grained configuration is requested, the active Coding Agent first
proposes one complete mapping from currently available information, clearly
states any account-availability uncertainty, and asks for confirmation. IDD
source does not contain a built-in table of recommended concrete models.

Dynamic aliases such as `cheapest`, `best`, `latest`, or `strongest` are
resolved during configuration to concrete model IDs; they are not persisted as
runtime identifiers.

## `idd-factory-update-effort-models`

Select `idd-factory-update-effort-models` directly or describe the requested
model update in natural language. Use it to refresh, upgrade, downgrade, or
replace the LLMs assigned to `economy`, `standard`, and `strong`. For example, ask to reduce cost
for economy only, strengthen standard, or replace an unavailable model.

These effort levels are execution profiles; platform reasoning settings remain
unchanged unless explicitly requested. The skill reads current policy and model
information, preserves unselected levels and other platforms, and presents the
current/proposed mapping with reasons and availability uncertainty. Concrete
recommended model IDs are not built into the skill.

Review-only requests return the proposal without writing. For application, the
skill hands the complete proposal, baseline, and user authorization to
`idd-factory-configure`, which remains the sole writer. Automatically selected
models require confirmation; exact authorized replacements or an already
confirmed proposal do not repeat that decision. Intervening edits are checked
before saving, and an unchanged policy is not rewritten.

Updates apply to the next worker spawn without a new plan. Running workers keep
their model. The workflow does not start Factory or mutate its temporary state.

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

`ExecutionProfile` is required exactly once and belongs to the immediately
preceding task. It expresses required execution capability only,
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
