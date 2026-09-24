# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent. The numbered documents themselves are normative; Git history contains previous versions. `GLOSSARY.md`, when present, is optional, unnumbered, and is not listed here.

## Current documents

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | Spec | Factory orchestration | Native-agent planner/worker loop, execution profiles with project-owned model policy, fresh contexts, TaskRelatedIntent + Conditional Engineering selection, deterministic Always Engineering propagation, minimal state, at-least-once execution, questions and project verification | — |
| IDD-0002 | ADR | Factory architecture | Superseded deterministic-runtime decision | — |
| IDD-0003 | Spec | IDD core and distribution | Durable intent plus optional Engineering Guardrails, separate verification/execution operational policy, source-knowledge import with bounded Engineering migration, ambiguity-only glossary, canonical generation, self-hosting boundary, and temporary references to durable project knowledge | — |
| IDD-0004 | ADR | Factory transport | Superseded blocking runtime-transport decision | — |
| IDD-0005 | Spec | Factory backend failures | Superseded runtime-owned backend failure semantics | — |
| IDD-0006 | ADR | Factory architecture | Native platform subagents replace Factory runtime and transport; Factory owns lightweight orchestration only | IDD-0002, IDD-0004, IDD-0005 |
| IDD-0007 | ADR | Factory architecture history | Custom orchestrator experiment, lessons learned, and deliberate return to native-agent orchestration | — |

The `Document` column contains stable `IDD-NNNN` identifiers only. Resolve an identifier to the unique current `.idd/intent/IDD-NNNN.*.md` file.
