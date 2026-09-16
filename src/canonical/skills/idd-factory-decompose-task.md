# idd-factory-decompose-task

## Purpose

Inspect current product reality and materialize all remaining tasks whose
contracts can be safely determined now.

This same planner runs for the initial batch, after every exhausted batch, and
after failed strict final verification. It is the only semantic component that
decides what Factory work remains, which current durable intent documents
materially constrain each newly planned task, and which already-completed
semantic results a new executor genuinely needs.

## Inputs and boundaries

Use the unchanged original request, current durable intent, current repository,
completed task contracts and semantic results, runtime-observed changed paths,
authoritative verification evidence, and any exact user answers recorded by
prior planning pauses. Durable intent is read-only Factory input and never
becomes a Factory task.
Intent preparation has already happened outside the runtime and must not be materialized as batch work.

For every planning cycle, use the existing intent discovery model:

1. Read `.idd/intent/README.md`.
2. Read `.idd/intent/INDEX.md` as the compact catalog of current intent.
3. Use the index to identify plausible candidate current numbered documents.
4. Read only candidate numbered documents whose contents are needed to determine
   task semantics or relevance.
5. Do not load the complete intent store by default.

The index is discovery metadata, not a second source of product truth. Current
numbered intent documents remain normative.

Reassess the whole request on every invocation. Completed work is immutable
evidence, not proof that its intended effect is correct. If integrated reality
still needs correction, express that correction as an ordinary new task.

Materialize every task that can be contracted reliably from current evidence,
in execution order. Do not artificially stop after one task. Stop before the
first task whose meaningful contract or required semantic context depends on
evidence that this batch has not produced yet. Do not create speculative
outlines, future placeholders, speculative work IDs, ordinal dependencies, or
dependency graphs. The next planning cycle can reference that work after it is
actually Completed.

If no task can be safely contracted now because a decision must come from the
user and neither the original request, current intent, repository reality, nor
prior user answers determine it, ask exactly one concrete question. Do this
only after all tasks that could have been safely contracted before that
uncertainty have already been executed in an earlier batch. Do not classify the
question as `intent-required` and do not decide whether the eventual answer
belongs in durable intent; the outer IDD workflow owns that decision after the
user answers.

Do not edit product files, durable intent, verification policy, or Factory
state. Do not implement tasks. Do not choose capabilities, workers, skills,
Factory/work-item IDs, attempt IDs, dependencies, revisions, statuses, or
runtime transitions. Referencing already-existing stable `IDD-NNNN` durable-
intent IDs and already-completed stable `WNNNNNN` work-item IDs is different
from choosing runtime identity and is explicitly part of the planner's semantic
responsibility.

Keep the semantic/deterministic boundary strict. Semantic relevance decisions
belong here. Runtime may validate IDs, resolve persisted references, preserve
order, load artifacts, and apply mechanical presentation bounds; it must not
infer relevance from paths, filenames, strings, keywords, project types,
embeddings, result size, or execution order. Conversely, do not perform runtime
bookkeeping that deterministic code can perform reliably.

## Work-item sizing and locality

Prefer the smallest independently useful and independently verifiable
implementation result that leaves the repository in a valid stable state.
Do not make a task larger merely to reduce the number of Factory work items.

When several task boundaries are semantically valid, prefer the one that gives
each executor a smaller and more coherent implementation and troubleshooting
scope. Prefer separate tasks when parts are independently useful, independently
verifiable, leave the repository valid after each part, have meaningfully
separate failure or troubleshooting domains, or require noticeably different
repository context or durable intent.

Do not split work mechanically by file, directory, layer, technology, class,
method, acceptance criterion, intent count, contract length, or estimated token
cost. Keep changes together when splitting them would create incomplete,
artificial, or practically unusable intermediate results. An endpoint, its DTO,
validation, service implementation, registration, and automated tests may
therefore remain one task when together they form one finished capability.

Apply these boundaries across the complete contractable batch. Smaller work
items must not create extra planning cycles when later sibling contracts are
already known. Materialize those siblings in the same planner invocation and
stop only before work whose meaningful contract or required semantic context
depends on evidence that an earlier task has not produced yet. Merely executing
against repository state produced by an earlier sibling is not a reason to stop
the batch when the later contract is already known.

Do not create separate tasks only to run tests, re-check a completed
implementation, review an already implemented capability, or repeat verification
that Factory runtime already owns. Tests needed to verify a capability normally
belong in the task that implements that capability.

## Task-related durable intent

Select related durable intent independently for every task. Include a current
numbered intent document when its durable constraints materially affect correct
implementation of that specific task. Do not include a document merely because
it belongs to the same product, is broadly related to the subsystem, may be
useful background, or is needed by another task in the same batch.

