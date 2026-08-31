# Factory skills

## Public entry point

`idd-factory-run` is a transport-neutral launcher contract. The generated Codex
skill uses the plugin's directly visible bundled `factory_run`,
`factory_continue`, and `factory_cancel` MCP tools. The generated Claude skill
uses the packaged CLI launcher. Neither form contains scheduling logic or
dispatches agents itself.

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

Workers return structured result envelopes through attempt-specific
`result.json`. Human-readable stdout and stderr are diagnostics only. Workers do
not own state mutations, authoritative verification, scheduling, retry, plan
replacement, or filesystem finalization.

The former `idd-factory-coordinate-step`, `factory-step-coordinator`, predefined
global Factory workflow, and LLM-driven finalization are not part of the current
plugin. `FactoryState.Completed`, `Current`, and `Remaining` are authoritative.
