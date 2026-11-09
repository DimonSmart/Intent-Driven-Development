# IDD Factory Run

Use Factory for an implementation request that benefits from temporary
multi-task orchestration. Factory is a thin semantic orchestration skill over
native child-agent capabilities. It is not a workflow engine, process runtime,
MCP transport, retry engine, or durable implementation database.

## Required references

Read `references/intent-preflight.md` before starting or replacing a Factory
run. `references/engineering-guardrails.md` before using an optional
`.idd/engineering/` layer. Read `references/project-verification.md` before
project verification. Read `references/factory-execution-policy.md` before
interpreting `ExecutionProfile` or `.idd/execution.yaml`.

## Required host capabilities

Factory requires the platform adapter to provide all of these properties:

- spawn a child agent in a fresh isolated semantic context;
- explicitly avoid inheriting the parent transcript;
- give the child access to the current repository;
- wait for a terminal child result without model-driven polling;
- receive the final child result;
- stop or close a child when necessary.

Use a read-only planner and workspace-writing workers when the platform can
express those sandboxes natively. If the host cannot provide the required
properties, Factory is unsupported on that host. Do not recreate the removed
C# runtime, MCP transport, shell process supervisor, status protocol, or polling
loop to compensate.

A new thread is not sufficient if it automatically receives the parent history.

## Registered native agent gate and assignment transport

Internal native agents are `idd-internal-factory-planner` and
`idd-internal-factory-worker`. On Claude Code the plugin-scoped identifiers
are `idd-factory:idd-internal-factory-planner` and
`idd-factory:idd-internal-factory-worker`; use the exact name reported by
the installed host. On Codex use their registered custom-agent `agent_type`,
never a generic child with a role-like `task_name`.

Before **any durable owner or Factory write** during new-run preflight, inspect
the installed host's native child-agent capabilities and resolve BOTH registered
roles unambiguously: exact type identity, host-loaded developer/system
instructions, fresh context, repository access, terminal wait and stop/close.
Repeat the concrete role-resolution check immediately before every spawn. A
missing, overridden, ambiguous, corrupt or unresolvable role blocks Factory;
neither Root nor a generic agent may substitute the role or discover a child
Skill from a short prompt. Internal names are not authorization.

Root must not read, reproduce, quote, or transmit the Planner/Worker protocol
bodies. The host supplies the registered instructions to the selected child.
Both child definitions include the canonical Engineering Guardrails contract
as role-local generated content; Root may read its own
`references/engineering-guardrails.md` for preflight, but never forward the
complete contract in place of the child's instructions.

Every spawn carries only the bounded Factory assignment and accessible paths
(or exact materialized request/answers where needed). Resolve paths against
the **child repository working directory**, not the plugin source tree or a
parent-only variable. Require the child's assignment guard and all required
references. A role without a complete verified packet returns `BLOCKED`
without work. Missing host capabilities, incorrect child identity, absent
instructions, malformed results, denied model overrides or unconfirmed child
termination are fail-closed: report host, role, phase and reason; preserve
pending work and forbid concurrent replacement writers. Do not switch to a
legacy Root-relayed protocol or custom runtime.

## Factory Preflight

For a new run, first resolve one complete self-contained logical request. Preserve
the user request text and materialize pasted text, explicitly supplied textual
content, and request-local textual documents that the user identifies as part of
the task. Host-local paths and attachment identifiers are transport metadata, not
durable semantics. The materialized request is authoritative for both preflight
and the later Factory run; do not replace it with a route summary, generated
summary, implementation plan, Engineering summary, or Intent diff.

Resolve the requested scope before durable work:

- `route-only`: report routing/diagnostics only; perform no durable mutation and
  do not start Factory;
- `intent-only`: durable Product Intent preparation and Engineering management
  are allowed when required, but implementation and Factory orchestration do not
  start;
- `intent-preserving`: no changes to approved product decisions or Engineering
  Rules. Only Factory Workers may add verified monotonic refinements to existing
  Intent owners when not forbidden by the user. Never run mutating preflight
  in this scope or silently expand it to `end-to-end`;
- `end-to-end`: durable preparation may be followed by implementation and
  verification.

Run the existing Product Intent classification from Intent Preflight before any
durable write:

```text
AlreadyCovered
ExplicitIntentChange
MissingIntentDecision
ImplementationOnly
```

For `ExplicitIntentChange`, determine the safe Intent handoff/owner, but defer
the actual Product Intent mutation until Engineering management has completed
successfully or as a no-op. Product Intent mutations remain owned by
`idd-intent-change`, including its internal document-creation workflow.
Factory Planner is read-only; only authorized Intent owner workflows may
change normative product decisions. Factory Workers may add monotonic
refinements in existing Intent owners, preserving the effective contract.

Separately classify only whether Engineering management is a durable concern:

```text
NoEngineeringManagementNeeded
EngineeringManagementRequired
EngineeringDecisionMissing
```

`EngineeringManagementRequired` means that the complete request explicitly
contains one or more durable implementation-only project decisions. It does not
authorize Factory to determine `new`, semantic equivalence, owning ENG ID,
add/modify/remove/no-op actions, allocator changes, or INDEX edits. Those remain
exclusively owned by `idd-engineering-change`.

Do not promote task-local implementation details into Engineering policy. A
technology name, class/file name, DI wiring step, implementation order, test
step, temporary migration instruction, or local library-use instruction is not
by itself a durable project-wide Engineering decision.

Distinguish existing mandatory contracts, autonomous implementation choices
and new durable project policies. An unchosen library, algorithm, private
architecture, new-app identifier or researchable API fact is not by itself
EngineeringDecisionMissing. Normal technical choices need no user delegation;
"choose yourself" cannot waive existing Rules, change product behavior,
authorize external purchases/registration or choose a new trust policy.
Policy changes still use the owner workflow and lifecycle guard.

Request-level MissingIntentDecision and genuine EngineeringDecisionMissing
return a question/diagnostic before durable writes, `preparation/`, and
the initial Planner. Only after successful preflight may the initial Planner
create `preparation/question.md`. Do not ask users to decide verifiable
platform facts.

Before any durable write, detect every request-level blocker that can be decided
without invoking a mutation owner, including `MissingIntentDecision`,
`EngineeringDecisionMissing`, conflicting requirements inside the request,
requested-scope violations, inability to materialize the complete request, and
an Engineering-changing replacement while an active Factory request exists. A
request-level blocker means no Product mutation, no Engineering mutation, and no
new Factory state.

