# IDD Factory Root (internal)

This is the **internal** Factory Root protocol, loaded by a fresh generic native
child of the Supervisor (future `idd-factory-run`). It owns the existing Factory
preflight, planning, implementation, provenance, execution policy, and verification.
Do not invoke this Skill as a public command or automatically start a new Factory
run merely because it was loaded. A complete bounded Supervisor assignment and
verified current run mode are required.

This staged canonical extraction preserves the existing one-level Factory
behavior until the Supervisor, batch limits and E2E gate are implemented.
Factory is a thin semantic orchestration skill over native child-agent
capabilities. It is not a workflow engine, process runtime, MCP transport,
retry engine, or durable implementation database.

## Required references

Read `references/engineering-guardrails.md` before using `.idd/engineering/`,
`references/project-verification.md` before project verification, and
`references/factory-execution-policy.md` before interpreting `ExecutionProfile`
or `.idd/execution.yaml`. The full preflight reference is required below.

## Native child readiness and spawn

Before any durable owner or Factory write, resolve the loaded
`idd-internal-factory-root/SKILL.md` to its real installed path. From the same installed
plugin root derive and verify these accessible files and their statically
declared mandatory references:

- `skills/idd-internal-factory-planner/SKILL.md`
- `skills/idd-internal-factory-worker/SKILL.md`

Use the exact installed version, never a guessed cache path, global name
search, source checkout or repository-relative path. Root inspects package
metadata and accessibility only. Each child reads its own protocol.

Check the actual native tool schema, including direct top-level tools. Require
fresh isolated contexts without inherited transcripts, access to installed
files and the shared working copy, bounded assignment delivery, terminal
results, blocking or event-driven wait, and native stop/interrupt with writer
termination evidence. Require exact model/effort overrides when policy calls
for them. Use the host's builtin generic child type if needed. A missing
Factory-specific `agent_type` or legacy role registration is valid. Prefer
native read-only Planner and workspace-writing Worker permissions when
available. Text instructions are not a sandbox. Missing required capabilities
or files block the run with a diagnostic. Use native tools throughout.

Before each spawn, recheck the selected Skill path and required capabilities.
Pass the semantic role, accessible absolute installed Skill path, explicit
working-copy path and bounded role packet. Require the child to read the entire
Skill and required references before project work, continuing truncated reads
until complete. Use quoted paths supported by the host. Root neither reads nor
relays child protocol bodies. Omit transcripts and full Intent/Engineering
trees. A child unable to load its Skill or validate its assignment returns
`BLOCKED` without work. Preserve phase state and any pending Task on failure.

On Codex MultiAgentV2 set `fork_turns: "none"` for both roles. Every native
`spawn_agent` call uses an explicit `task_name` matching
`^idd_factory_(planner|worker)_[0-9]+$`, for example `idd_factory_planner_01`
or `idd_factory_worker_01`. Use a fresh name for each attempt, including rejected
spawns. Inspect visible native calls for collisions in the shared host namespace
and keep counters out of project state. Pass the name as a tool argument and
use it only for trace correlation. Verify loading, settings and completion
from their own evidence. Platforms without this argument use their native
generic spawn schema.

## Factory Preflight — Root entrypoint

Read `references/intent-preflight.md` **in full** before starting or replacing a
run. It owns the logical request, requested scope, durable classifications,
blockers, baseline, mutation order, coverage, recovery and activation. If it
is unavailable, stop with an incomplete-installation diagnostic.

Apply its mode and scope rules even when invoked directly. `route-only` and
`intent-only` stop before orchestration. `intent-preserving` forbids mutating
Product or Engineering preflight.

Follow the reference's **New-run ordering**. Materialize the full request and
resolve determinable blockers before writes. Save the request, baseline and
pending provenance before authorized owner actions. Run Engineering management
first when required, then the authorized Product Intent owner. Verify coverage
against the full request and exact Q/A before initial planning. Use the Root
operations below to checkpoint actions, launch the initial Planner and activate.

On continuation or replacement follow the reference's recovery and replacement
rules. Reconcile pending evidence before retrying an owner. Preserve existing
active state until authorized replacement preflight and coverage succeed.

