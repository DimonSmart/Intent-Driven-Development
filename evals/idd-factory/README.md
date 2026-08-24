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
`artifacts/factory-evals/<run-id>/`. The evaluator creates an isolated
`CODEX_HOME`, adds the generated marketplace with `codex plugin marketplace
add`, installs `idd-factory` with `codex plugin add`, and launches the installed
`idd-factory-run`. The fixture does not receive copied Factory skills or a
project-local runtime. Artifacts include the generated marketplace, isolated
plugin cache, fixture workspace, Codex JSONL event stream, live `progress.log`,
process logs, metrics, assertions, and `report.md`.

The test requests `gpt-5.6-luna` with low reasoning effort by default. Override
it without fallback using `IDD_FACTORY_EVAL_MODEL` and
`IDD_FACTORY_EVAL_REASONING_EFFORT`; use `IDD_FACTORY_EVAL_TIMEOUT_MINUTES` to
change the 20-minute timeout and `IDD_FACTORY_EVAL_VERSION` to pin the generated
methodology version. Re-run the same case with the same values to compare its
artifacts.

The evaluator intentionally distinguishes product success from Factory-contract
success: `Product PASS / Factory FAIL` is a valuable baseline result, not a
reason to weaken assertions.

## Release certification

Developer live evals may run from a dirty tree. Release certification may not:

```powershell
.\certify-release.ps1 -Version 1.2.3
```

Certification requires clean `HEAD` at the exact `v1.2.3` tag, records the full
commit SHA, runs deterministic checks, performs the real installed-plugin live
eval, and reports unavailable effective worker model/reasoning telemetry as
`INCONCLUSIVE` rather than `PASS`. The release tag is pushed by
`publish-next-version.ps1` only after certification passes.

Release certification requires an authenticated stable Codex CLI 0.148.0 or
newer. It does not require a separately generated lifecycle report, executable
fingerprint, or hidden environment variable.
