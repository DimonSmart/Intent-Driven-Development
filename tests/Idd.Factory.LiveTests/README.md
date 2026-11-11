# Idd.Factory.LiveTests

This project contains explicit token-consuming end-to-end evaluations of the
native-agent Factory architecture. They are skipped unless
`IDD_RUN_LIVE_FACTORY_EVALS=1`.

The primary scenario builds the current generator and Codex `idd-factory`
plugin, creates a disposable `TwoStepCatalog` workspace, and invokes a real
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
