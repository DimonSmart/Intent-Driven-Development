# Idd.Factory.LiveTests

This project contains one explicit token-consuming end-to-end evaluation of the
native-agent Factory architecture. It is skipped unless
`IDD_RUN_LIVE_FACTORY_EVALS=1`.

The scenario builds the current generator, generates and installs the Codex
`idd-factory` plugin, copies the `TwoStepCatalog` workspace, and invokes a real
Codex host with `$idd-factory-run`.

The evaluation checks observable properties:

- the generated plugin contains no Factory runtime directory or `.mcp.json`;
- the host uses native child-agent delegation;
- legacy `factory_run` / `factory_status` tools are absent from the trace;
- the two required implementation responsibilities complete;
- independent final `dotnet test` passes;
- durable intent remains the input contract rather than a worker output.

The live test is not the place to recreate a deterministic Factory state-machine
simulator. Mechanical generation and packaging contracts belong in
`Idd.Generation.Tests`.

Run explicitly:

```powershell
pwsh ./scripts/Check.ps1 -Mode Live
```

The tracked [`live-eval.json`](live-eval.json) selects the default Codex model
and reasoning effort for this evaluation. Its initial default is `gpt-6-luna`
with `low` reasoning. Both convenience launchers accept one-run overrides,
which take precedence over `IDD_FACTORY_EVAL_MODEL` and
`IDD_FACTORY_EVAL_REASONING_EFFORT` environment overrides:

```zsh
./run-live-factory-evals.zsh --model gpt-6-luna --reasoning low
```

The environment variables remain supported for CI and direct `dotnet test`
invocations. A run records both resolved values and their sources in
`summary.json`. Both convenience launchers print the complete human-readable
Factory report after the test run and save it as `factory-report.md` beside the
TRX results.