For a new `end-to-end` run, the required order is:

```text
1. materialize the complete original logical request
2. resolve Requested scope, analyze Product Intent and Engineering disposition
3. detect all request-level blockers without writing anything
4. require clean managed IDD documents and a valid original Git HEAD
5. save preparation/request.md, baseline.md, provenance.md BEFORE owner mutations
6. record an open before-checkpoint; invoke Engineering owner if required
7. success/no-op -> record bounded owner after-checkpoint; ambiguous/blocked
   or interrupted -> keep preparation for recovery, never activate Factory
8. checkpoint and invoke authorized Product Intent owner workflows
9. validate coverage of the full request and exact saved answers
10. persist final verified preflight boundary in provenance.md
11. run a fresh initial Factory planner while current/request.md is absent
12. handle Question/Done or validate the first Task batch
13. transfer plan, answers, original baseline and provenance to current/
14. create current/request.md LAST as the only active marker
15. execute sequential Workers, keeping bounded before/after checkpoints
```

The Engineering step deliberately precedes the Product Intent write because
`idd-engineering-change` owns authoritative Engineering ownership/ambiguity
planning and has no separate dry-run protocol. This ordering avoids writing
Product Intent before Engineering ownership is resolved. It is not a transaction: owner failure after preparation is saved
preserves earlier writes and recovery context, not a new active Factory state.
Never automatically rollback or infer authorization from an interrupted write.

Coverage validation is against the complete logical request, not a generated
summary. Confirm that required Product behavior is represented, every explicit
Engineering concern was handled by `idd-engineering-change` with `success` or
`no-op`, material safety/compatibility constraints and explicit non-goals were
not lost, implementation-only details did not leak into Product Intent,
task-local details did not leak into Engineering, the two durable layers do not
contradict one another, and no required durable decision remains unresolved.
When `.idd/engineering/` exists, its structure must also be valid. Absence of
the Engineering layer is valid and produces no warning.

Successful durable coverage authorizes the initial planner, NOT activation.
The complete request, immutable baseline and bounded provenance MUST already
be saved in preparation before any mutating preflight owner was invoked. `.idd/factory/current/request.md` is created only after the initial
planner returns a fully validated executable Task batch, with active `plan.md`
and any required answers prepared first. Preserve the base request exactly; never
replace it with a generated summary, plan, or answers.

Continuation of existing runs and saved preparation never repeats new-run
cleanliness checks, recreates baseline, or replays completed owners blindly.

For a replacement with a genuinely active marker, keep the original baseline
and inherited dirty code/Intent edits with known or unknown attribution.
Do not apply the new-run cleanliness gate or claim inherited edits as new.
If effective contract/provenance cannot be recovered, keep old active state
and stop. Without an active marker this is preparation recovery or a new run.

For an explicit replacement request, resolve the complete replacement request
while leaving the current Factory state untouched. Replacement that needs no
Engineering mutation keeps the existing replacement Product Intent semantics:
run replacement preflight/coverage first and replace temporary state only after
success. If the active replacement request is
`EngineeringManagementRequired`, stop before any durable write. Do not bypass
the `idd-engineering-change` active-run guard, auto-cancel/restart the current
run, archive/unarchive it as a trick, or create a suspended/pending lifecycle.
Report that the current run must be completed or cancelled and the complete
replacement request then started as a new Factory run.

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

New:
- materialize the full logical base request and resolve requested scope;
- execute existing durable preflight and coverage validation;
- persist preparation, run the fresh initial planner and handle its result;
- activate only on a validated Task batch, then enter the worker loop.

Continue:
- if `current/request.md` exists, use ordinary active-run continuation
  without repeating entry preflight or initial planning;
- otherwise if `preparation/request.md` exists, restore the full base request,
  exact Q/A and pending question without depending on parent transcript;
- treat a precise reply as the answer to the saved `question.md`; when a new
  independent request appears, ask whether to continue or explicitly replace
  the pending preparation instead of silently combining two requests;
- incomplete preparation blocks automatic execution. Explain which source is
  missing rather than reconstructing it from memory.

Cancel:
- remove pending preparation, or archive/remove the active state as appropriate;
- never roll back repository or durable-knowledge changes automatically.

Restart / explicit replacement:
- preserve existing active replacement semantics and guard;
- an independent new request cannot silently overwrite pending preparation;
- on explicit replacement of preparation, discard only that temporary directory
  and perform new preflight. Existing active runs retain their own rules.

These are skill-level operations, not new transport operations or state schemas.

## Original Git baseline and bounded provenance

The clean-document gate applies **only to a new independent Factory run**,
not continuation, preparation recovery or genuine active-run replacement.
From the repository root run `git status --porcelain=v1 -z --untracked-files=all
-- .idd/intent/ .idd/engineering/`, parsing NUL-delimited status/rename
paths (staged, unstaged, untracked, deletions and renames). Missing optional
Engineering is valid; dirty code/tests elsewhere are not blocking. Recheck
before the first owner write if another process may have changed managed docs.
On any managed dirty path, stop before creating preparation, show the paths,
and never auto-commit. Require an actual full commit SHA from
`git rev-parse --verify "HEAD^{commit}"`; pass the revision as one literal
argument (quote it in PowerShell or other shells), and never invent a baseline.
If a check fails, verify the working directory and command invocation before
concluding that the repository has no commits.

After request-level blockers are ruled out and BEFORE any possibly mutating
owner workflow, durably write the full request, `preparation/baseline.md`
(original immutable HEAD, sanitized repo identity, clean managed-document
checkpoint, without secrets/host path as sole identity), and
`preparation/provenance.md`. Save a bounded pending before-checkpoint for
every owner/Worker invocation: phase (`preflight`, `worker`, or
`external-unknown`), owner/action, IDD paths, HEAD, and necessary
content hashes/hunks or diff identifiers. After trustworthy success/no-op,
record affected paths, after-state, result and the exact saved request/Q/A
authorization for normative owner changes. The preflight boundary is recorded
before first Worker. An owner label, commit or passing test does not establish
authorization; a crash between write and after-record leaves unknown
provenance, not an approved requirement or Worker refinement.

