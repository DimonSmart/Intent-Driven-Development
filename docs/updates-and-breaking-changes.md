# Updates and Breaking Changes

This page records IDD changes that require action in repositories that already use the toolkit.

## 2026-09-18 — Factory uses native agents; runtime and MCP removed

Factory has been simplified to native-agent orchestration.

Current Factory is now:

```text
fresh planner
-> current batch
-> fresh sequential worker per task
-> fresh planner
```

The packaged .NET Factory Runtime, Factory MCP server, `factory_run`,
`factory_continue`, `factory_restart`, `factory_retry`, `factory_cancel`,
and `factory_status` operations are removed. Generated plugins no longer contain
`runtime/idd-factory.dll`, `factory.yaml`, or Factory `.mcp.json`.

Planner and every worker must run in a fresh semantic context without inherited
parent transcript. Adapters use native child-agent spawn and terminal waiting;
model-driven status polling is not a compatibility fallback.

The separate `RelevantCompletedWork` protocol is removed. Planner tasks are
self-contained and may carry only optional stable `TaskRelatedIntent` IDs.
Workers resolve those IDs against current `.idd/intent/` themselves.

Factory temporary state is reduced to request, remaining plan, short completed
summaries, exact answers, and optional question/verification-failure files. The
repository is authoritative implementation reality and execution is deliberately
at-least-once.

Legacy `.idd/factory/current/state.json` runs are not migrated or exactly
continued. Preserve repository changes. For an explicit restart, reuse a valid
persisted `request.md` as the replacement request when appropriate, archive old
state for diagnostics if useful, and start the simplified Factory after Intent
Preflight.

`IDD-0006` supersedes the deterministic runtime, blocking transport, and
runtime-owned backend failure decisions in IDD-0002, IDD-0004, and IDD-0005.

Updating the installed plugins and migrating project-owned files are separate operations. Follow [Updating IDD](updating-idd.md) to refresh `idd-intent` and `idd-factory`. Then apply any relevant migration instructions below. Plugin updates do not automatically rewrite a repository's `.idd/intent/` directory.

## 2026-09-16 — Explicit Factory restart semantics

Run-level restart and cancellation are now unambiguous across launcher guidance,
Intent Preflight, Runtime recovery messages, and MCP operations:

```text
factory_restart = archive active run + start replacement run
factory_cancel  = archive active run only
```

A restart does not require a preceding `factory_cancel`. Before
`factory_restart`, the launcher resolves one complete replacement request and
runs replacement-run Intent Preflight against that exact request while leaving
the active run untouched. A plain operational restart command reuses the
persisted `.idd/factory/current/request.md`; a complete explicitly supplied
replacement request replaces it. An incomplete semantic delta is not merged
into the old request automatically.

`LEGACY_FACTORY_STATE` still does not support cross-version continuation or
implicit migration. Explicit restart uses `factory_restart`; explicit
cancellation without a replacement uses `factory_cancel`. Executor **Technical
Restart** remains a separate internal retry mechanism and is unchanged.

## 2026-09-13 — Task-related durable intent propagation

Factory planner tasks may now include optional `# TaskRelatedIntent` metadata
containing existing stable `IDD-NNNN` durable-intent IDs selected independently
for that task. The planner owns semantic relevance selection. Runtime owns
canonical-ID validation, unique direct-file resolution, persistence, retry and
recovery preservation, and executor-context assembly.

For every executor invocation Factory injects the complete current contents of
exactly the selected intent documents between the task contract and completed-
work context. The ordered selected IDs are immutable work-item metadata; intent
contents are not copied into `contract.md` or state and are resolved again from
current durable truth on every retry.

This changes authoritative work-item state. Factory runtime schema is now 13.
Active schema-12 state is treated as `LEGACY_FACTORY_STATE`; missing
`TaskRelatedIntentIds` on an older active task is not silently interpreted as an
empty authoritative selection. No implicit migration is provided. Restart an
older active run with `factory_restart`, or use `factory_cancel` if no
replacement run is wanted. Completed historical result directories remain
unchanged.

