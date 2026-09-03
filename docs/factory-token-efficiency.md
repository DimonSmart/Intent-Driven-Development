# Factory Token Efficiency

Factory is intended to add planning, isolation, and deterministic verification without making orchestration an uncontrolled token multiplier. Its token cost therefore needs to be understood as several scopes rather than one undifferentiated number:

- the root launcher or transport;
- semantic workers grouped by role and attempt;
- the complete end-to-end Factory total;
- tool activity, failures, retries, and repeated planning cycles that help explain the total.

Gross input, cached input, new input, and output are reported separately. New input is calculated as gross input minus cached input only when both counters are available and consistent. Sequential tool batches are also important: many independent tool calls issued together represent one model/tool round rather than many sequential rounds.

Factory telemetry by itself cannot say whether Factory is economical. A meaningful comparison must hold the task, model, reasoning effort, workspace, and correctness check constant while adding Factory mechanisms incrementally. The repository's [Factory Benchmark Runner](factory-benchmarking.md) provides that comparison.

The companion analyzer helps attribute run-level cost to individual Factory attempts and detect regressions over time.

IDD Factory runs can report surprisingly large `input_tokens` even for small tasks. This document explains how to measure that cost after a completed run, how to distinguish normal Codex agent overhead from Factory-specific waste, and how to detect regressions over time.

The companion tool is:

```text
tools/factory-token-analysis
```

It analyzes the persisted Factory result artifacts only; it does not call a model and does not modify the run.

## What was observed

A controlled Bubble Sort smoke run on August 14, 2026 used Codex CLI `0.148.0-alpha.15` with semantic workers on `gpt-5.6-luna`.

The completed Factory run contained six semantic attempts and reported:

```text
gross input        566,339
cached input       430,848
new input          135,491
output               8,979
```

The important finding was that the Factory invocation packets were small: the recorded dynamic inputs were only 713-3,470 characters. User-skill inheritance and the Factory skill bodies were also small contributors.

Controlled `codex exec` probes isolated the dominant fixed cost:

```text
empty worker, isolated CODEX_HOME               12,688 input tokens
empty worker, normal user skills                13,318 input tokens
worker with Factory decomposer skill            13,542 input tokens
two minimal shell commands in one tool batch    25,823 input tokens
two minimal shell commands sequentially         38,976 input tokens
```

These numbers are a snapshot, not a permanent budget. Codex runtime versions, models, system instructions, tool schemas, and cache behavior can change them.

The durable conclusion is the relationship:

```text
gross input ~= base Codex context x sequential model/tool rounds
              + accumulated conversation/tool context
```

A new sequential model/tool round can therefore add roughly another full base agent context to gross input even when the command itself returns almost nothing. Prompt caching reduces the cost of repeated content, but `cached_input_tokens` remains part of `input_tokens`; it is not additional input.

## Factory-specific waste found in the smoke run

The analysis also found costs that were not inherent to the base Codex worker:

- A redundant verification-only work item created an entire extra semantic attempt.
- Workers had to load their Factory-selected `SKILL.md` through an initial tool round because the runtime sent only `Use $skill`; the runtime now reads that installed skill itself and inlines its instructions into the self-contained worker prompt.
- Broad workspace inventories included `.idd/factory`, previous attempt logs, `bin`, and `obj`.
- Workers tried Git commands in a workspace without `.git`.
- Failed `git diff` calls emitted large help/error output that became context for later model rounds.
- Repeated semantic attempts accumulated many tool results over several sequential batches.

In the observed run, failed shell commands alone produced roughly 61 KB of text. This matters because tool output is not paid only once: it remains in the conversation and can be re-sent on later rounds.

The selected Factory worker skill is intentionally not exposed to that worker through its private `CODEX_HOME`. The runtime validates the packaged skill, reads its `SKILL.md`, and places the instructions directly in the worker packet. User skills may still be inherited according to the configured capability policy, but a user skill with the selected Factory skill name is excluded. A worker reading its own Factory `SKILL.md` is therefore a regression signal rather than expected bootstrap behavior.

## Metrics to watch

Do not use a single absolute token limit. Track both token and structural metrics.

The analyzer reports per attempt:

- role, skill, work item, and launch reason;
- dynamic Factory input size;
- sequential tool batches;
- shell command count;
- shell output characters;
- failed-command output characters;
- gross input tokens;
- cached input tokens;
- new input tokens (`gross - cached`);
- output tokens;
- duration.

It also aggregates the same cost by role.

The most useful regression signals are:

1. **Semantic attempts** — unexpected workers are expensive because every worker starts a fresh Codex agent.
2. **Tool batches** — a proxy for sequential model/tool rounds. Parallel tool calls in one batch are much cheaper than the same calls spread across separate rounds. `file_change` and `command_execution` events are included; other future tool types may require extending the analyzer.
3. **New input tokens** — useful for detecting genuinely new context independent of cache reuse.
4. **Gross input tokens** — useful for detecting round explosion.
5. **Tool output characters** — large outputs expand later model contexts.
6. **Failed tool output characters** — usually pure context pollution and should remain close to zero.

