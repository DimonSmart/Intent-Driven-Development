# Factory workflow

## Runtime boundary

The packaged .NET 10 runtime is a trusted orchestrator process. Codex reaches it
through the Factory plugin's bundled blocking MCP adapter; Claude and direct
operators retain the packaged CLI. Both transports start the same runtime from
the same installed plugin instance and use the same persisted state. MCP owns
transport only and never dispatches semantic workers or decides workflow
transitions.

Codex skill metadata can now declare MCP tool dependencies through
`agents/openai.yaml`. IDD does not use that mechanism as a replacement for the
bundled Factory transport: the packaged stdio server requires command arguments
and a plugin-relative working directory (`dotnet runtime/idd-factory.dll mcp`),
while the current skill dependency metadata is not a complete representation of
that launch contract. The generated plugin `.mcp.json` therefore remains the
authoritative Codex binding for the Factory runtime. Skill tool dependencies may
still be used for external MCP dependencies that can be represented by Codex
skill metadata; they are dependency metadata, not a permission boundary or a
requirement that the model invoke a particular tool.

The runtime needs an available native Codex CLI
(`IDD_FACTORY_CODEX_EXECUTABLE` can select it). Each semantic subprocess is
independently launched with `approval_policy=never`; implementers
receive `workspace-write`, while decomposition and review roles receive
`read-only` through a backend-neutral execution profile. The Codex adapter
exposes and explicitly activates the runtime-selected Factory skill. This keeps model-driven tool activity sandboxed without trapping
the child CLI network control plane inside another Windows sandbox.

On Windows the semantic backend removes `WindowsApps` entries from its child
PATH before launching sandboxed workers and retains `windows.sandbox =
"unelevated"`. It records only the sandbox selection and number of removed PATH
entries, never the full PATH.

For file-based Codex authentication, the backend creates an attempt-local
private `CODEX_HOME`, copies the credential cache, applies the configured
user-skill inheritance policy, and then exposes the runtime-selected Factory
skill from the exact plugin instance. A same-name project skill is rejected as
`FACTORY_SKILL_COLLISION`; a same-name user skill is not inherited. Release eval
uses a controlled profile with no user-global skill inheritance. The private
directory is removed after completion or cancellation, and credential material
is never part of immutable attempt evidence or final Factory results.

IDD Factory coordinates multi-stage implementation while current `.idd/intent/`
remains normative. Factory Runtime manages the workflow deterministically; LLM
agents perform bounded semantic work inside it.

```text
user / idd-factory-run
        |
        v
Codex bundled MCP or platform CLI
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

Codex native subagents are not the Factory orchestration transport. Semantic
workers are fresh `codex exec` subprocesses owned and awaited by the deterministic
runtime. If a Codex-specific workflow outside that runtime uses native subagents,
Multi-Agent V2 `wait_agent` should be treated as an event-driven wait for mailbox
activity: prefer one long wait allowed by the host when a child result is on the
critical path, rather than repeatedly waking the parent model with short timeout
polling. This capability does not remove the blocking MCP boundary between the
parent Codex session and Factory Runtime.

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

For every Subtask, the implementer performs semantic implementation and the
runtime then executes authoritative subtask verification. A checkpoint
verification gate must pass before its checkpoint reviewer is dispatched, and
the final verification gate must pass before the final reviewer is dispatched.
Reviewers consume runtime verification evidence but are not responsible for
running mandatory checks. A normal failed check is persisted as evidence and
may trigger the existing implementer skill in runtime-owned
`verification-fix` mode. The same gate is rerun within its deterministic budget.
Diagnostics run by workers never replace Runtime-owned authoritative evidence.

Every attempt records requested and effective execution configuration when the
backend can determine it, capability profile and skill source, and explicit
process termination metadata. `ForcedAfterResult` may preserve a valid semantic
result but remains an unclean attempt; release happy paths require `CleanExit`.

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

The MCP adapter returns that same structured outcome even when the CLI exit code
is non-zero for a legitimate Factory result. Interrupting an MCP request stops
the owned runtime process tree without synthesizing explicit Factory
cancellation; persisted state remains resumable. Release certification must
prove descendant cleanup for both normal interruption and hard host
termination. Codex 0.147.0 does not satisfy that lifecycle contract. OpenAI's
process-tree cleanup fix (codex PR #37366) was released starting with Codex
0.148.0, and Codex 0.149.0 is the next IDD certification target. Upstream
availability is not sufficient for an IDD release: the exact Codex host binary
and fingerprint used for certification must still pass the process-tree
lifecycle probe, including normal interruption, hard-kill descendant cleanup,
and resumable Factory state. Release certification remains fail-closed until
that report exists for the host being used.

See [Factory workflow configuration](factory-workflow-configuration.md) for the
supported YAML composition.
