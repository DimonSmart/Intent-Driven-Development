import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { calculate, validate } from "../../src/canonical/methodology/cosmic-validator.mjs";

const srcPath = ".idd/intent/IDD-0001.spec-product.md";
const text = "# Product\nThe user starts the function, the software moves required data and responds.\n";
const evidence = [{ path: srcPath, quote: "The user starts the function" }];
const store = mkdtempSync(join(tmpdir(), "idd-cosmic-tests-"));
mkdirSync(join(store, ".idd", "intent"), { recursive: true });
writeFileSync(join(store, srcPath), text);
test.after(() => rmSync(store, { recursive: true, force: true }));

function model(spec = ["E", "W"], opts = {}) {
  const users = opts.users ?? [{id:"User",kind:"human"}];
  const unit = { measurementUnitId:"U1", softwarePiece:"App", boundary:"App boundary",
    layer:"Application", decompositionLevel:"whole", furGranularity:"functional process",
    scope:"Selected documented scope", functionalUsers:users };
  const moves = spec.map((value, i) => {
    const kind = typeof value === "string" ? value : value.type;
    const entry = { id:"M"+(i+1), functionalProcessId:"P1", measurementUnitId:"U1",
      type:kind, dataGroup: typeof value === "string" ? "Group" : value.group,
      justification:"FUR requires this movement", evidence,
      evidenceType: typeof value === "string" || !value.special ?
        "regular FUR" : "error-confirmation special rule",
      semanticStatus:"Supported by FUR" };
    if (!value.special) entry.objectOfInterest = "Object";
    if (kind === "E" || kind === "X") {
      entry.direction = kind === "E" ? "in" : "out";
      entry.interactingFunctionalUser = typeof value === "string" ?
        "User" : (value.user ?? "User");
    } else entry.persistenceJustification = "Stored beyond functional process lifetime";
    return entry;
  });
  const p = { id:"P1", measurementUnitId:"U1", name:"Operation",
    functionalUser:opts.triggerUser ?? "User", triggeringEvent:"User starts operation",
    triggeringEntryId:"M1", status:"Measured", semanticStatus:"Supported by FUR",
    semanticCompletenessJustification:"Input and required full outcome represented",
    evidence, dataMovements:moves };
  return { schemaVersion:1, methodVersion:"COSMIC Measurement Manual v5.0",
    purpose:"Measure selected FUR", scope:"Current documented FUR",
    measurementDate:"2026-11-13", requestedFullTotal:true,
    measurementUnits:[unit], sourceDocuments:[{path:srcPath,read:true}],
    coverage:{documentationCoverage:{inventoryComplete:true,unreadPaths:[]},
      functionalCoverage:{processInventoryComplete:true},
      methodApplicability:{status:"Applicable"},
      coverageItems:[{name:"Product",status:"covered"}]},
    processes:[p], aggregation:{mode:"single-unit"} };
}

const calc = x => calculate(x, {workspaceRoot:store});
const errors = x => calc(x).diagnostics.filter(d=>d.severity==="error").map(d=>d.code);
const clone = value => structuredClone(value);

