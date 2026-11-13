# COSMIC v5.0 measurement guide — Product Intent only

Normative source: [COSMIC Measurement Manual v5.0 (combined September 2024)](https://cosmic-sizing.org/wp-content/uploads/2022/11/Combination-of-Parts-v5.0-Sep-2024.pdf). Part 1 defines the rules; Part 2 clarifies edge cases. This guide is packaged with the `idd-intent-cosmic` analytical skill. **No JSON model, executable validator, Node.js, custom orchestration runtime or implementation code is needed.** The separate `cosmic-delegation-protocol.md` requires host-native isolated readers, owner/connector reducers and process measurers and permits session-only Markdown scratch packets **outside the target repository**. All COSMIC judgments remain semantic LLM self-review, not independent certification.

## COSMIC rules

- **Strategy — Part 1 Rules 1–9:** define purpose, documented FUR scope, software piece/boundary, functional users, one layer, decomposition and functional-process granularity. Functional users may be human, external software/hardware or clock (relative to the chosen boundary). Logical persistent storage is inside the boundary, not an external functional user.
- **Processes — Rule 10 (including 10c):** cohesive response to a real triggering event, starting with an Entry from a functional user and including at least one Exit or Write. Count all required outcomes and alternatives. A retry, callback, UI control, internal component or tool invocation is not automatically another functional process.
- **Objects and groups — Rule 11:** a data group collects attributes about an Object of Interest (OOI) meaningful to the functional user. It is not each class, field, table, DTO or screen row. Multiple groups for one OOI need distinct functional meaning supported by FUR.
- **Movements — Rules 12–20:** E is data entering from an external functional user, X is data leaving to one, R reads logical persistent storage, W writes it (including deletion). Validation, transformation, formatting and temporary memory do not count. An external clock event may provide an E; merely asking for the time is not automatically E.
- **Repeated movements — Part 2 guidance:** a type/data-group/recipient combination normally counts once per functional process. A repeated instance can count separately only with evidence for a distinct required movement, not just because it happens multiple times.
- **Errors and confirmations — Part 2 guidance:** all alternative error/confirmation messages to human users normally share one special X per process; a substantive OOI response may require another ordinary X. Software/hardware errors use regular movement rules.
- **Aggregation — Rules 21–23:** CFP equals justified E+X+R+W movements. Count each current owner-defined process once; a connector to it is not another process or movement. Multiple normative definitions of the same process violate IDD single ownership and belong to Intent structure audit, not COSMIC semantic deduplication. Sum only compatible units at matching boundary/layer/decomposition/FUR granularity. Never add nested system/component sizes or unrelated layers. Report incompatible units separately.
- **Reporting — Rule 25:** state COSMIC v5.0, date, scope, sources, measurement boundaries, every process/movement, coverage, CFP calculation and limitations. CFP is not an effort, cost, schedule or implementation-complexity estimate.

## Exhaustive scope contract (whole-product requests)

The default `idd-intent-cosmic` request means **measure all current documented in-scope FUR**, not "measure whatever is convenient in one pass". A model must not end after a single easily counted process while other current spec files are still accessible. A file title or INDEX description cannot stand in for reading its complete contents.

Maintain a **separate addressable coverage ledger**, one record per in-scope numbered spec: actual path, full-text read status, source-reader packet reference, processes owned by that spec, connector links to other owners (or verified "No independent process" with a concrete reason), exact evidence anchor and cross-spec dependencies. An isolated native child reader must inspect each spec; recursively reduce bounded packet indices in fresh child contexts. Join owner keys and connector references across all groups using retained lossless evidence; do not reinterpret a connector as process redefinition or deduplicate semantics across owners. No executable validation or target-project writes.

**Finish gate:** the ledger must cover every numbered current in-scope spec, with no accessible document unattempted. Show `Full-text sources reviewed: X/Y` and this per-spec ledger in the final report. Only after passing that gate can a requested whole-product measurement be Complete or Analytical Partial. Inability to access a spec must be supported by an actual attempted read and the resulting error. Unknown functional semantics after reading are Analytical Partial; simply stopping before reading available specs is **Execution incomplete (BLOCKED)**, never a successful Partial report. Report a concrete host/context/tool limit if one truly prevented further attempts, and list the unprocessed sources. File volume or finding a supported subtotal is not itself a blocker.

A confirmed, independently measured sub-scope may have a valid CFP, but when the user requested the whole product, do not promote a single opportunistic CFP into a finished whole-product assessment. If the task is still executable, continue reading rather than returning a sample.

## Workflow for small and large projects

1. **Inventory real files.** Read README/INDEX and independently list numbered files in `.idd/intent/`. Include current specs absent from INDEX, record stale INDEX entries and inaccessible sources. README, INDEX, ADR, Spike and GLOSSARY do not independently supply missing FUR.
2. **Read full FUR through isolated spec readers.** Assign one fresh native child context to every numbered in-scope spec; it reads its own source and returns a small Markdown evidence packet, including initiating events, outcomes, groups, actors, persistence, related owners and exact source headings/quotes. Track source state in an independent ledger, keeping detailed packets retrievable outside the coordinator's context. When a spec itself exceeds context, use isolated section readers with gap-free source-range reconciliation. A pass through titles/headings is *not* full coverage. Do not finish while available sources remain unread.
3. **Assemble single-owner processes and connectors in bounded hierarchy.** Each process belongs to exactly one owning spec (a spec may own several); other specs may only reference it via `LINK`. Reduce compact owner/connector indices in fresh child contexts, grouping by area and directed dependencies, without losing original evidence. Resolve every target against its existing owner, including cross-group links. **Do not semantically deduplicate independently owned processes.** A true duplicate normative definition is a `STRUCTURE_ISSUE` for lint/structure audit, not a task to repair during measurement. Classify owned processes Measured, Needs clarification, Ambiguous, Out of scope or Not a functional process.
4. **Fix the boundary first.** Identify the software piece, external functional users and the storage boundary before assigning E/X/R/W. In distributed systems, different perspectives may produce different CFP; do not silently mix them.
5. **Establish movements and evidence.** Identify complete data-group movements, success/error alternatives and actual persistent lifecycle. Attach an existing current spec path plus a local exact heading or verbatim short quote to every Measured process **and** each of its movements. Source headings must substantively support the claim; they are not an automatic proof by themselves.
6. **Isolated measurement and counting.** After owner/link indexing, spawn a fresh process-measurer for each owner-defined process or bounded group to validate movements and source evidence. The coordinator checks every measurement, computes `CFP = E+X+R+W` and sums each distinct owner exactly once in compatible units. Optional trusted `cosmic_state.py` (or equivalent host tools) can check hashes, coverage, links, identifiers, Rule 10c structure and arithmetic, not semantic correctness. Do not assign CFP to connector edges. Report **Coupling / Change Impact** separately: confirmed inbound caller-process count, referring-spec count and justified integration/change risks; fan-in is not a COSMIC weight.
7. **Report gaps, not guesses.** If some specs could not be read after attempted access, report their exact paths, failures and implications; defensible independent CFP may still form an Analytical Partial subtotal after all other sources are processed. If accessible specs were simply left unread, report an explicitly unfinished execution with its real blocker, **not** a completed Partial with a sample CFP. An unknown reaction blocks that process, not necessarily every other process. An unknown initial boundary may block all CFP.

## Mandatory analytical self-review

- [ ] Declared purpose/scope and software boundary are explicit; a whole-product request is not silently narrowed.
- [ ] Full-text source coverage is X/Y, with an independently maintained, evidence-based per-spec ledger and addressable isolated-reader packets. Every accessible spec has actually been read by its reader, or a real host/tool blocker and remaining files are explicitly reported; INDEX discrepancies are recorded.
- [ ] All triggering events have a **single normative owner** and its processes; cross-spec references are LINK records, not extra candidates/movements. Every reducer input packet is partitioned once, every process owner preserved, and cross-group LINK targets resolved or marked unresolved. True duplicate definitions are reported to structure audit, not merged.
- [ ] Every Measured process has a triggering E from its functional user and at least one X or W (Rule 10c).
- [ ] Each named functional user is external to the selected boundary; logical persistent storage is not itself a user.
- [ ] Every movement identifies a data group, OOI (unless special human-message X), type, direction/user or persistent store, and exact FUR source path plus heading/quote.
- [ ] Every R/W requires logical persistence supported by FUR, not a guess from implementation or temporary memory.
- [ ] Duplicate movements, repeated invocation links, retries and human error alternatives do not inflate CFP. Source-only references and confirmed caller-process references are distinguished in the separate non-COSMIC coupling summary.
- [ ] All required reactions are included in Measured processes; ambiguous movements remain uncounted.
- [ ] Independent process-measurer agents reviewed the owner-defined processes; mechanically recount E/X/R/W where possible and verify `CFP = E + X + R + W`, one result per owner key.
- [ ] Sum only distinct processes in compatible boundaries; do not sum a parent and its components.
- [ ] Complete is claimed only when all **documented** in-scope FUR are read and mapped, aggregation is valid and COSMIC is applicable.
- [ ] The report distinguishes method applicability (e.g., computational algorithms) from missing FUR and discloses **LLM semantic self-review** rather than implying independent validation.

These checks improve traceability but do not guarantee semantic correctness. An unsupported claim is removed from Measured status, not silently accepted.

## Result categories

| Status | When to use | What to publish |
| --- | --- | --- |
| Complete | All declared documented FUR in scope read and reconciled, compatible units and applicable method | `Overall size: N CFP (v5.0)` |
| Analytical Partial | After attempting the full declared scope, at least one complete process is measured but other requirements are inaccessible, unresolved or method applicability limited | `Measured subtotal: N CFP (v5.0)`; `Overall size: Not determined` |
| Diagnostic only | Source analysis is completed/attempted but no defensibly measured processes or stable boundary is available | Blockers and no numeric CFP |
| Execution incomplete (BLOCKED) | An explicit host/tool/context limit prevents completing analysis of accessible current specs | Actual blocker, full-text X/Y, remaining paths; no completed measurement claim |
| Proven empty | All current documented requirements in the declared scope prove it contains no functional processes | Explicitly justified `0 CFP (v5.0)` |

Complete describes the *documented scope*, not the complete real-world product. A partial subtotal is never extrapolated into a whole-product size.

## Examples

**C-Reg — Add a Professor**, from COSMIC v5.0 Part 3d §4.1: a registrar enters professor details (E); the application reads existing professor details (R), writes professor details (W), and reports a human error/confirmation (X). For **this documented example**, total is **4 CFP (v5.0)**. This does not impose a four-point pattern on other create operations.

**Partially documented product:** Add a Professor is measured at 4 CFP, but another feature, Synchronize Payment, lacks a clear triggering event and boundary. Report `Measured subtotal: 4 CFP (v5.0)` and `Overall size: Not determined`.

**Algorithm-heavy scope:** operator input E and result X may be supported while optimization/computation itself contributes no CFP. If this means important functionality is not represented by functional size, report method applicability Limited; small CFP does not imply low implementation complexity.

## Report contract

Produce a compact separate reconciliation handoff (scope, X/Y, unique process/status IDs, source-packet references and unresolved ownership) **and** a detailed user Markdown report. The report provides Measurement Strategy (date, scope, real inventory, full-text X/Y and per-spec coverage ledger, exclusions, boundaries, layer, functional users), unique candidate process inventory (with statuses and cross-group reconciliation), and for each Measured process a table of every movement with type, data group, OOI, functional user or persistence, current spec path and exact heading/quote. Show E/X/R/W counts, per-process CFP, compatible unit subtotals and a complete overall total only when justified. State unresolved questions and absence of independent mechanical validation. No executable validation contract exists.
