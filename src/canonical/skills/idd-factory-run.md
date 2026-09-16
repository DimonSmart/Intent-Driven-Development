# idd-factory-run

## Purpose

Launch, continue, restart, or cancel an explicit IDD Factory workflow while keeping semantic intent preparation outside the deterministic runtime.

Use this skill when the user explicitly asks to run Factory, continue an existing Factory run, restart it, answer a Factory planner question, cancel it, or inspect its status after transport loss.

Read `references/intent-preflight.md` completely before any new-run or replacement-run intent preflight. Treat it as the canonical classification, intent-update, and coverage contract. This skill decides which logical Factory request is preflighted and which run-level Runtime operation is invoked; it does not duplicate the shared semantic rules.

## Runtime boundary

The launcher/skill owns semantic preparation:

```text
resolve logical Factory request
-> intent preflight
-> invoke the correct Factory operation
```

The packaged Runtime owns Factory state transitions and execution. In particular:

```text
factory_restart
    = archive existing run
    + start replacement run

factory_cancel
    = archive existing run
    + no replacement run
```

Do not implement restart as `factory_cancel` followed by `factory_run`. Do not call `factory_cancel` before `factory_restart`.

This run-level `factory_restart` is distinct from executor **Technical Restart**, which is an internal retry kind within one existing run. Do not change or reinterpret Technical Restart behavior here.

## MCP dependency

- Codex requires the bundled `idd-factory` MCP server configured by the plugin. If `factory_run`, `factory_restart`, `factory_continue`, `factory_retry`, `factory_cancel`, and `factory_status` are unavailable, stop and report that the Factory MCP dependency is unavailable. Do not replace it with shell invocation, generic subagents, or manual orchestration.
- Claude uses the packaged Factory launcher supplied by its adapter. Follow the same run-level semantics even when the transport surface differs.
- Factory runtime operations are blocking. Progress notifications, when supported by the host, are informational only and do not change the outcome contract.

## Materialize a new-run request

For a genuinely new run, the logical Factory request is the current user request together with any user-supplied pasted or attached source content that is part of that request.

Before preflight or `factory_run`, materialize that logical request losslessly into self-contained text:

- Resolve user-supplied pasted/attached text from the platform attachment surface when needed.
- Preserve the exact supplied text; do not summarize or semantically rewrite it.
- Do not pass host-local attachment paths, placeholder envelopes, upload IDs, or chat-memory references as the Factory request.
- Do not add launcher commentary, inferred requirements, or implementation decisions to the request.

The same materialized new-run request must be the request checked by intent preflight and the request passed to `factory_run`.

## New-run intent preflight

New-run preflight is used only when there is no active Factory run.

Before starting a new run:

1. Check whether `.idd/factory/current/state.json` exists.
2. If it exists, do **not** perform new-run preflight. Follow the existing-run rules below instead.
3. If it does not exist, materialize the current user request as the logical Factory request.
4. Apply the canonical Intent Preflight from `references/intent-preflight.md` in **new-run** mode to that request.
5. If preflight blocks, do not create Factory state.
6. If preflight succeeds, call `factory_run` with exactly the preflighted request.

## Resolve a replacement request

An explicit restart always requires one complete, self-contained replacement request before replacement-run preflight begins.

### Full replacement request supplied

If the user explicitly supplies a complete self-contained replacement request, materialize it losslessly and use it. The exact same request must be used for replacement-run preflight and `factory_restart`.

An explicit complete replacement request does not depend on the old `.idd/factory/current/request.md`. Missing or unreadable old request text therefore does not block restart in this case.

### Restart without changed product or implementation semantics

For an operational restart command such as `restart Factory`, `restart current run`, `restart on the new version`, or `restart with the current runtime`, when the user does not supply new product/implementation semantics, read:

```text
.idd/factory/current/request.md
```

and use its complete text as the replacement request.

Treat `request.md` as the persisted original logical request. Do not reconstruct the replacement request from model memory, chat history, planner output, work items, executor output, completed results, or obsolete semantic fields in legacy `state.json`. Operational restart wording does not become part of the replacement request unless it actually changes the requested product/implementation semantics.

If the persisted request is missing, unreadable, corrupted, or cannot satisfy the same lossless-text requirements as an ordinary Factory request, do not restart and do not archive the existing run. Ask the user for a complete replacement request.

### Partial semantic modification

If the user supplies only a semantic delta, for example `restart, but now the API must not change`, and the message is not itself a complete self-contained replacement request, do not semantic-merge it with the persisted request and do not reconstruct a new request. Ask the user for the complete replacement request instead.

## Replacement-run intent preflight

Replacement-run preflight is used only after the user explicitly chooses restart and the complete replacement request has been resolved.

The sequence is:

```text
explicit restart
-> resolve replacement request
-> replacement-run intent preflight
-> factory_restart(replacement request)
```

Apply the canonical Intent Preflight from `references/intent-preflight.md` in **replacement-run** mode to the resolved replacement request. It uses the same semantic classification, intent-update, and coverage-validation rules as new-run preflight.

