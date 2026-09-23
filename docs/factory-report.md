# IDD Factory report

`idd-factory-report` is a repository development/diagnostic utility for post-run
inspection of ordinary IDD Factory executions. It is not part of the
`idd-factory` or `idd-intent` plugins and is never required by Factory at
runtime.

The utility is read-only with respect to the repository, `.idd/`, Codex
sessions, archived sessions, and Codex state databases. It writes only files
explicitly requested with `--json` or `--markdown`.

## Factory run versus Codex thread

A Codex root thread can contain ordinary work before and after one or more
Factory runs. The report therefore treats a Factory run as a segment inside a
root thread, not as the whole thread.

The reporter uses deterministic evidence such as an explicit
`idd-factory-run` invocation/reference, the exact `spawn_agent` prompt,
native child linkage (including `receiver_thread_ids`), explicit agent-role
metadata, and terminal Factory events. Structured terminal assistant JSON such
as `{"status":"COMPLETED","reason":"..."}` is the highest-priority result
evidence. It does not use an LLM, embeddings, Git diffs, or inferred task
descriptions.

## Data sources

The detailed execution trace belongs to the coding-agent host. For Codex the
reporter reads:

- `CODEX_HOME/sessions/**/*.jsonl`;
- `CODEX_HOME/archived_sessions/**/*.jsonl`, when present;
- `state_*.sqlite` as an optional read-only index/linkage source.

The project-local `.idd/factory/current/` and `.idd/factory/results/`
directories are semantic/checkpoint context only. They are never treated as a
replacement execution log.

Codex home resolution order is:

1. `--codex-home <path>`;
2. `CODEX_HOME`;
3. the user's default `~/.codex`.

## Usage

From the repository being inspected:

```text
dotnet run --project tools/idd-factory-report -- latest
dotnet run --project tools/idd-factory-report -- list
dotnet run --project tools/idd-factory-report -- <thread-id>
dotnet run --project tools/idd-factory-report -- <thread-id> --run 2
```

A different repository can be selected with `--repo <path>`.

Output options can be combined:

```text
dotnet run --project tools/idd-factory-report -- latest \
  --json report.json \
  --markdown report.md \
  --verbose
```

The executable/assembly name is `idd-factory-report`; global tool installation
is not required.

## What the report contains

When the host trace exposes the required evidence, the report shows:

- root/planner/worker/subagent hierarchy;
- worker tasks from the actual spawn/input data;
- planner outcomes (`Tasks`, `Question`, `Done`, `Unknown`);
- Factory and per-agent timing;
- input, cached-input, new-input, and output token counts;
- tool calls, overlapping tool batches, command executions, and failed commands;
- a bounded chronological timeline;
- Factory result plus result evidence;
- current Factory checkpoint consistency;
- host-trace and reporter diagnostics.

Root token accounting is segment-aware. Cumulative counters are differenced
across the Factory boundary when a trustworthy pre-run baseline exists;
per-turn counters are summed only inside the Factory segment. The terminal
root-reported usage is also preserved separately even when a segment delta
cannot be proven. If root and child accounting may overlap, aggregate Total is
reported as unavailable with `overlap-unknown` status rather than adding the
counters and risking double counting.

## Partial and damaged traces

The parser is tolerant of unknown event types, malformed individual JSONL
records, a trailing partial line in an actively written rollout, missing state
DBs, unsupported DB schemas, and missing child rollouts.

Recoverable problems produce a partial report plus diagnostics. Missing facts
remain unavailable; the reporter does not reconstruct them from source code,
Git changes, or Factory checkpoint files.

Diagnostics are separated conceptually into Factory, host-trace, and reporter
problems. A warning is not itself a Factory failure.

## Privacy

Default output is summary-oriented. It does not print raw transcripts, complete
prompts, reasoning, stdout/stderr, environment variables, authentication data,
or arbitrary repository files.

Worker tasks and failed command text are mechanically normalized and bounded.
`--verbose` adds thread IDs, rollout paths, boundary evidence, Codex
capabilities, and parser/linkage detail, but still does not dump raw host
history.

## Exit codes

```text
0  report/list produced successfully
1  invalid arguments or reporter/infrastructure failure
2  requested Factory run not found
3  reserved for a trace too damaged to build a meaningful report
```

Warnings and partial metrics do not by themselves cause a non-zero exit code.

## Troubleshooting

If `latest` finds nothing, verify that:

- `--repo` resolves to the same Git repository recorded as the Codex session
  working directory;
- the correct Codex home is selected;
- the session history still exists in `sessions/` or `archived_sessions/`;
- the run contains deterministic Factory evidence.

Use `--verbose` to see detected Codex capabilities and boundary/linkage
diagnostics.

## Live-eval artifacts

`run-live-factory-evals.bat` assigns one artifact directory for the live run
and writes `factory-report.json` and `factory-report.md` there after the
test. The live harness also preserves a `report-source/` allow-list containing
only session history, archived session history, and `state_*.sqlite` files
when present. Authentication files such as `auth.json` are never copied into
that report source.
