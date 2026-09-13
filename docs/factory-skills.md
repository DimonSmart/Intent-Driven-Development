# Factory skills

## Public entry point

`idd-factory-run` performs Intent Preflight and then invokes the packaged
deterministic runtime. The launcher does not schedule semantic work itself.

## Semantic workers

- `idd-factory-decompose-task` is the sole planner. It returns ordered `# Task`
  Markdown sections, each optionally terminated by `# TaskRelatedIntent`
  metadata containing existing stable durable-intent IDs selected semantically
  for that task; exactly one `# Question` when a user decision is required; or
  exactly `# Done` after semantic reassessment finds nothing remains. Blank or
  whitespace-only planner output is malformed.
- `idd-factory-execute-subtask` executes one immutable task and returns a
  free-form human-readable report. Factory supplies the concrete contract plus
  the complete current contents of exactly the persisted task-related intent
  documents before every executor invocation.

The planner discovers intent through `.idd/intent/README.md` and `INDEX.md`, then
opens only plausible current numbered documents as needed. It decides semantic
relevance per task. Runtime mechanically validates canonical IDs, requires each
to resolve uniquely to a direct `IDD-NNNN.*.md` child, persists the ordered ID
sequence as immutable work-item metadata, and reconstructs executor context from
those same references on retries and recovery.

`TaskRelatedIntent` is not another source of truth and its contents are not
copied into `contract.md` or Factory state. Current numbered intent documents
remain normative. Runtime does not infer relevance; workers do not spend
semantic reasoning on ID resolution, persistence, or context assembly.

There are no research, checkpoint-review, final-review, or standalone replan
skills in the Factory protocol. Research can be included in an ordinary task
contract when it is necessary to make that task coherent; discoveries that
change future work are evaluated by the next planner after batch exhaustion.

Workers never return semantic control JSON. They do not select capabilities,
Factory/work-item identity, retries, corrections, or transitions. The planner
may reference already-existing `IDD-NNNN` durable-intent IDs; that is semantic
input selection, not runtime identity selection. `# Done` is only the planner's
minimal explicit no-more-work marker and is mechanically followed by existing
strict final verification; it is not a Factory completion outcome. Runtime owns
materialization, ordering, verification, retry, recovery, persistence, and
finalization.

Each attempt keeps semantic text separate from machine metadata:

```text
invocation.json       runtime-owned invocation identity
planning-output.md    planner-created batch document
semantic-result.md    executor's task-specific report
result.json           runtime-owned provenance and semantic-result path
process-telemetry.json
```

Authoritative `state.json` separately stores each work item's ordered
`TaskRelatedIntentIds` so the selection survives Remaining -> Current -> retry ->
Completed without becoming part of the human-readable contract.
