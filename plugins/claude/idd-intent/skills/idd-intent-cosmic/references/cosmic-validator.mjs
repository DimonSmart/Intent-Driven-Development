// COSMIC v5.0 structural and arithmetic validator. No dependencies, network or file writes.
// Semantic interpretation of FUR remains the measurer's responsibility.
import { existsSync, readFileSync, realpathSync } from "node:fs";
import { dirname, isAbsolute, relative, resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

export const SCHEMA_VERSION = 1;
const statuses = new Set(["Measured", "Needs clarification", "Ambiguous", "Out of scope", "Not a functional process"]);
const movements = new Set(["E", "X", "R", "W"]);
const kinds = new Set(["human", "software", "hardware", "clock"]);
const hasText = value => typeof value === "string" && value.trim().length > 0;
const array = value => Array.isArray(value) ? value : [];
const key = value => String(value ?? "");
const own = (value, prop) => Object.prototype.hasOwnProperty.call(value ?? {}, prop);
const canonicalIntentPath = p => typeof p === "string" &&
  /^\.idd\/intent\/IDD-\d{4}\.(spec|adr|spike)-[a-z0-9-]+\.md$/.test(p);
const inside = (child, parent) => child === parent ||
  (child.startsWith(parent.endsWith(sep) ? parent : parent + sep));

export function validate(model, options = {}) {
  const diagnostics = [];
  const add = (code, path, reason, severity = "error") =>
    diagnostics.push({ code, path, reason, severity });
  if (!model || typeof model !== "object" || Array.isArray(model)) {
    add("SCHEMA", "$", "Model must be an object");
    return diagnostics;
  }
  if (model.schemaVersion !== SCHEMA_VERSION) add("SCHEMA_VERSION", "$.schemaVersion", "Expected schemaVersion 1");
  if (model.methodVersion !== "COSMIC Measurement Manual v5.0")
    add("METHOD_VERSION", "$.methodVersion", "Use COSMIC Measurement Manual v5.0");
  for (const field of ["purpose", "scope", "measurementDate"])
    if (!hasText(model[field])) add("REQUIRED", "$." + field, "Missing measurement metadata");
  const units = new Map(), processIds = new Set(), movementIds = new Set(), moveById = new Map();
  for (const [i, u] of array(model.measurementUnits).entries()) {
    const path = "$.measurementUnits[" + i + "]";
    if (!hasText(u?.measurementUnitId) || units.has(u.measurementUnitId))
      add("UNIT_ID", path, "Measurement unit ID must be nonempty and unique");
    else units.set(u.measurementUnitId, u);
    for (const field of ["softwarePiece", "boundary", "layer", "decompositionLevel", "furGranularity", "scope"])
      if (!hasText(u?.[field])) add("STRATEGY", path + "." + field, "Missing measurement strategy dimension");
    if (!array(u?.functionalUsers).length) add("FUNCTIONAL_USERS", path, "Functional users must be identified");
    const users = new Set();
    for (const [j, user] of array(u?.functionalUsers).entries()) {
      if (!hasText(user?.id) || users.has(user.id) || !kinds.has(user.kind))
        add("FUNCTIONAL_USER", path + ".functionalUsers[" + j + "]", "User ID must be unique; kind must be human/software/hardware/clock");
      users.add(user?.id);
    }
  }
  if (!units.size) add("STRATEGY", "$.measurementUnits", "At least one measurement unit required");

  const docs = new Map(), sourceContents = new Map();
  let root = null;
  if (hasText(options.workspaceRoot)) {
    try {
      root = realpathSync(resolve(options.workspaceRoot, ".idd", "intent"));
    } catch {
      add("INTENT_ROOT", "$.sourceDocuments", "Cannot verify .idd/intent/ against the workspace");
    }
  }
  for (const [i, doc] of array(model.sourceDocuments).entries()) {
    const p = "$.sourceDocuments[" + i + "]";
    if (!canonicalIntentPath(doc?.path) || docs.has(doc.path)) {
      add("SOURCE_PATH", p, "Document must be a unique current IDD spec/ADR/spike path");
      continue;
    }
    docs.set(doc.path, doc);
    if (doc.read !== true) add("SOURCE_UNREAD", p, "Source document not read");
    if (root) {
      const full = resolve(options.workspaceRoot, doc.path);
      try {
        if (!existsSync(full)) throw Error("file missing");
        const real = realpathSync(full);
        if (!inside(real, root) || isAbsolute(relative(root, real)) || relative(root, real).startsWith(".."))
          throw Error("source escapes current Intent");
        sourceContents.set(doc.path, readFileSync(real, "utf8"));
      } catch (error) {
        add("SOURCE_UNVERIFIED", p, "Cannot independently read current FUR: " + error.message);
      }
    } else if (!options.trustProvidedInventory) {
      add("SOURCE_UNVERIFIED", p, "Workspace root was not supplied; evidence is not mechanically verified");
    }
  }
  const checkEvidence = (evidence, path, requireSpec = true) => {
    if (!array(evidence).length) { add("EVIDENCE_MISSING", path, "Source-backed evidence required"); return; }
    let backed = false;
    for (const [j, ev] of evidence.entries()) {
      const ep = path + "[" + j + "]";
      if (!docs.has(ev?.path)) { add("EVIDENCE_SOURCE", ep, "Path not in verified source inventory"); continue; }
      const doc = docs.get(ev.path);
      if (requireSpec && !ev.path.includes(".spec-")) {
        add("FUR_SOURCE", ep, "Functional behavior must be supported by a current spec");
        continue;
      }
      if (!hasText(ev.quote) && !hasText(ev.section)) {
        add("EVIDENCE_ANCHOR", ep, "Exact short quote or local section required");
        continue;
      }
      if (root && sourceContents.has(ev.path)) {
        const source = sourceContents.get(ev.path);
        if (hasText(ev.quote) && !source.includes(ev.quote)) {
          add("EVIDENCE_QUOTE", ep, "Exact quote not found in current document"); continue;
        }
        if (hasText(ev.section) && !source.split(/\r?\n/).some(line => /^#{1,6} /.test(line) && line.trim() === ev.section.trim())) {
          add("EVIDENCE_SECTION", ep, "Heading not found in current document"); continue;
        }
      } else if (!options.trustProvidedInventory) {
        add("EVIDENCE_UNVERIFIED", ep, "Source could not be checked against current workspace");
        continue;
      }
      if (doc.read === true) backed = true;
    }
    if (!backed) add("EVIDENCE_UNVERIFIED", path, "No verified source evidence");
  };
  for (const [i, p] of array(model.processes).entries()) {
    const path = "$.processes[" + i + "]";
    const unit = units.get(p?.measurementUnitId);
    if (!hasText(p?.id) || processIds.has(p.id)) add("PROCESS_ID", path, "Process ID must be nonempty and globally unique");
    processIds.add(p?.id);
    if (!unit) add("PROCESS_UNIT", path, "Unknown measurement unit");
    if (!statuses.has(p?.status)) add("PROCESS_STATUS", path, "Invalid process status");
    if (!hasText(p?.name)) add("PROCESS_NAME", path, "Process name required");
    if (p?.status === "Measured") {
      for (const f of ["triggeringEvent", "triggeringEntryId", "functionalUser", "semanticCompletenessJustification"])
        if (!hasText(p?.[f])) add("PROCESS_REQUIRED", path + "." + f, "Missing required process field");
      if (!array(unit?.functionalUsers).some(u => u.id === p?.functionalUser))
        add("PROCESS_USER", path, "Triggering functional user not in unit");
      if (p?.semanticStatus !== "Supported by FUR") add("PROCESS_SEMANTIC", path, "Measured process must be semantically supported by FUR");
      checkEvidence(p?.evidence, path + ".evidence");
    } else if (p?.status === "Needs clarification" || p?.status === "Ambiguous") {
      if (!hasText(p?.uncertainty)) add("UNCERTAINTY", path, "Unmeasured candidate requires reason and clarification");
    }
    const localMoves = [], distinct = new Map();
    for (const [j, m] of array(p?.dataMovements).entries()) {
      const mp = path + ".dataMovements[" + j + "]";
      if (!hasText(m?.id) || movementIds.has(m.id)) add("MOVEMENT_ID", mp, "Movement ID must be globally unique");
      movementIds.add(m?.id);
      moveById.set(m?.id, { movement: m, process: p });
      if (m?.measurementUnitId !== p?.measurementUnitId || m?.functionalProcessId !== p?.id)
        add("MOVEMENT_OWNER", mp, "Data movement must belong to its process and unit");
      if (!movements.has(m?.type)) add("MOVEMENT_TYPE", mp, "Movement must be E, X, R or W");
      if (!hasText(m?.dataGroup)) add("DATA_GROUP", mp, "Data group is required");
      if (!hasText(m?.justification)) add("MOVEMENT_REASON", mp, "Data movement requires functional justification");
      if (m?.semanticStatus !== "Supported by FUR") add("MOVEMENT_SEMANTIC", mp, "Measured movement must be backed by FUR");
      const special = m?.evidenceType === "error-confirmation special rule";
      if (special) {
        if (m?.type !== "X" || !array(unit?.functionalUsers).some(u => u.id === m?.interactingFunctionalUser && u.kind === "human"))
          add("ERROR_MESSAGE", mp, "Special confirmation/error rule applies only to an Exit to a human user");
      } else if (m?.evidenceType !== "regular FUR" || !hasText(m?.objectOfInterest)) {
        add("OBJECT_OF_INTEREST", mp, "Regular movement needs evidenceType and OOI");
      }
      if (m?.type === "E" || m?.type === "X") {
        if (!array(unit?.functionalUsers).some(u => u.id === m?.interactingFunctionalUser))
          add("CROSS_BOUNDARY", mp, "E/X requires a known external functional user");
        if (m?.direction !== (m.type === "E" ? "in" : "out"))
          add("DIRECTION", mp, "E must enter and X must exit the software boundary");
      }
      if ((m?.type === "R" || m?.type === "W") && !hasText(m?.persistenceJustification))
        add("PERSISTENCE", mp, "R/W requires FUR-supported persistent lifecycle (not code or temporary storage)");
      if (p?.status === "Measured") checkEvidence(m?.evidence, mp + ".evidence");
      const repeatKey = [m?.type, special ? "human-message" : m?.dataGroup,
        special ? "" : (m?.type === "E" || m?.type === "X") ? m?.interactingFunctionalUser : ""].join("|");
      if (distinct.has(repeatKey) && !hasText(m?.distinctOccurrenceReason))
        add("DUPLICATE_MOVEMENT", mp, "Repeated type/data group/functional user needs distinct FUR evidence; occurrences alone do not count");
      distinct.set(repeatKey, m?.id);
      localMoves.push(m);
    }
    if (p?.status === "Measured") {
      const es = localMoves.filter(m => m.type === "E");
      if (!es.some(m => m.id === p?.triggeringEntryId && m.interactingFunctionalUser === p.functionalUser))
        add("TRIGGERING_ENTRY", path, "Triggering Entry must exist and originate with initiating functional user");
      if (localMoves.length < 2 || !localMoves.some(m => m.type === "X" || m.type === "W"))
        add("RULE_10C", path, "Measured process needs an Entry and at least one Exit or Write");
    } else if (localMoves.length) {
      add("CANDIDATE_MOVEMENTS", path, "Movements of unmeasured candidates are provisional and cannot be counted", "warning");
    }
  }
  if (!model.coverage || typeof model.coverage !== "object") {
    add("COVERAGE", "$.coverage", "Documentation, functional and method applicability coverage required");
  } else {
    const c = model.coverage;
    if (typeof c.documentationCoverage?.inventoryComplete !== "boolean" || !Array.isArray(c.documentationCoverage?.unreadPaths))
      add("DOC_COVERAGE", "$.coverage.documentationCoverage", "Explicit inventory completeness and unreadPaths required");
    if (typeof c.functionalCoverage?.processInventoryComplete !== "boolean")
      add("FUR_COVERAGE", "$.coverage.functionalCoverage", "Explicit process inventory completeness required");
    if (!["Applicable", "Limited", "Unknown"].includes(c.methodApplicability?.status))
      add("METHOD_APPLICABILITY", "$.coverage.methodApplicability", "Applicability must be Applicable, Limited or Unknown");
    for (const [j, area] of array(c.coverageItems).entries())
      if (!hasText(area?.name) || !["covered", "unread", "unknown"].includes(area?.status))
        add("COVERAGE_ITEM", "$.coverage.coverageItems[" + j + "]", "Inventory area needs valid name/status");
  }
  const ag = model.aggregation;
  if (!ag || !["single-unit", "separate", "compatible-components"].includes(ag.mode))
    add("AGGREGATION", "$.aggregation", "Explicit aggregation mode required");
  else if (ag.mode === "single-unit" && units.size !== 1)
    add("AGGREGATION", "$.aggregation", "Single-unit aggregation requires exactly one unit");
  else if (ag.mode === "compatible-components") {
    const all = [...units.values()];
    if (all.length < 2 || !all.every(u =>
      u.layer === all[0].layer && u.decompositionLevel === all[0].decompositionLevel &&
      u.furGranularity === all[0].furGranularity && hasText(u.parentBoundary) &&
      u.parentBoundary === all[0].parentBoundary && u.boundary !== all[0].parentBoundary))
      add("AGGREGATION_UNIT", "$.aggregation", "Components must share layer, decomposition, FUR granularity and a declared parent boundary");
    if (new Set(all.map(u => u.boundary)).size !== all.length)
      add("AGGREGATION_BOUNDARY", "$.aggregation", "Component boundaries must be distinct");
    if (!hasText(ag.justification) || !hasText(ag.processMergeEvidence))
      add("AGGREGATION_EVIDENCE", "$.aggregation", "Document component correspondence and functional-process reconciliation");
    const excluded = new Set();
    for (const [j, pair] of array(ag.internalPairs).entries()) {
      const path = "$.aggregation.internalPairs[" + j + "]";
      const x = moveById.get(pair?.exitMovementId), e = moveById.get(pair?.entryMovementId);
      if (!x || !e || x.movement.type !== "X" || e.movement.type !== "E" ||
          x.process.measurementUnitId === e.process.measurementUnitId ||
          x.movement.dataGroup !== e.movement.dataGroup ||
          !hasText(pair.justification) || excluded.has(pair.exitMovementId) || excluded.has(pair.entryMovementId))
        add("AGGREGATION_PAIR", path, "Internal pair must be distinct X/E in different units for same data group with evidence");
      else {
        excluded.add(pair.exitMovementId); excluded.add(pair.entryMovementId);
      }
    }
  }
  if (!array(model.sourceDocuments).length &&
      !model.coverage?.documentationCoverage?.documentedScopeProvenEmpty)
    add("SOURCE_INVENTORY", "$.sourceDocuments", "No source inventory; only a proven empty scope may have no specs");
  return diagnostics;
}

export function calculate(model, options = {}) {
  const diagnostics = validate(model, options);
  const errors = diagnostics.filter(d => d.severity === "error");
  const impacted = new Set();
  for (const d of errors) {
    const match = d.path.match(/^\$\.processes\[(\d+)\]/);
    if (match) impacted.add(Number(match[1]));
  }
  const perProcess = [], subtotals = new Map();
  for (const [i, p] of array(model?.processes).entries()) {
    if (p.status !== "Measured" || impacted.has(i)) continue;
    const counts = { E: 0, X: 0, R: 0, W: 0 };
    for (const m of p.dataMovements) counts[m.type]++;
    const cfp = counts.E + counts.X + counts.R + counts.W;
    if (own(p, "claimedCfp") && p.claimedCfp !== cfp)
      diagnostics.push({ code: "CLAIMED_CFP", path: "$.processes[" + i + "].claimedCfp",
        reason: "Claimed CFP differs from the mechanically calculated value", severity: "error" });
    perProcess.push({ id: p.id, measurementUnitId: p.measurementUnitId, counts, cfp });
    subtotals.set(p.measurementUnitId, (subtotals.get(p.measurementUnitId) ?? 0) + cfp);
  }
  // A process cannot be mechanically validated when any error exists, even outside its own entry.
  const safe = !diagnostics.some(d => d.severity === "error");
  const coverage = model?.coverage ?? {};
  const unresolved = array(model?.processes).some(p => p.status === "Needs clarification" || p.status === "Ambiguous");
  const completeSources = coverage.documentationCoverage?.inventoryComplete === true &&
    array(coverage.documentationCoverage?.unreadPaths).length === 0 &&
    array(model?.sourceDocuments).every(d => d.read === true);
  const completeScope = coverage.functionalCoverage?.processInventoryComplete === true &&
    array(coverage.coverageItems).every(a => a.status === "covered");
  const methodApplicable = coverage.methodApplicability?.status === "Applicable";
  const countedCount = perProcess.length;
  const provenEmpty = coverage.documentationCoverage?.documentedScopeProvenEmpty === true &&
    completeSources && completeScope && !array(model?.processes).length;
  const mode = model?.aggregation?.mode;
  const aggregationValid = mode === "single-unit" || mode === "compatible-components";
  const fullAllowed = safe && completeSources && completeScope && !unresolved &&
    methodApplicable && aggregationValid && (countedCount > 0 || provenEmpty) &&
    array(model?.processes).every(p => p.status !== "Measured" ||
      perProcess.some(row => row.id === p.id));
  if (model?.requestedFullTotal && !fullAllowed)
    diagnostics.push({ code: "FULL_TOTAL_BLOCKED", path: "$.requestedFullTotal",
      reason: "Full CFP requires complete verified FUR coverage, compatible aggregation, validated structure and applicable scope",
      severity: "error" });
  const unitSubtotals = [...subtotals].map(([measurementUnitId, cfp]) =>
    ({ measurementUnitId, cfp }));
  if (!unitSubtotals.length && array(model?.measurementUnits).length === 1 && provenEmpty)
    unitSubtotals.push({ measurementUnitId: model.measurementUnits[0].measurementUnitId, cfp: 0 });
  let combined = null;
  if (mode === "single-unit" && unitSubtotals.length === 1) combined = unitSubtotals[0].cfp;
  if (mode === "compatible-components" && safe) {
    const excludedIds = new Set(array(model?.aggregation?.internalPairs)
      .flatMap(p => [p.exitMovementId, p.entryMovementId]));
    combined = perProcess.reduce((sum, p) => sum + p.cfp, 0) - excludedIds.size;
  }
  const status = fullAllowed ? "Complete" :
    countedCount ? "Partial" : "Diagnostic only";
  return {
    schemaVersion: SCHEMA_VERSION,
    methodVersion: "COSMIC Measurement Manual v5.0",
    status,
    diagnostics,
    mechanicallyValidated: !diagnostics.some(d => d.severity === "error"),
    perProcess,
    unitSubtotals,
    measuredSubtotal: safe ? combined : null,
    overallSize: fullAllowed ? combined : null,
    label: fullAllowed ? "Overall size: " + combined + " CFP (v5.0)" :
      countedCount && safe && combined !== null ? "Measured subtotal: " + combined +
      " CFP (v5.0); Overall size: Not determined" :
      "Overall size: Not determined"
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [input, workspaceRoot] = process.argv.slice(2);
  if (!input || !workspaceRoot) {
    process.stderr.write("Usage: node cosmic-validator.mjs <temporary-model.json> <workspace-root>\n");
    process.exitCode = 2;
  } else {
    try {
      const model = JSON.parse(readFileSync(resolve(input), "utf8"));
      const result = calculate(model, { workspaceRoot });
      process.stdout.write(JSON.stringify(result, null, 2) + "\n");
      if (!result.mechanicallyValidated) process.exitCode = 1;
    } catch (error) {
      process.stderr.write("COSMIC validator failure: " + error.message + "\n");
      process.exitCode = 2;
    }
  }
}
