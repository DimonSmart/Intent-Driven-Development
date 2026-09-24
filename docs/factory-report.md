# IDD Factory report

`idd-factory-report` is a read-only diagnostic utility for post-run inspection of IDD Factory executions. Raw host-owned Codex JSONL is normalized into semantic events, `FactoryReportEngine` builds one `FactoryRunReport`, and console/Markdown/JSON format that report.

Core evidence rule:

```text
structured evidence > deterministic legacy fallback > unavailable
```

The reporter does not use an LLM, embeddings, Git diff, filenames/class names, fuzzy role matching, or timestamp grace periods to invent missing execution facts.

## Current native Codex wire format

The normalization layer supports the current native shapes:

```text
event_msg
  payload.type = item_started / item_completed
  payload.item.type = CollabAgentToolCall / CommandExecution / FileChange

token_usage_record
  thread_id / turn_id / root_turn_id / response_id
  turn_token_usage / thread_token_usage
```

Known lifecycle/item aliases are normalized deterministically. Legacy `item.started`, `item.completed`, `collab_tool_call`, `command_execution`, and response-item wrappers remain supported.

Structured `sender_thread_id -> receiver_thread_ids[]` is the preferred parent/child evidence. Textual UUID extraction is retained only as a legacy fallback.

## Factory run versus Codex thread

A root Codex thread may contain ordinary work before and after Factory. Root Factory tokens therefore represent the Factory segment, not the whole root thread.

For current native Codex, `thread_token_usage` is treated as an authoritative cumulative counter for its thread. Root Factory usage is `terminal - pre-run baseline` when both boundaries are trustworthy. A terminal token record written after the terminal assistant response is included only when structural correlation (`turn_id`, `root_turn_id`, or `response_id`) ties it to that turn; it never moves `Run.FinishedAt`.

Factory-owned child threads use their final authoritative `thread_token_usage`. Aggregate Total is emitted only when every distinct thread is authoritative and counted exactly once. Legacy formats whose overlap semantics are not proven remain unavailable / `overlap-unknown`.

## Agent topology and tasks

Agent topology remains flat in JSON (`threadId`, `parentThreadId`, `role`, `sequence`) and is rendered as an arbitrary-depth tree in human-readable output:

```text
Factory root        5m 22s   668.8k in / 607.0k cached / 2.4k out
├─ Planner           42.1s     60.6k in / 47.1k cached / 763 out
├─ Worker #1        108.2s    166.1k in / 147.2k cached / 1.8k out   ProductCode
└─ Worker #2         78.7s    129.4k in / 107.8k cached / 1.8k out   Catalog
```

A single planner is labeled `Planner`; multiple planners are numbered. Missing parents and cycles are reported as topology diagnostics and are not silently re-parented to root.

Worker tasks are extracted from Factory spawn/direct instructions. A short task title is only a bounded presentation projection of that text.

## Native operations and wrappers

Semantic metrics distinguish native operations from host/Code Mode wrapper calls. Native `CommandExecution`, `FileChange`, `spawn_agent`, and `wait_agent`/`wait` are counted once per logical operation. Wrappers are reported separately in verbose output.

`Tool batches` is nullable. It is calculated only when reliable started/completed intervals are available; completed-only operations do not create synthetic batches.

## Completion and protocol validation

The normalized report includes:

```text
Planner done:          yes / no / unavailable
Project verification: passed / failed / not configured / unavailable
Declared result:       COMPLETED / ...
Protocol validation:   ok / warning / unavailable
```

A structured root response such as `{"status":"COMPLETED"}` is authoritative for the declared result, but it does not prove protocol compliance. If workers ran and no fresh planner `# Done` follows them, the declared result remains COMPLETED and diagnostic `factory/completed_without_planner_done` is emitted.

Project verification is reported as passed/failed only from structured command/result evidence after the final planner `# Done`. Absence of a verification command is not treated as `not configured`.

## Factory project state

`.idd/factory/current/` and `.idd/factory/results/` are supplemental project-state evidence only. They are never used to reconstruct execution history. Missing request/plan after a successful run is normal; a missing archived result is not itself a warning.

## JSON schema

Schema version is `2`. `toolBatches` and other unavailable metrics use nullable representation so a real zero is distinct from unavailable evidence.

## Usage

```text
dotnet run --project tools/idd-factory-report -- latest
dotnet run --project tools/idd-factory-report -- list
dotnet run --project tools/idd-factory-report -- <thread-id>
dotnet run --project tools/idd-factory-report -- <thread-id> --run 2
```

Options: `--repo`, `--codex-home`, `--run`, `--json`, `--markdown`, `--verbose`.

## Partial/damaged traces

Unknown unrelated Codex events are tolerated. Missing Factory-relevant evidence remains unavailable rather than being guessed. Malformed records, missing child rollouts, unavailable state DBs, topology damage, and incomplete accounting produce partial reports plus `factory`, `host-trace`, or `reporter` diagnostics.

## Regression fixture

The test suite contains a sanitized current-native JSONL fixture preserving `event_msg/item_completed`, `CollabAgentToolCall`, `CommandExecution`, `FileChange`, and `token_usage_record` structures. Synthetic helpers remain appropriate for isolated legacy/parser unit tests.
