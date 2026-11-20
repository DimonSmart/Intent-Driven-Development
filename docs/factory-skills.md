# Factory skills

IDD Factory exposes two registered user commands: `idd-factory-run` and
`idd-factory-configure`. Configure can also be selected automatically by the agent;
selection alone never authorizes a write.
The public `idd-factory-run` is a two-level Supervisor. Factory Root, Planner
and Worker are three internal Skills shipped in the same installed Factory
package, not additional public commands. The Supervisor launches fresh Roots;
each Root launches fresh native Planner/Worker children. Every child loads its
full internal Skill from the installed package without inheriting transcripts.
Automatic Intent workflows remain available to the agent and are hidden from
the Claude Code slash menu. Codex currently has no equivalent documented
visibility switch, so its automatic workflows may remain visible.

## `idd-factory-run`

This is the public Supervisor entry point. It validates the installed internal
Root Skill and nested native host capabilities, selects the entry mode, and
dispatches one fresh Root at a time. On `CONTINUE / BATCH_LIMIT`, it verifies
the complete plan-boundary checkpoint and stopped descendants before dispatching
the next Root. It never performs Preflight, plans Tasks, launches Planner/Worker,
edits Factory state or runs final project verification.

For a new run the **internal Root** performs Factory Preflight: it materializes
the complete request, analyzes Product Intent, coordinates authorized Engineering
and Product owner workflows, and validates durable coverage. Root saves the
request in `.idd/factory/preparation/request.md` before owner mutations and
invokes a fresh initial Planner WHILE FACTORY IS INACTIVE. Initial `# Question`
saves exact Q/A and triggers renewed authorized preparation before a fresh
Planner. Initial `# Done` runs configured final verification without activating
Factory. Initial valid Tasks are saved in `current/plan.md`; Root writes
`current/request.md` LAST, then executes Workers sequentially.

Immediately before each Worker spawn, **Root** mechanically maps its
`ExecutionProfile` through optional `.idd/execution.yaml`. New planner output
requires one canonical profile per Task; existing pending Tasks with no profile
use `standard`. An absent policy or missing profile/platform mapping inherits
host settings. Malformed explicit mappings block; valid overrides apply exactly.

Neither Supervisor nor Root launches a packaged runtime, uses Factory MCP
transport, supervises external child processes, polls status, maintains retry
budgets or owns a new workflow state machine.

## `idd-factory-configure`

The sole Factory model-management command handles initial configuration, exact
model or reasoning edits, targeted inheritance, read-only reviews/comparisons,
and evidence-based refreshes of `economy`, `standard` and `strong`.
It owns validation and authorized persistence of optional `.idd/execution.yaml`.
No other Factory model-update command or persistence entry point is published.

Custom Factory agent registration is no longer part of this command.
Requests to inspect obsolete project/user agent definitions receive an explanation
of the internal-Skill mechanism; such files are not migrated or deleted.
See [Factory installation](factory-native-agent-installation.md).

- **Initial setup:** choose once between `default (inherit)` and
  `configure` for explicit partial platform/profile mappings. For selected
  mappings, propose model and optional effort together, with an explicit
  `host default (omitted)` option. An absent policy already means
  inheritance; review-only requests do not create one.
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
for multiple profiles with different effort values. Optional `reasoningEffort`
(Codex) and `effort` (Claude) are preserved, including absence; a reasoning-only
override without a model is invalid. For an explicit model, omitted effort means
the child host/model default, not necessarily the parent session's effort.
Guided proposals must check model/effort support, avoid hardwired
profile-to-effort rules, and show effort alongside the model. Clearing an effort
removes only its field. Known incompatibility with a preserved setting blocks the proposal
until separately resolved. Unknown availability or compatibility is reported
honestly instead of prompting silent fallback.

Before saving, configure validates the full existing policy, checks for
intervening edits, rebases only authorized operations, and either safely writes
and re-reads or reports no-op/conflict. It never modifies temporary Factory
state. The next worker spawn uses the updated policy, without replanning or
changing a running worker. Model IDs and account availability are never assumed
from model names.

