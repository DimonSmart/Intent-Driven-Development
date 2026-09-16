# Factory Benchmarking

The Factory Benchmark Runner answers one practical question: does the production Factory complete the same implementation task more cheaply and reliably than a direct agent run?

## Modes

The runner has only two execution modes:

| Mode | Execution |
| --- | --- |
| `direct` | One ordinary Codex session receives the complete task and works directly in the workspace. |
| `factory` | The production Factory Runtime receives the same task and executes it with the configured planner/executor workflow. |

Both modes use the same task, model, reasoning effort, workspace fixture, Windows sandbox setting, and acceptance verification. The benchmark intentionally does not replay ideal work items or try to isolate causal costs for structuring, context isolation, decomposition choice, or orchestration.

Model execution is nondeterministic even with identical configuration, so repeated runs and successful-run medians are the primary statistics.

## Correctness and isolation

Every iteration starts in a new workspace built from the same optional fixture template. No iteration can see a previous iteration's product files. After agent execution the same fixture-owned acceptance command runs outside the LLM and records stdout, stderr, exit code, and duration.

On Windows, acceptance runs against a fresh runner-owned source snapshot that excludes `.git`, `.idd`, `bin`, and `obj`. This prevents sandbox-owned build artifacts or lingering build locks from changing correctness results; snapshot preparation remains runner file I/O and is excluded from token measurements.

The benchmark pins the Windows sandbox explicitly. The bubble-sort fixture uses `elevated`, which avoids the ACL ownership changes made by the `unelevated` fallback. Override it only for a deliberate comparison with `--windows-sandbox elevated|unelevated`.

Token numbers are meaningful only for successful results. Reports retain failed runs and show success count/rate, while median token aggregates use successful runs only. Runner file I/O, workspace preparation, acceptance, and report generation do not enter token totals. Agent and total benchmark durations remain separate in JSON.

The primary report values are:

- success;
- agent invocations;
- gross input tokens;
- new input tokens;
- output tokens;
- tool batches;
- duration.

Production Factory decomposition is retained as diagnostic evidence. The benchmark does not score the exact number or shape of work items.

## Running

Build and test the runner without invoking Codex:

```powershell
dotnet test tests/FactoryBenchmark.Tests/FactoryBenchmark.Tests.csproj
```

Run one quick comparison:

```powershell
dotnet run --project tools/factory-benchmark -- run benchmarks/bubble-sort --repeat 1 --model gpt-5.6-luna --modes direct,factory --windows-sandbox elevated --timeout-minutes 60
```

Run the configured repeated comparison:

```powershell
dotnet run --project tools/factory-benchmark -- run benchmarks/bubble-sort --repeat 3 --model gpt-5.6-luna --windows-sandbox elevated --timeout-minutes 60
```

Options are `--repeat N`, `--model MODEL`, `--output PATH`, `--modes direct,factory`, `--keep-workspaces`, `--timeout-minutes N`, `--windows-sandbox elevated|unelevated`, and `--force`. A fixed `--output` directory enables successful-run resume; `--force` reruns existing results. Successful-run workspaces are removed after capture unless `--keep-workspaces` is set; failed-run workspaces are retained for diagnosis.

Exit code 0 means every requested mode has at least one successful iteration. Exit code 2 means at least one requested mode has zero successful iterations. Configuration or infrastructure failure before iterations can run returns 1. One failed iteration is recorded and does not stop later iterations.

## Reading reports

Each benchmark directory contains `report.json` and `report.md`. JSON is authoritative and contains raw runs, invocations, environment evidence, aggregates, production Factory decomposition metadata, and the direct-vs-Factory comparison. Markdown is a compact human-readable projection.

Compare optimization results only when Codex version, model, reasoning effort, Factory/plugin versions, skill identities, source revision, and benchmark definition are suitable for comparison. Treat Direct/Factory as a reliability-and-cost comparison, not simply “useful tokens versus wasted tokens”: planning, isolated execution, verification, and retries can change both cost and success rate.