The effective normative contract consists of the original approved
requirements plus exact owner-authorized changes; Worker refinements never
replace or enlarge it. Baseline-to-HEAD diff is not sufficient for
attribution (commits, uncommitted and preexisting code/external edits can
overlap). Save only bounded review evidence, not full snapshots or a separate
Git history. If baseline objects/identity are unavailable or Git history was
rewritten, report the limitation; never reset baseline to current HEAD.
Preserve code and safe recovery but block autonomous Intent refinement.

Recover existing preparation by re-reading saved request/answers, baseline,
pending checkpoints and current documents. Avoid blindly repeating a
possibly completed mutating workflow; use trusted owner results plus semantic
comparison against exact authorization. If origin remains unclear, preserve
files/preparation and stop the unsafe action with an explicit diagnostic.
Continuation never requires cleanliness or a new commit. Legacy pre-baseline
runs resume conservatively with no newly granted Intent write permission;
missing baseline from a known-new run is corrupted recovery state.

On partial activation, validate inactive `current/plan.md`, `baseline.md`,
`provenance.md` and answers against trusted preparation; a partial marker
with missing/mismatched plan/baseline blocks Worker. Write `current/request.md`
last; it remains the sole active-run marker. Native timeout does not prove
the old Worker stopped writing: confirm close/termination before new writer.

## Preparation planner and activation

This path applies to new implementation-capable runs only; `route-only` and
`intent-only` never spawn an initial planner. Requested scope remains binding
through all follow-up questions. A reply does not implicitly promote
`intent-preserving` to `end-to-end`. An unfinished legacy run with explicit
`implementation-only` scope keeps its original no-Intent-edit semantics;
ambiguous legacy scope/provenance forbids Worker Intent writes. The installed
plugin version does not imply permission.

1. Following successful coverage, verify already saved complete
   `preparation/request.md`, original `baseline.md` and `provenance.md`.
   Resolve the registered native Planner and invoke the SAME fresh type used
   for active runs. Give it the unchanged base request or verified saved path,
   chronological exact Q/A (if any), repository access, Engineering discovery
   metadata and bounded preparation verification failure. The child's own
   host-loaded instructions include the canonical Engineering contract.
   No `completed.md` exists before any Worker. No active marker exists yet.
2. `# Question`: require exactly one concrete question. Save the exact text
   to `preparation/question.md` before asking the user and stop this turn.
   Do NOT write `current/request.md` or `current/plan.md`, or spawn a Worker.
3. On the next exact answer, append the original question AND answer verbatim
   and in order to `preparation/answers.md` before removing the pending question.
   Reclassify the complete base request plus all applicable clarifications,
   against the current repository and original requested scope. Detect
   request-level blockers BEFORE any further durable write. An unclear,
   conflicting, or forbidden decision blocks or elicits one concrete question.
4. When clarification requires authorized durable Engineering changes, pass
   the FULL base request, accumulated exact clarifications, and latest exact
   Planner question/answer to normal `idd-engineering-change`. Product Intent
   changes use the normal `idd-intent-change` workflow. If both are needed,
   complete Engineering before Product Intent. Factory never owns semantic
   ENG mapping or directly edits either durable layer. Treat owner `ambiguous`
   or `blocked` as stop conditions and retain preparation; `success` or
   `no-op` can continue after renewed coverage validation. No automatic
   rollback of an already completed owner's writes.
5. Launch a NEW isolated planner after each successful clarification and
   validation. Never resume the earlier child or inherit its transcript.
   Do not repeatedly ask an unchanged question without new user input or
   material repository change; stop with the question or diagnostic instead.
6. `# Task`: require one or more complete self-contained Task contracts,
   exactly one explicit `# ExecutionProfile` per new Task from
   `economy | standard | strong`, and no mixed Question/Done output.
   Verify each `TaskRelatedIntent` has an existing unique `IDD-NNNN`
   owner and each `TaskRelatedEngineering` is an existing unique Conditional
   `ENG-NNNN` (never Always). Validate the existing Engineering layer
   structurally when present. Recheck requested scope, durable coverage and
   repository/knowledge freshness; any material changes invalidate the batch
   and require renewed validation and fresh planning.
7. Recheck `current/request.md` is absent. Prepare a valid
   `current/plan.md` containing THAT exact Task batch and transfer exact
   `answers.md`, original `baseline.md` and `provenance.md` before activation;
   validate orphaned inactive files against preparation. Write complete
   `current/request.md` LAST, preferably by an atomic
   same-directory rename of a fully written temporary file. Only after BOTH
   marker and valid plan are present may the existing sequential Worker loop
   start. After confirmed activation remove preparation. Never overwrite a
   competing active marker or claim multi-file atomicity/exactly-once execution.
8. `# Done` before activation: use existing project verification in the
   `final` context, honoring the existing absent-policy completion fallback.
   On success, clean preparation and finish WITHOUT active marker or empty plan.
   On failure, persist only check/command, exit result and bounded diagnostic in
   `preparation/verification-failure.md`, then invoke a fresh planner.
   `Not verified` or required human confirmation cannot be reported as success.
   If the same failure persists after another Done without intervening change,
   stop with diagnostic rather than looping or fixing checks in the root.
9. Empty/mixed/malformed Planner output, unresolved IDs, invalid profile,
   infrastructure/owner blockers or interrupted activation are NOT success,
   Question or executable Tasks. Preserve preparation and report the exact
   reason; do not launch workers or invent automatic retry machinery.

Before the active marker exists, stray `current/plan.md` or legacy
`current/state.json` does not signal an active run. Safe resume verifies
preparation provenance and the entire pending batch before replacing any
orphaned files. After the marker exists, use existing active-run behavior and
at-least-once Worker convergence. Never move/delete an active marker to get
around the Engineering guard.

## Orchestration loop

Conceptually:

```text
while true:
    if waiting for user:
        stop

    if plan.md has no remaining task:
        run a fresh planner

        Question -> persist question.md and stop
        Done     -> run project verification
        Tasks    -> require one canonical ExecutionProfile per new task, then persist in plan.md

    take the first remaining task
    read its ExecutionProfile; missing metadata in existing plan.md means standard
    reject empty, repeated, or unknown explicit profiles
    if .idd/execution.yaml is absent, inherit host settings
    otherwise re-read and structurally validate .idd/execution.yaml
    mechanically resolve the active-platform profile mapping
    if that profile/platform mapping is absent, inherit host settings
    deterministically re-read the current Engineering INDEX
    enumerate all current Always ENG IDs
    verify registered worker type and prepare the isolated worker assignment
    record pending Worker before-checkpoint in provenance.md
    spawn a fresh native worker with the resolved native model override
    wait natively for its terminal result

    if no trusted completed result:
        leave the task first in plan.md
        stop

    record bounded Worker after-checkpoint, including unknown edits
    append a short result to completed.md
    remove the completed task from plan.md
```

