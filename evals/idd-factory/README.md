# IDD Factory live eval

The Factory live evaluation is a deliberately small real-host safety net. It consumes Codex usage and requires an authenticated Codex CLI, Git, and a .NET 10 SDK. All deterministic Factory contracts remain covered by `Idd.Factory.Tests`; benchmark and efficiency research belongs to `tools/factory-benchmark`.

There is exactly one token-consuming evaluation:

`FactoryEndToEndLiveTests.TwoStepCatalog_CompletesThroughOneBlockingFactoryCall`

Run it with:

```bat
run-live-factory-evals.bat
```

Or invoke the same selection contract directly:

```powershell
$env:IDD_RUN_LIVE_FACTORY_EVALS = "1"
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj `
  --filter "Category=LiveFactoryEval" `
  --logger "console;verbosity=detailed"
```

The scenario creates an isolated `CODEX_HOME`, builds and installs the current generated IDD plugin, checks the prepared `TwoStepCatalog` baseline, and launches real Codex with the supported unrestricted outer profile. Factory Runtime still applies its production worker capabilities: implementation workers receive workspace-write while planning/review workers remain restricted.

`CODEX_HOME`, Codex plugin/cache data, and the generated marketplace are created under the OS temporary directory and deleted after the outer Codex process finishes. They are intentionally not retained under `artifacts/factory-evals` because they are reproducible infrastructure rather than diagnostic evidence. If cleanup cannot complete because files remain locked, the run keeps only a small `temporary-cleanup-warning.txt` pointing to the temporary directory.

The evaluation requires at least one completed work item but does not pin the planner to an exact decomposition or an exact number of semantic turns.

The same invocation checks the blocking transport contract: exactly one `factory_run`, no `factory_status` polling, and no completed model turn while the blocking call is active. It also checks Factory `COMPLETED`, final verification `passed`, independent final build/tests, the expected product change, and preservation of durable intent and protected scenario inputs.

Each run preserves diagnostic evidence under `artifacts/factory-evals/<run-id>/`: the workspace including Factory current/result data, Codex `events.jsonl`, stderr/final response, verification logs, progress log, task input, and git status/diff. Reproducible Codex/plugin caches and generated marketplace contents are not persisted there.

Defaults are `gpt-5.6-luna`, low reasoning effort, and a 20-minute timeout. Override them with `IDD_FACTORY_EVAL_MODEL`, `IDD_FACTORY_EVAL_REASONING_EFFORT`, `IDD_FACTORY_EVAL_TIMEOUT_MINUTES`, and optionally `IDD_FACTORY_EVAL_VERSION`.

Do not use real-model evaluations for deterministic failure paths, process mechanics, transport details, or other contracts that can be checked reliably in `Idd.Factory.Tests`.
