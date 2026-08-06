# Semantic Changes

A change is semantic when it changes what future CodingAgents should build,
preserve, avoid, depend on, or verify.

Semantic changes include product behavior, domain contracts, supported
input/output, validation rules, compatibility expectations, scope, non-goals,
accepted architectural decisions, product-defining library choices, and durable
implementation constraints.

A change is non-semantic when it only improves wording, organization,
terminology, references, formatting, or local code shape without changing what
should be built.

Do not hide semantic changes as cleanup. If product intent changed, update or
replace the relevant specification.

## Decision-Relevant Future Intent

A future intention belongs in current intent only when knowing it can materially
change a decision being made now or make an otherwise acceptable choice
unacceptable.

Ask:

> Would knowing this future intent materially change the current decision?

- No: do not persist it in current intent.
- Yes: record the minimum required capability, invariant, or prohibited lock-in,
  not a speculative future implementation.

Put a durable requirement in a spec, the architectural decision and its
tradeoffs in an ADR, or an unresolved question in a spike.
