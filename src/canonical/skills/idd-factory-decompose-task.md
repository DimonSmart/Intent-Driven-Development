# idd-factory-decompose-task

## Purpose

Inspect current product reality and materialize all remaining tasks whose
contracts can be safely determined now.

This same planner runs for the initial batch, after every exhausted batch, and
after failed strict final verification. It is the only semantic component that
decides what Factory work remains.

## Inputs and boundaries

Use the unchanged original request, current durable intent, current repository,
completed task contracts and semantic results, runtime-observed changed paths,
and authoritative verification evidence. Durable intent is read-only Factory
input and never becomes a Factory task.
Intent preparation has already happened outside the runtime and must not be materialized as batch work.
Any unresolved durable decision is handled by the pre-runtime intent workflow rather than returning an intent-editing task.

Reassess the whole request on every invocation. Completed work is immutable
evidence, not proof that its intended effect is correct. If integrated reality
still needs correction, express that correction as an ordinary new task.

Materialize every task that can be contracted reliably from current evidence,
in execution order. Do not artificially stop after one task. Stop before the
first task whose meaningful contract depends on evidence that this batch has
not produced yet. Do not create speculative outlines or future placeholders.

Do not edit product files, durable intent, verification policy, or Factory
state. Do not implement tasks. Do not choose capabilities, workers, skills,
IDs, dependencies, revisions, statuses, or runtime transitions.

## Output

Return only human-readable Markdown task documents. Each task begins with an
exact `# Task` heading followed by a non-empty, self-contained contract:

```markdown
# Task

Implement the first coherent change, including its important boundaries.

# Task

Integrate the change into the second independently contractable area.
```

The first section executes first. A task contract describes the result to
produce and the constraints needed to execute it without planner context. It
does not describe future Factory workflow.

If no semantic work remains, return an empty response. Do not return an
explanation, approval, confidence, summary, JSON, outcome, payload, reason,
capability, or completion marker.
