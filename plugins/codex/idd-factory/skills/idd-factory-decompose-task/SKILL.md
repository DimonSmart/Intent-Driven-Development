---
name: idd-factory-decompose-task
description: Return all currently contractable remaining Factory work in execution order, deferring only work that needs evidence not yet available.
---

# idd-factory-decompose-task

## Purpose

Inspect current product reality and materialize all remaining tasks whose
contracts can be safely determined now.

This same planner runs for the initial batch, after every exhausted batch, and
after failed strict final verification. It is the only semantic component that
decides what Factory work remains and which current durable intent documents
materially constrain each newly planned task.

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
first task whose meaningful contract depends on evidence that this batch has
not produced yet. Do not create speculative outlines or future placeholders.

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
intent IDs is different from choosing runtime identity and is explicitly part
of the planner's semantic responsibility.

## Work-item sizing and failure domains

Each task should normally describe one coherent implementation result with a
bounded failure domain that a single executor can reasonably implement and
verify within one semantic attempt. Prefer a useful independently verifiable
outcome over a contract that bundles several otherwise independent results.

Prefer separate tasks when parts of the work have independently meaningful
outcomes and substantially independent verification paths, failure or
troubleshooting domains, toolchains, infrastructure prerequisites, or
implementation lifecycles. A particularly strong signal for a task boundary is
the combination of an independently useful result, independent verification,
and an independent failure or troubleshooting domain. No single signal requires
a mechanical split; make the boundary decision semantically from the whole
change.

Use stable intermediate repository states as natural task boundaries. A
completed task becomes repository reality for later executors, so do not keep
independently verifiable work together merely because every contract is already
known. Apply this sizing rule across the complete contractable batch: do not
return only one small task merely to force replanning when later tasks are also
reliably contractable now. Conversely, still stop before the first task whose
meaningful contract depends on evidence this batch has not produced yet.

Different ecosystems or infrastructure are useful sizing signals only when they
create meaningfully separate implementation, verification, or troubleshooting
paths. For example, backend creation, frontend scaffolding, database
provisioning, orchestration, production publishing, and unrelated verification
should not be combined into one task merely because all of them can already be
contracted. Failure in one such area should not unnecessarily force one
executor to retain and diagnose several unrelated implementation areas.

Do not split work mechanically by file, directory, technology, architectural
layer, acceptance criterion, class, method, or implementation step. Several
files, layers, or technologies may belong to one task when they form one small,
coherent, practically inseparable capability with one meaningful verification
path. An endpoint, its service implementation, DTO contract, registration, and
automated tests may therefore remain one task when together they form one
finished backend capability.

The goal is not the smallest possible task. The goal is one coherent result
with a bounded failure domain and an independently verifiable outcome. Do not
invent dependency graphs, sizing metadata, or runtime heuristics to enforce this
semantic judgment. In particular, do not infer task size from counts of files,
directories, technologies, tokens, acceptance criteria, tool names, or contract
length; deterministic Factory bookkeeping remains a runtime responsibility.

## Task-related durable intent

Select related durable intent independently for every task. Include a current
numbered intent document when its durable constraints materially affect correct
implementation of that specific task. Do not include a document merely because
it belongs to the same product, is broadly related to the subsystem, may be
useful background, or is needed by another task in the same batch.

The task contract must remain self-contained about the concrete work to perform.
`TaskRelatedIntent` adds normative durable constraints; it does not replace the
contract. Do not write a contract such as `Implement IDD-0012.` and do not copy,
paraphrase, or summarize durable intent into the contract merely to avoid using
`TaskRelatedIntent`.

Keep the semantic/deterministic boundary strict. You decide which intent matters
to each task. Do not try to resolve filenames, validate persistence, choose work
IDs, or perform other runtime bookkeeping. Conversely, do not expect Factory to
infer relevance through filenames, keywords, embeddings, ranking, or another
semantic agent.

## Output

Normally return only human-readable Markdown task documents. Each task begins
with an exact `# Task` heading followed by a non-empty, self-contained contract.
When durable intent materially constrains that task, terminate the contract with
an optional `# TaskRelatedIntent` metadata section containing the selected
stable IDs:

```markdown
# Task

Implement the first coherent change, including its important boundaries.

# TaskRelatedIntent
IDD-0012
IDD-0027

# Task

Rename the local helper used only by this implementation.
```

`# TaskRelatedIntent`:

- belongs only to the immediately preceding `# Task`;
- may occur at most once for that task;
- terminates the task contract body;
- must be followed only by another `# Task` or the end of planner output;
- contains one or more stable IDs in canonical `IDD-NNNN` form;
- contains exactly one ID per non-empty line and preserves semantic selection
  order;
- must not contain bullets, filenames, paths, Markdown links, comments, prose,
  comma-separated lists, summaries, or duplicate IDs.

Do not emit an empty `# TaskRelatedIntent` section. A task without the section
has an empty related-intent set. `# TaskRelatedIntent` is reserved planner
protocol metadata and never attaches to `# Question` or `# Done`.

The first task executes first. A task contract describes the result to produce
and the constraints needed to execute it without planner conversation context.
It does not describe future Factory workflow. Different tasks in one batch may
have completely different related-intent sets.

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
per-task `# TaskRelatedIntent` metadata), exactly one `# Question` section, or
exactly `# Done`. Do not mix these forms.
