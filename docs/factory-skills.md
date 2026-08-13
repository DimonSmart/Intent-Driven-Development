# Factory skills

## Public entry point

`idd-factory-run` is a transport-neutral launcher contract. The generated Codex
skill uses the plugin's directly visible bundled `factory_run`,
`factory_continue`, and `factory_cancel` MCP tools. The generated Claude skill
uses the packaged CLI launcher. Neither form contains the state-transition
algorithm or dispatches agents itself.

The Codex launcher makes one blocking MCP call and never falls back to launching
the runtime through a shell. If the installed Codex host does not expose the
bundled Factory tools, update to a supported host instead of using a polling
loop.

Example:

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

## Semantic worker skills

- `idd-factory-decompose-task` returns a versioned ordered decomposition.
- `idd-factory-execute-subtask` implements one self-contained active contract.
- `idd-factory-review-checkpoint` independently reviews selected completed work.
- `idd-factory-review-task` performs mandatory integrated final review.
- `idd-factory-replan` proposes bounded changes to remaining work.

Workers return structured result envelopes through attempt-specific
`result.json`. Human-readable stdout and stderr are diagnostics only. Workers do
not own state mutations, authoritative verification, routing, retry, or
filesystem finalization.

The former `idd-factory-coordinate-step`, `factory-step-coordinator`, and
LLM-driven finalization are not part of the current plugin.