## Minimal temporary state

The presence of `.idd/factory/current/` alone is not active state.
Only `.idd/factory/current/request.md` is the authoritative active-run
marker. One pending preparation per working tree is sufficient:

```text
.idd/factory/preparation/
    request.md               # full materialized base request
    baseline.md              # immutable original commit and repository identity
    provenance.md            # bounded owner/Worker attribution checkpoints
    answers.md               # chronological exact question/answer pairs, optional
    question.md              # outstanding exact question, optional
    verification-failure.md  # bounded final-check diagnostic, optional

.idd/factory/current/
    plan.md                  # validated remaining Task batch
    baseline.md              # unchanged original baseline
    provenance.md            # preflight, Worker and unknown-change boundaries
    answers.md               # exact answers transferred from preparation, optional
    request.md               # active marker, written LAST
    completed.md             # active-run summaries, as needed
    question.md              # active-run question, optional
    verification-failure.md  # active-run failure, optional
```

A baseline commit is not a snapshot of the entire working tree: pre-existing
dirty code/tests are allowed and must not be attributed to Factory. Temporary
baseline and provenance are diagnostic, not normative or new active markers.

The repository is the authoritative implementation reality. `plan.md`
contains only the remaining implementation/research tasks. `completed.md`
contains bounded semantic summaries, not proof of completed code. Exact Q/A
are kept separately from the unchanged base request. The preparation directory
is ignored by Git through the existing `.idd/factory` rule.

`.idd/execution.yaml`, when present, is deliberately outside
`.idd/factory/current/`. It is project-owned execution policy that survives
Factory runs, not temporary Factory state or workflow/runtime configuration.

Do not introduce retry counters, process IDs, new lifecycle statuses or a
universal Git log. Only bounded path/hash/hunk provenance needed for recovery
and review is permitted; Git remains the sole normative history.

If a legacy `.idd/factory/current/state.json` exists, do not deserialize,
migrate, or continue its old state machine. Preserve product changes. An
explicit restart may reuse a valid self-contained legacy `request.md` as the
replacement request and may archive the old directory for diagnostics.

## User-level operations

- **New** follows the preflight and initial Planner flow.
- **Continue** uses active state when `current/request.md` exists. Otherwise
  resume valid preparation from the saved request, exact Q/A and pending
  question. A precise reply answers that question. Ask whether to continue or
  explicitly replace when a new independent request arrives. Report missing
  preparation inputs instead of reconstructing them from memory.
- **Cancel** removes pending preparation or archives/removes active state as
  appropriate. Repository and durable knowledge changes are preserved.
- **Replace** an inactive preparation only on explicit request by discarding
  that temporary directory and running new preflight. For active replacement,
  follow the reference's Engineering guard and preserve the current run until
  the authorized replacement is ready.

## Original Git baseline and bounded provenance — Root operations

Follow the baseline, provenance and replacement rules in the preflight
reference. For a new independent run only, run from the repository root:

```text
git status --porcelain=v1 -z --untracked-files=all -- .idd/intent/ .idd/engineering/
git rev-parse --verify "HEAD^{commit}"
```

Parse the NUL-delimited managed status, including rename pairs, and verify the
full commit SHA. Preserve the original baseline on continuation, recovery and
replacement.

Before each mutating owner or Worker, save the pending action and original
authorization in `provenance.md`. After a trusted result, record affected paths
and bounded before/after evidence. Treat unverified attribution as
`external-unknown`. If provenance, unavailable Git objects or partial
activation make a write/refinement unsafe, diagnose and preserve the evidence.
An unfinished legacy run keeps its original no-Intent-write boundary when it
had implementation-only scope. Ambiguous legacy scope/provenance forbids
Worker Intent writes.

## Preparation planner and activation — Root operations

After verified durable coverage, use the **Fresh Planner** packet in preparation.
Handle the result under the preflight reference:

1. **Question.** Save the exact question to `preparation/question.md` and stop
   before activation. On reply append exact Q/A to `preparation/answers.md`,
   remove the pending question and renew blocker/owner/coverage checks before a
   fresh Planner.
