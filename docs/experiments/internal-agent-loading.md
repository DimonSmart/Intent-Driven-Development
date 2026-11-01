# Internal Factory Skill loading experiment — 2026-11-01

The Codex candidate completed all three ordinary TwoStepCatalog evaluations,
and every active child loaded its complete registered Skill. The migration gate
is **not passed**: root avoided reading child protocols in only two of three
runs, root output did not consistently decrease, successful baseline total usage
is incomplete, and Claude is unavailable. Keep this an opt-in experiment.

## Snapshot and method

- Baseline commit: `faf980c82544a9084a11c2b5da97b1f1822fe04d` (`main`).
- Experiment branch: `codex/internal-agent-eval`, isolated managed worktree.
- Host: macOS arm64; Codex CLI `0.165.0-alpha.1.2`; .NET SDK `10.0.400`.
- Root and Planner: `gpt-6-luna`, `medium` reasoning.
- Workers: `standard → gpt-5.6-terra`, `strong → gpt-6.1-sol`,
  `economy → gpt-6-luna`, all with `medium` reasoning.
- Same tracked TwoStepCatalog template, task, models, effort, independent nine
  product tests, and native V2 assertions in both variants. No model substitution.
- Three ordinary evaluations per variant; also exercise the existing unavailable
  worker model and final-verification recovery scenarios. Additional scenarios
  are smoke checks, not three-repeat reliability evidence.
- Each evaluation generates and installs a local marketplace into a separate
  temporary Codex home. The user’s installed plugins are not replaced.
- Ordinary repetitions overlap in time. Host load and cache history are not
  controlled, so latency and small token differences are descriptive observations.

The first full baseline suite started before prototype implementation. Its
already-generated ordinary baseline was unchanged. Later default generation
was independently compared against generation from an archive of the exact
baseline commit: **all 277 files are byte-identical**. The suite wrapper's
working-tree integrity check also failed because this chat edited experiment
sources while that suite was running; this is separate from its two genuine
Live failures. Per-case summary, TRX and native evidence retain those failures.

MSBuild node reuse held redirected output pipes open after restore in that
first suite. Only its identified orphan MSBuild nodes were terminated. Later
runs set `MSBUILDDISABLENODEREUSE=1` and
`DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`. Exclude the first failed run's timing
from successful-run performance comparisons.

## Implemented candidate

Generator option `--internal-agent-experiment` applies an experimental output
overlay for Codex and Claude. Default generation is unchanged. It creates:

- `idd-internal-factory-planner`;
- `idd-internal-factory-worker`;
- each Skill's own `references/engineering-guardrails.md`.

The generated root instruction requests a fresh native child plus a short
instruction to load the appropriate Skill. The old planner/worker protocol
references are removed from the root package. Child bodies come from the
existing canonical protocols. Only their delivery preambles change; everything
from `## Inputs` onward is preserved. Missing assignment or missing required
instructions require `BLOCKED`. Claude output uses `user-invocable: false`
and `disable-model-invocation: false`; actual Claude behavior is unverified.

The Live harness enables this path only with
`IDD_FACTORY_EVAL_INTERNAL_SKILLS=1`. The canonical production manifest,
public command names, Intent workflows and normative Product Intent are not
migrated. This overlay is an experiment, not the final inventory implementation.

## Ordinary evaluation results

All token values below are native counters, not estimates from text length.
Root input includes cached input; new input is `input - cached`.

| Run | Functional evaluation | Root input | Cached input | New input | Root output | Complete total tokens |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Baseline 1 | FAIL: malformed final JSON | 2,310,083 | 2,232,448 | 77,635 | 20,504 | 3,048,017 |
| Baseline 2 | PASS | 1,299,559 | 1,245,696 | 53,863 | 9,499 | unavailable |
| Baseline 3 | FAIL: root reported native tools unavailable | 147,674 | 117,504 | 30,170 | 1,167 | 148,841 |
| Candidate 1 | PASS; root read both child Skills | 1,149,649 | 1,098,112 | 51,537 | 10,381 | 1,683,893 |
| Candidate 2 | PASS; extra replanning | 1,549,209 | 1,498,368 | 50,841 | 10,472 | 2,381,840 |
| Candidate 3 | PASS | 1,046,351 | 1,003,392 | 42,959 | 7,582 | 1,683,588 |

Baseline 3 never attempted a native spawn. Other runs demonstrate that this
host supports the required structured arguments; this was a tool-discovery
failure, not proof that the host lacks `task_name`.

Only successful runs are eligible for performance comparison. There is just
**one successful baseline**, so the following is descriptive, not a reliable
estimate of the mechanism's effect:

| Metric | Successful baseline (n=1) | Candidate median (n=3) | Change |
| --- | ---: | ---: | ---: |
| Root input | 1,299,559 | 1,149,649 | −11.5% |
| Root new input | 53,863 | 50,841 | −5.6% |
| Root output | 9,499 | 10,381 | **+9.3%** |
| Whole evaluation elapsed, seconds | 347.470 | 409.814 | **+17.9%** |
| Complete total tokens / cost | unavailable | 1,683,893 / unavailable | unavailable |

Candidate root output range: 7,582–10,472. Whole evaluation elapsed range:
377.675–430.367 seconds, including setup and independent verification. Native
trace root durations are retained separately; do not equate their clock domain
with local harness elapsed time. Candidate 3 alone used 20.2% fewer root output
tokens than baseline 2, but this did not reproduce across all three candidates.

Candidate 2 used four Planners and three Workers, versus two Planners and three
Workers in candidates 1 and 3. Additional root turns and replanning can erase
the savings from shorter spawn instructions. Candidate 1 directly read both
Skill bodies before spawning, violating the root-loading criterion.