Task output without `# TaskRelatedIntent` remains valid and means an empty
selected-intent set. `# Question` and `# Done` semantics are unchanged.

## 2026-09-03 — Explicit planner completion marker

Planner completion is now explicit. When semantic reassessment finds no
remaining work and no missing user decision, the planner must return exactly
`# Done`. Blank or whitespace-only planner output is now
`MALFORMED_PLANNER_OUTPUT` and never starts strict final verification.

This is a controlled breaking change of the planner Markdown protocol:

```text
before: empty response = no remaining semantic work
after:  # Done = no remaining semantic work
        empty response = malformed planner output
```

`# Done` has no body and cannot be mixed with `# Task` or `# Question`. Runtime
mechanically maps validated `# Done` to the existing empty-batch representation,
then runs the existing strict final deterministic verification. It does not add
a semantic completion field to `state.json`, a new runtime command, a final
reviewer, or an extra LLM invocation.

Update the packaged runtime and planner skill together. Persisted blank planner
results are not treated as legacy completion evidence after the update; use
`factory_restart` for an affected run if it cannot resume under the new
protocol, or `factory_cancel` if no replacement run is wanted. No runtime-state
schema migration is required solely for this marker change.

## 2026-09-03 — Batch planning Factory Runtime

Factory now runs a single semantic loop: a planner emits an ordered Markdown
batch, the runtime executes and verifies every task in that batch, and the
planner is invoked again. An empty planner response after a successful batch
leads to strict final verification; a failed final verification returns its
evidence to the planner.

The only runtime semantic roles are `planner` and `executor`. Worker JSON
outcomes, capability routing, research and review roles, checkpoint/final
review stages, and the standalone replan skill have been removed. Executor
output is free-form Markdown evidence and cannot alter control flow.

This is intentionally incompatible with active runs from the previous runtime.
Restart such a run with `factory_restart`, or archive it with `factory_cancel`
when no replacement is wanted. Runtime state schema is 10, attempt metadata
schema is 3, Factory configuration schema is 2, and final result schema is 4.

## 2026-08-23 — Codex host compatibility and event-driven agent waiting

Codex Multi-Agent V2 now exposes `wait_agent` as an event-driven wait for
mailbox activity and allows long wait timeouts. Codex-specific IDD adapters must
not model this as a wait for one explicit child id and should prefer a single
long wait when a native subagent result is on the critical path instead of a
short-timeout polling loop that repeatedly wakes the parent model.

This does not change Factory orchestration. Factory semantic workers remain
fresh `codex exec` subprocesses owned by the deterministic .NET runtime, and the
parent Codex session continues to enter that runtime through the bundled
blocking MCP transport. Returning orchestration to model-driven native
subagents would weaken the deterministic runtime boundary and is not part of
this update.

Codex skills can also declare MCP dependencies in `agents/openai.yaml`. IDD may
use that metadata for representable external dependencies, but it does not
replace the bundled Factory `.mcp.json`: the packaged Factory stdio launch
requires command arguments and a plugin-relative working directory. Tool
metadata also does not grant permissions or require the model to call a tool.

OpenAI's process-tree cleanup fix from codex PR #37366 was released starting
with Codex 0.148.0. IDD release certification requires stable Codex 0.148.0 or
newer. Process-tree containment is a property of the Codex host release and is
covered by upstream Codex tests; maintainers do not create or supply a separate
lifecycle JSON report for each IDD release.

## 2026-08-13 — Codex Factory Bundled MCP Launcher

The Codex `idd-factory` plugin now exposes a bundled blocking MCP transport for
explicit Factory run, continue, restart, and cancel operations. The generated
Codex launcher no longer starts `idd-factory.dll` through a shell and has no
shell fallback. Existing CLI commands and Factory state remain compatible,
including continuation across CLI and MCP transports. Claude retains its
packaged CLI launcher.

