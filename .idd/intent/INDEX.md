# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent. The numbered documents themselves are normative; Git history contains previous versions. `GLOSSARY.md`, when present, is optional, unnumbered, and is not listed here.

## Current documents

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | Spec | Factory orchestration | Deterministic runtime, task-related durable-intent propagation, verification, temporary execution state, semantic workers, recovery and finalization | — |
| IDD-0002 | ADR | Factory architecture | Programmatic workflow ownership and replaceable agent backends | — |
| IDD-0003 | Spec | IDD core and distribution | Durable intent model, ambiguity-only glossary, canonical generation, self-hosting boundary, and temporary references to durable intent | — |
| IDD-0004 | ADR | Factory transport | Blocking adapter transport avoids model-driven polling and remains replaceable | — |
| IDD-0005 | Spec | Factory Agent Backend failures | Deterministic quota/rate/auth classification, normalized diagnostics, resumable external blockers, and budget-neutral replay | — |

The `Document` column contains stable `IDD-NNNN` identifiers only. Resolve an identifier to the unique current `.idd/intent/IDD-NNNN.*.md` file.
