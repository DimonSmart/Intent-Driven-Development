# Intent-Driven Development Pack

Use Intent-Driven Development for durable product intent.

## Required Reading

Read these concepts before deciding whether a specification change is needed:

- Intent is stable product truth that future implementations must preserve.
- A specification is a durable product description, not a task list.
- Specifications should be complete enough to rebuild the product from scratch,
  and strict enough not to become a task tracker.
- Current normative intent lives directly under `.specs/`.
- Archived documents are historical and are not current intent.
- Implementation evidence is not product intent by itself.
- Semantic changes must be represented in specifications or ADRs.

## Method Summary

Use `.specs/` when a change affects product behavior, domain contracts,
architectural shape, durable patterns, compatibility expectations, non-goals,
acceptance criteria, verification rules, or shared behavior.

Do not create or modify specifications for local tasks, temporary status,
ordinary dependency updates, formatting, small refactoring, or implementation
details that do not define the product. Generated agent output is not
authoritative.

Numbered documents give intent a stable identity. Preserve references by number
when titles or filenames change.
