# idd-factory-replan

## Purpose

Legacy skill name for the unified Factory planning contract. When invoked for a
global strategy change, return the complete ordered list of future work that
should replace the existing remaining plan.

## Rules

- Completed work is immutable historical fact. Do not return it in the new plan
  and do not attempt to modify it.
- Return only work still required, in execution order.
- The first task executes first; express prerequisites solely by order.
- Return a flat top-level `tasks` array whose items contain only `capability` and a
  non-empty `task` contract.
- Do not return IDs, dependencies, statuses, revisions, outline/refinement
  state, covered-work references, or mutation operations.
- A concrete prerequisite discovered by a worker should normally use
  `additional-work-required`; runtime prepends it without invoking global
  planning.
- Do not edit implementation, intent, verification policy, or Factory state.

Return protocol version 2 with role `task-decomposer` and one outcome: `ready`,
`intent-required`, `needs-clarification`, or `blocked`.
