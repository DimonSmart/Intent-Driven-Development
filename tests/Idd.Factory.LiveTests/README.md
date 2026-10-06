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
`strong → gpt-6.1-sol`, all with `medium` reasoning. Both convenience
launchers accept one-run overrides,
which take precedence over `IDD_FACTORY_EVAL_MODEL` and
`IDD_FACTORY_EVAL_REASONING_EFFORT` environment overrides:

```zsh
./run-live-factory-evals.zsh --model gpt-6-luna --reasoning medium
```

The environment variables remain supported for CI and direct `dotnet test`
invocations. A run records both resolved values and their sources in
`summary.json`, including resolved worker mappings. Both convenience launchers
print the complete human-readable
Factory report after the test run and save it as `factory-report.md` beside the
TRX results.

The host trace is authoritative for requested spawn arguments. If it does not
publish resolved child-model fields, the report deliberately records actual
model/reasoning as unknown rather than treating the request as confirmation.

`--model`, `--reasoning`, and their environment equivalents override only the
root session. Worker profiles require explicit models in `live-eval.json`.
Override individual mappings with
`IDD_FACTORY_EVAL_ECONOMY_MODEL`, `IDD_FACTORY_EVAL_STANDARD_MODEL`, or
`IDD_FACTORY_EVAL_STRONG_MODEL`, and the corresponding `_REASONING_EFFORT`
variables. The configuration is rejected before live execution unless all
three resolved models are distinct, even if reasoning settings differ.
Host rejection of an unsupported setting
fails the evaluation without choosing a fallback.

The fixture specifies planner profiles `standard`, `strong`, then `economy`
to isolate routing from classification variability. Assertions compare each
native spawn with the profile found in the planner trace and check actual
settings when the host publishes them. Worker spawns are matched by child
thread ID or canonical agent path and role metadata, so encrypted spawn
prompts and native `task_name` results remain supported.
Ordinary local regression tests also prove that swapped mappings, inherited
root defaults, missing profiles or spawn
settings, and wrong actual settings fail the same routing assertion.

`live-eval.json` configures only this evaluation. Its mappings are materialized
as `.idd/execution.yaml` in the temporary test project. Real projects keep their
own mappings in `.idd/execution.yaml`, managed by `idd-factory-configure` and
tracked in Git. Factory re-reads that project file before each worker spawn;
planner output contains only the execution profile.