This is conceptual behavior, not a requirement to implement a new programmatic
state machine.

### Codex MultiAgentV2 spawn identity

On Codex MultiAgentV2, **every Factory-owned native `spawn_agent` call**
(planner and worker) MUST include a unique explicit `task_name` argument:
`idd_factory_planner_01`, `idd_factory_planner_02`, ... for planners,
or `idd_factory_worker_01`, `idd_factory_worker_02`, ... for workers.
Only names matching `^idd_factory_(planner|worker)_[0-9]+# IDD Factory Run

Use Factory for an implementation request that benefits from temporary
multi-task orchestration. Factory is a thin semantic orchestration skill over
native child-agent capabilities. It is not a workflow engine, process runtime,
MCP transport, retry engine, or durable implementation database.

## Required references

Read `references/intent-preflight.md` before starting or replacing a Factory
run. `references/engineering-guardrails.md` before using an optional
`.idd/engineering/` layer. Read `references/project-verification.md` before
project verification. Read `references/factory-execution-policy.md` before
interpreting `ExecutionProfile` or `.idd/execution.yaml`.

## Required host capabilities

Factory requires the platform adapter to provide all of these properties:

- spawn a child agent in a fresh isolated semantic context;
- explicitly avoid inheriting the parent transcript;
- give the child access to the current repository;
- wait for a terminal child result without model-driven polling;
- receive the final child result;
- stop or close a child when necessary.

Use a read-only planner and workspace-writing workers when the platform can
express those sandboxes natively. If the host cannot provide the required
properties, Factory is unsupported on that host. Do not recreate the removed
C# runtime, MCP transport, shell process supervisor, status protocol, or polling
loop to compensate.

A new thread is not sufficient if it automatically receives the parent history.

## Registered native agent gate and assignment transport

Internal native agents are `idd-internal-factory-planner` and
`idd-internal-factory-worker`. On Claude Code the plugin-scoped identifiers
are `idd-factory:idd-internal-factory-planner` and
`idd-factory:idd-internal-factory-worker`; use the exact name reported by
the installed host. On Codex use their registered custom-agent `agent_type`,
never a generic child with a role-like `task_name`.

Before **any durable owner or Factory write** during new-run preflight, inspect
the installed host's native child-agent capabilities and resolve BOTH registered
roles unambiguously: exact type identity, host-loaded developer/system
instructions, fresh context, repository access, terminal wait and stop/close.
Repeat the concrete role-resolution check immediately before every spawn. A
missing, overridden, ambiguous, corrupt or unresolvable role blocks Factory;
neither Root nor a generic agent may substitute the role or discover a child
Skill from a short prompt. Internal names are not authorization.

Root must not read, reproduce, quote, or transmit the Planner/Worker protocol
bodies. The host supplies the registered instructions to the selected child.
Both child definitions include the canonical Engineering Guardrails contract
as role-local generated content; Root may read its own
`references/engineering-guardrails.md` for preflight, but never forward the
complete contract in place of the child's instructions.

Every spawn carries only the bounded Factory assignment and accessible paths
(or exact materialized request/answers where needed). Resolve paths against
the **child repository working directory**, not the plugin source tree or a
parent-only variable. Require the child's assignment guard and all required
references. A role without a complete verified packet returns `BLOCKED`
without work. Missing host capabilities, incorrect child identity, absent
instructions, malformed results, denied model overrides or unconfirmed child
termination are fail-closed: report host, role, phase and reason; preserve
pending work and forbid concurrent replacement writers. Do not switch to a
legacy Root-relayed protocol or custom runtime.

## Factory Preflight

For a new run, first resolve one complete self-contained logical request. Preserve
the user request text and materialize pasted text, explicitly supplied textual
content, and request-local textual documents that the user identifies as part of
the task. Host-local paths and attachment identifiers are transport metadata, not
durable semantics. The materialized request is authoritative for both preflight
and the later Factory run; do not replace it with a route summary, generated
summary, implementation plan, Engineering summary, or Intent diff.

Resolve the requested scope before durable work:

- `route-only`: report routing/diagnostics only; perform no durable mutation and
  do not start Factory;
- `intent-only`: durable Product Intent preparation and Engineering management
  are allowed when required, but implementation and Factory orchestration do not
  start;
- `intent-preserving`: no changes to approved product decisions or Engineering
  Rules. Only Factory Workers may add verified monotonic refinements to existing
  Intent owners when not forbidden by the user. Never run mutating preflight
  in this scope or silently expand it to `end-to-end`;
- `end-to-end`: durable preparation may be followed by implementation and
  verification.

Run the existing Product Intent classification from Intent Preflight before any
durable write:

```text
AlreadyCovered
ExplicitIntentChange
MissingIntentDecision
ImplementationOnly
```

For `ExplicitIntentChange`, determine the safe Intent handoff/owner, but defer
the actual Product Intent mutation until Engineering management has completed
successfully or as a no-op. Product Intent mutations remain owned by
`idd-intent-change`, including its internal document-creation workflow.
Factory Planner is read-only; only authorized Intent owner workflows may
change normative product decisions. Factory Workers may add monotonic
refinements in existing Intent owners, preserving the effective contract.

Separately classify only whether Engineering management is a durable concern:

```text
NoEngineeringManagementNeeded
EngineeringManagementRequired
EngineeringDecisionMissing
```

`EngineeringManagementRequired` means that the complete request explicitly
contains one or more durable implementation-only project decisions. It does not
authorize Factory to determine `new`, semantic equivalence, owning ENG ID,
add/modify/remove/no-op actions, allocator changes, or INDEX edits. Those remain
exclusively owned by `idd-engineering-change`.

Do not promote task-local implementation details into Engineering policy. A
technology name, class/file name, DI wiring step, implementation order, test
step, temporary migration instruction, or local library-use instruction is not
by itself a durable project-wide Engineering decision.

Distinguish existing mandatory contracts, autonomous implementation choices
and new durable project policies. An unchosen library, algorithm, private
architecture, new-app identifier or researchable API fact is not by itself
EngineeringDecisionMissing. Normal technical choices need no user delegation;
"choose yourself" cannot waive existing Rules, change product behavior,
authorize external purchases/registration or choose a new trust policy.
Policy changes still use the owner workflow and lifecycle guard.

Request-level MissingIntentDecision and genuine EngineeringDecisionMissing
return a question/diagnostic before durable writes, `preparation/`, and
the initial Planner. Only after successful preflight may the initial Planner
create `preparation/question.md`. Do not ask users to decide verifiable
platform facts.

Before any durable write, detect every request-level blocker that can be decided
without invoking a mutation owner, including `MissingIntentDecision`,
`EngineeringDecisionMissing`, conflicting requirements inside the request,
requested-scope violations, inability to materialize the complete request, and
an Engineering-changing replacement while an active Factory request exists. A
request-level blocker means no Product mutation, no Engineering mutation, and no
new Factory state.

For a new `end-to-end` run, the required order is:

```text
1. materialize the complete original logical request
2. resolve Requested scope, analyze Product Intent and Engineering disposition
3. detect all request-level blockers without writing anything
4. require clean managed IDD documents and a valid original Git HEAD
5. save preparation/request.md, baseline.md, provenance.md BEFORE owner mutations
6. record an open before-checkpoint; invoke Engineering owner if required
7. success/no-op -> record bounded owner after-checkpoint; ambiguous/blocked
   or interrupted -> keep preparation for recovery, never activate Factory
