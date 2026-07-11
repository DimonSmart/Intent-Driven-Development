# Document Types

## spec

`spec` describes what the system should be. It is not a task list.

Use `spec` when the change affects product behavior, domain contracts,
durable architecture boundaries, durable technical constraints, compatibility
expectations, non-goals, acceptance criteria, verification rules, or shared
behavior.

A spec document has no lifecycle status. Its presence in the current intent
directory means that it is current. Do not mark a spec as `Current`,
`Completed`, `Deprecated`, `Retired`, or `Superseded`. Edit the owning spec in
place when its product area remains current; migrate remaining current intent
and delete it when obsolete. Git history is the only history of spec revisions.

Shared specification is a normal `spec`. Common rendering, input, validation,
or dialog behavior should be a separate specification when multiple product
areas depend on it.

## adr

`adr` describes a durable architectural decision, including decisions that later
turned out to be wrong.

An ADR answers why a decision seemed correct, which alternatives were
considered, and which consequences were accepted.

Accepted ADRs must not be rewritten semantically. If the decision changes,
mark the old ADR as `Superseded` and create a new ADR that replaces it.

ADR status values are:

```text
Proposed | Accepted | Superseded | Rejected
```

ADR status is part of the decision record lifecycle and does not apply to specs.

ADRs are decision records, not current behavior specs. Do not archive ADRs. The
replacing ADR should reference the superseded ADR.

## spike

`spike` describes an experiment, research task, or hypothesis check before a
product or architecture decision.

A spike should state the question, constraints, evaluation method, result, and
recommended follow-up.

A spike is active research only while the question is unresolved. When resolved,
move durable product behavior into a spec, move durable architecture decisions
into an ADR, and delete the spike unless it remains useful as active research.