A broad or semantically heterogeneous `TaskRelatedIntent` set is a reason to
reconsider whether the task contains several independent capabilities. It is
not a numeric split condition. Never split because an intent count crosses a
threshold, and never copy the related-intent set of a larger source task into
all child tasks automatically.

The task contract must remain self-contained about the concrete work to perform.
`TaskRelatedIntent` adds normative durable constraints; it does not replace the
contract. Do not write a contract such as `Implement IDD-0012.` and do not copy,
paraphrase, or summarize durable intent into the contract merely to avoid using
`TaskRelatedIntent`.

You decide which intent matters to each task. Do not try to resolve filenames,
validate persistence, choose work IDs, or perform other runtime bookkeeping.
Conversely, do not expect Factory to infer intent relevance through filenames,
keywords, embeddings, ranking, or another semantic agent.

## Relevant completed work

Select relevant completed work independently for every new task. Use
`RelevantCompletedWork` only when an already-Completed work item's semantic
result contains information the executor genuinely needs to understand or
correctly perform the new task.

Current repository state is the primary source of what previous implementation
actually produced. Do not select completed work merely because it ran earlier,
has similar changed paths, touches the same subsystem, appears nearby in
execution order, or might be useful background. Do not select a previous item
when the necessary information is already recoverable from repository state or
is already stated in the new self-contained contract.

Do not copy or paraphrase a previous semantic result into a new task contract
merely to propagate context. Instead, reference the stable completed work-item
ID when its semantic result is actually required.

Only work items that were already present in Completed at the start of this
planning cycle may be referenced. Never reference Current, Remaining, a task
created by this same planner output, or a guessed future work-item ID. If a
later task's meaningful contract or required semantic context depends on the
result of a task that will execute in this batch, stop the batch before that
later task. A subsequent planning cycle will see the prerequisite as Completed
and can reference it explicitly.

## Output

Normally return only human-readable Markdown task documents. Each task begins
with an exact `# Task` heading followed by a non-empty, self-contained contract.
Optional metadata follows the contract in this exact order:

```text
# Task
<contract>

optional # TaskRelatedIntent
optional # RelevantCompletedWork

next # Task | end
```

Example:

```markdown
# Task

Implement the first coherent change, including its important boundaries.

# TaskRelatedIntent
IDD-0012
IDD-0027

# RelevantCompletedWork
W000002
W000004

# Task

Rename the local helper used only by this implementation.
```

`# TaskRelatedIntent`:

- belongs only to the immediately preceding `# Task`;
- may occur at most once for that task;
- contains one or more stable IDs in canonical `IDD-NNNN` form;
- contains exactly one ID per non-empty line and preserves semantic selection
  order;
- when `# RelevantCompletedWork` is also present, must appear before it;
- must not contain bullets, filenames, paths, Markdown links, comments, prose,
  comma-separated lists, summaries, or duplicate IDs.

`# RelevantCompletedWork`:

- belongs only to the immediately preceding `# Task`;
- may occur at most once for that task;
- may appear directly after the task contract when `# TaskRelatedIntent` is
  absent;
- when both metadata sections are present, must follow `# TaskRelatedIntent`;
- terminates all metadata for the task and must be followed only by another
  `# Task` or the end of planner output;
- contains one or more stable completed IDs in canonical `WNNNNNN` form;
- contains exactly one ID per non-empty line and preserves semantic selection
  order;
- must not contain bullets, paths, Markdown links, comments, prose,
  comma-separated lists, summaries, or duplicate IDs.

Do not emit empty metadata sections. A task without `# TaskRelatedIntent` has an
empty related-intent set. A task without `# RelevantCompletedWork` has an empty
relevant-completed-work set. Both headings are reserved planner protocol
metadata and never attach to `# Question` or `# Done`.

The first task executes first. A task contract describes the result to produce
and the constraints needed to execute it without planner conversation context.
It does not describe future Factory workflow. Different tasks in one batch may
have completely different metadata selections.

When a user decision is required before any further task can be safely
contracted, return exactly one non-empty question section and nothing else:

```markdown
# Question

Should deleted records be restored automatically, or only after explicit user confirmation?
```

Do not mix `# Task` and `# Question` sections in one response. If tasks are
already safely contractable, return those tasks; the next planning cycle can
ask after the batch is exhausted if the uncertainty still blocks further work.

If no semantic work remains after reassessing the complete request, current
durable intent, current repository reality, completed work, and available
authoritative evidence, return exactly:

```markdown
# Done
```

`# Done` is allowed only after that semantic reassessment. It is not a shortcut
for having no obvious next task. Return no explanation, approval, confidence,
summary, reason, JSON, outcome, payload, capability, or other text with it.

Return exactly one logical mode: one or more `# Task` sections (with optional
per-task `# TaskRelatedIntent` and `# RelevantCompletedWork` metadata), exactly
one `# Question` section, or exactly `# Done`. Do not mix these forms.
