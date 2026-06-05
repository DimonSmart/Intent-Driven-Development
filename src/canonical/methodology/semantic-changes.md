# Semantic Changes

A change is semantic when it changes what future coding agents should build,
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
