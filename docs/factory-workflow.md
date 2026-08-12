# Factory workflow

## Runtime boundary

The packaged .NET 10 runtime is a trusted orchestrator process. It needs an
available native Codex CLI (`IDD_FACTORY_CODEX_EXECUTABLE` can select it) and,
on Windows, must not itself run inside a parent Codex OS sandbox. Each semantic
subprocess is independently launched with `approval_policy=never`; implementers
receive `workspace-write`, while decomposition and review roles receive
`read-only` through a backend-neutral execution profile. The Codex adapter
exposes and explicitly activates the runtime-selected Factory skill. This keeps model-driven tool activity sandboxed without trapping
the child CLI network control plane inside another Windows sandbox.

For file-based Codex authentication, the backend creates an attempt-local
private `CODEX_HOME`, copies only the credential cache, and removes that private
directory after completion or cancellation. Startup recovery deletes a stale
private directory left by an interrupted process. Credential material is never
part of immutable attempt evidence or final Factory results.

IDD Factory coordinates multi-stage implementation while current `.idd/intent/`
remains normative. Factory Runtime manages the workflow deterministically; LLM
agents perform bounded semantic work inside it.

```text
user / idd-factory-run
        |
        v
packaged .NET 10 Factory Runtime
        |
        +-- task-decomposer
        +-- implementer
        +-- checkpoint-reviewer
        +-- factory-replanner (only when needed)
        +-- final-reviewer
```

There is no semantic step coordinator on the happy path. The runtime chooses the
next item, validates dependencies and results, runs authoritative verification,
applies retry and correction budgets, routes reviews, persists state, recovers,
and finalizes the result.

Each invocation carries `Role`, `SkillName`, `ExecutionProfile`, and dynamic
input. Factory Core neither reads installed `SKILL.md` or role-prompt files nor
contains Codex skill/sandbox syntax. The selected Factory skill is the sole
semantic contract; backend activation is adapter-owned, and project/domain
skills remain available to workers.

## Execution model

The complete request is saved unchanged in `request.md`. The decomposer receives
it and returns ordered self-contained contracts. An implementer receives only
its active contract, optional shared run context, relevant intent, repository
evidence, and retry evidence. Every semantic invocation is a fresh subprocess
context.

Review checkpoints are selective. A checkpoint covers a contiguous group whose
early independent review protects later work. Final integrated review is always
required. A `needs-fix` result inserts a new corrective Subtask; completed work
is immutable. A semantic decomposition defect invokes the bounded replanner,
whose proposal is validated before runtime state changes.

Runtime verification precedes checkpoint and final review. A normal failed
check is persisted as evidence and may trigger the existing implementer skill in
`verification-fix` mode. The same gate is rerun, with the per-gate attempt count
bounded by workflow configuration. Diagnostics run by workers never replace
Runtime-owned authoritative evidence.

## State and recovery

The authoritative state is `.idd/factory/current/state.json`. Stable work-item
contract filenames never encode status. Each mutation increments `revision` and
uses compare-and-swap atomic persistence. Attempt identity and invocation data
are written before agent launch. `events.jsonl` is an audit stream, not replayed
state.

Legacy `.ready.md`, `.active.md`, `.completed.md`, and `.blocked.md` runs are not
migrated. Finish them with the prior Factory version or cancel and restart.

## Outcomes

The CLI emits one structured outcome and deterministic exit code. Successful
finalization creates a collision-safe directory under `.idd/factory/results/`,
validates `commit-message.md` and `factory-result.json`, preserves the execution
event log and verification evidence there, then clears `current/`. If result
creation fails, current state remains resumable.

See [Factory workflow configuration](factory-workflow-configuration.md) for the
supported YAML composition.
