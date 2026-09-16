# Factory skills

## Public entry point

`idd-factory-run` performs Intent Preflight and then invokes the packaged
deterministic runtime. The launcher does not schedule semantic work itself.

## Semantic workers

- `idd-factory-decompose-task` is the sole planner. It returns ordered `# Task`
  Markdown sections, each optionally followed by `# TaskRelatedIntent` metadata
  containing existing stable durable-intent IDs and then by
  `# RelevantCompletedWork` metadata containing already-Completed stable
  work-item IDs selected semantically for that task; exactly one `# Question`
  when a user decision is required; or exactly `# Done` after semantic
  reassessment finds nothing remains. Blank or whitespace-only planner output is
  malformed.
- `idd-factory-execute-subtask` executes one immutable task and returns a
  free-form human-readable report. Factory supplies the concrete contract, the
  complete current contents of exactly the persisted task-related intent
  documents, and only the bounded semantic results of completed work items that
  the planner explicitly selected for this task.

The planner discovers intent through `.idd/intent/README.md` and `INDEX.md`, then
opens only plausible current numbered documents as needed. It decides semantic
relevance per task both for durable intent and for previous completed semantic
results. Runtime mechanically validates canonical IDs, resolves persisted
references, preserves planner order, loads artifacts, and applies deterministic
presentation bounds. Runtime never infers completed-work relevance from paths,
filenames, keywords, strings, project types, embeddings, result size, or
execution order.

`TaskRelatedIntent` is not another source of truth and its contents are not
copied into `contract.md` or Factory state. Current numbered intent documents
remain normative. `RelevantCompletedWork` is likewise metadata rather than
contract text. Current repository state is the primary source of what previous
implementation produced; selected previous semantic results exist only for
information that is not necessarily recoverable from repository state.

A planner may reference only work items that were already in Completed at the
start of that planning cycle. If a later task depends semantically on a result
that a task in the current batch has not produced yet, that dependency is a
batch boundary: the next planning cycle can reference the prerequisite after it
is actually Completed. Runtime does not invent speculative IDs, sibling
inheritance, dependency graphs, or relevance heuristics.

There are no research, checkpoint-review, final-review, or standalone replan
skills in the Factory protocol. Research can be included in an ordinary task
contract when it is necessary to make that task coherent; discoveries that
change future work are evaluated by the next planner after batch exhaustion.

Workers never return semantic control JSON. They do not select capabilities,
Factory/work-item identity, retries, corrections, or transitions. The planner
may reference already-existing `IDD-NNNN` durable-intent IDs and already-
Completed `WNNNNNN` work-item IDs; that is semantic input selection, not runtime
identity selection. `# Done` is only the planner's minimal explicit no-more-work
marker and is mechanically followed by existing strict final verification; it
is not a Factory completion outcome. Runtime owns materialization, ordering,
verification, retry, recovery, persistence, and finalization.

Each attempt keeps semantic text separate from machine metadata:

```text
invocation.json       runtime-owned invocation identity
planning-output.md    planner-created batch document
semantic-result.md    executor's task-specific report
result.json           runtime-owned provenance and semantic-result path
process-telemetry.json
```

Authoritative `state.json` stores each work item's ordered
`TaskRelatedIntentIds` and `RelevantCompletedWorkIds` so both immutable
selections survive Remaining -> Current -> retry -> Completed without becoming
part of the human-readable contract. Planner completed-history context remains
available for semantic reassessment; executor completed-work context is built
separately and contains only the explicitly selected IDs and their bounded
semantic results.
