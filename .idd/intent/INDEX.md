# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent. The
numbered documents themselves are normative; Git history contains previous
versions. `GLOSSARY.md`, when present, is optional, unnumbered, and not listed
here.

## Current documents

| Document | Role | Area | Notes |
| --- | --- | --- | --- |
| IDD-0001 | Spec | Factory orchestration | Current native-agent planner/worker lifecycle, model policies, durable preflight, verification and at-least-once behavior |
| IDD-0003 | Spec | IDD core and distribution | Current Intent, Engineering, lint/audit, generation, import and self-hosting contracts |
| IDD-0006 | ADR | Factory architecture | Native-agent orchestration; non-polling waits, at-least-once recovery and findings from the former custom-runtime experiment |

The `Document` column contains stable `IDD-NNNN` identifiers only. Resolve
each ID to the unique current `.idd/intent/IDD-NNNN.*.md` document. Deleted IDs
are not reused; Git retains their earlier contents.