Release certification requires a Codex host that proves process-tree cleanup
for normal interruption and hard termination. Codex 0.147.0 is not supported
for this launcher. The first stable upstream release containing the required
cleanup fix is 0.148.0, which is the minimum supported release-certification
host.

## 2026-08-11 — Programmatic Factory Runtime

Factory orchestration is now owned by the packaged .NET 10 runtime. Active runs
use authoritative `.idd/factory/current/state.json`, stable work-item filenames,
versioned worker results, and a pinned workflow hash. The former LLM step
coordinator and LLM finalizer are removed.

Legacy active runs containing `.ready.md`, `.active.md`, `.completed.md`, or
`.blocked.md` work items are not migrated. Finish such a run with the previous
Factory version, or after updating the plugin restart it with `factory_restart`;
use `factory_cancel` instead if no replacement run is wanted. Existing
`.idd/factory/results/` directories remain valid and are not changed.

## 2026-07-31 — Factory Task and Subtask terminology

Factory now reserves `Task` for the complete user-requested unit of work and
uses `Subtask` for each decomposed executable unit. Existing persisted Factory
state remains readable: `request.md`, `run-context.md`, work-item filenames,
and their content-based type detection are unchanged.

Update manual invocations and generated integrations using this migration map:

- `idd-factory-decompose-work` → `idd-factory-decompose-task`
- `idd-factory-execute-task` → `idd-factory-execute-subtask`
- `idd-factory-review-work-result` → `idd-factory-review-task`
- `idd-factory-finish-work` → `idd-factory-finalize-run`

`idd-factory-review-task` formerly meant checkpoint review and now means final
Task review. It has no runtime alias because routing the old name would be
ambiguous; use `idd-factory-review-checkpoint` for a Review checkpoint.

## 2026-07-23 — Intent document filename namespace

Intent document identifiers and filenames now use the `IDD-` prefix: `IDD-0001.spec-example.md` instead of `0001.spec-example.md`. The namespace makes document IDs unambiguous in prose, search results, links, logs, and automated repository scans.

This is a breaking change. IDD does not provide an automatic migration or compatibility with the old bare numeric format. Update an existing `.idd/intent/` directory by running the prompt below with a Coding Agent.

<details>
<summary>Prompt: update intent document numbering and internal links</summary>

```text
Update this repository's `.idd/intent/` document identifiers to the current IDD naming convention.

Required result:
- Rename every current intent document from `NNNN.type-short-title.md` to `IDD-NNNN.type-short-title.md`.
- Preserve each existing four-digit number, document type, slug, content, and product meaning.
- Update each renamed document heading so its identifier starts with the same `IDD-NNNN` value.
- Update `.idd/intent/INDEX.md` document identifiers from `NNNN` to `IDD-NNNN` while preserving the ID-only index representation. The `Document` column must contain only the stable `IDD-NNNN` identifier, not filenames, paths, or Markdown links.
- Rewrite normative relations and prose document references from bare `NNNN` identifiers to stable `IDD-NNNN` identifiers.
- When an existing Markdown link actually targets an intent file, update only its link target from the old filename to the corresponding `IDD-NNNN.type-short-title.md` filename. Do not introduce a new filename-based Markdown link where the source used a document identifier.
- Treat `IDD-NNNN` as the stable document ID. Do not renumber documents.
- Do not add aliases, redirect files, fallback parsing, migration code, or compatibility with the old naming convention.
- Do not modify unrelated numbers such as versions, dates, issue numbers, task sequence numbers, ports, or quantities.
- Verify that no current intent filename uses the old `NNNN.type-short-title.md` format, no normative internal relation uses a bare four-digit document number, and every `INDEX.md` Document entry is exactly an `IDD-NNNN` identifier.
- Run or simulate `idd-intent-lint` and fix all mechanical errors caused by the rename.

Report the renamed files, rewritten references, and verification result.
```

</details>