2. **Done.** Run `final` project verification. Success removes preparation and
   completes without an active marker or empty plan. Failure writes a bounded
   `preparation/verification-failure.md` and goes to a fresh Planner, following
   the verification rules below.
3. **Tasks.** Validate the entire fresh batch as described under **Planner
   outcomes**. Recheck that `current/request.md` is absent and reconcile any
   orphaned files against trusted preparation evidence. Write `current/plan.md`
   and transfer exact Q/A, the immutable baseline and provenance. Write the
   complete original `current/request.md` **LAST**, preferring a same-directory
   atomic rename. Then enter the Worker loop. A competing active marker blocks
   activation.

## Orchestration loop

While active, stop for pending user input. When `plan.md` has no remaining
Tasks, spawn a fresh Planner and handle its outcome below. Otherwise execute
the first remaining Task:

1. Validate the Worker packet, resolve native model settings immediately
   before spawn, and checkpoint the pending action.
2. Spawn one fresh Worker and wait natively for a trusted terminal result.
3. Only on trusted successful completion, record the after-checkpoint including
   unknown edits, append a short result to `completed.md`, and remove that Task
   from `plan.md`. Otherwise leave the Task first and stop the invocation.

### Fresh planner

Give the fresh read-only Planner the complete original request or its accessible
saved path, preparation/active phase, exact chronological Q/A, original baseline,
verified provenance, current Engineering discovery context and latest bounded
verification failure for that phase. Include completed summaries only in
active runs. Planner selects relevant Intent and Conditional Engineering
semantically. Execution policy is outside Planner inputs.

### Worker execution profile

For every Worker spawn, follow **Runtime lookup** in
`references/factory-execution-policy.md`. Re-read and validate the policy when
present, then use the exact resolved model and optional effort or omit both
overrides on inheritance. The reference distinguishes legacy missing profile
metadata from invalid explicit or new output. Report a blocking diagnostic
when validation fails or the host cannot honor required arguments. Keep the
resolved profile and the common Worker protocol.

### Fresh worker

Before spawn, check material current Intent/Engineering constraints,
request/answer freshness and baseline/provenance. Replan a stale Task batch.
Supply one self-contained Task, authorized scope, explicit user prohibitions
and accessible context paths. Resolve supplied stable IDs mechanically.
If Engineering is present, validate it and re-read its current INDEX to
enumerate all Always IDs separately from the Planner's Conditional IDs. Use
this packet, omitting empty optional sections:

```text
--- Factory worker assignment ---
# Task
<exactly one complete Task>
# TaskRelatedIntent
<selected IDD-NNNN identifiers>
# TaskRelatedEngineering
<planner-selected Conditional ENG-NNNN identifiers>
# AlwaysEngineering
<all current Always ENG-NNNN identifiers, if any>
--- End Factory worker assignment ---
```

Workers share the working copy, execute sequentially, run focused task-local
checks and return a short semantic result. A failed Skill load or guard leaves
the Task pending.

### Waiting and interruption

Use the platform's longest appropriate blocking or event-driven native wait.
A long execution alone warrants no model-driven polling. On timeout or wait
failure, stop or close the child natively when possible, preserve pending state
and end the invocation. Require confirmed termination before starting any
replacement writer in the same workspace.

At-least-once recovery uses a fresh Worker on the pending Task. Inspect actual
repository state and preserve correct partial work.

## Technical autonomy during execution

Planner chooses a compliant direction, delegates bounded implementation choices
or contracts focused research with observable evidence. A fresh Planner uses
that evidence before contracting dependent Tasks. Ask only for a user-owned
product, policy, trust, compatibility or external-obligation decision.

If realistic Worker choices could trigger different Conditional Rules, Planner
selects their relevant IDs or contracts a narrower approach or research first.
Worker honors applicable assigned rules and reports a blocker for missing
coverage, incompatible constraints or scope conflicts. Factory handles
replanning or stopping. Worker may refine existing Product Intent owners only
under its monotonic refinement contract. Engineering mutations stay with the
authorized owner before activation.

## Planner outcomes

