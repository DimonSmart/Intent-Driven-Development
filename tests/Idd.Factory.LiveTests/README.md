# Idd.Factory.LiveTests

This project contains explicit token-consuming end-to-end evaluations of the
native-agent Factory architecture. They are skipped unless
`IDD_RUN_LIVE_FACTORY_EVALS=1`.

The primary scenario builds the current generator and Codex `idd-factory`
plugin, creates a disposable `TwoStepCatalog` workspace, and invokes a real
Codex host with `$idd-factory-run`. The harness uses ordinary
Marketplace installation into a disposable `CODEX_HOME` and verifies that
both canonical internal Planner/Worker Skills are present in the generated
Factory package. It does NOT copy, register, force-add, or track custom
`.codex/agents/` role files. Before Codex starts, the harness independently
verifies the exact Git `HEAD^{commit}`, repository root and clean workspace,
recording `git-baseline.txt`. It does not alter real host/user registration.

The main scenario tests actual Factory orchestration without hidden native
Factory-role setup. A separate read-only Marketplace generic-child probe
below checks bounded child spawn and guards; it does not replace full E2E.
The host is started with MultiAgentV2 explicitly enabled; a model-selected
runtime that does not report V2 fails the evaluation instead of silently
testing a different spawn protocol.

The new preparation-before-activation contract additionally requires explicit
native-host scenarios: initial `# Question` with no active marker/Worker,
authorized Engineering owner handoff from exact saved Q/A, fresh Planner,
validated initial Task batch, marker-last activation and first Worker; plus
pre-activation `# Done` with successful/failing final verification, and
recovery from interrupted preparation. These live traces are not simulated by
static contract tests and must not be marked passed unless a compatible host
actually executes them.

The evaluation checks observable properties:

- the generated plugin contains no Factory runtime directory or `.mcp.json`;
- the host uses native child-agent delegation;
- the effective root runtime is MultiAgentV2 (verified from `turn_context`), and
  every worker spawn explicitly uses `fork_turns: "none"`;
- legacy `factory_run` / `factory_status` tools are absent from the trace;
- the generated live-eval `execution.yaml` supplies a complete Codex mapping
  with three distinct models; every worker spawn receives the
  mapping for its planner-assigned profile, with all three profiles exercised;
- the two required implementation responsibilities complete;
- a third focused verification worker completes;
- independent final `dotnet test` passes;
- durable intent remains the input contract rather than a worker output.

The live test is not the place to recreate a deterministic Factory state-machine
simulator. Mechanical generation and packaging contracts belong in
`Idd.Generation.Tests`.

Run explicitly:

```powershell
pwsh ./scripts/Check.ps1 -Mode Live
```

The tracked [`live-eval.json`](live-eval.json) selects the root Codex model
and reasoning effort, plus the three worker mappings. Its initial root default
is `gpt-6-luna` with `medium` reasoning. Worker mappings are
`economy → gpt-6-luna`, `standard → gpt-5.6-terra`, and
`strong → gpt-6.1-sol`, all with `medium` reasoning. `Check.ps1`
accepts one-run overrides, taking precedence over
`IDD_FACTORY_EVAL_MODEL` and `IDD_FACTORY_EVAL_REASONING_EFFORT`:

```powershell
pwsh ./scripts/Check.ps1 -Mode Live -Model "gpt-6-luna" -Reasoning medium
```

The same command works on Windows, macOS and Linux, from any working directory.
For a local preset copy `scripts/Run-Live.local.ps1.example` to
`scripts/Run-Live.local.ps1` (Git-ignored) and edit its values.
The environment overrides remain supported for direct `dotnet test` runs.

The entry point creates unique artifacts, captures TRX, handles locked test
assemblies, generates JSON/Markdown Factory Reports, prints the human-readable
report and preserves failed-test exit codes. It cleans up only temporary
directories it created, not caller-owned locations. `summary.json`
records resolved model/effort settings, their sources and worker mappings.

