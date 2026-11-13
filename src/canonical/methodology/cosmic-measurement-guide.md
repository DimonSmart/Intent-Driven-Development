# COSMIC v5.0 measurement guide and JSON contract

Normative method: [COSMIC Measurement Manual v5.0, combined September 2024](https://cosmic-sizing.org/wp-content/uploads/2022/11/Combination-of-Parts-v5.0-Sep-2024.pdf). Part 1 defines mandatory rules and Part 2 explains edge cases. This guide and validator are packaged under **references/** for both host plugins. The validator proves structural/arithmetic invariants only; deciding what FUR really mean requires semantic review.

## Rule mapping

- Strategy: Part 1 Rules 1–9 require purpose/scope, software boundary, functional users, a single layer per piece, decomposition and functional process-level **FUR granularity**. Persistent storage is not a functional user (Rule 7).
- Processes: Rule 10, including 10c, requires coherent response to a real triggering event, triggering Entry from the functional user, and at least one Exit or Write. All alternative outcomes are part of the same process. An Entry + Read alone is invalid. Repeated execution is not new size.
- Objects/groups: Rule 11, OOI from users' perspective, one group of attributes about an OOI (not one field or table). One OOI can have functionally distinct data groups; justify the distinction.
- Movements: Rules 12–20, E crossing boundary from functional user, X to user, R from persistent storage, W to persistent storage including deletion. Do not measure processing alone. Distinct repeated moves require independent FUR evidence (Part 2 Guidance Rules 13–15).
- Error/confirmation: Part 2 Guidance on Rules 16–19, one special Exit for all error/confirmation messages to **human** functional users within one process. Additional substantive OOI output may need a separate X. Ordinary software/hardware messages use standard movement rules.
- Time: independently triggered clock events can provide E; asking the clock for current time is not automatically a data movement. Physical disk/DB, code or temporary variables cannot alone establish persistence.
- Aggregation: Rules 21–23 and Part 2 Guidance on Rule 23. Do not sum system and nested components, unequal decomposition/functional granularity, or different layers; internal E/X matching requires explicit process correspondence, not a subtraction heuristic.
- Reporting: Rule 25 identifies method/version and measured units. Output must state **N CFP (v5.0)**, date, read inventory, coverage, applicability and limitations.

## Versioned input JSON (schemaVersion 1)

- Top-level: schemaVersion = 1; methodVersion = "COSMIC Measurement Manual v5.0"; purpose, scope, measurementDate nonempty; requestedFullTotal boolean. Optional sourceRevision only if already available without Git/history access.
- measurementUnits: list of records with **unique** measurementUnitId, softwarePiece, boundary, layer, decompositionLevel, furGranularity, scope; functionalUsers list [{id,kind}] with kind human/software/hardware/clock. For composite cases parentBoundary is the common declared outer boundary.
- sourceDocuments: list of inventoried current numbered files with path, read=true/false, optional kind/id/notes. Only canonical relative paths of the form .idd/intent/IDD-NNNN.spec-*.md (also adr/spike) are valid; functional evidence MUST come from current specs. README and INDEX plus mismatches remain in the Markdown inventory/coverage, not extra FUR claims.
- coverage: documentationCoverage {inventoryComplete: boolean, unreadPaths: string[], documentedScopeProvenEmpty?: boolean}; functionalCoverage {processInventoryComplete: boolean}; methodApplicability {status: "Applicable"|"Limited"|"Unknown", reason?: string}; coverageItems [{name,status}], status covered/unread/unknown. Claims must be grounded in an actual document inventory; boolean assertions cannot certify undocumented reality.
- processes: each has globally unique report-local id, measurementUnitId, name, status (Measured / Needs clarification / Ambiguous / Out of scope / Not a functional process). Measured additionally has functionalUser (from unit), triggeringEvent, triggeringEntryId, semanticStatus = "Supported by FUR", semanticCompletenessJustification, evidence, dataMovements; optionally primaryArea, relatedAreas. Unmeasured/ambiguous need uncertainty. Candidate data movements are not counted until process is Measured.
- dataMovements: unique id, functionalProcessId and measurementUnitId matching owner, type E/X/R/W, dataGroup, justification, evidence, semanticStatus = "Supported by FUR", evidenceType = "regular FUR" or "error-confirmation special rule". Regular movements require objectOfInterest. E/X require direction in/out and interactingFunctionalUser from unit. R/W require persistenceJustification. Identical movement type+group+recipient within a process needs distinctOccurrenceReason based on separate FUR; code cannot decide justification. Optional ruleRefs.
- evidence: array [{path,quote?,section?}]. A current spec path must be **physically read** from target workspace, and exact short quote or Markdown heading (including its # prefix) must exist in that file. Multiple paths allowed for shared FUR. A title/ID without an actual file or source anchor cannot prove a movement.
- aggregation: mode single-unit / separate / compatible-components. Compatible requires equal layer, decomposition and FUR granularity, common parentBoundary, distinct component boundaries, justification and processMergeEvidence, internalPairs [{exitMovementId,entryMovementId,justification}]. Internal pairs must be X/E in different units of the same data group. If insufficient evidence, use mode separate; it never publishes combined total.
- claimedCfp, if present, is *never* trusted, must match the recomputed number. calculated CFP MUST be derived from counted E/X/R/W.
- There are no percentage-completeness shortcuts or automatic early estimates. Missing known full-scope source paths or unmeasured FUR block the full number. An unknown methodApplicability blocks full, even if every source file is read.

## Minimal illustrative JSON — **not** an official COSMIC golden case

Assume that the actual target spec file .idd/intent/IDD-0001.spec-task.md has the heading "# Task" and the exact sentence "User submits and stores a task." No extra FUR are required in the declared scope. This is an instructional 2-CFP model, NOT a generally implied CRUD process.

~~~json
{
  "schemaVersion": 1,
  "methodVersion": "COSMIC Measurement Manual v5.0",
  "purpose": "Measure documented task creation",
  "scope": "Create task only",
  "measurementDate": "2026-11-13",
  "requestedFullTotal": true,
  "measurementUnits": [{
    "measurementUnitId": "MU-1", "softwarePiece": "Task app",
    "boundary": "Whole task app", "layer": "Application",
    "decompositionLevel": "whole application",
    "furGranularity": "functional process",
    "scope": "Create task",
    "functionalUsers": [{"id":"User","kind":"human"}]
  }],
  "sourceDocuments": [{"path":".idd/intent/IDD-0001.spec-task.md","read":true}],
  "coverage": {
    "documentationCoverage": {"inventoryComplete":true,"unreadPaths":[]},
    "functionalCoverage": {"processInventoryComplete":true},
    "methodApplicability": {"status":"Applicable"},
    "coverageItems": [{"name":"Create task","status":"covered"}]
  },
  "processes": [{
    "id":"FP-1", "measurementUnitId":"MU-1", "name":"Create task",
    "status":"Measured", "functionalUser":"User",
    "triggeringEvent":"User submits task",
    "triggeringEntryId":"M-1",
    "semanticStatus":"Supported by FUR",
    "semanticCompletenessJustification":"FUR specifies entry and persistent storage",
    "evidence":[{"path":".idd/intent/IDD-0001.spec-task.md",
      "section":"# Task","quote":"User submits and stores a task."}],
    "dataMovements": [
      {"id":"M-1","functionalProcessId":"FP-1","measurementUnitId":"MU-1",
       "type":"E","direction":"in","interactingFunctionalUser":"User",
       "dataGroup":"Task details","objectOfInterest":"Task",
       "evidenceType":"regular FUR","semanticStatus":"Supported by FUR",
       "justification":"User enters task",
       "evidence":[{"path":".idd/intent/IDD-0001.spec-task.md",
         "quote":"User submits and stores a task."}]},
      {"id":"M-2","functionalProcessId":"FP-1","measurementUnitId":"MU-1",
       "type":"W","dataGroup":"Task details","objectOfInterest":"Task",
       "persistenceJustification":"Task survives process completion",
       "evidenceType":"regular FUR","semanticStatus":"Supported by FUR",
       "justification":"Store task",
       "evidence":[{"path":".idd/intent/IDD-0001.spec-task.md",
         "quote":"User submits and stores a task."}]}
    ]
  }],
  "aggregation": {"mode":"single-unit"}
}
~~~

## Validator API and CLI

- Import from local Node.js 20+ ES module: validate(model, {workspaceRoot}) -> array of diagnostics [{code,path,reason,severity}], calculate(model, {workspaceRoot}) -> {diagnostics,perProcess,unitSubtotals,measuredSubtotal,overallSize,status,mechanicallyValidated,label}.
- CLI: node "<installed>/references/cosmic-validator.mjs" "<OS-temp-input.json>" "<target-workspace-root>".
- Exit 0 indicates structurally valid and mechanically computed, 1 validation errors, 2 invalid invocation/input. This read-only validator never writes, contacts services or modifies the model. It reads numbered FUR files from the target workspace, refusing paths outside .idd/intent.
- Only use the *installed* validator, not a source-tree substitute. If an execution capability, local Node, source access or ephemeral JSON is unavailable, status is Diagnostic only; do not claim a verified numeric CFP.
- Measured process needs both triggering Entry and valid E+X/W minimum; all moves need appropriate data group, OOI or special error marker, user/persistence, source quotes, unique ownership and rule rationale. Invalid or incomplete candidates may not contribute to **overallSize**.
- Complete is relative to declared and documented FUR scope only; Partial counts verified unique processes in a compatible unit; Diagnostic only has no defensible number. A product dominated by algorithms/streaming can be fully documented but have methodApplicability Limited.

## Official reference examples — golden values fixed before evaluation

The source is the v5.0 **combined Manual, Part 3d (May 2022 edition), Sections 4.1, 4.2 and 5.2**. Fixtures must preserve the explicit FUR, group identity and movement sequence:
- §4.1 **C-Reg: Add a Professor** — E Professor details, R Professor details, W Professor details, X error/confirmation to registrar; **4 CFP**.
- §4.2 **Rice cooker: Start cooking** — E start signal from button, X turn-on command to Heater, X turn-on to Lamp; **3 CFP**.
- §5.2 **Display list of cleaning operations** — E search term, R operations, X operations, X error message; **4 CFP**.

These official reference cases constrain both expected E/X/R/W structures and CFP, not only the numerical totals. The example of generic E+W above is **not** a Golden case, and official example text is not a Product Intent document for real user projects.