8. checkpoint and invoke authorized Product Intent owner workflows
9. validate coverage of the full request and exact saved answers
10. persist final verified preflight boundary in provenance.md
11. run a fresh initial Factory planner while current/request.md is absent
12. handle Question/Done or validate the first Task batch
13. transfer plan, answers, original baseline and provenance to current/
14. create current/request.md LAST as the only active marker
15. execute sequential Workers, keeping bounded before/after checkpoints
```

The Engineering step deliberately precedes the Product Intent write because
`idd-engineering-change` owns authoritative Engineering ownership/ambiguity
planning and has no separate dry-run protocol. This ordering avoids writing
Product Intent before Engineering ownership is resolved. It is not a transaction: owner failure after preparation is saved
preserves earlier writes and recovery context, not a new active Factory state.
Never automatically rollback or infer authorization from an interrupted write.

Coverage validation is against the complete logical request, not a generated
summary. Confirm that required Product behavior is represented, every explicit
Engineering concern was handled by `idd-engineering-change` with `success` or
`no-op`, material safety/compatibility constraints and explicit non-goals were
not lost, implementation-only details did not leak into Product Intent,
task-local details did not leak into Engineering, the two durable layers do not
contradict one another, and no required durable decision remains unresolved.
When `.idd/engineering/` exists, its structure must also be valid. Absence of
the Engineering layer is valid and produces no warning.

Successful durable coverage authorizes the initial planner, NOT activation.
The complete request, immutable baseline and bounded provenance MUST already
be saved in preparation before any mutating preflight owner was invoked. `.idd/factory/current/request.md` is created only after the initial
planner returns a fully validated executable Task batch, with active `plan.md`
and any required answers prepared first. Preserve the base request exactly; never
replace it with a generated summary, plan, or answers.

Continuation of existing runs and saved preparation never repeats new-run
cleanliness checks, recreates baseline, or replays completed owners blindly.

For a replacement with a genuinely active marker, keep the original baseline
and inherited dirty code/Intent edits with known or unknown attribution.
Do not apply the new-run cleanliness gate or claim inherited edits as new.
If effective contract/provenance cannot be recovered, keep old active state
and stop. Without an active marker this is preparation recovery or a new run.

For an explicit replacement request, resolve the complete replacement request
while leaving the current Factory state untouched. Replacement that needs no
Engineering mutation keeps the existing replacement Product Intent semantics:
run replacement preflight/coverage first and replace temporary state only after
success. If the active replacement request is
`EngineeringManagementRequired`, stop before any durable write. Do not bypass
the `idd-engineering-change` active-run guard, auto-cancel/restart the current
run, archive/unarchive it as a trick, or create a suspended/pending lifecycle.
Report that the current run must be completed or cancelled and the complete
replacement request then started as a new Factory run.

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

New:
- materialize the full logical base request and resolve requested scope;
- execute existing durable preflight and coverage validation;
- persist preparation, run the fresh initial planner and handle its result;
- activate only on a validated Task batch, then enter the worker loop.

Continue:
- if `current/request.md` exists, use ordinary active-run continuation
  without repeating entry preflight or initial planning;
- otherwise if `preparation/request.md` exists, restore the full base request,
  exact Q/A and pending question without depending on parent transcript;
- treat a precise reply as the answer to the saved `question.md`; when a new
  independent request appears, ask whether to continue or explicitly replace
  the pending preparation instead of silently combining two requests;
- incomplete preparation blocks automatic execution. Explain which source is
  missing rather than reconstructing it from memory.

Cancel:
- remove pending preparation, or archive/remove the active state as appropriate;
- never roll back repository or durable-knowledge changes automatically.

Restart / explicit replacement:
- preserve existing active replacement semantics and guard;
- an independent new request cannot silently overwrite pending preparation;
- on explicit replacement of preparation, discard only that temporary directory
  and perform new preflight. Existing active runs retain their own rules.

These are skill-level operations, not new transport operations or state schemas.

## Original Git baseline and bounded provenance

The clean-document gate applies **only to a new independent Factory run**,
not continuation, preparation recovery or genuine active-run replacement.
From the repository root run `git status --porcelain=v1 -z --untracked-files=all
-- .idd/intent/ .idd/engineering/`, parsing NUL-delimited status/rename
paths (staged, unstaged, untracked, deletions and renames). Missing optional
Engineering is valid; dirty code/tests elsewhere are not blocking. Recheck
before the first owner write if another process may have changed managed docs.
On any managed dirty path, stop before creating preparation, show the paths,
and never auto-commit. Require an actual full commit SHA from
`git rev-parse --verify "HEAD^{commit}"`; pass the revision as one literal
argument (quote it in PowerShell or other shells), and never invent a baseline.
If a check fails, verify the working directory and command invocation before
concluding that the repository has no commits.

After request-level blockers are ruled out and BEFORE any possibly mutating
owner workflow, durably write the full request, `preparation/baseline.md`
(original immutable HEAD, sanitized repo identity, clean managed-document
checkpoint, without secrets/host path as sole identity), and
`preparation/provenance.md`. Save a bounded pending before-checkpoint for
every owner/Worker invocation: phase (`preflight`, `worker`, or
`external-unknown`), owner/action, IDD paths, HEAD, and necessary
content hashes/hunks or diff identifiers. After trustworthy success/no-op,
record affected paths, after-state, result and the exact saved request/Q/A
authorization for normative owner changes. The preflight boundary is recorded
before first Worker. An owner label, commit or passing test does not establish
authorization; a crash between write and after-record leaves unknown
provenance, not an approved requirement or Worker refinement.

The effective normative contract consists of the original approved
requirements plus exact owner-authorized changes; Worker refinements never
replace or enlarge it. Baseline-to-HEAD diff is not sufficient for
attribution (commits, uncommitted and preexisting code/external edits can
overlap). Save only bounded review evidence, not full snapshots or a separate
Git history. If baseline objects/identity are unavailable or Git history was
rewritten, report the limitation; never reset baseline to current HEAD.
Preserve code and safe recovery but block autonomous Intent refinement.

Recover existing preparation by re-reading saved request/answers, baseline,
pending checkpoints and current documents. Avoid blindly repeating a
possibly completed mutating workflow; use trusted owner results plus semantic
comparison against exact authorization. If origin remains unclear, preserve
files/preparation and stop the unsafe action with an explicit diagnostic.
Continuation never requires cleanliness or a new commit. Legacy pre-baseline
runs resume conservatively with no newly granted Intent write permission;
missing baseline from a known-new run is corrupted recovery state.

On partial activation, validate inactive `current/plan.md`, `baseline.md`,
`provenance.md` and answers against trusted preparation; a partial marker
with missing/mismatched plan/baseline blocks Worker. Write `current/request.md`
last; it remains the sole active-run marker. Native timeout does not prove
the old Worker stopped writing: confirm close/termination before new writer.

## Preparation planner and activation

This path applies to new implementation-capable runs only; `route-only` and
`intent-only` never spawn an initial planner. Requested scope remains binding
through all follow-up questions. A reply does not implicitly promote
`intent-preserving` to `end-to-end`. An unfinished legacy run with explicit
`implementation-only` scope keeps its original no-Intent-edit semantics;
ambiguous legacy scope/provenance forbids Worker Intent writes. The installed
plugin version does not imply permission.

1. Following successful coverage, verify already saved complete
   `preparation/request.md`, original `baseline.md` and `provenance.md`.
   Resolve the registered native Planner and invoke the SAME fresh type used
   for active runs. Give it the unchanged base request or verified saved path,
   chronological exact Q/A (if any), repository access, Engineering discovery
   metadata and bounded preparation verification failure. The child's own
   host-loaded instructions include the canonical Engineering contract.
   No `completed.md` exists before any Worker. No active marker exists yet.
2. `# Question`: require exactly one concrete question. Save the exact text
   to `preparation/question.md` before asking the user and stop this turn.
   Do NOT write `current/request.md` or `current/plan.md`, or spawn a Worker.