test("E + W is 2 CFP", () => {const r=calc(model()); assert.equal(r.overallSize,2);});
test("E + R + X is 3 CFP", () => {const r=calc(model(["E","R","X"]));assert.equal(r.overallSize,3);});
test("independent processes sum", () => {
  const m=model(), p=clone(m.processes[0]);
  p.id="P2";p.triggeringEntryId="M3";p.dataMovements.forEach((v,i)=>{v.id="M"+(i+3);v.functionalProcessId="P2";});
  m.processes.push(p);assert.equal(calc(m).overallSize,4);
});
test("triggering Entry absent is blocked",()=>{const m=model();m.processes[0].triggeringEntryId="missing";assert.ok(errors(m).includes("TRIGGERING_ENTRY"));});
test("E alone and E+R fail Rule 10c",()=>{for(const s of [["E"],["E","R"]])assert.ok(errors(model(s)).includes("RULE_10C"));});
test("data group absent blocks",()=>{const m=model();m.processes[0].dataMovements[1].dataGroup="";assert.ok(errors(m).includes("DATA_GROUP"));});
test("unjustified repeated movement is rejected",()=>{const m=model(["E","W","W"]);assert.ok(errors(m).includes("DUPLICATE_MOVEMENT"));});
test("justified duplicate allowed",()=>{const m=model(["E","W","W"]);m.processes[0].dataMovements[2].distinctOccurrenceReason="FUR requires two separately identified stored groups";assert.equal(calc(m).overallSize,3);});
test("distinct data groups valid",()=>{const m=model([{type:"E",group:"Request"},{type:"W",group:"Order"},{type:"W",group:"Audit"}]);assert.equal(calc(m).overallSize,3);});
test("claimed CFP never trusted",()=>{const m=model();m.processes[0].claimedCfp=99;assert.ok(errors(m).includes("CLAIMED_CFP"));assert.equal(calc(m).overallSize,null);});
test("unmeasured candidate allows only subtotal",()=>{const m=model();m.processes.push({id:"P2",measurementUnitId:"U1",name:"Unknown",status:"Needs clarification",uncertainty:"Missing event"});const r=calc(m);assert.equal(r.status,"Partial");assert.equal(r.measuredSubtotal,2);assert.equal(r.overallSize,null);});
test("partial with full requested reports blocked",()=>{const m=model();m.processes.push({id:"P2",measurementUnitId:"U1",name:"Unknown",status:"Ambiguous",uncertainty:"Boundary unclear"});assert.ok(errors(m).includes("FULL_TOTAL_BLOCKED"));});
test("different unit boundaries cannot be freely summed",()=>{const m=model();m.measurementUnits.push({...clone(m.measurementUnits[0]),measurementUnitId:"U2",boundary:"Service"});m.aggregation={mode:"single-unit"};assert.ok(errors(m).includes("AGGREGATION"));});
test("different decomposition levels block composition",()=>{const m=model();m.measurementUnits[0].parentBoundary="System";m.measurementUnits.push({...clone(m.measurementUnits[0]),measurementUnitId:"U2",boundary:"Other",decompositionLevel:"component"});m.aggregation={mode:"compatible-components",justification:"Combine",processMergeEvidence:"Mapped",internalPairs:[]};assert.ok(errors(m).includes("AGGREGATION_UNIT"));});
test("different FUR granularity blocks composition",()=>{const m=model();m.measurementUnits[0].parentBoundary="System";m.measurementUnits.push({...clone(m.measurementUnits[0]),measurementUnitId:"U2",boundary:"Other",furGranularity:"goal"});m.aggregation={mode:"compatible-components",justification:"Combine",processMergeEvidence:"Mapped",internalPairs:[]};assert.ok(errors(m).includes("AGGREGATION_UNIT"));});
test("bad source path rejected",()=>{const m=model();m.processes[0].evidence[0].path=".idd/intent/IDD-9999.spec-missing.md";assert.ok(errors(m).includes("EVIDENCE_SOURCE"));});
test("missing actual source rejected",()=>{const m=model();m.sourceDocuments[0].path=".idd/intent/IDD-1111.spec-unavailable.md";assert.ok(errors(m).includes("SOURCE_UNVERIFIED"));});
test("unresolved exact quote rejected",()=>{const m=model();m.processes[0].evidence=[{path:srcPath,quote:"Does not exist"}];assert.ok(errors(m).includes("EVIDENCE_QUOTE"));});
test("duplicate process IDs rejected",()=>{const m=model();m.processes.push({...clone(m.processes[0]),dataMovements:[]});assert.ok(errors(m).includes("PROCESS_ID"));});
test("duplicate movement IDs rejected",()=>{const m=model();m.processes[0].dataMovements[1].id="M1";assert.ok(errors(m).includes("MOVEMENT_ID"));});
test("proven empty scope can be 0 CFP",()=>{const m=model();m.processes=[];m.sourceDocuments=[];m.coverage.documentationCoverage.documentedScopeProvenEmpty=true;assert.equal(calc(m).overallSize,0);});
test("empty unknown scope cannot return 0",()=>{const m=model();m.processes=[];m.sourceDocuments=[];assert.equal(calc(m).overallSize,null);});
test("invalid movement type rejected",()=>{const m=model();m.processes[0].dataMovements[1].type="DELETE";assert.ok(errors(m).includes("MOVEMENT_TYPE"));});
test("human error message special OOI exemption",()=>{const m=model(["E",{type:"X",group:"Error",special:true}]);assert.equal(calc(m).overallSize,2);});
test("two human error codes collapse to one Exit",()=>{const m=model(["E",{type:"X",group:"Error 1",special:true},{type:"X",group:"Error 2",special:true}]);assert.ok(errors(m).includes("DUPLICATE_MOVEMENT"));});
test("special X plus OOI X both count",()=>{const m=model(["E",{type:"X",group:"Error",special:true},{type:"X",group:"Result"}]);assert.equal(calc(m).overallSize,3);});
test("special rule cannot target software user",()=>{const m=model(["E",{type:"X",group:"Error",special:true,user:"Service"}],{users:[{id:"User",kind:"human"},{id:"Service",kind:"software"}]});assert.ok(errors(m).includes("ERROR_MESSAGE"));});
test("different functional users may need distinct exits",()=>{const m=model(["E",{type:"X",group:"Result",user:"User"},{type:"X",group:"Result",user:"Service"}],{users:[{id:"User",kind:"human"},{id:"Service",kind:"software"}]});assert.equal(calc(m).overallSize,3);});
test("invocation count does not multiply CFP",()=>{const m=model();m.processes[0].invocations=500;assert.equal(calc(m).overallSize,2);});
test("temporary memory not persistence",()=>{const m=model();m.processes[0].dataMovements[1].persistenceJustification="";assert.ok(errors(m).includes("PERSISTENCE"));});
test("unread source blocks full total",()=>{const m=model();m.coverage.documentationCoverage.unreadPaths=[".idd/intent/IDD-0002.spec-other.md"];assert.equal(calc(m).overallSize,null);});
test("unread area blocks full total",()=>{const m=model();m.coverage.coverageItems.push({name:"Other",status:"unknown"});assert.equal(calc(m).overallSize,null);});
test("limited applicability prevents total but allows partial",()=>{const m=model();m.coverage.methodApplicability={status:"Limited",reason:"Complex audio"};assert.equal(calc(m).status,"Partial");assert.equal(calc(m).overallSize,null);});
test("independent unit results without unjustified combined size",()=>{const m=model();m.aggregation={mode:"separate"};const r=calc(m);assert.equal(r.overallSize,null);assert.equal(r.unitSubtotals[0].cfp,2);});
test("wrong version and measurement metadata rejected",()=>{const m=model();m.methodVersion="v4";m.purpose="";assert.ok(errors(m).includes("METHOD_VERSION"));assert.ok(errors(m).includes("REQUIRED"));});
test("adversarial source path does not traverse root",()=>{const m=model();m.sourceDocuments[0].path="../../secret.md";assert.ok(errors(m).includes("SOURCE_PATH"));});
test("error to different human users cannot duplicate special X",()=>{const m=model(["E",{type:"X",group:"E1",special:true,user:"User"},{type:"X",group:"E2",special:true,user:"Other"}],{users:[{id:"User",kind:"human"},{id:"Other",kind:"human"}]});assert.ok(errors(m).includes("DUPLICATE_MOVEMENT"));});
test("runtime CLI validates actual workspace and never writes project",()=>{
  const input=join(store,"measurement.json"), original=readFileSync(join(store,srcPath),"utf8");
  writeFileSync(input,JSON.stringify(model()));
  const run=spawnSync("node",[resolve("src/canonical/methodology/cosmic-validator.mjs"),input,store],{encoding:"utf8"});
  assert.equal(run.status,0,run.stderr);
  assert.equal(JSON.parse(run.stdout).overallSize,2);
  assert.equal(readFileSync(join(store,srcPath),"utf8"),original);
});


