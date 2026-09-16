# idd-factory-run

## Purpose

Prepare durable intent when an end-to-end request explicitly defines new product
truth, then launch, continue, restart, or cancel the packaged deterministic IDD
Factory Runtime. The runtime, not this skill or an LLM coordinator, owns
authoritative linear work state, scheduling, verification, retries, plan
replacement, restart state transitions, and finalization.

Use this skill when the user explicitly invokes IDD Factory, when an end-to-end
IDD route selects Factory for orchestrated implementation, or when the user
explicitly asks to continue, restart, or cancel an existing Factory run. Read
`references/intent-preflight.md` completely before any new-run or replacement-run
preflight. Treat it as the canonical requested-scope, classification, intent-
update, normalization, and coverage contract.

The launcher must use the runtime packaged with this installed plugin instance
and block until the runtime returns one structured Factory outcome.

## Run-level operation model

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

Do not implement restart as `factory_cancel` followed by `factory_run`. Do not
call `factory_cancel` before `factory_restart`.

This run-level `factory_restart` is distinct from executor **Technical Restart**,
which is an internal retry kind within one existing run. Do not change or
reinterpret Technical Restart behavior here.

## Runtime dependency and blocking transport

- Use the packaged Factory runtime exposed by the installed platform adapter. If
  `factory_run`, `factory_restart`, `factory_continue`, `factory_retry`,
  `factory_cancel`, and `factory_status` are unavailable, stop and report that
  the packaged Factory dependency is unavailable. Do not replace it with shell
  invocation, generic subagents, or manual orchestration.
- Factory runtime operations are blocking. Progress notifications, when
  supported by the host, are informational only and do not change the outcome
  contract.
- If a blocking tool call is interrupted or times out at the host/tool layer, do
  not infer Factory failure and do not immediately invoke another mutating
  operation. Call `factory_status` once to determine whether Runtime is still
  active or a durable outcome was persisted. If it is still active, report that
  state instead of polling or starting another operation.

## New-run intent preflight

For a genuinely new run, build one self-contained logical request from the
visible user request and the exact textual inputs explicitly supplied with it.
Preserve all user-authored content exactly. Mechanically materializing host-local
attachment references such as `pasted-text.txt` is transport normalization, not
a semantic rewrite.

Before calling Runtime for a new run:

1. Determine whether `.idd/factory/current/state.json` exists without reading or
   changing runtime-owned state.
2. If it exists, do **not** perform new-run preflight. Follow existing-run rules.
3. If it does not exist, materialize the complete current user request losslessly
   as the logical Factory request. Prefer the host file/attachment reader. When a
   host-local supplied-text file must be decoded directly, use strict UTF-8,
   reject invalid Unicode or U+FFFD, and do not expand arbitrary file paths that
   were not explicitly supplied as request input.
4. Apply `references/intent-preflight.md` in **new-run** mode to that request.
   The reference owns requested-scope, classification, intent-update, durable-
   normalization, and coverage rules; do not create a second copy of them here.
5. If preflight blocks, do not call Runtime, create
   `.idd/factory/current/`, or create Factory work items.
6. If preflight succeeds and scope permits implementation, invoke the packaged
   Runtime new-run operation with exactly the same self-contained logical request
   and resolved absolute workspace.

The persisted `request.md` must remain sufficient after any host-local pasted or
attachment file disappears. Missing documentation alone is not
`INTENT_REQUIRED`.

## Resolve a replacement request

An explicit restart always requires one complete, self-contained replacement
request before replacement-run preflight begins.

### Full replacement request supplied

If the user explicitly supplies a complete self-contained replacement request,
materialize it losslessly and use it. The exact same request must be used for
replacement-run preflight and `factory_restart`.

An explicitly supplied complete replacement request does not depend on the old
`.idd/factory/current/request.md`. Missing or unreadable old request text does
not block restart in this case.

### Restart without changed product or implementation semantics

For an operational command such as `restart Factory`, `restart current run`,
`restart on the new version`, or `restart with the current runtime`, when the
user does not supply new product/implementation semantics, read:

```text
.idd/factory/current/request.md
```

and use its complete text as the replacement request.

Treat `request.md` as the persisted original logical request. Do not reconstruct
the replacement request from model memory, chat history, planner output, work
items, executor output, completed results, or obsolete semantic fields in legacy
`state.json`. Operational restart wording does not become part of the
replacement request unless it actually changes the requested product or
implementation semantics.

If the persisted request is missing, unreadable, corrupted, or cannot satisfy
the same lossless-text requirements as an ordinary Factory request, do not
restart and do not archive the existing run. Ask the user for a complete
replacement request.

