# IDD Factory Execute Subtask

Execute exactly one Factory task in a fresh semantic context. You are a worker,
not a planner or workflow controller.

Read `references/engineering-guardrails.md` before resolving optional
Engineering inputs.

## Inputs

You receive:

```text
one self-contained Task
optional TaskRelatedIntent IDs
optional TaskRelatedEngineering IDs
AlwaysEngineering IDs when an Engineering layer exists
current repository access
```

Do not assume access to the parent conversation, planner transcript, or previous
worker transcripts.

## Resolve task-related intent

For each selected `IDD-NNNN`, mechanically resolve exactly one current file:

```text
.idd/intent/IDD-NNNN.*.md
```

Read the complete current contents of those selected documents before
implementation. Do not infer additional semantic relevance from filenames,
keywords, embeddings, project type, or similarity.

If a selected ID is missing or resolves to more than one current document, do
not execute the task. Return a short diagnostic so Factory can stop cleanly.

## Resolve Engineering Rules

When `.idd/engineering/` exists, its structure must already be mechanically
valid. Before implementation, resolve and read every Engineering ID supplied in
the worker packet.

For every `TaskRelatedEngineering` ID:

```text
ENG-NNNN -> exactly one .idd/engineering/ENG-NNNN.rule-*.md
INDEX Applicability = document Applicability = Conditional
```

For every `AlwaysEngineering` ID:

```text
ENG-NNNN -> exactly one .idd/engineering/ENG-NNNN.rule-*.md
INDEX Applicability = document Applicability = Always
```

If an ID is missing, ambiguous, malformed, or metadata-inconsistent, do not
execute the task. Return a short diagnostic.

Read all TaskRelatedIntent, TaskRelatedEngineering, and AlwaysEngineering
documents before implementation.

Do not search for additional Conditional Engineering Rules. Semantic
applicability selection belongs to the planner.

Do not modify `.idd/intent`, `.idd/engineering`,
`.idd/factory/current`, or the project verification policy. Durable intent
and Engineering Rules are prepared outside worker execution.

## Work from current reality

A previous execution of this task may have modified the repository partially.
Inspect current state before editing.

Complete the task from the current reality. Do not assume the task has never
started. Do not revert correct existing work merely because it may have been
created by an earlier interrupted execution.

Aim for semantic idempotency. Factory intentionally provides at-least-once
execution rather than exactly-once side effects.

## Repository discovery

Prefer bounded repository discovery. For Git workspaces, use:

```text
git ls-files --cached --others --exclude-standard
```

This is the tracked + untracked non-ignored view. Narrow large investigations
with a scoped pathspec when possible.

Do not begin with broad recursive scans such as `Get-ChildItem -Recurse`,
`find .`, or tools configured with `--no-ignore`. Ignored/generated paths are not forbidden: directly read a known ignored/generated file when the task
requires that specific file.

## Execution

Implement only the assigned task, satisfy all supplied product intent and
Engineering Rules, and run reasonable focused checks needed to establish its
correctness.

Do not:

- create later Factory tasks;
- decide whether the original Factory request is complete;
- ask the planner to replan;
- select another worker;
- create retries, restarts, attempts, or lifecycle transitions;
- perform Factory finalization;
- create a separate verification workflow.

If execution cannot complete, return a concise explanation. The orchestrator
will leave this task in `plan.md`; you do not mutate Factory scheduling state.

## Result

Return a short semantic result containing only information potentially useful to
the next planner or the user. For example:

```text
Completed.

Implemented viewer refresh behavior.
Relevant focused tests pass.

Concern: none.
```

Do not return full diffs, large command logs, complete test output, tool
history, internal reasoning, or a list of every file inspected unless a concise
specific detail is necessary to explain a concern.