## Loading, isolation and accounting evidence

All **17 active candidate children** (8 Planners, 9 Workers) have their full
expected generated Skill text in model-visible tool output, including the
Worker using `gpt-6.1-sol`. This comparison also unwraps JSON-encoded command
results. Their first command includes reading their own Skill; observed file
changes follow that read. Native Live assertions pass for fresh histories,
requested and actual worker model/effort, task ownership, preserved durable
Intent, and independent final verification. Every observed spawn explicitly
uses `fork_turns: "none"`.

Native spawn message bodies are encrypted in retained traces. They are not
decoded. The overlay establishes the requested delivery behavior, and child
reads establish actual loading, but absence of complete protocol text in every
actual spawn body cannot be directly inspected. Do not claim that ciphertext
length proves token savings or exact prompt contents.

The comparison tool uses each thread's native `thread_token_usage` only after
reconciling it with the sum of unique `response_id` usage records. It also checks
for cross-thread response overlap. Cached input is never added twice; reasoning
output is not added to output again. Candidate totals reconcile. Baseline 2
includes one early child trace without model/usage records, so its complete
total stays unavailable. The existing Factory Report similarly reports
`overlap-unknown` rather than a trustworthy baseline total. No dollar price or
total savings is invented.

## Gate

| Criterion | Codex | Claude |
| --- | --- | --- |
| Registered Skill fully loaded by fresh child | PASS in 3 ordinary runs, 17 active children | NOT VERIFIED: CLI absent |
| Fresh histories, task ownership and model settings | PASS in ordinary Live assertions; no extra native sandbox claimed | NOT VERIFIED |
| Ordinary functional correctness | PASS 3/3 candidate; baseline 1/3 | NOT VERIFIED |
| Root avoids reading child protocol bodies | **FAIL: 1/3 root read both** | NOT VERIFIED |
| Root output / total cost / latency improvement | **NOT VERIFIED: inconsistent output, incomplete baseline total, uncontrolled load** | NOT VERIFIED |
| Engineering Always + Conditional scenario | NOT VERIFIED | NOT VERIFIED |
| Actual autocomplete / menu behavior | NOT VERIFIED | NOT VERIFIED |

The direct internal Worker invocation without a Factory task/packet returned
`BLOCKED` and left an empty isolated fixture unchanged (one smoke invocation).
Additional existing Live contract scenarios ran once per variant:

| Scenario | Baseline | Candidate |
| --- | --- | --- |
| Unavailable worker model; pending Task preserved, no fallback | PASS | PASS |
| Replan after injected final-verification failure, then finish verification | FAIL: root stopped BLOCKED | PASS |

Their raw evidence is retained in the contract-evaluation directories. These
single samples do not establish a statistically improved recovery rate.

**Decision: do not promote the candidate or apply the broad command rename.**
Codex child Skill loading is feasible, but reliable savings and the complete
cross-host contract have not been established. Retain the small opt-in prototype
and measurements. A next experiment should front-load the root prohibition on
opening child Skills, investigate avoidable replanning, stabilize baseline tool
discovery/final output, resolve missing native usage, and rerun both variants
sequentially with enough successful comparable runs. Claude and Engineering
need their own Live evidence before the migration gate can pass.

## Reproduction and retained artifacts

Build the solution. Set `MSBUILDDISABLENODEREUSE=1` and
`DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`. Run the normal Live command with
`IDD_FACTORY_EVAL_INTERNAL_SKILLS` absent/`0` for baseline and `1` for candidate:

```sh
pwsh -NoProfile -File scripts/Check.ps1 -Mode Live
```

For ordinary repetitions, set `IDD_RUN_LIVE_FACTORY_EVALS=1` and a unique
`IDD_FACTORY_EVAL_ARTIFACT_DIR`, then run:

```sh
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --no-build \
  --filter FullyQualifiedName~Factory_UsesNativeAgentsAndCompletesTwoStepCatalog \
  --logger 'trx;LogFileName=live-tests.trx' --results-directory "$IDD_FACTORY_EVAL_ARTIFACT_DIR"
```

Retained local evidence (Git-ignored, no auth files retained):

- `artifacts/factory-evals/20261101-172626001-2eaf17a1da774cffa93b3e634ef01bc8/`:
  first full baseline suite, with `model-unavailable/` and `verification-recovery/`.
- `artifacts/internal-agent-eval/baseline-{2,3}/` and `candidate-{1,2,3}/`:
  per-case summaries, native sources, reports, TRX, workspaces and command logs.
- `artifacts/internal-agent-eval/candidate-contracts/`: additional candidate scenarios.
- `artifacts/internal-agent-eval/direct-worker-guard/`: direct invocation smoke.
- `artifacts/internal-agent-eval/snapshot-check/result.json`: hashes of all
  277 identical default-generated files from the exact baseline and current code.
- `artifacts/internal-agent-eval/environment.json`, `comparison.json`, `run-logs/`.

Regenerate `comparison.json` with `scripts/compare-internal-agent-evals.py`,
passing repeated `--baseline` and `--candidate` artifact paths, the generated
`--candidate-marketplace artifacts/internal-agent-eval/prototype`, and `--output`.
It retains failures and per-agent counters separately from successful-run summaries.

Fast validation passes: 222 generation tests, 141 report tests, 36 local Live
harness tests, and repository script checks. The added generation test proves
unchanged task-rule bodies, local Engineering references, Claude invocation
metadata, repeatable generation and untouched default output. Focused conformance
review covers the default generation and Factory contracts in IDD-0001,
IDD-0003 and IDD-0006; no normative Product/Engineering documents changed.