### Partial semantic modification

If the user supplies only a semantic delta, for example `restart, but now the API
must not change`, and the message is not itself a complete self-contained
replacement request, do not semantic-merge it with the persisted request and do
not reconstruct a new request. Ask the user for the complete replacement
request instead.

## Replacement-run intent preflight

Replacement-run preflight is used only after the user explicitly chooses restart
and the complete replacement request has been resolved.

```text
explicit restart
-> resolve replacement request
-> replacement-run intent preflight
-> factory_restart(replacement request)
```

Apply `references/intent-preflight.md` in **replacement-run** mode to the
resolved replacement request. It uses the same requested-scope, classification,
intent-update, durable-normalization, and coverage rules as new-run preflight.

The presence of `.idd/factory/current/state.json` is expected in replacement-run
preflight. Do not route replacement-run preflight back to existing-run handling
merely because `state.json` exists, and do not require a preliminary
`factory_cancel`.

Until replacement-run preflight succeeds, leave the existing Factory run
unchanged: do not call `factory_restart`, do not call `factory_cancel`, and do
not archive or edit current Factory state.

The request passed to `factory_restart` must be the same complete losslessly
materialized logical request that passed replacement-run preflight. Never
preflight one request and restart with another.

## Run, continue, user questions, restart, and cancel

- For a new run whose preflight succeeds and scope permits implementation,
  invoke the packaged Runtime new-run operation with the exact self-contained
  logical request.
- If supplied request text cannot be read losslessly, do not start or restart
  Factory. Report an input transport/encoding failure; do not summarize around
  the lost content or continue with replacement characters.
- For an ordinary existing supported run, continue Factory without repeating
  initial preflight, rematerializing host attachments, or inventing a user
  answer. The persisted self-contained request is authoritative.
- For an explicit restart of an existing supported run, resolve the replacement
  request, perform replacement-run preflight, then call `factory_restart`.
- For explicit cancellation when no replacement run is wanted, call
  `factory_cancel`. Cancellation preserves product changes and diagnostics and
  starts no replacement run.
- When `RETRY_BUDGET_EXHAUSTED` blocks the current work item and the user
  explicitly asks for more attempts, invoke `factory_retry` with the requested
  additional attempt count. It resumes only that current immutable work item,
  preserves prior attempts and verification evidence, and rejects totals above
  ten attempts. Do not call it for another blocker or infer the user's consent.
- `USER_DECISION_REQUIRED` is a resumable planning-boundary pause. Report the
  planner's question to the user exactly enough to preserve its semantic choice;
  do not answer it yourself and do not create implementation work around the
  missing decision.
- When the user answers a pending planner question, evaluate that answer together
  with the persisted question and current relevant intent. If the answer defines
  or changes durable product truth and scope permits intent writes, use the
  existing `idd-intent-change` workflow and validate coverage before resuming.
  If the answer defines or changes durable product truth but the requested scope
  forbids intent writes, do not pass the answer to Factory as an implementation
  choice; leave the run paused and report that continuing requires an allowed
  intent update or explicit cancellation. If the answer is only an
  implementation choice, do not write durable intent. After any required intent
  work is complete, pass the user's exact answer to `factory_continue` so the
  same run records it and the next planner can use it. Do not rewrite
  `request.md`.
- If the user chooses not to continue, cancel the Factory run instead of
  fabricating an answer.
- Cancellation is explicit. Warn that product changes are preserved; do not
  delete Factory state or revert code in the launcher.

## `LEGACY_FACTORY_STATE`

If an active run uses an older unsupported state schema, do not reinstall the
older plugin automatically and do not edit its state. Cross-version continuation
is unavailable unless the current Runtime provides an explicit migration for
that exact source schema.

It is allowed to determine that a legacy run exists and to read
`.idd/factory/current/request.md` as the persisted original user request. Do not
use obsolete semantic fields in legacy `state.json` to restore the request,
continue workflow state, determine remaining work, build a replacement request,
or migrate state.

Handle the user's explicit choice as follows:

```text
continue
    -> continuation unavailable
    -> ask for restart or cancellation decision

restart
    -> resolve replacement request
    -> replacement-run intent preflight
    -> factory_restart

cancel
    -> factory_cancel
```

For restart: The current runtime owns the complete restart operation. It archives
the complete existing run, preserving product changes and diagnostics, and then
starts the replacement run with the supplied request. This is a sequential
Runtime operation; do not promise transactional atomicity, rollback, or
restoration of the old active run if replacement creation later fails.

