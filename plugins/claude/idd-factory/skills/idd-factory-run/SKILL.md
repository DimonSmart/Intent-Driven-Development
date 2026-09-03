---
name: idd-factory-run
description: Prepare explicit durable product changes when required, then run, resume, or cancel a deterministic IDD Factory Runtime execution.
---

# idd-factory-run

## Purpose

Prepare durable intent when an end-to-end request explicitly defines new product
truth, then launch or resume the packaged deterministic IDD Factory Runtime.
The runtime, not this skill or an LLM coordinator, owns authoritative linear
work state, scheduling, verification, retries, plan replacement, and
finalization.

Use this skill when the user explicitly invokes IDD Factory or when an
end-to-end IDD route selects Factory for orchestrated implementation. Read
`references/intent-preflight.md` completely before a new run. Treat it as the
canonical preflight contract.
For a new run, build one self-contained logical request from the visible user
request and the exact textual inputs explicitly supplied with it. Preserve all
user-authored content exactly; mechanically materializing host-local attachment
references such as `pasted-text.txt` is transport normalization, not a semantic rewrite. Use that same
logical request for intent preparation and the new-run invocation.

The launcher must use the runtime packaged with this installed plugin instance
and block until the runtime returns one structured Factory outcome.

## New-run intent preflight

Before calling the runtime for a new run:

1. Determine whether `.idd/factory/current/state.json` exists without reading or
   changing runtime-owned state. If it exists, follow existing-run rules instead
   of initial preflight.
2. Resolve explicit requested scope. Explicit prohibitions on intent writes or
   implementation take precedence over Factory invocation.
3. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, only relevant current
   documents, and any pasted or attached text explicitly supplied as part of
   the user request. If the host exposes supplied text only through a local
   request reference, resolve it before classification and materialize its exact
   content into the logical request. The path or attachment marker itself is
   transport metadata, not request semantics.
4. Materialize supplied text losslessly. Prefer the host file/attachment reader.
   If a host-local supplied-text file must be read directly, decode it as strict
   UTF-8 rather than using shell-default or locale-dependent text decoding.
   Reject invalid Unicode or U+FFFD replacement characters. Do not call Factory
   with an unresolved host-local attachment envelope and do not expand arbitrary
   file paths that were not explicitly supplied as request input.
5. Compare every explicit durable claim in the materialized logical request with
   relevant current intent, then classify it as `Covered`,
   `ExplicitIntentChange`, `MissingIntentDecision`, or `ImplementationOnly`.
   A clear request-side contradiction that supersedes current durable behavior
   takes precedence over `Covered` and `ImplementationOnly`.
6. For an allowed `ExplicitIntentChange`, invoke `idd-intent-change` with the
   materialized logical request. Let it hand off to `idd-intent-new-document`
   when normal ownership rules require a new spec, ADR, or spike.
7. After any intent update, validate semantic coverage against that same logical
   request as defined by the required reference.
8. Start Factory only for permitted `end-to-end` or `implementation-only` scope
   when current intent covers the product semantics or the request is strictly
   `ImplementationOnly`.

Do not call the runtime, create `.idd/factory/current/`, or create Factory work
items while intent preparation or request materialization is incomplete or
blocked. Missing documentation alone is not `INTENT_REQUIRED`.

## Run, continue, user questions, and cancel

- For a new run whose preflight is covered, invoke the packaged runtime's new-run
  operation with the exact self-contained materialized logical request and
  resolved absolute workspace. The persisted `request.md` must remain sufficient
  after any host-local pasted or attachment file disappears.
- If supplied request text cannot be read losslessly, do not start Factory.
  Report an input transport/encoding failure; do not summarize around the lost
  content or continue with replacement characters.
- For an ordinary existing run, continue Factory without repeating initial
  preflight, rematerializing host attachments, or inventing a user answer. The
  persisted self-contained request is authoritative.
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
  fabricating an answer. Cancellation preserves product changes and diagnostics.
- Cancellation is explicit. Warn that product changes are preserved; do not
  delete Factory state or revert code in the launcher.

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
  recovery; Factory workers and the runtime do not edit it.
- Do not interpret executor output for workflow control. A structured
  `USER_DECISION_REQUIRED` result comes from runtime parsing of the planner's
  bounded planning output, not from executor reports.
- `FACTORY_CONFIGURATION_CHANGED`, `LEGACY_FACTORY_STATE`,
  `CORRUPT_FACTORY_STATE`, `UNMATERIALIZED_REQUEST_INPUT`, and lock outcomes are
  terminal for the current launcher attempt and must be reported exactly.

## Reporting

For a structured result returned by a new-run or continuation operation, report
separately:

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

## Claude launcher

Resolve the installed plugin root as two parent directories above
this `SKILL.md`. Invoke the packaged runtime with the platform shell:

```text
dotnet <plugin-root>/runtime/idd-factory.dll run
  --workspace <absolute-workspace>
  --request-stdin true
  --plugin-root <plugin-root>
```

Pipe the exact request as UTF-8 standard input, wait for process
exit, and parse the single structured outcome. Use `continue` with
the same workspace and plugin root, and `cancel` for explicit
cancellation. Always remove launcher-owned temporary files. Do not
search for a repository-local runtime.

On Windows, launch this trusted packaged Factory Runtime outside
the parent agent OS sandbox. The runtime applies a fresh,
role-appropriate sandbox to every semantic worker. If that launcher
boundary cannot be provided, report `BLOCKED` instead of starting
the nested runtime inside the parent sandbox. Do not weaken semantic
worker sandboxing.

In Windows PowerShell, configure the native pipeline for BOM-less
UTF-8 before piping the exact request to `dotnet`:

```powershell
$utf8 = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = $utf8
[Console]::OutputEncoding = $utf8
```
