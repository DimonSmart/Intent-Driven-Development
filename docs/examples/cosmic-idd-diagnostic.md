# COSMIC experimental measurement: Intent-Driven-Development (13 Nov 2026)

Status: **Diagnostic only — no mechanically validated CFP result**.

This is a conservative read-only **preparation trial**, not a claim of executing the generated skill with an active local Coding Agent. The document records the visible normative sources and the unresolved questions before any defensible full measurement. Running the installed idd-intent-cosmic skill with workspace access and the packaged validator is still needed for an actual numeric report.

## Verified current source inventory (GitHub main, 13 Nov 2026)

- .idd/intent/README.md — project-owned Product Intent authority.
- .idd/intent/INDEX.md — index of 3 numbered current documents.
- .idd/intent/IDD-0001.spec-factory-orchestration.md — current Factory orchestration behavior.
- .idd/intent/IDD-0003.spec-core-intent-and-generation.md — product memory, workflows, generation and distribution.
- .idd/intent/IDD-0006.adr-native-agent-factory-orchestration.md — current accepted Factory architecture, not a new independent FUR.
- No active numbered Spike listed in the source tree at this inspection.

The INDEX refers to IDD-0001, IDD-0003 and IDD-0006. No additional numbered Intent was observed in this snapshot. This is an inventory of documented material, not evidence that every functional requirement in the real IDD product has been recorded or decomposed into processes.

## Strategy and measurement blockers

**Provisional purpose:** measure the functional size of the current IDD product, not its implementation or task runtime.

**Possible software pieces:** (1) the IDD Intent plugin's outward-facing read-only and mutation skills, (2) the optional Factory plugin, (3) plugin distribution/generation behavior. Whether these are one measured software piece at one COSMIC layer or distinct units depends on the chosen software boundary, FUR granularity and functional users.

**Possible functional users:** human who requests a workflow; Coding Agent host and its subagents; external project file state or other tools where the *functional boundary* dictates interactions. A generated file, local test, database or API cannot be treated as a functional user merely because it exists.

**Candidate processes, not measured:**
- Product Intent mutation/update based on confirmed requirement (IDD-0003).
- Read-only audit/measurement invocation (IDD-0003 and evolving installed skills).
- Factory orchestration: preflight, planner/worker execution and final verification (IDD-0001), which must be normalized to real triggering events rather than each agent/tool call.
- Generator/distribution behavior described by IDD-0003, whose scope may be separate from the runtime Skills.

**Unresolved:** external functional user perspectives and boundaries (whole IDD vs host plugin vs Factory component); whether an asynchronous Factory worker result is an internal interaction or E/X at the chosen boundary; persistence semantics for project-owned durable Intent vs temporary Factory state; the required FUR granularity for comparing the components; and whether all material functional reactions are represented in the accessible normative specs.

Because several candidate processes can change their movement count when a boundary is chosen, this preparation does **not** assign E/X/R/W or CFP. One could measure a narrower well-supported selected scope after confirming its perspective, but that would remain a separate measurement unit.

## Output

Measurement date: 2026-11-13.

Overall product size: **Not determined**.

Measured subtotal: **Not available** (no locally validated measurement model).

Documentation inventory: reviewed listed current normative paths; semantic functional-process coverage incomplete.

Method applicability: COSMIC captures justified data movement but does not value Factory reasoning/computational complexity.

Required next action: run idd-intent-cosmic on the installed plugin in this repository, verify current FUR, resolve the boundary and event questions, create the temporary JSON model, and validate it with the **packaged** Node validator. Do not infer an effort estimate from any eventual CFP.