The presence of `.idd/factory/current/state.json` is expected in replacement-run preflight. Do not route replacement-run preflight back to existing-run handling merely because `state.json` exists, and do not require a preliminary `factory_cancel`.

Until replacement-run preflight succeeds, leave the existing Factory run unchanged: do not call `factory_restart`, do not call `factory_cancel`, and do not archive or edit current Factory state.

The request passed to `factory_restart` must be byte-for-byte the same logical request that passed replacement-run preflight after required lossless materialization. Never preflight one request and restart with another.

## Existing supported run

When `.idd/factory/current/state.json` belongs to the supported current schema, distinguish continuation, restart, and cancellation:

- Ordinary continuation -> call `factory_continue`.
- Explicit restart -> resolve the replacement request, run replacement-run intent preflight, then call `factory_restart`.
- Explicit cancellation with no replacement run wanted -> call `factory_cancel`.
- Do not interpret an explicit restart as ordinary continuation.
- Do not restart automatically without an explicit user request.

When the Runtime reports `USER_DECISION_REQUIRED`, present the exact planner question and stop. On the user's next answer:

1. If the answer changes durable product truth, use the normal IDD intent workflow to update and validate intent first.
2. If the answer is only a local implementation choice, do not create durable intent for it.
3. After any required intent update succeeds, pass the user's exact answer to `factory_continue`.
4. If the user does not want to continue, `factory_cancel` is the explicit terminal operation and starts no replacement run.

Do not fabricate an answer to a planner question.

## `LEGACY_FACTORY_STATE`

An active run written by an unsupported older state schema cannot be continued by the current Runtime unless an explicit migration exists for that exact schema. Do not reinstall the old plugin automatically, deserialize the legacy semantic state as the current schema, edit it, or infer remaining work from it.

It is allowed to detect that a legacy run exists and to read `.idd/factory/current/request.md` as the persisted original user request. Do not use obsolete semantic fields in legacy `state.json` to restore the request, continue workflow state, determine remaining work, build a replacement request, or migrate state.

Handle explicit user choices as follows:

```text
user asks to continue
    -> continuation unavailable
    -> ask for restart or cancellation decision

user asks to restart
    -> resolve replacement request
    -> replacement-run intent preflight
    -> factory_restart

user asks to cancel
    -> factory_cancel
```

For restart: The current runtime owns the complete restart operation. It archives the existing run, preserving product changes and diagnostics, and then starts the replacement run with the supplied request. This is a sequential Runtime operation; do not promise transactional atomicity, rollback, or restoration of the old active run if replacement creation later fails.

For cancellation, `factory_cancel` archives the old run without starting a replacement.

## Blocking call and transport loss

For Codex MCP operations:

- Treat a structured Factory response as the authoritative outcome.
- If a blocking tool call is interrupted or times out at the host/tool layer, do not infer Factory failure and do not immediately invoke another mutating operation.
- Call `factory_status` once to determine whether the Runtime is still active or whether a durable outcome was persisted.
- If status says Runtime execution is still active, report that state rather than polling in a loop or starting another operation.
- If status returns a durable blocker, surface its `Reason` and `ResumeWhen` without rewriting the run state.

## Retry budget exhaustion

`factory_retry` is only for an explicit user choice to extend the semantic retry budget after `RETRY_BUDGET_EXHAUSTED`. It is not a run-level restart and does not replace `factory_restart`.

When offered by the Runtime, preserve its stated maximum and pass the requested additional attempt count unchanged.

## Cancellation

Cancellation is explicit and destructive to the active Factory slot while preserving product changes and diagnostic history. Use `factory_cancel` only when no immediate replacement run is wanted.

Do not delete `.idd/factory/current/` manually, revert product changes, or rewrite state in the launcher.

## Outcome reporting

Report the structured Runtime outcome directly:

- `COMPLETED`: summarize the result directory and completed work at a high level.
- `USER_DECISION_REQUIRED`: present the exact question and continuation requirement.
- `RETRY_BUDGET_EXHAUSTED`: present the retry option or explicit restart/cancel alternatives from Runtime guidance.
- `LEGACY_FACTORY_STATE`: explain that continuation is unavailable; an explicit restart uses `factory_restart`, while cancellation without replacement uses `factory_cancel`.
- Other blockers: report the persisted reason and recovery instruction without inventing a state transition.

Do not conflate run-level restart with executor Technical Restart in user-facing reporting.

## Boundaries

- Intent Preflight remains semantic launcher work; Runtime does not resolve replacement requests or mutate durable intent.
- Runtime remains the sole owner of Factory orchestration and restart state transitions.
- `factory_restart` is the public run-level replacement operation for both supported and legacy active runs.
- `factory_cancel` archives an active run without creating a replacement.
- Cross-version continuation is unavailable without an explicit migration for the exact source schema.
- No implicit legacy-state migration, semantic reconstruction, semantic merge, launcher-level `cancel + run`, transactional rollback, or changes to executor Technical Restart are introduced by this workflow.