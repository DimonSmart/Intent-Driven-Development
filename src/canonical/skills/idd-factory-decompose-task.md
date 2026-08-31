# idd-factory-decompose-task

## Purpose

Return the ordered work that remains to be done. This is the shared planning
contract for initial planning, planning after new evidence, and global
replanning. A complete up-front plan is not required.

Runtime owns persistent IDs, contracts, state transitions, ordering,
persistence, verification, recovery, and finalization.

## Inputs and boundaries

Read the supplied original request, relevant durable intent, immutable completed
work, the current planning trigger, and the existing future plan. Treat
completed work as historical fact: do not reproduce, change, reorder, or reopen
it. If previous work needs correction, return a new corrective task.

Return only work whose contract is known now. Do not materialize vague future
scope. Later planning can add work after research or implementation supplies
the missing facts.

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
`implementation`, `research`, or `semantic-review`. Documentation changes are
ordinary `implementation` work because they have the same runtime semantics.

An empty list is valid only when no product work remains. Otherwise return the
smallest ordered prefix that can safely make progress.

## Intent-required payload

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