3. On the next exact answer, append the original question AND answer verbatim
   and in order to `preparation/answers.md` before removing the pending question.
   Reclassify the complete base request plus all applicable clarifications,
   against the current repository and original requested scope. Detect
   request-level blockers BEFORE any further durable write. An unclear,
   conflicting, or forbidden decision blocks or elicits one concrete question.
4. When clarification requires authorized durable Engineering changes, pass
   the FULL base request, accumulated exact clarifications, and latest exact
   Planner question/answer to normal `idd-engineering-change`. Product Intent
   changes use the normal `idd-intent-change` workflow. If both are needed,
   complete Engineering before Product Intent. Factory never owns semantic
   ENG mapping or directly edits either durable layer. Treat owner `ambiguous`
   or `blocked` as stop conditions and retain preparation; `success` or
   `no-op` can continue after renewed coverage validation. No automatic
   rollback of an already completed owner's writes.
5. Launch a NEW isolated planner after each successful clarification and
   validation. Never resume the earlier child or inherit its transcript.
   Do not repeatedly ask an unchanged question without new user input or
   material repository change; stop with the question or diagnostic instead.
6. `# Task`: require one or more complete self-contained Task contracts,
   exactly one explicit `# ExecutionProfile` per new Task from
   `economy | standard | strong`, and no mixed Question/Done output.
   Verify each `TaskRelatedIntent` has an existing unique `IDD-NNNN`
   owner and each `TaskRelatedEngineering` is an existing unique Conditional
   `ENG-NNNN` (never Always). Validate the existing Engineering layer
   structurally when present. Recheck requested scope, durable coverage and
   repository/knowledge freshness; any material changes invalidate the batch
   and require renewed validation and fresh planning.
7. Recheck `current/request.md` is absent. Prepare a valid
   `current/plan.md` containing THAT exact Task batch and transfer exact
   `answers.md`, original `baseline.md` and `provenance.md` before activation;
   validate orphaned inactive files against preparation. Write complete
   `current/request.md` LAST, preferably by an atomic
   same-directory rename of a fully written temporary file. Only after BOTH
   marker and valid plan are present may the existing sequential Worker loop
   start. After confirmed activation remove preparation. Never overwrite a
   competing active marker or claim multi-file atomicity/exactly-once execution.
8. `# Done` before activation: use existing project verification in the
   `final` context, honoring the existing absent-policy completion fallback.
   On success, clean preparation and finish WITHOUT active marker or empty plan.
   On failure, persist only check/command, exit result and bounded diagnostic in
   `preparation/verification-failure.md`, then invoke a fresh planner.
   `Not verified` or required human confirmation cannot be reported as success.
   If the same failure persists after another Done without intervening change,
   stop with diagnostic rather than looping or fixing checks in the root.
9. Empty/mixed/malformed Planner output, unresolved IDs, invalid profile,
   infrastructure/owner blockers or interrupted activation are NOT success,
   Question or executable Tasks. Preserve preparation and report the exact
   reason; do not launch workers or invent automatic retry machinery.

Before the active marker exists, stray `current/plan.md` or legacy
`current/state.json` does not signal an active run. Safe resume verifies
preparation provenance and the entire pending batch before replacing any
orphaned files. After the marker exists, use existing active-run behavior and
at-least-once Worker convergence. Never move/delete an active marker to get
around the Engineering guard.

## Orchestration loop

Conceptually:

```text
while true:
    if waiting for user:
        stop

    if plan.md has no remaining task:
        run a fresh planner

        Question -> persist question.md and stop
        Done     -> run project verification
        Tasks    -> require one canonical ExecutionProfile per new task, then persist in plan.md

    take the first remaining task
    read its ExecutionProfile; missing metadata in existing plan.md means standard
    reject empty, repeated, or unknown explicit profiles
    if .idd/execution.yaml is absent, inherit host settings
    otherwise re-read and structurally validate .idd/execution.yaml
    mechanically resolve the active-platform profile mapping
    if that profile/platform mapping is absent, inherit host settings
    deterministically re-read the current Engineering INDEX
    enumerate all current Always ENG IDs
    verify registered worker type and prepare the isolated worker assignment
    record pending Worker before-checkpoint in provenance.md
    spawn a fresh native worker with the resolved native model override
    wait natively for its terminal result

    if no trusted completed result:
        leave the task first in plan.md
        stop

    record bounded Worker after-checkpoint, including unknown edits
    append a short result to completed.md
    remove the completed task from plan.md
```

This is conceptual behavior, not a requirement to implement a new programmatic
state machine.

### Codex MultiAgentV2 spawn identity

On Codex MultiAgentV2, **every Factory-owned native `spawn_agent` call**
(planner and worker) MUST include a unique explicit `task_name` argument:
`idd_factory_planner_01`, `idd_factory_planner_02`, ... for planners,
or `idd_factory_worker_01`, `idd_factory_worker_02`, ... for workers.
 are valid.
A `task_name` identifies an attempt, not its `agent_type`; supply both
when the host supports these arguments.
Use a fresh name for **each attempt**, including a spawn rejected by the host.
Keep names unique within the root-session/Factory invocation and any shared
host namespace; inspect visible native calls before selecting the next suffix.
Do not persist counters or create another Factory runtime/state machine.

Pass `task_name` in native spawn arguments, **not** in child instructions or
worker assignments. This is a trace-visible role marker, not evidence that a
child was accepted, completed or used its requested model. Also explicitly set
`fork_turns: "none"` for **planner and worker** spawns; a fresh context must
not inherit the parent or an earlier child's transcript. Apply exact model and
reasoning overrides when execution policy requires them; if the host cannot
express a required argument or honor an override, block with a host-compatibility
diagnostic rather than changing model or backend. Other platform adapters are
unchanged when they lack `task_name`.

### Fresh planner

Resolve and spawn the **registered** `idd-internal-factory-planner`
native agent (Claude plugin-scoped name where applicable). Host-loaded role
instructions must be current and complete. Root supplies only the bounded
Planner assignment, never a copy or citation of the full Planner protocol.
Require no parent transcript, and verify the actual child type in native trace.
Malformed `Tasks`, `Question`, or `Done` is not a successful result.

Give the planner only:

- the complete unchanged base Factory request (from preparation/request.md or current/request.md);
- current repository access;
- chronological exact question AND answer pairs (preparation/answers.md or current/answers.md);
- bounded completed summaries (active runs only);
- the latest bounded project-verification failure from the same phase, when present;
- original immutable baseline and bounded verified preflight/Worker
  provenance, including any unknown changes or deferred contract issues;
- whether the optional Engineering layer exists and the validated discovery
  paths; the child already has its own generated canonical Engineering contract.

The planner discovers current durable intent through the normal
`.idd/intent/README.md` and `INDEX.md` flow and reads only relevant documents.
When `.idd/engineering/` exists, also give it access to the repository's
Engineering README and INDEX. The planner semantically selects only relevant
Conditional rules and emits them as `TaskRelatedEngineering`. It does not emit
Always rules. Full Always documents are not automatically loaded into planner
context, though the planner may read one when its content is necessary for
correct decomposition.

Do not rely on child Skill discovery or inherited Root references. Do not
forward the Root transcript, earlier child transcripts, full role protocols,
or complete Intent/Engineering trees. Fail closed if host-loaded instructions
or required role-local Engineering contents cannot be established.

The planner creates only implementation/research tasks. It must not execute
implementation or create tasks to update Product Intent, create/modify
Engineering Rules, run `idd-engineering-change`, import the request, or
perform durable preparation. Before activation, Planner questions may expose
new durable decisions; only Factory entry orchestration and normal durable
owners can resolve those decisions while the marker is absent.

### Worker execution profile

Immediately before every worker spawn, apply the bounded lookup from
`references/factory-execution-policy.md`:

```text
planner ExecutionProfile
-> missing ExecutionProfile in an existing plan.md means standard
-> reject empty, repeated, or unknown explicit profiles
-> re-read one project configuration document when present
-> absent policy or absent profile/platform mapping means inherit
-> otherwise use exact configured active-platform model/settings
-> native child-agent spawn
```

New planner output must include exactly one canonical profile per task; validate
it before saving a new batch. When continuing an existing run, pending tasks in
`.idd/factory/current/plan.md` without `# ExecutionProfile` remain valid and use
`standard`. Do not require a restart, migrate the plan, or reconsider complexity
solely because that legacy metadata is absent.

This is a mechanical protocol step. Do not reconsider task complexity, compare
candidate models, optimize cost, upgrade/downgrade the profile, or choose a
model that is not the exact configured mapping.

If the file is absent, inherit normal host model/reasoning settings without
writing a default file. Partial mappings are valid: a missing profile or missing
active-platform mapping also inherits host model/reasoning settings. Validate
all present mappings. A malformed existing policy blocks the affected worker
with a concrete diagnostic, as do unknown profile/platform names and empty or
repeated explicit task profiles. Inheritance omits Factory-specific model and reasoning
overrides; it does not copy the current model into the policy.

Malformed explicit configuration blocks the worker spawn with a clear
diagnostic; never silently fall back to `inherit`. If the native host rejects a
configured model/reasoning value or cannot honor an explicit per-child override,
stop without spawning a substitute worker and suggest rerunning
`idd-factory-configure`.

Model selection changes only native spawn settings. The same canonical Factory
worker protocol is used for every execution profile; profiles change native
child-agent settings, never worker instructions. The worker does not choose its
own model.

### Fresh worker

Before **every** Worker spawn, resolve the registered native
`idd-internal-factory-worker` (`idd-factory:idd-internal-factory-worker`
for Claude) and validate its host-loaded instructions and effective tool/model
capabilities. If missing, corrupt, ambiguous or unreadable, block the spawn
and leave the Task first in `plan.md`. Never reconstruct the protocol,
invoke a public Worker Skill or fall back to a generic agent.