Accept exactly one Planner result form: a nonempty batch of `# Task` contracts,
one concrete `# Question`, or exact `# Done`. Reject blank, mixed or malformed
output. Before saving a fresh batch, verify self-contained executable contracts,
exactly one canonical `ExecutionProfile` per Task, unique resolvable current
Intent and Conditional Engineering IDs, structural validity of any Engineering
layer, and durable/repository freshness. Use the preflight reference for preparation
outcomes. During active runs, a valid Task batch is saved in `plan.md`.

During an **active** run, a Planner `# Question` stops task execution:
persist exactly one `current/question.md`, then on answer append the
exact Q/A before a fresh Planner. Ordinary implementation decisions need
no durable write. Product changes use their authorized outer Intent
workflow. A **new durable Engineering decision** while
`current/request.md` exists must pause the run: no direct ENG edit,
no Engineering owner in the active run, no Worker Task substituting for
that decision, and no implicit cancellation. Completion/cancellation
followed by a new complete request is required. Do not resume an old
child transcript.

## Project verification and completion

When the Planner returns `# Done`, run the existing project checks in the
`final` context according to `references/project-verification.md`.

On verification success, record Implementation Result and perform the bounded
Intent Refinement Review below. Archive required review context in ignored
`.idd/factory/results/<run-name-or-time>/` when needed, then remove active
`current/request.md`. Deferred review never keeps Factory active; if archive
fails, preserve the only remaining evidence and report the limitation.

On verification failure, write only a bounded diagnostic to
`verification-failure.md`:

```text
check/command
exit result
bounded relevant diagnostic
```

Then invoke a fresh planner. The planner decides whether a correction task is
needed.

This also applies to infrastructure failures such as missing restore assets.
Do not repair a failed final check directly in the orchestration context and
retry it without the diagnostic and fresh planner handoff. After any correction,
wait for a new planner `# Done` and run every configured final check again.
An exit code of zero without evidence that tests actually executed is not proof
that those tests passed; report the missing evidence to the planner.

Do not create verification attempts, retry budgets, confirmation state,
correction workflows, or final-review roles. If project verification is not
configured, planner `# Done` after ordinary worker checks completes Factory.

## Intent Refinement Review

After final verification, re-read committed and uncommitted changes against
the immutable baseline, exact owner authorizations, preflight boundary,
Worker checkpoints and current Git status. Separate authorized preflight,
later authorized Intent workflow changes, candidate monotonic Worker edits,
and external/unknown changes. Unknown origin stays unknown and cannot become
approved merely because it was committed. Explain durable verified findings,
changed IDD paths, why the approved contract remains equivalent and how to
inspect the Git diff. No refinements => no empty approval ceremony.

Offer normal user/Git review choices: accept all or selected refinements,
reject isolated edits, request correction or defer. Implementation Result
and Intent Refinement Review are independent: pending valid refinements do
not fail a verified implementation, but an unresolved normative conflict does.
For selective rejection, re-read current files, check independent edits
sharing a hunk and code dependencies, then remove only safely isolated
changes and rerun affected checks. Never `git reset --hard`, blindly
restore a whole file or revert an unrelated commit. If safe separation is
impossible, stop with an explicit conflict and controlled correction.

Deferred context may be kept in ignored `.idd/factory/results/` with
baseline/authorization/necessary hunks; it is machine/worktree-local and
depends on available Git objects. Review later must revalidate current
patch applicability. Do not create a permanent approval DB or leave an
active `current/request.md` for deferred review.

## Design boundary

Semantic relevance belongs to agents. Mechanical operations belong to code or
the host.

Factory entry preflight coordinates owner workflows for normative Product
changes; `idd-engineering-change` remains Engineering mutation owner.
Planner is read-only; Factory Worker may only refine existing Product Intent
monotonically and never edit Engineering Rules.

Keep profile classification and Conditional applicability with semantic
agents. Use bounded host operations for lookup, persistence, validation and
Always enumeration. When the host lacks a mechanical hook, follow the exact
protocol steps. New lifecycle states, retry classes, transitions or recovery
protocols require separate justification.
