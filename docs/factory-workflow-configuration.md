# Factory workflow configuration

Factory Runtime loads the packaged `factory-workflow.yaml` unless the workspace
contains `.idd/factory.yaml`. The effective normalized bytes are hashed and the
workflow name/hash are pinned in `state.json`. A changed hash during `continue`
returns `WORKFLOW_CHANGED`; restore the original workflow or cancel and restart.

## Schema version 1

The root contains `schemaVersion`, `name`, `limits`, and ordered `steps`.
Supported limits are `maxAgentAttempts`, `maxReplans`,
`maxCorrectiveCycles`, and per-gate `maxVerificationFixAttempts` (default `1`).
Runtime safety ceilings still apply.

`maxAgentAttempts` bounds repeated semantic attempts for mutable non-review work
items. A checkpoint review repeated after `needs-fix` is not additionally capped
by `maxAgentAttempts`: the review/correction loop is bounded by
`maxCorrectiveCycles`, while authoritative verification repair remains bounded by
`maxVerificationFixAttempts`. This keeps checkpoint review semantics aligned with
final review instead of allowing the generic work-item attempt budget to truncate
a valid corrective cycle early.

Exhausting a pinned run budget is terminal for that workflow instance. `continue`
does not add budget, and changing `.idd/factory.yaml` changes the workflow hash;
cancel and restart with a different workflow budget when a larger budget is
required.

Each step has an `id`, a registered `uses`, optional semantic `agent`, optional
known `handlers`, and typed outcome transitions under `on`.

Registered primitives are:

- `factory.decompose`
- `factory.intent`
- `factory.execute`
- `factory.replan`
- `factory.final-review`
- `factory.finalize`

Supported semantic roles are `task-decomposer`, `implementer`,
`checkpoint-reviewer`, `final-reviewer`, and `factory-replanner`.

## Validation

Loading rejects unsupported schemas, duplicate step IDs, unknown primitives or
roles, missing transition targets, invalid limits, unreachable finalization,
and obvious unconditional cycles. YAML cannot disable state validation,
completed-work immutability, attempt identity, protocol validation,
authoritative evidence, one-writer execution, retry ceilings, atomic saves,
runtime locking, or safe finalization.

The DSL has no arbitrary scripts, expressions, embedded code, shell nodes,
generic loops, dynamic loading, distributed DAGs, or parallel worker primitive.

## Example: compact review budgets

```yaml
schemaVersion: 1
name: compact-review

limits:
  maxAgentAttempts: 2
  maxReplans: 2
  maxCorrectiveCycles: 2
  maxVerificationFixAttempts: 1

steps:
  - id: decompose
    uses: factory.decompose
    agent: task-decomposer
    on:
      ready: execute
      blocked: $stop
  - id: execute
    uses: factory.execute
    handlers:
      subtask: implementer
      review-checkpoint: checkpoint-reviewer
    on:
      advanced: execute
      exhausted: final-review
      blocked: $stop
  - id: final-review
    uses: factory.final-review
    agent: final-reviewer
    on:
      approved: finalize
      blocked: $stop
  - id: finalize
    uses: factory.finalize
```

Omitted semantic outcomes are rejected if encountered; start from the packaged
default when customizing a production workflow.
