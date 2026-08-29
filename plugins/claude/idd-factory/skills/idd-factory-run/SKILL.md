---
name: idd-factory-run
description: Run, resume, or cancel a deterministic IDD Factory Runtime execution.
---

# idd-factory-run

## Purpose

Launch or resume the packaged deterministic IDD Factory Runtime. The runtime,
not this skill or an LLM coordinator, owns workflow state and transitions.

Use this skill only when the user explicitly invokes the IDD Factory workflow.
Pass the complete user request to the platform launcher unchanged. The launcher
must use the runtime packaged with this installed plugin instance and block
until the runtime returns one structured Factory outcome.

## Run, continue, and cancel

- For a new run, start Factory with the exact user request and resolved absolute
  workspace.
- For an existing run, continue Factory without inventing a user answer.
- When Factory returns `NEEDS_CLARIFICATION`, report the question, collect the
  user's answer, and continue with that answer unchanged.
- When Factory returns `INTENT_REQUIRED`, use the existing intent workflow
  outside Factory. Continue Factory only after the durable intent work is
  complete.
- Cancellation is explicit. Warn that product changes are preserved; do not
  delete Factory state or revert code in the launcher.

## Boundaries

- Do not select work items, inspect operational state, route checkpoints, apply
  retries, create corrections, choose final review, or finalize files.
- Do not spawn semantic or coordinator agents. The packaged backend creates
  fresh semantic subprocess contexts through the runtime.
- Do not weaken the worker sandbox to compensate for launcher constraints.
- Do not mutate `.idd/factory/current/` or `.idd/intent/`.
- Do not interpret output from semantic workers. Only the runtime outcome is the
  public machine result.
- `WORKFLOW_CHANGED`, `LEGACY_FACTORY_STATE`, `CORRUPT_FACTORY_STATE`, and lock
  outcomes are terminal for the current launcher attempt and must be reported
  exactly.

## Reporting

Report separately:

```text
Factory outcome: <outcome>
Reason: <reason when present>
Resume when: <condition when present>
Result directory: <path when present>
```

After reporting the structured runtime outcome, do not perform more Factory
work in the same launcher attempt.

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
the same workspace and plugin root; when supplying an answer, write
it to a temporary UTF-8 file and pass `--answer-file`. Use `cancel`
for explicit cancellation. Always remove launcher-owned temporary
files. Do not search for a repository-local runtime.

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