`commands` alone is a weak metric because many commands can execute concurrently in one model/tool round.

## Analyze a completed run

Pass either a specific result directory or a workspace. When a workspace is supplied, the latest directory under `.idd/factory/results` is used. Finalization moves the complete run directory into `results`, so completed runs retain `events.jsonl`, `attempts`, and the other execution diagnostics consumed by this analyzer.

```bat
dotnet run --project tools/factory-token-analysis -- analyze C:\Private\FactoryBubbleSortSmoke
```

To also save a machine-readable report:

```bat
dotnet run --project tools/factory-token-analysis -- analyze C:\Private\FactoryBubbleSortSmoke --json token-report.json
```

The tool reads:

```text
events.jsonl
attempts/*/attempt-telemetry.json
attempts/*/invocation.json
attempts/*/stdout.log
```

It also flags common context-pollution patterns such as:

- a worker reading its own Factory `SKILL.md` even though the runtime inlines the selected skill instructions;
- broad recursive workspace inventory;
- Git failures caused by a non-Git workspace;
- failed commands producing at least 10,000 characters;
- five or more sequential tool batches;
- planning attempts consuming more than 60% of the run's gross input.

These warnings are diagnostic signals, not proof of a defect. For example, a genuinely large repository may legitimately require more inspection.

## Establish a baseline

Token counts vary across runs, so do not baseline from one execution if it can be avoided. Run the same small canonical scenario 3-5 times with comparable:

- Codex CLI/runtime;
- semantic model and reasoning configuration;
- Factory version/skills;
- workspace contents;
- user-skill setup.

Keep each completed result directory, then create a median baseline:

```bat
dotnet run --project tools/factory-token-analysis -- baseline factory-token-baseline.json ^
  C:\FactoryRuns\run-1 ^
  C:\FactoryRuns\run-2 ^
  C:\FactoryRuns\run-3
```

The baseline stores the median of the structural and token metrics. Median is preferred to mean because one unusual agent trajectory should not redefine the expected cost.

The baseline also records the requested model set and Factory skill versions. A mismatch is reported as a comparability note.

## Compare a later run

```bat
dotnet run --project tools/factory-token-analysis -- analyze C:\Private\FactoryBubbleSortSmoke ^
  --baseline factory-token-baseline.json
```

The analyzer currently uses these regression thresholds:

| Metric | Warning | Critical |
| --- | ---: | ---: |
| Gross input | > 1.25x baseline | > 1.50x baseline |
| New input | > 1.25x baseline | > 1.50x baseline |
| Tool output chars | > 1.50x baseline | > 2.00x baseline |
| Semantic attempts | > baseline + 1 | > baseline + 2 |
| Tool batches | > baseline + 2 | > baseline + 5 |
| Failed tool output | — | >= 10 KB above baseline |

For CI or a release-certification check, add:

```bat
--fail-on-regression
```

The tool returns exit code `2` when a critical regression is detected. Without that option it reports findings but returns success, which is useful while the baseline is still being tuned.

## How not to miss real overuse

A token regression can hide if only one metric is watched.

For example:

- Good cache reuse can keep `new input` moderate while excessive sequential rounds make gross input explode.
- A model/runtime update can raise the fixed base context while the Factory workflow itself remains efficient.
- A redundant semantic worker can add a large cost even if every individual attempt looks reasonable.
- One failed command can inject tens of kilobytes of useless context without noticeably changing the original Factory input packet.

For that reason, treat these together as the primary efficiency contract:

```text
semantic attempts
tool batches
gross input
new input
tool output chars
failed tool output chars
```

When one of them moves, inspect the per-attempt table before optimizing prompts.

A good canonical smoke task should be deliberately small and stable. Bubble Sort with a console app and a fixed seven-case test suite is suitable because the repository context is tiny, the expected product is unambiguous, and unexpected semantic attempts or tool rounds stand out immediately.

Do not compare unrelated production tasks to this smoke baseline. Use the smoke baseline to detect Factory/Codex orchestration regressions, and maintain separate baselines for other repeatable workload classes if needed.

## Interpreting cached input correctly

Codex usage reports:

```text
input_tokens
cached_input_tokens
output_tokens
```

`cached_input_tokens` is a subset of `input_tokens`. Therefore:

```text
new_input_tokens = input_tokens - cached_input_tokens
```

Do not calculate total input as `input + cached`; that double-counts cached tokens.

Both gross and new input are useful:

- **gross input** shows how much context was repeatedly presented to the model and is sensitive to sequential-round growth;
- **new input** shows how much non-cached context was introduced.

The analyzer reports both deliberately.
