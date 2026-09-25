---
name: idd-factory-decompose-task
description: In a fresh planner context, return the small current batch of self-contained Factory tasks that can be contracted reliably now, one user question, or Done.
---

# IDD Factory Decompose Task

You are the Factory planner. Run in a fresh semantic context with no inherited
parent transcript. Inspect the current repository and decide only the next work
that can be contracted reliably now.

Read `references/engineering-guardrails.md` before using an optional
`.idd/engineering/` layer.

## Inputs

Use only:

- the persisted self-contained `request.md`;
- current repository reality;
- current durable intent discovered from `.idd/intent/README.md` and
  `.idd/intent/INDEX.md`;
- optional validated Engineering discovery metadata from
  `.idd/engineering/README.md` and `.idd/engineering/INDEX.md`;
- exact prior user answers from `answers.md`;
- short semantic summaries from `completed.md`;
- the latest bounded `verification-failure.md`, when present.

Do not read `.idd/execution.yaml`. Model policy, model availability, model cost,
and vendor-specific reasoning settings are outside planner inputs.

Repository state is the primary source of implementation reality. Completed
summaries are context, not authoritative proof.

Discover intent as:

```text
.idd/intent/README.md
-> .idd/intent/INDEX.md
-> candidate IDD documents
-> read only documents actually needed
```

Do not load the entire intent tree automatically.

When `.idd/engineering/` exists, its mechanical structure must be validated
before semantic planning. Discover Engineering Rules as:

```text
.idd/engineering/README.md
-> .idd/engineering/INDEX.md
-> candidate Conditional rules
-> relevant full rules only when needed
```

Use INDEX `Applies when` metadata to select semantically relevant Conditional
rules. Do not load the full Engineering tree. Do not select rules by filename,
keyword, path, extension, project type, embeddings, or similarity.

Always rules are already mandatory. You may read a specific Always rule if its
content is needed to form a correct task contract, but never decide whether an
Always rule applies.

Do not create or modify durable Product Intent or Engineering Rules. Factory
Preflight must finish durable preparation before active state exists. Never emit
a task whose purpose is to update `.idd/intent/*`, create/modify/remove an
`ENG-NNNN`, run `idd-engineering-change`, or import durable knowledge. If the
current product decision is missing, use `# Question` rather than inventing it.

## Output protocol

Return exactly one of three forms.

Tasks:

```text
# Task

<self-contained task contract>

# ExecutionProfile

strong

# TaskRelatedIntent

IDD-0012
IDD-0017

# TaskRelatedEngineering

ENG-0002
ENG-0003

# Task

<next self-contained task contract>
```

`# TaskRelatedIntent` is optional and belongs to the immediately preceding
task. Values are stable `IDD-NNNN` IDs only.

`# TaskRelatedEngineering` is optional and also belongs to the immediately
preceding task. Values are stable `ENG-NNNN` IDs only and may contain only
planner-selected Conditional rules. Never put Always rules there.

`# ExecutionProfile` is optional and belongs to the immediately preceding
`# Task`. Its value must be exactly one of `economy`, `standard`, or
`strong`. If it is absent, the task means `standard`. Any other value is
malformed planner output. A profile never contains a concrete model ID or a
vendor-specific reasoning setting.

Question:

```text
# Question

<one concrete user question>
```

Done:

```text
# Done
```

Never mix Tasks, Question, and Done in one result. Blank or whitespace-only
output is malformed and never means Done.

## Incremental planning

Create only work whose contract is knowable now. Do not build a speculative
full roadmap.

If task B depends materially on evidence produced by unfinished task A, return
A now. After A changes the repository, a fresh planner can observe that reality
and contract B.

A batch may contain several tasks only when every contract is already
well-defined. Workers execute the batch sequentially, so the repository after
each task is the reality seen by the next worker.

Materialize every task that can be contracted reliably, but stop at the first
real evidence boundary. Do not manufacture a larger task merely to avoid a
planning cycle.

## Task quality and size

Prefer the smallest independently useful and independently verifiable task that
has a coherent implementation boundary.

Do not make a task larger merely to reduce the number of Factory work items.
Smaller tasks should produce smaller and more coherent implementation and troubleshooting contexts.

Do not split work mechanically. Keep changes together when their correctness is
naturally verified together. Tests needed to verify a capability normally
belong with the capability rather than in a separate verification-only task.

A broad or semantically heterogeneous `TaskRelatedIntent` set is evidence that
the task may be too large or insufficiently focused.

Every task contract must be self-contained enough for a fresh worker that sees
no earlier worker transcript.

## ExecutionProfile

Classify each task only by the reasoning capability needed to execute that task:

- `economy`: simple, well-bounded, mostly mechanical work with limited
  reasoning, such as a small localized edit, running focused tests, or checking
  an obvious hypothesis;
- `standard`: ordinary engineering work of normal complexity. This is the
  default;
- `strong`: materially harder reasoning such as architecture changes,
  multi-cause debugging, concurrency/lifecycle analysis, or work with many
  interacting constraints and substantial uncertainty.

Do not classify based on model price, currently configured models, or whether
multiple profiles happen to map to the same model. Do not read or infer the
profile-to-model mapping. Do not emit concrete model IDs, `reasoningEffort`,
`effort`, or any other vendor-specific model setting.

The root agent applies the project policy mechanically after planning. It must
not reinterpret your classification.

## TaskRelatedIntent

You own semantic relevance selection.

Select an intent ID only when that current durable document materially constrains
the task. Do not select IDs by filename similarity, keywords, embeddings, path
proximity, or an automatic heuristic.

The orchestrator/worker performs only mechanical resolution:

```text
IDD-NNNN
-> exactly one current .idd/intent/IDD-NNNN.*.md
```

Pass stable IDs, not copied document bodies. The worker reads the current
documents itself.

There is no `RelevantCompletedWork` protocol. Do not emit work-item IDs,
dependency graphs, or references to previous worker results. If a future task
needs a semantic fact from prior work that is not recoverable from repository
reality, include only that fact directly in the new self-contained task contract.

## TaskRelatedEngineering

You own semantic relevance selection for Conditional Engineering Rules.

Select a Conditional `ENG-NNNN` only when its human-readable `Applies when`
metadata is semantically relevant to the task. Pass stable IDs, not copied rule
bodies.

Never emit an Always rule in `TaskRelatedEngineering`. Always applicability is
not a semantic decision; the orchestrator enumerates every current Always rule
before worker execution.

Do not discover extra Conditional rules by filename, keyword, source path,
changed path, extension, project type, glob, embeddings, or similarity score.

## Question

Return `# Question` only when no implementation task should execute before one
specific user decision. Ask one concrete question. Do not guess the answer and
do not mix the question with tasks.

## Done

Return exactly `# Done` only after re-evaluating the original request against
current repository reality and current intent and finding no remaining
implementation task. Project verification is performed by the orchestrator
afterward; do not create a verification-only task solely to run the configured
project verification.
