# Factory skills

## Public entry point

`idd-factory-run` is a transport-neutral launcher contract. The generated Codex
skill uses the plugin's directly visible bundled `factory_run`,
`factory_continue`, and `factory_cancel` MCP tools. The generated Claude skill
uses the packaged CLI launcher. Neither form contains scheduling logic or
dispatches agents itself.

For a new run, the launcher first performs the bounded Intent Preflight defined
by the packaged `intent-preflight.md` reference. It classifies the unchanged
request against relevant current intent, invokes the existing intent-change or
new-document workflow when an end-to-end request already contains a complete
product decision, validates coverage, and only then calls the runtime. This
pre-runtime stage does not create Factory work items or alter scheduler state.

`INTENT_REQUIRED` therefore denotes a genuinely missing durable decision.
Absence of a corresponding spec is not sufficient. Explicit
`implementation-only` scope still forbids intent writes.

The Codex launcher makes one blocking MCP call and never falls back to launching
the runtime through a shell. If the installed Codex host does not expose the
bundled Factory tools, update to a supported host instead of using a polling
loop.

Example:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

## Semantic worker skills

- `idd-factory-decompose-task` returns the smallest safe ordered list of remaining work and is reused after new evidence or a global strategy change.
- `idd-factory-execute-subtask` executes one focused workspace-writing implementation work item, including documentation changes when its contract requires them.
- `idd-factory-research` performs one focused read-only research work item whose findings become completed-work context.
- `idd-factory-review-checkpoint` independently reviews completed work at its ordered position.
- `idd-factory-review-task` performs mandatory integrated final semantic review as a terminal orchestration phase.
- `idd-factory-replan` is a legacy name for returning a complete replacement future list.

Workers return identity-free semantic JSON through an invocation-specific
backend channel. For the Codex backend, the captured body is retained as
diagnostic `raw-result.json`. It contains only `outcome` and fields defined for
that outcome; workers never return run, attempt, role, work-item, protocol,
schema, result-path, or execution-profile bookkeeping.

Runtime validates the raw body against the assigned capability and role, then
atomically creates authoritative `result.json`. That persisted artifact has its
own schema version, runtime-owned invocation provenance, the validated semantic
result, receipt time, and backend-observed termination kind. Human-readable
stdout and stderr remain diagnostics only. Workers do not own state mutations,
authoritative verification, scheduling, retry, plan replacement, persistence,
or filesystem finalization.

The former `idd-factory-coordinate-step`, `factory-step-coordinator`, predefined
global Factory workflow, and LLM-driven finalization are not part of the current
plugin. `FactoryState.Completed`, `Current`, and `Remaining` are authoritative.