The host trace is authoritative for requested spawn arguments.
The canonical Factory skill requires per-attempt native `task_name` values
matching `idd_factory_planner_[0-9]+` or `idd_factory_worker_[0-9]+`.
An encrypted `message` is not decoded; independently structured
`task_name`, `model`, `reasoning_effort` and `fork_turns` are still
required. A requested name is distinct from the canonical child `agent_path`.
Role and child association require consistent, parent-aware native evidence.
The host's explicit rejection must be correlated to the attempted call;
a missing child alone does not establish rejection.
 Actual settings
are read from each worker's own `turn_context.model` / `turn_context.effort` or
explicit resolved/actual fields. If those values cannot be read, the report
records `unknown` with a diagnostic and the routing evaluation fails rather
than treating the requested settings as confirmation.

`-Model`, `-Reasoning`, and their environment equivalents override only the
root session. Worker profiles require explicit models in `live-eval.json`.
Override individual mappings with
`IDD_FACTORY_EVAL_ECONOMY_MODEL`, `IDD_FACTORY_EVAL_STANDARD_MODEL`, or
`IDD_FACTORY_EVAL_STRONG_MODEL`, and the corresponding `_REASONING_EFFORT`
variables. The configuration is rejected before live execution unless all
three resolved models are distinct, even if reasoning settings differ.
Host rejection of an unsupported setting
fails the evaluation without choosing a fallback. Before a Live run, confirm
on the **same Codex host and authorization** that root uses MultiAgentV2 and
that all three worker models can actually spawn with the requested per-child
model/effort overrides and `fork_turns: "none"`. Version numbers and a model
list alone do not establish compatibility. The checked-in `strong` preset is
not changed merely because it was unavailable on one host. After independently
confirming a compatible distinct model, override it locally, for example:

```powershell
$env:IDD_FACTORY_EVAL_STRONG_MODEL = "<confirmed-available-model>"
pwsh ./scripts/Check.ps1 -Mode Live
```

When the host hides or rejects required overrides, report a host/environment
blocker, not a passed LiveTest. The positive test checks declared `BLOCKED`
first and reports the model/reason and preserved artifact paths rather than
misreporting missing worker counts. `COMPLETED` is not sufficient: the test
requires three distinct completed worker threads in planner order
`standard → strong → economy`, plus trusted accepted spawns, isolated
planner/worker histories, actual child settings and independent project TRX.


The fixture specifies planner profiles `standard`, `strong`, then `economy`
to isolate routing from classification variability. Assertions compare each
native spawn with the profile found in the planner trace and require matching
actual model and reasoning settings. Worker spawns are matched by child
thread ID or canonical agent path and role metadata, so encrypted spawn
prompts and native `task_name` results remain supported.
Ordinary local regression tests also prove that swapped mappings, inherited
root defaults, missing profiles or spawn
settings, and missing or wrong actual settings fail the same routing assertion.

`live-eval.json` configures only this evaluation. Its mappings are materialized
as `.idd/execution.yaml` in the temporary test project. Real projects keep their
own mappings in `.idd/execution.yaml`, managed by `idd-factory-configure` and
tracked in Git. Factory re-reads that project file before each worker spawn;
planner output contains only the execution profile.

## Internal Skill discovery through Marketplace (separate opt-in test)

`Factory_MarketplaceRunsGenericInternalSkillsWithoutRegisteredRoles` uses a
fresh Git repository, separate `CODEX_HOME`, and the normal generated
Marketplace plugin installation. It does NOT create custom `.codex/agents/`
definitions. It checks both internal `SKILL.md` files are packaged and asks
generic native children (without Factory-specific `agent_type`) to read their
respective installed Skills and reject intentionally incomplete assignments
with `BLOCKED`, without changing project files. The test examines native
`task_name`, `fork_turns: "none"`, bounded Skill-path delivery and accepted
child spawns; full file-read and lifecycle evidence needs the real child trace.

To run only this token-consuming capability test:

```powershell
$env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS = "1"
try {
    dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj `
        --filter "FullyQualifiedName~Factory_MarketplaceRunsGenericInternalSkillsWithoutRegisteredRoles"
}
finally {
    Remove-Item Env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS -ErrorAction SilentlyContinue
}
```

This probe is excluded from normal `Check.ps1 -Mode Live`.
Native traces and failure artifacts remain under
`artifacts/factory-evals/marketplace-internal-skills-*/`.

## Effort configuration (opt-in live test)

`FactoryConfigure_UpdatesAndClearsEffortWithoutLosingOtherMappings` runs the
installed Codex `idd-factory-configure` skill against a disposable project.
It verifies the parsed YAML structure, not instruction-text keywords: an
exact effort-only edit preserves all other Codex/Claude mappings; an exact
model-and-effort edit changes only its two fields; clearing effort preserves
the concrete model; and an unrelated malformed blank effort blocks further
edits without silently repairing or replacing policy. This test requires a
working Codex plugin host and is never run by ordinary unit-test commands.

Run only this case directly:

```powershell
$env:IDD_RUN_LIVE_FACTORY_EVALS = "1"
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --filter FullyQualifiedName~FactoryConfigure_UpdatesAndClearsEffortWithoutLosingOtherMappings
Remove-Item Env:IDD_RUN_LIVE_FACTORY_EVALS
```

This is not a deterministic simulation of a model provider's capability
catalog. Known model/effort incompatibilities must still be verified against
the active native host: a rejected explicit pair must block without dropping
the effort or choosing a substitute. Existing native Factory routing Live tests
assert both requested `reasoning_effort` spawn arguments and actual worker
`turn_context.effort` evidence; the standard unit tests also check that
distinct profile effort values cannot be swapped or silently changed.

## Unavailable worker model

`Factory_BlocksWhenConfiguredWorkerModelIsUnavailable` keeps the configured root
model valid and replaces only the first worker's `standard` mapping with the
deliberately nonexistent `idd-eval-removed-worker-model`. This exercises the
failure mode of a previously configured model that has been retired or removed
without relying on a real vendor model's changing availability.

The evaluation requires `BLOCKED` with the unavailable model named in the
reason, no substitute or completed worker, no successful final verification,
and unchanged execution policy and product code. The test requires a readable planner result, an active pending plan, and
trace evidence of an attempted native worker spawn with the unavailable model.
Rejection before creating a child is valid, but a mere `BLOCKED` response
without an attempted spawn is not evidence of model unavailability. Requested
settings never prove successful execution.

When Check.ps1 supplies shared artifact/temp directories, this case uses their
`model-unavailable/` subdirectories. Its Markdown/JSON reports and source traces
are saved there independently of the successful catalog evaluation.
Run just this case with `IDD_RUN_LIVE_FACTORY_EVALS=1 dotnet test
tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --filter
FullyQualifiedName~Factory_BlocksWhenConfiguredWorkerModelIsUnavailable`.
Local tests cover rejection reporting and prove that the negative-case
assertions reject silent fallback, inheritance, missing explanations, and false
completion.

## Workspace and final verification evidence

The fixture includes Intent README/INDEX discovery files and explicitly marks
its test project with `IsTestProject=true`. The harness restores the copied
solution before starting Codex. Workers can therefore use the configured
`--no-restore` commands without silently skipping test execution.

Independent final verification saves `project-verification.trx` before the
report assertions. It must execute and pass all 9 prepared tests, including a
collection mutation regression for `Catalog.Codes`. A zero exit code alone is
not sufficient. Logs and TRX are retained even if a report assertion fails.

`Factory_ReplansAfterFinalVerificationFailure` uses `verification-recovery/`
artifact/temp directories and an evaluation-only final gate. Its first root
invocation fails until the normal bounded failure diagnostic exists. The
evaluation requires a fresh planner before verification resumes, another
`# Done`, successful gate/build/tests checks, and archived failure evidence.
The gate does not maintain attempts or implement a Factory runtime.


## Stage 0: native nested Factory-agent compatibility gate (opt-in)

Before migrating `idd-factory-run` to a Supervisor, run
`Factory_Stage0ProvesNestedNativeGenericAgents` on the **actual intended Codex
host and account**. This is a pre-migration, technical native-capability probe,
not the two-level Factory implementation or its 10-task acceptance test.
It uses a disposable repository, generated and installed Marketplace package,
and generic native children without custom `agents/` registrations.

