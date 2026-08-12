# Factory skills

## Public entry point

`idd-factory-run` is a small launcher. It locates the runtime packaged beside the
installed plugin and invokes `run`, `continue`, or `cancel`. It does not contain
the state-transition algorithm and does not dispatch agents itself.

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
