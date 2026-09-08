# IDD Factory live eval

The opt-in LiveTests are a small real-host safety net. They consume Codex usage and require an authenticated Codex CLI, Git, and a .NET 10 SDK. Deterministic Factory behavior is covered by `Idd.Factory.Tests`; benchmark and efficiency research belongs to `tools/factory-benchmark`.

Run the main end-to-end case with:

```bat
run-live-factory-evals.bat
```

Or directly:

```powershell
$env:IDD_RUN_LIVE_FACTORY_EVALS = "1"
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj `
  --filter "FullyQualifiedName~FactoryEndToEndLiveTests" `
  --logger "console;verbosity=detailed"
```

The end-to-end scenario creates an isolated `CODEX_HOME`, builds and installs the current generated IDD plugin, checks the prepared TwoStepCatalog baseline, and launches real Codex with the supported unrestricted outer profile. Factory Runtime still applies its production worker capabilities: implementation workers receive workspace-write while planning/review workers remain restricted.

The same invocation checks the blocking transport contract from a deliberately tiny JSONL reader: exactly one `factory_run`, no `factory_status` polling, and no completed model turn while the blocking call is active. Product build/tests, Factory `COMPLETED`, completed work count, final verification, and preservation of durable intent are checked directly.

A second `WorkspaceWriteLiveTests` smoke case can be run with the same opt-in to verify real Codex workspace-write behavior without running Factory.

Each run preserves raw evidence under `artifacts/factory-evals/<run-id>/`: the workspace including Factory current/result data, Codex `events.jsonl`, stderr/final response, verification logs, progress log, and git status/diff. The suite no longer reconstructs rollout trees, agent traces, effective model/reasoning telemetry, efficiency metrics, launch-profile reports, or other derived diagnostics.

Defaults are `gpt-5.6-luna`, low reasoning effort, and a 20-minute timeout. Override them with `IDD_FACTORY_EVAL_MODEL`, `IDD_FACTORY_EVAL_REASONING_EFFORT`, `IDD_FACTORY_EVAL_TIMEOUT_MINUTES`, and optionally `IDD_FACTORY_EVAL_VERSION`.
