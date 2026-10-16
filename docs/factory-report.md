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

For Codex MultiAgentV2, Factory explicitly sets a unique native `task_name`
(`idd_factory_planner_01`, `idd_factory_worker_01`, etc.) for **each**
planner/worker spawn attempt, including rejected attempts, and
`fork_turns: "none"` for both roles. The **requested** `task_name` in
spawn arguments is recorded separately from the **canonical** child agent path
returned by the host. The requested name is a role marker, not evidence that
the host created a child or honored the model/effort overrides.

Spawn calls and their started/completed/response outputs are deduplicated by
native call ID. Correlation prefers explicit child-thread IDs, then a canonical
output path matching child `session_meta.agent_path` **under the confirmed
parent**, then a unique requested short name matching the final child-path
segment within that parent namespace. A matching basename in another parent
tree is never sufficient. Conflicting native role, task name and path evidence
or duplicate names produce diagnostics and a fail-closed `unknown` role; the
`agent_path` field is not an `agent_role` field.

Native host errors (code/message or unambiguous rejection output) are retained
on the attempted spawn as `rejected`, even when no child rollout exists.
Missing child evidence *without* rejection is `unknown`, never assumed to be
rejected. Rejected attempts do not fabricate AgentReport or TaskReport entries;
`Tasks` may be empty on a genuinely blocked first-worker spawn.


## Factory run versus Codex thread

A root Codex thread may contain ordinary work before and after Factory. Root Factory tokens therefore represent the Factory segment, not the whole root thread.

For current native Codex, `thread_token_usage` is the preferred, authoritative **cumulative counter for its own thread**; `turn_token_usage` on the same record is a different representation and is never added to it. Repeated cumulative snapshots replace earlier snapshots, rather than being summed.

The root agent's **Factory Run usage** is `EndCumulativeUsage - StartCumulativeUsage`. The baseline is the last reliable per-thread cumulative snapshot before Factory starts. It may be zero **only if the rollout starts with a session metadata record and a complete, recognized initialization prelude** without prior model work. Known session-start events include developer/system instructions, the host's structured environment context, `task_started`, `world_state`, `turn_context`, and `thread_settings_applied`. These events alone are not evidence that the model already ran. A prior user turn or model activity without a trustworthy cumulative snapshot, an unknown prelude event, or a truncated beginning does **not** justify a zero baseline: the root segment remains unavailable. If Factory is invoked again in the same thread, its baseline is the latest cumulative snapshot before **that** invocation, so earlier Factory Runs and intermediate work are excluded.

The root agent's **Root reported usage** is a separate metric: the cumulative Codex counter at the run's established terminal boundary. This can equal the root Factory Run usage in a fresh session, but it will usually be larger in a reused root thread. Console and Markdown show both; JSON preserves `metrics.rootReportedTokens` for cumulative usage, while the root entry in `agents[].tokens` records the Factory segment.

A `token_usage_record` written **after** the terminal assistant message is included in both calculations only when native identifiers correlate it to the terminal response, and its `thread_id` matches the owning root thread. The parser recognizes `payload.internal_chat_message_metadata_passthrough.turn_id` on response messages and `payload.turn_id` on usage records. Conflicting turn IDs fail correlation with a `conflicting_turn_ids` diagnostic. A matching `root_turn_id` alone cannot override two different `turn_id` values; timestamps and arbitrary UUIDs in message text cannot establish identity. Post-terminal counters never extend `Run.FinishedAt` or the Factory duration.

Factory-owned child threads use their own final authoritative `thread_token_usage`. Aggregate `Total` is available only when the root Factory segment and **every** distinct planner/worker thread have complete, independent, non-overlapping, trustworthy per-thread counters. Each thread is counted once; missing or inconsistent counters and ambiguous identity yield `unavailable` or `overlap-unknown`, not made-up zeros. Legacy `turn_token_usage` and other supported trace shapes continue to be parsed, but without established independent cumulative thread accounting they cannot justify an exact aggregate.

**What InputTokens means:** these are the actual input tokens **reported by Codex**, potentially including system/developer instructions, registered tools, skills, plugins, and other context supplied by the host. This is not the incremental cost attributable solely to Factory. `CachedInputTokens` are already **part of** `InputTokens`; they must not be added again. `NewInputTokens = InputTokens - CachedInputTokens`, and output tokens are reported separately.

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

