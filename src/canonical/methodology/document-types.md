# Document Types

## spec

`spec` describes what the system should be. It is not a task list.

Use `spec` when the change affects product behavior, domain contracts,
architectural shape, durable implementation patterns, compatibility
expectations, non-goals, acceptance criteria, verification rules, or shared
behavior.

Shared specification is a normal `spec`. Common rendering, input, validation,
or dialog behavior should be a separate specification when multiple product
areas depend on it.

## adr

`adr` describes a durable architectural decision, including decisions that later
turned out to be wrong.

An ADR answers why a decision seemed correct, which alternatives were
considered, and which consequences were accepted.

Accepted ADRs must not be rewritten semantically. If the decision changes,
archive the old ADR and create a new ADR that replaces it.

## spike

`spike` describes an experiment, research task, or hypothesis check before a
product or architecture decision.

A spike should state the question, constraints, evaluation method, result, and
recommended follow-up.