## Internal planner protocol

The Planner is not a standalone public command. The internal Root resolves
the installed Planner `SKILL.md` from its own installed Factory package
and spawns a fresh generic child with that exact path and bounded
request/phase/baseline/provenance packet. The child reads its entire protocol
before planning and rejects incomplete packets with `BLOCKED`.

The same fresh Planner protocol accepts either
`preparation/request.md` and `preparation/answers.md` before activation, or
`current/request.md` and `current/answers.md` afterward. These carry the
unchanged original request and chronological exact Q/A as separate inputs.
The Planner inspects the repository and relevant current Intent and Engineering,
with bounded completed summaries only after Workers have started and the
phase-appropriate verification failure when present. It does not read
`.idd/execution.yaml`.

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

New Planner output contains no more than **4** complete `# Task`
sections; an oversized result is invalid before persistence/Worker
execution and must not be truncated. Choose coherent independent work
without filler Tasks; existing saved plans larger than four remain valid.
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

The Worker is not a public command. Before each spawn, Root verifies the installed
internal Worker SKILL.md path and sends it to a fresh generic child together with
one bounded Task, selected Intent/Engineering IDs, scope and provenance. The
child reads the whole Skill and required references first and returns `BLOCKED`
if required context is missing. Root does not read or relay the protocol.

Every task runs in a fresh isolated context against the shared current
repository. The root agent has already selected the model/context before the
worker begins. The worker never reads `.idd/execution.yaml`, changes the
execution profile, or chooses another model.

The worker receives one self-contained task plus optional `TaskRelatedIntent`
IDs, optional Conditional `TaskRelatedEngineering` IDs, and the current
mechanically enumerated `AlwaysEngineering` IDs. It resolves and reads those
documents itself, performs the implementation, runs focused task-local checks,
and returns a short semantic result. Only Factory Worker may make the existing
strictly monotonic refinements to existing Intent owners when authorized; it
cannot edit Engineering Rules or change approved product decisions.

## Context isolation

A separate thread is not sufficient when the platform automatically forks the
parent transcript. The adapter must explicitly deliver a fresh semantic context.

Codex and Claude generated guidance describe how to apply an already-authorized
active-platform model override through native child-agent controls. Platform
adapters do not contain profile-to-concrete-model recommendations.

Internal Skill installation and host limitations are documented in
[Factory native-agent installation](factory-native-agent-installation.md).


## Two-level Supervisor and bounded internal Root

`idd-factory-run` is a thin public Supervisor. It resolves and validates the
internal `idd-internal-factory-root/SKILL.md` from the same installed plugin,
checks real nested native child capability and spawns one fresh Root per batch.
The positive invocation-local `max_workers_per_batch` defaults to **8**
and is a minimum Worker-count **target** for rotation, not a hard cap
or a setting saved to `.idd/execution.yaml`.

Each internal Root owns the Factory execution contract:
Preflight, Engineering/Product owners, Planner/Worker,
pending provenance, ExecutionProfile routing, project verification,
Intent Refinement Review, recovery and cleanup. After every verified
Worker checkpoint Root increments its local counter. It MUST execute
the rest of the current Planner plan before a normal `BATCH_LIMIT`
rotation, even if that leads to `CompletedWorkers > max_workers_per_batch`.
A confirmed boundary includes all Workers stopped, completed results
reconciled across `completed.md`, `plan.md` and `provenance.md`,
and no pending checkpoint. An empty plan is not proof by itself.
Root returns CONTINUE only at such a boundary with an actual count
at least the target; the next Root continues the unchanged saved
request with a fresh Planner (possibly `# Done`), not a transcript.

Supervisor never performs these project operations or replays Workers.
It verifies Root's terminal result and the plan boundary mechanically.
NEEDS_INPUT, COMPLETED, BLOCKED or ambiguous state/writer-stop
evidence end automatic spawning.

Codex native task names are batch-scoped; old flat
`idd_factory_planner_01`/`idd_factory_worker_01` are historical report
inputs only. Both Codex and Claude packages contain all three internal
Skills, but compatibility must be established independently on each host.

