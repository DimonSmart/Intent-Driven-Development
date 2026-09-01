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
`references/intent-preflight.md` completely before a new run or when handling a
runtime `INTENT_REQUIRED` result. Treat it as the canonical preflight contract.
Pass the complete user request to intent preparation and the platform launcher
unchanged. The launcher must use the runtime packaged with this installed plugin
instance and block until the runtime returns one structured Factory outcome.

## New-run intent preflight

Before calling the runtime for a new run:

1. Determine whether `.idd/factory/current/state.json` exists without reading or
   changing runtime-owned state. If it exists, follow existing-run rules instead
   of initial preflight.
2. Resolve explicit requested scope. Explicit prohibitions on intent writes or
   implementation take precedence over Factory invocation.
3. Read `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant
   current documents.
4. Classify the request as `Covered`, `ExplicitIntentChange`,
   `MissingIntentDecision`, or `ImplementationOnly`.
5. For an allowed `ExplicitIntentChange`, invoke `idd-intent-change` with the
   unchanged original request. Let it hand off to `idd-intent-new-document` when
   normal ownership rules require a new spec, ADR, or spike.
6. After any intent update, validate semantic coverage against the original
   request as defined by the required reference.
7. Start Factory only for permitted `end-to-end` or `implementation-only` scope
   when current intent covers the product semantics or the request is strictly
   `ImplementationOnly`.

Do not call the runtime, create `.idd/factory/current/`, or create Factory work
items while intent preparation is incomplete or blocked. Missing documentation
alone is not `INTENT_REQUIRED`.

## Run, continue, and cancel

- For a new run whose preflight is covered, start Factory with the exact user
  request and resolved absolute workspace.
- For an ordinary existing run, continue Factory without repeating initial
  preflight or inventing a user answer.
- When Factory returns `NEEDS_CLARIFICATION`, report the question, collect the
  user's answer, and continue with that answer unchanged.
- When Factory returns structured `INTENT_REQUIRED`, compare the missing durable
  decisions with the unchanged original request and current intent. If the
  request already resolves them and scope permits writes, use the existing
  intent workflow outside Factory, validate coverage, and continue the exact
  persisted operation. Otherwise report the genuinely missing decision and
  pause for user input.
- Cancellation is explicit. Warn that product changes are preserved; do not
  delete Factory state or revert code in the launcher.

## Boundaries

- Do not select work items, inspect operational state, route reviews, apply
  retries, create corrections, choose final review, or finalize files.
- Do not choose a next phase or maintain a second workflow model.
  `FactoryState.Completed`, `Current`, `Remaining`, and runtime-owned
  continuations are authoritative.
- Do not spawn semantic or coordinator agents. The packaged backend creates
  fresh semantic subprocess contexts through the runtime.
- Do not weaken the worker sandbox to compensate for launcher constraints.
- Do not mutate `.idd/factory/current/`. Durable intent may change only through
  the existing intent workflows during allowed preflight or intent-gate
  recovery; Factory workers and the runtime do not edit it.
- Do not interpret output from semantic workers. Only the runtime outcome is the
  public machine result.
- `FACTORY_CONFIGURATION_CHANGED`, `LEGACY_FACTORY_STATE`,
  `CORRUPT_FACTORY_STATE`, and lock outcomes are terminal for the current
  launcher attempt and must be reported exactly.

## Reporting

Report separately:

```text
Factory outcome: <outcome>
Reason: <reason when present>
Resume when: <condition when present>
Result directory: <path when present>
Intent preparation: unchanged | updated | blocked
Intent before/after hash: <hashes when available>
Intent paths changed: <paths when present>
```

When durable intent was updated from the original request before
implementation, say so explicitly. After reporting the final structured runtime
outcome, do not perform scheduler work outside the runtime.

## Codex launcher

Use the bundled direct `mcp__factory` tools:

- new run: `factory_run`
- continue without or with a clarification answer: `factory_continue`
- explicit cancellation: `factory_cancel`

Do not start `idd-factory.dll` through a shell. Do not use a
command execution, wait, write-stdin, or status-polling loop as
the Factory launcher. Do not use tool search for Factory tools
and do not enable Code Mode.

If the bundled Factory tools are unavailable, report that the
installed Codex host does not expose the bundled IDD Factory MCP
transport and that a supported Codex version is required. Do not
fall back to the shell launcher.