test("official COSMIC v5.0 examples preserve published movement structures and golden CFP", () => {
  const fixture=JSON.parse(readFileSync("tests/Idd.Cosmic.Tests/golden-cases.json","utf8"));
  assert.equal(fixture.cases.length,3);
  const expected = {"CReg-add-professor":4,"Rice-cooker-start":3,"Cleaning-display-list":4};
  for (const item of fixture.cases) {
    assert.equal(item.expectedCfp,expected[item.id]);
    assert.ok(item.section && fixture.reference.part && fixture.reference.partEdition);
    const docPath=".idd/intent/"+item.document;
    writeFileSync(join(store,docPath),item.fur);
    const m=model(item.movements.map(x=>({type:x.type,group:x.group,user:x.user,special:x.special})),
      {users:item.functionalUsers,triggerUser:item.triggerUser});
    m.sourceDocuments=[{path:docPath,read:true}];
    m.processes[0].evidence=[{path:docPath,section:item.fur.split("\n")[0]}];
    for (const move of m.processes[0].dataMovements) {
      move.evidence=[{path:docPath,section:item.fur.split("\n")[0]}];
      if (move.type === "R" || move.type === "W")
        move.persistenceJustification="Persistent data is required by documented FUR";
    }
    assert.deepEqual(m.processes[0].dataMovements.map(x=>[x.type,x.dataGroup]),
      item.movements.map(x=>[x.type,x.group]));
    const result=calc(m);
    assert.equal(result.mechanicallyValidated,true,JSON.stringify(result.diagnostics));
    assert.equal(result.overallSize,item.expectedCfp,item.id);
  }
});
