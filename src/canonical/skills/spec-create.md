# spec-create

Use this skill to create a new specification, ADR, or spike.

## Rules

- Do not create a spec for task-level changes.
- Do not create a spec for an ordinary dependency update.
- Create a spec only for durable product intent.
- Create an ADR for durable architectural decisions.
- Create a spike for research before a decision.

## Workflow

1. Read `.specs/README.md`, `.specs/INDEX.md`, and relevant current numbered
   documents directly under `.specs/`.
2. Decide whether the change is durable intent, a decision, or research.
3. Find the next number by scanning `.specs/` and `.specs/archive/`.
4. Create the document from the matching template.
5. Keep the document normative. Do not add local task notes.
