# IDD Factory live eval

This opt-in test measures a real IDD Factory run against a deterministic,
minimal .NET project. It consumes Codex usage and requires an authenticated
Codex CLI, Git, and a .NET 10 SDK.

It does not run during normal `dotnet test` or `scripts/Check.ps1`. Run the one
available case explicitly:

```bat
run-live-factory-evals.bat
```

The batch launcher selects `unrestricted-runtime-launch`. On Windows the
trusted Factory Runtime must run outside the parent Codex OS sandbox; the
runtime itself applies `workspace-write` to implementers and `read-only` to
decomposition and review workers, always with approvals disabled.

The equivalent direct invocation is:

```powershell
$env:IDD_RUN_LIVE_FACTORY_EVALS = "1"
$env:IDD_CODEX_LAUNCH_PROFILE = "unrestricted-runtime-launch"
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj `
  --filter "FullyQualifiedName~TwoStepCatalogFactoryEvalTests" `
  --logger "console;verbosity=detailed"
```

Each invocation writes an immutable artifact directory under
`artifacts/factory-evals/<run-id>/`. It includes the generated local skills,
fixture workspace, Codex JSONL event stream, process logs, metrics, assertions,
and `report.md`.

The test requests `gpt-5.6-luna` with low reasoning effort by default. Override
it without fallback using `IDD_FACTORY_EVAL_MODEL` and
`IDD_FACTORY_EVAL_REASONING_EFFORT`; use `IDD_FACTORY_EVAL_TIMEOUT_MINUTES` to
change the 20-minute timeout and `IDD_FACTORY_EVAL_VERSION` to pin the generated
methodology version. Re-run the same case with the same values to compare its
artifacts.

The evaluator intentionally distinguishes product success from Factory-contract
success: `Product PASS / Factory FAIL` is a valuable baseline result, not a
reason to weaken assertions.