For each worker, the report preserves the planner `ExecutionProfile` and the
model/reasoning settings requested in that worker's native spawn arguments.
The planner profile is authoritative; a profile found in the spawn prompt is
recorded separately as `spawnExecutionProfile`. Conflicting values emit
`factory/worker_execution_profile_mismatch`. A readable legacy planner task
without the profile section uses `standard`, independently of the spawn profile.
Empty, repeated, or unknown explicit profiles remain unknown; so does a worker
without readable matching planner evidence.
Native results containing only a canonical `task_name` are correlated with the
child's `agent_path` and parent thread metadata to preserve requested settings.
It reports actual settings from direct `model` and `effort` fields in the
worker's own `turn_context`, or explicit resolved/actual fields in spawn events,
spawn output, or the worker's own events. Readable worker `turn_context` fields
take precedence over spawn metadata and other worker events. A disagreement
between spawn metadata and the worker context emits
`host-trace/worker_spawn_settings_conflict`, preserving both sources in the
diagnostic. When multiple worker contexts contain different model or effort
values, `host-trace/worker_turn_context_settings_conflict` lists the evidence.
Each conflicting field remains `unknown` rather than selecting the first turn
or falling back to spawn metadata; stable fields are retained. Routing cannot
be fully verified in that case. Repeated identical contexts are consistent.
Absent context fields may still use explicit resolved/actual evidence.
Nested collaboration defaults and
contexts explicitly belonging to other threads are excluded.
Settings attached to a worker's child-agent tool calls are not its own settings.
It never infers actual settings from the current `.idd/execution.yaml`
or root session settings. Missing actual fields remain `unknown` and emit
`reporter/worker_actual_settings_unavailable`, naming the worker and missing
fields. The live routing evaluation requires both actual settings to be present
and match, so missing evidence cannot silently pass that check. When explicit
requested and actual values differ, the report emits
`factory/worker_execution_settings_mismatch`. Later changes to
`.idd/execution.yaml` cannot rewrite these historical spawn records.

Encrypted spawn prompts emit `host-trace/spawn_prompt_unreadable` and are not
rendered as task titles or contracts. Readable planner tasks supply the task
and profile when available; otherwise explicit unavailable-data diagnostics
identify the missing task or profile.

## Native operations and wrappers

Semantic metrics distinguish native operations from host/Code Mode wrapper calls. Native `CommandExecution`, `FileChange`, `spawn_agent`, and `wait_agent`/`wait` are counted once per logical operation. Wrappers are reported separately in verbose output.

Response-item spawn/wait calls are retained when the host also emits native
command events. Calls sharing an ID with native evidence are counted once.

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

Project verification is reported as passed/failed only from structured command/result evidence after the final planner `# Done`. Command evidence is matched using its effective working directory, including an explicit `workdir` or `cwd` carried inside native tool arguments; checks from another directory are not credited. A configured `confirmation: required` check cannot be approved by a successful process exit alone: in the absence of independently verifiable user confirmation in the trace, a successful command remains `unavailable`. Failed commands still count as failed. Absence of a verification command is not treated as `not configured`.

When `.idd/verification.yaml` is available, the reporter reads its final check
selection and matches standalone command argv against those checks. Every
selected check must have conclusive evidence; the latest recorded result for
each check determines its outcome. A recovered failure remains in failed-command
metrics and diagnostics but does not override a later successful execution of
the same check. `dotnet test` additionally requires a nonempty successful VSTest
execution summary; a silent zero exit code is unavailable evidence.

The policy is read from the supplied repository snapshot, not reconstructed from
Git history. Use the run's saved workspace for historical inspection. Invalid
policy, final path rules without authoritative changed scope, manual checks,
unsupported shell expressions, and missing results remain `unavailable`. Without
policy, only recognizable platform verification commands are considered;
`restore`, `git status`, or an archive command cannot prove verification.

After an observed final-check failure, the reporter expects a fresh planner
before root verification resumes. A missing handoff emits
`factory/verification_failure_without_fresh_planner` and makes protocol validation
`warning`, even when the repeated checks pass. Declared `COMPLETED` without all
configured final checks also produces a protocol warning. These checks inspect
trace evidence; they do not execute checks or create Factory lifecycle state.

## Factory project state

`.idd/factory/current/` and `.idd/factory/results/` are supplemental project-state evidence only. They are never used to reconstruct execution history. Missing request/plan after a successful run is normal; a missing archived result is not itself a warning.

## JSON schema

Schema version remains `3` (no metric fields were added or renamed). `toolBatches`, actual worker settings, and other
unavailable metrics use nullable representation so a real zero is distinct from
unavailable evidence.

## Usage

```text
dotnet run --project tools/idd-factory-report -- latest
dotnet run --project tools/idd-factory-report -- list
dotnet run --project tools/idd-factory-report -- <thread-id>
dotnet run --project tools/idd-factory-report -- <thread-id> --run 2
```

Options: `--repo`, `--codex-home`, `--run`, `--json`, `--markdown`, `--verbose`.

## Partial/damaged traces

Unknown unrelated Codex events are tolerated. Missing Factory-relevant evidence remains unavailable rather than being guessed. Malformed records, including a damaged final JSONL line, missing child rollouts, unavailable state DBs, topology damage, and incomplete accounting produce partial reports plus `factory`, `host-trace`, or `reporter` diagnostics. Missing worker settings, task/profile, agent timestamps/token fields, final verification/result evidence, and tool-batch intervals are explicitly explained in Diagnostics in console, Markdown, and JSON output. Unspecified requested overrides are distinguished from unreadable actual settings: omitted overrides may intentionally inherit host defaults.

## Regression fixture

The test suite includes sanitized native root JSONL fixtures from the three October 16 Live Eval token-counter shapes, with the final usage record following the structured Factory reply, plus a sanitized current-native JSONL fixture preserving `event_msg/item_completed`, `CollabAgentToolCall`, `CommandExecution`, `FileChange`, and `token_usage_record` structures. Synthetic helpers remain appropriate for isolated legacy/parser unit tests.
