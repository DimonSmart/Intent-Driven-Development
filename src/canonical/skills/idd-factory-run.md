# idd-factory-run

## Purpose

Launch or resume the packaged deterministic IDD Factory Runtime. The runtime,
not this skill or an LLM coordinator, owns workflow state and transitions.

## Runtime discovery

Resolve the installed plugin root as exactly two parent directories above this
`SKILL.md` (`skills/idd-factory-run/SKILL.md` → plugin root). The packaged runtime is
under `<plugin-root>/runtime/` and its entry assembly is
`idd-factory.dll`. Do not look for a project-local runtime and do not build the
runtime in the user's workspace.

V1 requires the .NET 10 runtime and an executable production agent backend
available to the runtime. `IDD_FACTORY_CODEX_EXECUTABLE` may name the exact
native executable for the bundled backend. On Windows, launch the trusted
runtime outside the parent agent OS sandbox;
the runtime applies a fresh role-appropriate sandbox to every semantic worker.
If the launcher cannot provide that boundary, return `BLOCKED` instead of
starting a nested CLI whose network control plane is trapped in the parent
sandbox.

## New run

1. Resolve the workspace and preserve the complete user request unchanged as
   UTF-8 standard input to the runtime. Do not create a launcher-owned request
   file.
2. Invoke:

   ```text
   dotnet <plugin-root>/runtime/idd-factory.dll run
     --workspace <workspace>
     --request-stdin true
     --plugin-root <plugin-root>
   ```

   Pipe the complete request to this process through standard input encoded as
   UTF-8. On Windows PowerShell, set the native-pipeline output encoding before
   piping the request:

   ```powershell
   $utf8 = [System.Text.UTF8Encoding]::new($false)
   $OutputEncoding = $utf8
   [Console]::OutputEncoding = $utf8
   ```

   Do not rely on the Windows PowerShell legacy native-pipeline encoding because
   it can replace non-ASCII request text before the runtime receives it.
3. Wait for the process to exit and parse its single structured JSON outcome.
4. Report the compact Factory outcome, reason/resume condition, and result
   directory when supplied.

## Continue and cancel

For an existing run invoke the same assembly with:

```text
continue --workspace <workspace> --plugin-root <plugin-root>
cancel --workspace <workspace> --plugin-root <plugin-root>
```

When the runtime returns `NEEDS_CLARIFICATION`, collect the user's answer in a
temporary file and resume with `continue --answer-file <file>`. When it returns
`INTENT_REQUIRED`, run the existing IDD intent workflow outside Factory; after
the durable intent changes, `continue` detects the new intent hash and resumes
decomposition or bounded replanning. Intent changes never become Subtasks.

Cancellation is explicit. Warn that product changes are preserved; do not
delete Factory state or revert code in the launcher.

## Boundaries

- Do not select work items, inspect status filenames, route checkpoints, apply
  retries, create corrections, choose final review, or finalize files.
- Do not spawn semantic or coordinator agents. The packaged backend creates
  fresh semantic subprocess contexts through the runtime.
- Do not weaken the worker sandbox to compensate for a sandboxed launcher.
- Do not mutate `.idd/factory/current/` or `.idd/intent/`.
- Do not interpret stdout from semantic workers. Only the runtime outcome is the
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
