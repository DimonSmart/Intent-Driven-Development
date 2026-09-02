---
name: idd-factory-decompose-task
description: Return all currently contractable remaining Factory work in execution order, deferring only work that needs evidence not yet available.
---

# idd-factory-decompose-task

## Purpose

Return the ordered work that remains to be done. This is the shared planning
contract for initial planning, planning after new evidence, and global
replanning. A complete up-front plan is not required, but planning should include
every remaining work item whose executable contract can be safely determined
from the evidence available now.

Runtime owns persistent IDs, contracts, state transitions, ordering,
persistence, verification, recovery, and finalization.

## Inputs and boundaries

Read the supplied original request, relevant durable intent, immutable completed
work, the current planning trigger, and the existing future plan. Treat
completed work as historical fact: do not reproduce, change, reorder, or reopen
it. If previous work needs correction, return a new corrective task.

Intent preparation is outside Factory runtime. Durable intent is read-only
Factory input and is never remaining Factory work. For a normal new end-to-end
run, required Intent Preflight has already completed before initial planning.
The original request is intentionally unchanged, so instructions in it to create
or update durable intent describe pre-runtime scope and must not be materialized
as Factory tasks. Plan only implementation, research, semantic review, and
non-intent documentation work that remains against current durable intent. If
current durable intent still lacks a genuinely required product decision, use
`intent-required` rather than returning an intent-editing task.

Return all work whose contract is known now. Do not stop after the first
executable task merely to keep the plan small. Preserve execution order and
continue materializing known work until the next task cannot be safely
contracted without evidence or results from earlier work. Do not materialize
vague future scope past that uncertainty boundary. Later planning can add that
work after research or implementation supplies the missing facts.

Do not implement changes, edit intent or verification policy, mutate Factory
state, choose roles/skills, or expose runtime bookkeeping.

## Planning result

`ready` returns top-level `tasks`, an ordered array. Each item contains only:

```json
{
  "capability": "implementation",
  "task": "A self-contained Markdown task contract."
}
```

The first task executes first. Express prerequisites through list order. Do not
return IDs, sequence numbers, dependencies, statuses, work kinds, definition
states, covered-work references, revisions, verification check selections, or
mutation operations.

Use only capabilities allowed by the supplied Factory policy, such as
`implementation`, `research`, or `semantic-review`. Non-intent documentation
changes are ordinary `implementation` work because they have the same runtime
semantics. Durable intent changes are excluded by the planning boundary above.

An empty list is valid only when no product work remains. Otherwise return all
currently contractable remaining work in execution order. This may be many
tasks. Stop only at the first material uncertainty where defining the next task
contract depends on evidence or results that are not yet available.

## Intent-required payload

Use `intent-required` only when a durable product decision required for safe
planning cannot be determined from the original request and current durable
intent. Missing documentation alone is not sufficient, and technical
implementation uncertainty that can be represented as `research` is not a
product-decision blocker.

`payload.missingIntentDecisions` must be non-empty. Each item contains:

```text
area
whyBlocking
requiredDecisions[]
intentReferences[]
recommendedNextWorkflow?
```

## Result outcomes

Return one JSON object containing only `outcome` and the outcome-specific fields
defined above. Use one outcome: `ready`, `needs-clarification`,
`intent-required`, `focused-handoff`, or `blocked`.

Do not return invocation identity, role, capability, work-item ID, attempt ID,
run ID, protocol or schema version, skill, execution profile, result path, or
other runtime bookkeeping.

The worker never creates Factory files and never chooses what runtime does next.