Before every Worker spawn, recheck material Intent/Engineering constraints,
request/answer freshness and the effective contract. A stale Task batch is
replanned by a fresh Planner rather than run with outdated commitments.
For the first remaining task, prepare one self-contained worker assignment.
Immediately before execution, check whether `.idd/engineering/` exists.
Absence is normal: do not bootstrap the layer, invent ENG identifiers, or
require a nonexistent INDEX. When present, mechanically validate its structure,
re-read the current INDEX, enumerate every `Always` entry, and ensure that
each ID resolves uniquely to a valid matching document. Pass those IDs
separately from the planner-selected Conditional `TaskRelatedEngineering`
IDs. A missing, duplicated, malformed, or metadata-inconsistent mandatory
rule blocks execution without repairing the Engineering layer. Do not
semantically reselect Conditional rules.

The host-loaded Worker definition includes the canonical Engineering Guardrails
contract; pass the Worker verified baseline/provenance context or paths to the saved
files, and carry explicit user document prohibitions and conservative legacy
scope. Do not add another `# Task` section. The root resolves the execution
profile and native model
settings before spawn; the worker never selects a model or reads execution
policy.

Use this logical native spawn instruction structure, keeping role instructions
separate from the one-task assignment:

```text
--- Factory worker assignment ---
# Task
<exactly one self-contained Task>
# TaskRelatedIntent
<optional IDD-NNNN identifiers>
# TaskRelatedEngineering
<optional planner-selected Conditional ENG-NNNN identifiers>
# AlwaysEngineering
<all current Always ENG-NNNN identifiers, if Engineering exists>
--- End Factory worker assignment ---
```

Omit empty optional sections. The delimiters define a delivery boundary for
native traces, not a persistent Factory task schema. The native adapter sends
this packet as the child task, and the host adds its registered role instructions
separately. Never put role bodies in the packet or task metadata into the
canonical role definition.

Spawn a **fresh registered native Worker** with only its one-task assignment.
Use the shared repository without inheriting the parent conversation, Planner
transcript or earlier Workers. Verify the child identity and actual model/effort
when evidence is available. `inherit` omits Factory-specific overrides;
explicit model/effort must match the validated mapping exactly. A host that
cannot apply required exact settings must block the affected Worker without
fallback and recommend `idd-factory-configure`.

The worker mechanically resolves and reads every supplied current Intent and
Engineering ID before implementation, performs focused task-local checks, and
returns a short semantic result. Workers run sequentially against the shared
workspace; no next writer may start until a trusted terminal result is received
and the prior child is no longer writing.

`AlwaysEngineering` is not planner output and is never persisted in
`plan.md`. Preserve the existing task format and continuation state.

### Waiting and interruption

Use the platform's longest appropriate blocking or event-driven native wait.
Worker duration by itself must not create repeated parent model turns.

If native waiting ends with a host timeout or error:

1. do not start another worker for the same task;
2. do not begin status polling;
3. stop or close the still-running child natively when possible;
4. leave the task first in `plan.md`;
5. end the current Factory invocation.

Never run a replacement writer while the previous child may still be writing to
the same workspace.

Factory deliberately uses at-least-once execution. A later fresh worker must
inspect current repository reality, finish partially completed work, and avoid
reverting correct existing changes merely because an interrupted execution may
have produced them.

## Technical autonomy during execution

Planner can select a suitable compliant implementation direction, delegate
bounded technical choices to Worker, or contract focused research with
observable evidence. A fresh Planner consumes research results from the
repository before dependent Tasks are contracted. Multiple viable techniques
alone do not justify `# Question`; ask only for a genuine user-owned
product, normative, trust, compatibility or external-obligation decision.

When realistic alternatives left to Worker activate different Conditional
Rules, Planner semantically supplies the relevant `TaskRelatedEngineering`
IDs *before* Worker spawn, or first narrows the Task/researches the decision.
Conditions remain conditional. The root only resolves IDs and enumerates
Always Rules; no heuristic Conditional rule selection is introduced.

Worker chooses local implementation methods inside the Task and supplied
Rules. If rules are insufficient for the selected approach or required
constraints conflict, Worker reports a precise blocker and cannot widen
the packet, write `# Question`, alter Task, edit Engineering or approved product decisions or
remove/rename the active marker. Factory handles further planning/stopping. Worker may add only permitted
monotonic refinements to existing Product Intent owners.
A new Engineering policy remains forbidden during an active run.

## Planner outcomes

The following forms apply to BOTH preparation and active-run planning. The
pre-activation Question/Task/Done handling is defined above; do not apply
active-run Engineering restrictions while no active marker exists.

The planner returns exactly one of:

- one or more `# Task` sections with exactly one `# ExecutionProfile`, optional
  `# TaskRelatedIntent`, and optional `# TaskRelatedEngineering`;
- exactly one `# Question`;
- exactly `# Done`.

Blank output is not `Done`. Do not mix forms.

When a question is returned, save it to `question.md` and stop. After the user
answers, classify the exact answer before resuming:

- if it changes Product Intent, use the normal outer Product Intent workflow when
  allowed, then append the exact Q/A and start a fresh planner;
- if it is an ordinary implementation decision, change no durable knowledge,
  append the exact Q/A, and start a fresh planner;
- if it introduces a new durable Engineering decision, do not invoke
  `idd-engineering-change` while `request.md` marks the run active. Keep Factory
  paused and report that the user must complete/cancel this run and start a new
  complete request containing the original request plus the Engineering decision.

Never patch an Engineering Rule directly, turn the decision into a worker task,
weaken the active-run guard, auto-cancel/restart the run, or continue the old
planner thread.

## Project verification and completion

Workers perform focused checks needed for their own tasks.

When the planner returns `# Done`, use the existing project verification
configuration if one is configured. Factory does not own a separate verification
engine.

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

Do not solve semantic tasks with deterministic code. Execution-profile
classification and Conditional Engineering applicability are model decisions.
Profile-to-model lookup is mechanical and must follow the project policy exactly.
Conditional Engineering applicability is a model decision. Do not ask an LLM to do simple mechanical
resolution, persistence, structural validation, or Always-rule enumeration that
the host can perform directly. When the host has no native non-LLM hook, execute
those operations as strict protocol steps without semantic selection. Any proposed Factory feature that requires a new lifecycle
state, retry class, transition type, or recovery protocol should be presumed
outside Factory unless separately justified.
