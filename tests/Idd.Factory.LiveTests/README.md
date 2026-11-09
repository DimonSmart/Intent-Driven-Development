# Idd.Factory.LiveTests

This project contains explicit token-consuming end-to-end evaluations of the
native-agent Factory architecture. They are skipped unless
`IDD_RUN_LIVE_FACTORY_EVALS=1`.

The primary scenario builds the current generator and Codex `idd-factory`
plugin, creates a disposable `TwoStepCatalog` workspace, and invokes a real
Codex host with `$idd-factory-run`. It copies the **generated** Planner and
Worker TOML definitions to that workspace's `.codex/agents/` directory, then
force-adds only those two otherwise ignored definitions to its initial Git
commit. Before starting Codex, the harness independently verifies the exact
`HEAD^{commit}` object, Git root, and both tracked agent files; it stores
`git-baseline.txt` and `agent-registration.json` as evidence.
No global user or host agent configuration is changed.

This makes the main scenario a Factory orchestration/routing evaluation;
it is **not evidence** that the Marketplace plugin independently registers
its native roles. That question is tested separately below.
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

## Native agent discovery through Marketplace (separate opt-in test)

`Factory_MarketplaceAloneDiscoversNativeAgents` uses a fresh Git repository,
a separate `CODEX_HOME`, and a normal generated Marketplace plugin
installation. It **does not** create `.codex/agents/` or copy role definitions.
The root attempts one native `spawn_agent` for each of the two required agent
types with an intentionally invalid, no-write Factory assignment. The child
role guard is expected to block work; the test succeeds only with host trace
evidence that each **named native role was actually accepted and spawned**.
No generic-agent fallback counts as discovery. The test is separate because
some Codex versions may install the plugin but not register its `agents/`.

To run only this token-consuming capability test:

```powershell
$env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS = "1"
try {
    dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj `
        --filter "FullyQualifiedName~Factory_MarketplaceAloneDiscoversNativeAgents"
}
finally {
    Remove-Item Env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS -ErrorAction SilentlyContinue
}
```

It is excluded from the normal `Check.ps1 -Mode Live` selection.
Native traces, Git verification and failure diagnostics are retained under
`artifacts/factory-evals/marketplace-agents-*/`.

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
