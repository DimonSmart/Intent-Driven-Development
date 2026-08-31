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

- `idd-factory-decompose-task` creates the smallest safe initial task graph and may leave future work as `outline` for later scoped refinement.
- `idd-factory-execute-subtask` executes one focused implementation or documentation work item.
- `idd-factory-research` performs one focused read-only research work item whose findings can unblock dependent graph work.
- `idd-factory-review-checkpoint` independently reviews selected completed graph work.
- `idd-factory-review-task` performs mandatory integrated final semantic review as graph work.
- `idd-factory-replan` proposes bounded changes to remaining global graph strategy without mutating authoritative state.

Workers return structured result envelopes through attempt-specific
`result.json`. Human-readable stdout and stderr are diagnostics only. Workers do
not own state mutations, authoritative verification, scheduling, retry, graph
mutation, or filesystem finalization.

The former `idd-factory-coordinate-step`, `factory-step-coordinator`, predefined
global Factory workflow, and LLM-driven finalization are not part of the current
plugin. `FactoryState.WorkItems` is the authoritative execution graph.
