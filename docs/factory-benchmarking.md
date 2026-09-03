# Factory Benchmarking

The Factory Benchmark Runner measures the observed cost and success rate of the same implementation task under five execution modes. It separates the base task from structuring, fresh-context isolation, Factory-selected decomposition, and the complete production Factory runtime.

## Modes

| Mode | Execution | What the transition estimates |
| --- | --- | --- |
| B0 `direct` | One ordinary Codex session receives the complete task. | Absolute task baseline. |
| B1 `structured-single` | One session receives the task plus ideal ordered work items. | B1 − B0: structuring overhead. |
| B2 `manual-isolated` | One fresh Codex process executes each ideal work item against a shared workspace. | B2 − B1: fresh-context isolation overhead. |
| B3 `factory-split-replay` | The canonical Factory decomposer creates contracts; ordinary fresh workers replay its subtasks without Runtime gates or reviews. | B3 − B2: observed effect of Factory's decomposition choice. |
| B4 `factory` | The production Factory Runtime executes the complete task using its linear plan and deterministic policy. | B4 − B3: observed Factory runtime/orchestration overhead. |

B3 currently obtains decomposition independently from B4. Reports mark this explicitly and retain every generated contract. Consequently B4 − B3 is an estimate, not an exact causal measurement. Model execution is nondeterministic even with identical configuration, which is why repeated runs and successful-run medians are the primary statistics.

## Correctness and isolation

Every iteration starts in a new workspace built from the same optional fixture template. No iteration can see a previous iteration's product files. After agent execution the same fixture-owned acceptance command runs outside the LLM and records stdout, stderr, exit code, and duration.

On Windows, acceptance runs against a fresh runner-owned source snapshot that excludes `.git`, `.idd`, `bin`, and `obj`. This prevents sandbox-owned build artifacts or lingering build locks from changing correctness results; snapshot preparation remains runner file I/O and is excluded from token measurements.

The benchmark pins the Windows sandbox explicitly. The bubble-sort fixture uses `elevated`, which avoids the ACL ownership changes made by the `unelevated` fallback. Override it only for a deliberate comparison with `--windows-sandbox elevated|unelevated`.

Token numbers are meaningful only for successful results. Reports retain failed runs and show success count/rate, while min/median/max token aggregates use successful runs only. Runner file I/O, workspace preparation, acceptance, and report generation do not enter token totals. Agent, acceptance, and total benchmark durations remain separate.

## Running

Build and test the runner without invoking Codex:

```powershell
dotnet test tests/FactoryBenchmark.Tests/FactoryBenchmark.Tests.csproj
```

Run a cheap subset explicitly:

```powershell
dotnet run --project tools/factory-benchmark -- run benchmarks/bubble-sort --repeat 1 --model gpt-5.6-luna --modes direct,manual-isolated,factory --windows-sandbox elevated --timeout-minutes 60
```

Run all B0–B4 modes:

```powershell
dotnet run --project tools/factory-benchmark -- run benchmarks/bubble-sort --repeat 3 --model gpt-5.6-luna --windows-sandbox elevated --timeout-minutes 60
```

Options are `--repeat N`, `--model MODEL`, `--output PATH`, `--modes mode1,mode2`, `--keep-workspaces`, `--timeout-minutes N`, `--windows-sandbox elevated|unelevated`, and `--force`. A fixed `--output` directory enables successful-run resume; `--force` reruns existing results. Successful-run workspaces are removed after capture unless `--keep-workspaces` is set; failed-run workspaces are retained for diagnosis.

Exit code 0 means every requested mode has at least one successful iteration. Exit code 2 means at least one requested mode has zero successful iterations. Configuration or infrastructure failure before iterations can run returns 1. One failed iteration is recorded and does not stop later iterations.

## Reading reports

Each benchmark directory contains `report.json` and `report.md`. JSON is authoritative and contains raw runs, invocations, environment evidence, aggregates, decomposition metadata, and derived comparisons. Markdown is a human-readable projection; no metric exists only there.

Compare optimization results only when Codex version, model, reasoning effort, Factory/plugin versions, skill identities, source revision, and benchmark definition are suitable for comparison. Treat Direct/Factory as a reliability-and-cost comparison, not simply “useful tokens versus wasted tokens”: repeated planning, isolated execution, verification, and retries can change both cost and success rate.