```powershell
$env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS = "1"
try {
    dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj `
        --filter "FullyQualifiedName~Factory_Stage0ProvesNestedNativeGenericAgents"
}
finally {
    Remove-Item Env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS -ErrorAction SilentlyContinue
}
```

The test captures `codex --version`, the generated Marketplace package and
the Codex native JSONL trace under
`artifacts/factory-evals/nested-stage0-*/`.
Its isolated `CODEX_HOME` does not inherit the user's `config.toml`.
On native Windows, it therefore explicitly configures
`[windows] sandbox = "elevated"` (the native Windows sandbox backend),
while still running Codex with `--sandbox read-only`.
Without this setting, Codex can reject even `Get-Content` as
`blocked by policy`; a successful command in the user's normal
`CODEX_HOME` does not verify the isolated environment. It requires a genuine
Supervisor -> generic Root -> generic Planner/Worker thread hierarchy,
independent native session IDs, exact parent IDs and task names, accepted
spawns at both levels with `fork_turns=none`, Skill reads from the installed
package, sequential assignments, native waits, successful explicit
grandchild model/effort routing, and an unchanged shared Git workspace.
It injects a Supervisor-only nonce to detect accidental transcript inheritance.
A false/partial signal fails the test; `stage0-result.json` is written only
when the assertions pass, while `stage0-blocker.txt` retains failure details.
A requested `Get-Content` operation is not proof that a child read a Skill:
the live probe correlates the native tool invocation and its response and
requires the complete contents of the exact installed `SKILL.md` in a successful
tool result. Tool denials (`blocked by policy`) and truncated responses fail
closed. It also records each session's final native spawn/wait/interrupt events
when the host process times out.

On Windows Codex installations a read-only native `exec_command` may be
rejected by **host policy**, independently of the Factory plugin. The same
host must permit reading the installed Skill through an approved mechanism
for Stage 0 to pass. Do not simulate the read, silently copy its text into
spawn prompts, or change the public Factory in response to this blocker.
The normal-completion probe must not call `interrupt_agent`: cancellation
and descendant quiescence require a separate dedicated Stage 0 evaluation.

The technical probe intentionally exercises both internal Skills with
incomplete, read-only assignments, expecting `BLOCKED` from each.
It does **not** prove simultaneous write safety, full Factory checkpoint
semantics, forced child cancellation or end-to-end rotation. Those require
additional live evaluations (including a controlled writer probe and
10-worker / limit-3 E2E) before the public Supervisor can be shipped.
A green normal CI build, `agents.max_depth=2`, a trace from a different
host, or an unexecuted smoke test do not satisfy the Stage 0 gate.
Until verified native nested-spawn evidence exists, preserve the current
one-level `idd-factory-run` behavior.

## Two-level Supervisor acceptance gates (feature branch only)

Before merging the public Factory Supervisor into main, run these **real native
Codex host tests** on the same supported host used for the successful Stage-0
nested-smoke gate. Normal Linux/Windows CI does NOT run either live test.
Tests consume Codex tokens and write only disposable temporary workspaces.
Use a separate working branch/worktree rather than switching away from active
user project modifications. The intended host must be able to invoke real
native depth-2 generic agents, deliver installed Skills, run a write-capable
child and attest full child/descendant stop.

In PowerShell, from a checkout of the feature branch (Gate 2 only runs when
Gate 1 passes):

~~~powershell
pwsh ./scripts/Verify-FactoryV2-Native.ps1
~~~

For the Writer gate alone:

~~~powershell
pwsh ./scripts/Verify-FactoryV2-Native.ps1 -WriterOnly
~~~

Equivalent manual commands:

~~~powershell
$env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS = "1"
$env:IDD_FACTORY_EVAL_STANDARD_MODEL = "gpt-6.1-terra"
$env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES = "15"

dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --filter "FullyQualifiedName~Factory_Stage0NativeDescendantFirstCancellationStopsGrandchildWriter"

$env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES = "60"
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --filter "FullyQualifiedName~Factory_NativeTenTasksRotatesOnlyAtPlanBoundariesWithLimitThree"

Remove-Item Env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS -ErrorAction SilentlyContinue
Remove-Item Env:IDD_FACTORY_EVAL_STANDARD_MODEL -ErrorAction SilentlyContinue
Remove-Item Env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES -ErrorAction SilentlyContinue
~~~

**Controlled writer/stop gate:** the technical test creates a disposable Git
repository, launches a native Supervisor → Root → synthetic Writer, and
executes a tracked, single-use 40-second heartbeat script (atomic CreateNew
guard rejects accidental reruns). It deliberately interrupts the *Writer
directly first*, then the Root. It independently requires exact native
interrupt targets in descendant-first order, aborted native child turns,
fewer than 40 written heartbeat lines, no natural-completion marker and no
further writes during an 8-second observation window. A native interrupted
agent is not sufficient if its launched PowerShell command keeps writing.
The prior parent-only Root interrupt failed on Windows Codex 0.163.0:
Writer stayed running and executed the heartbeat loop twice (80 lines).
The first descendant-first attempt interrupted both agents and stopped
heartbeat at 33 lines without natural completion, but its Supervisor
generated invalid inline PowerShell during the readiness wait and correctly
returned BLOCKED. The harness now provides a prewritten
`native-wait-for-heartbeat.ps1` that prints `HEARTBEAT_READY` and creates
a readiness marker only after three lines are observed; the test verifies
the wait tool was invoked before Writer interruption, that the marker exists,
and that real early quiescence follows. Native shell commands that merely
read or inspect `native-writer-probe.ps1` do not violate Writer isolation;
a separately unit-tested invocation classifier rejects actual execution of
that script by Supervisor or Root, including `pwsh -File` and `&`. This gate tests direct descendant
cancellation, not recursive host cancellation. The synthetic Writer is not a Product Worker and does
not run Factory Preflight; the earlier read-only Stage-0 installed-Skill
probe independently checks child Skill loading. If direct descendant stop
or in-flight command quiescence is unproven, the gate MUST fail and the
public Supervisor MUST NOT be merged.

**Confirmed host blocker (Windows Codex CLI 0.163.0, 2026-11-19):**
The full native trace of `nested-writer-stop-f7c88ef66ad942e68f087394e4a3e604`
shows a successful heartbeat-readiness marker (3 lines), native Writer
interrupt at 20:41:47Z, Root interrupt at 20:41:49Z and both agent threads
`turn_aborted`. Nevertheless the Writer's launched PowerShell child continued
writing: heartbeat rose from 24 to 26 lines after interruption, then from
38 to 40. A second Writer interrupt at 20:42:13Z returned
`previous_status: interrupted`; the child command eventually exited 0 and
the natural-completion marker exists in the saved workspace. This is a
**real host/process-cancellation failure**, not duplicate trace events.
The live gate must fail with `HOST_UNSUPPORTED_WRITER_COMMAND_SURVIVED_INTERRUPT`
rather than a misleading `Assert.Single` failure. Until the host demonstrably
terminates in-flight writing commands, do not repeat the costly Gate 1 on the
same host/version, run Gate 2, merge PR #77 or enable two-level Factory. A
native host fix and fresh real-host Gate 1 are required.

**Ten-step/limit-3 gate:** the test installs a fresh generated Marketplace
package into a new isolated CODEX_HOME, creates a minimal self-contained
`.idd/intent/` + .NET project, invokes the public Supervisor ONCE, requires
a reproducible first Planner result with FOUR independent Tasks and a
Root target of 3, verifies that all four Workers finish under the SAME
Root before rotation, permits `CompletedWorkers > 3`, and also verifies
10 distinct Worker child threads, exact batch-scoped native task names, plan completion at every normal
Root rotation regardless of actual Planner grouping, valid model/effort
routing, final `# Done`/Project Verification and ten
independently passing xUnit cases. It stores raw native sessions,
`factory-report.json`, trace, Git status/diff and xUnit TRX under
`artifacts/factory-evals/batch-ten-*/`.
A successful code build, synthetic topology fixture or Stage-0 read-only
smoke does not substitute for this real end-to-end gate. Inspect any failures
rather than automatically retrying expensive live execution.

For the final acceptance report, also capture and compare equivalent
one-level vs two-level host/model/snapshot runs with per-thread input/output,
Supervisor/Root usage, compactions, waits and independently passing project
tests. Do not claim token savings based on unexecuted experiments or compare
different project outcomes.