For cancellation, `factory_cancel` archives the legacy run without starting a
replacement. Do not call it as a prerequisite for `factory_restart`.

## Boundaries

- Do not select work items, inspect operational state, apply retries, create
  corrections, choose future work, or finalize files.
- Do not choose a next phase or maintain a second workflow model.
  `FactoryState.Completed`, `Current`, `Remaining`, and runtime-owned
  continuations are authoritative.
- Do not spawn semantic or coordinator agents. The packaged backend creates
  fresh semantic subprocess contexts through the runtime.
- Do not weaken the worker sandbox to compensate for launcher constraints.
- Do not mutate `.idd/factory/current/`. Durable intent may change only through
  the existing intent workflows during allowed preflight or user-question
  recovery; Factory workers and the runtime do not edit it. Reading persisted
  `request.md` to resolve an explicit restart does not mutate Factory state.
- Do not interpret executor output for workflow control. A structured
  `USER_DECISION_REQUIRED` result comes from runtime parsing of the planner's
  bounded planning output, not from executor reports.
- `FACTORY_CONFIGURATION_CHANGED`, `CORRUPT_FACTORY_STATE`,
  `UNMATERIALIZED_REQUEST_INPUT`, and lock outcomes are terminal for the current
  launcher attempt and must be reported exactly.
- `LEGACY_FACTORY_STATE` is terminal for continuation. Explicit restart uses
  `factory_restart`; explicit cancellation without replacement uses
  `factory_cancel`.
- Intent Preflight remains semantic launcher work. Runtime does not resolve
  replacement requests, classify intent, or mutate durable intent.
- Do not add implicit legacy-state migration, semantic reconstruction, semantic
  merge, launcher-level `cancel + run`, transactional restart rollback, or
  changes to executor Technical Restart.

## Reporting

For a structured result returned by a new-run, restart, continuation, retry, or
cancellation operation, report separately:

```text
Factory outcome: <outcome>
Reason: <reason when present>
Resume when: <condition when present>
Result directory: <path when present>
Intent preparation: unchanged | updated | blocked
Intent before/after hash: <hashes when available>
Intent paths changed: <paths when present>
```

For `USER_DECISION_REQUIRED`, present the question and stop until the user
answers or cancels. Do not report it as a terminal Factory failure.

For `LEGACY_FACTORY_STATE`, explain that continuation is unavailable. If the
user wants a replacement run, the route is replacement-request resolution,
replacement-run preflight, then `factory_restart`. If no replacement run is
wanted, the route is `factory_cancel`. Do not present both operations as a
required sequence.

For `VERIFICATION_INFRASTRUCTURE_FAILURE` and
`BASELINE_VERIFICATION_INFRASTRUCTURE_FAILURE`, trust the runtime's `Reason`,
minimal primary-failure payload, and `ResumeWhen`. Do not infer a different
cause, reclassify the failure, or derive workflow state from stdout or stderr.
Report the runtime classification directly:

```text
Verification infrastructure failure
Check: <checkId>
Cause: <failureKind> at <failureStage>
Evidence: <evidencePath, when present>
Reason: <runtime Reason>
Resume when: <runtime ResumeWhen>
```

The payload is only a reference to the primary failure. Do not expect or
reconstruct `checks`, stream tails or paths, timing, termination details,
metadata truncation state, or secondary diagnostic entries from transport.
When detailed diagnosis is needed, inspect the referenced verification evidence
instead. Treat that persisted evidence as authoritative for stdout/stderr,
full-log references, timing, exception details, termination outcome, and
secondary diagnostic issues. The launcher/LLM must not analyze stdout or stderr
to choose Factory workflow; runtime classification and continuation semantics
remain authoritative. Omit `Evidence` when `evidencePath` is null. Preserve the
runtime-provided `Reason` and `ResumeWhen` without inventing a different recovery
route.

When a read-only runtime status operation is used after a lost or timed-out
blocking response, its `status` is launcher/runtime ownership state, not a
Factory outcome. Report it as `Factory status: <status>`. In particular,
`ACTIVE` must never be reported as `Factory outcome: ACTIVE`: it means the run
has not finished and no final Factory outcome is available yet. For `ACTIVE`,
include the current work item, attempt, phase, completed/remaining counts,
runtime operation, and start time when the status payload provides them, then
report the returned reason and resume condition. Do not imply that the current
semantic attempt has completed merely because the workspace remains owned.

When durable intent was updated from the logical request or from a user answer
before resuming implementation, say so explicitly. After reporting the final
structured runtime outcome, do not perform scheduler work outside the runtime.
