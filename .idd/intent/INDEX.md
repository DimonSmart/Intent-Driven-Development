# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent. The
numbered documents themselves are normative; Git history contains previous
versions. `GLOSSARY.md`, when present, is optional, unnumbered, and not listed
here.

## Current documents

| Document | Role | Area | Notes |
| --- | --- | --- | --- |
| IDD-0001 | Spec | Factory orchestration | Supervisor/Root, preflight, Planner/Worker lifecycle, task knowledge and final verification |
| IDD-0003 | Spec | IDD core and distribution | Current Intent, Visual Intent, initialization, distribution and self-hosting |
| IDD-0006 | ADR | Factory architecture | Native generic subagents and internal Skills; rationale, alternatives and experimental evidence |
| IDD-0008 | Spec | Engineering guardrails | Durable Engineering Rules, ownership, allocator and mutation boundaries |
| IDD-0009 | Spec | Intent inspection | Lint, semantic structure audit and implementation drift audit |
| IDD-0010 | Spec | Business complexity assessment | Qualitative scope, complexity, confidence and COSMIC preparation |
| IDD-0011 | Spec | COSMIC functional size | Approximate/strict sizing, conventional assumptions, functional areas and indicative effort |
| IDD-0012 | Spec | Factory recovery and refinement | Temporary state, immutable baseline, provenance, interruption, recovery and refinement review |
| IDD-0013 | Spec | Factory model policy | Execution profiles, configuration, inheritance and Worker model mapping |

The `Document` column contains stable `IDD-NNNN` identifiers only. Resolve
each ID to the unique current `.idd/intent/IDD-NNNN.*.md` document. Deleted IDs
are not reused; Git retains their earlier contents.
