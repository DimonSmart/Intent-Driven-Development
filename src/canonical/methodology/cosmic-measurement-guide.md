# COSMIC v5.0 measurement guide — Product Intent only

Normative source: [COSMIC Measurement Manual v5.0 (combined September 2024)](https://cosmic-sizing.org/wp-content/uploads/2022/11/Combination-of-Parts-v5.0-Sep-2024.pdf). Part 1 defines the rules; Part 2 clarifies edge cases. This guide is packaged with the `idd-intent-cosmic` analytical skill. **No JSON model, executable validator, Node.js, shell command, temporary file, or implementation code is needed.** All checks are semantic self-review by the LLM, not independent certification.

## COSMIC rules

- **Strategy — Part 1 Rules 1–9:** define purpose, documented FUR scope, software piece/boundary, functional users, one layer, decomposition and functional-process granularity. Functional users may be human, external software/hardware or clock (relative to the chosen boundary). Logical persistent storage is inside the boundary, not an external functional user.
- **Processes — Rule 10 (including 10c):** cohesive response to a real triggering event, starting with an Entry from a functional user and including at least one Exit or Write. Count all required outcomes and alternatives. A retry, callback, UI control, internal component or tool invocation is not automatically another functional process.
- **Objects and groups — Rule 11:** a data group collects attributes about an Object of Interest (OOI) meaningful to the functional user. It is not each class, field, table, DTO or screen row. Multiple groups for one OOI need distinct functional meaning supported by FUR.
- **Movements — Rules 12–20:** E is data entering from an external functional user, X is data leaving to one, R reads logical persistent storage, W writes it (including deletion). Validation, transformation, formatting and temporary memory do not count. An external clock event may provide an E; merely asking for the time is not automatically E.
- **Repeated movements — Part 2 guidance:** a type/data-group/recipient combination normally counts once per functional process. A repeated instance can count separately only with evidence for a distinct required movement, not just because it happens multiple times.
- **Errors and confirmations — Part 2 guidance:** all alternative error/confirmation messages to human users normally share one special X per process; a substantive OOI response may require another ordinary X. Software/hardware errors use regular movement rules.
- **Aggregation — Rules 21–23:** CFP equals justified E+X+R+W movements. Count each unique process once; avoid double-counting shared FUR. Sum only compatible units at matching boundary/layer/decomposition/FUR granularity. Never add nested system/component sizes or unrelated layers. Report incompatible units separately.
- **Reporting — Rule 25:** state COSMIC v5.0, date, scope, sources, measurement boundaries, every process/movement, coverage, CFP calculation and limitations. CFP is not an effort, cost, schedule or implementation-complexity estimate.

## Workflow for small and large projects

1. **Inventory real files.** Read README/INDEX and independently list numbered files in `.idd/intent/`. Include current specs absent from INDEX, record stale INDEX entries and inaccessible sources. README, INDEX, ADR, Spike and GLOSSARY do not independently supply missing FUR.
2. **Read full FUR.** Work through all current in-scope specs, in manageable batches when there are many files. For each, identify initiating events, outcomes, data groups, actors, persistence, relationships with other owners and exact source headings/quotes. Keep temporary notes in the conversation only. A pass through titles/headings is *not* full coverage.
3. **Reconcile cross-spec behavior.** Create a unique process inventory: merge multiple specs that describe one cohesive process; split a spec if it defines independent triggering events. Mark all remaining candidates as Measured, Needs clarification, Ambiguous, Out of scope or Not a functional process.
4. **Fix the boundary first.** Identify the software piece, external functional users and the storage boundary before assigning E/X/R/W. In distributed systems, different perspectives may produce different CFP; do not silently mix them.
5. **Establish movements and evidence.** Identify complete data-group movements, success/error alternatives and actual persistent lifecycle. Attach an existing current spec path plus a local exact heading or verbatim short quote to every Measured process **and** each of its movements. Source headings must substantively support the claim; they are not an automatic proof by themselves.
6. **Review and count.** Apply the checklist, independently re-count E/X/R/W in the displayed table for each supported process, reconcile ownership and total only compatible measurement units. Do not depend on a machine-generated CFP number or a tool execution.
7. **Report gaps, not guesses.** If some specs were unread, report the unmeasured scope, but preserve defensible independent process subtotals. An unknown reaction blocks that process, not necessarily every other process. An unknown initial boundary may block all CFP.

## Mandatory analytical self-review

- [ ] Declared purpose/scope and software boundary are explicit; a whole-product request is not silently narrowed.
- [ ] All numbered current specs in scope are accounted for as read, unread or inaccessible; INDEX discrepancies are recorded.
- [ ] All triggering events in read FUR have candidate processes, including required alternatives and cross-spec ownership.
- [ ] Every Measured process has a triggering E from its functional user and at least one X or W (Rule 10c).
- [ ] Each named functional user is external to the selected boundary; logical persistent storage is not itself a user.
- [ ] Every movement identifies a data group, OOI (unless special human-message X), type, direction/user or persistent store, and exact FUR source path plus heading/quote.
- [ ] Every R/W requires logical persistence supported by FUR, not a guess from implementation or temporary memory.
- [ ] Duplicate movements, inter-spec overlaps, retries and human error alternatives do not inflate CFP.
- [ ] All required reactions are included in Measured processes; ambiguous movements remain uncounted.
- [ ] Independently recount the displayed E/X/R/W rows for each process and verify `CFP = E + X + R + W`.
- [ ] Sum only distinct processes in compatible boundaries; do not sum a parent and its components.
- [ ] Complete is claimed only when all **documented** in-scope FUR are read and mapped, aggregation is valid and COSMIC is applicable.
- [ ] The report distinguishes method applicability (e.g., computational algorithms) from missing FUR and discloses **LLM semantic self-review** rather than implying independent validation.

These checks improve traceability but do not guarantee semantic correctness. An unsupported claim is removed from Measured status, not silently accepted.

## Result categories

| Status | When to use | What to publish |
| --- | --- | --- |
| Complete | All declared documented FUR in scope read and reconciled, compatible units and applicable method | `Overall size: N CFP (v5.0)` |
| Partial | At least one complete process is measured but other requirements are unread, unresolved or method applicability limited | `Measured subtotal: N CFP (v5.0)`; `Overall size: Not determined` |
| Diagnostic only | No defensibly measured processes or no defensible initial software boundary | Blockers and no numeric CFP |
| Proven empty | All current documented requirements in the declared scope prove it contains no functional processes | Explicitly justified `0 CFP (v5.0)` |

Complete describes the *documented scope*, not the complete real-world product. A partial subtotal is never extrapolated into a whole-product size.

## Examples

**C-Reg — Add a Professor**, from COSMIC v5.0 Part 3d §4.1: a registrar enters professor details (E); the application reads existing professor details (R), writes professor details (W), and reports a human error/confirmation (X). For **this documented example**, total is **4 CFP (v5.0)**. This does not impose a four-point pattern on other create operations.

**Partially documented product:** Add a Professor is measured at 4 CFP, but another feature, Synchronize Payment, lacks a clear triggering event and boundary. Report `Measured subtotal: 4 CFP (v5.0)` and `Overall size: Not determined`.

**Algorithm-heavy scope:** operator input E and result X may be supported while optimization/computation itself contributes no CFP. If this means important functionality is not represented by functional size, report method applicability Limited; small CFP does not imply low implementation complexity.

## Report contract

Provide Measurement Strategy (date, scope, real inventory, exclusions, boundaries, layer, functional users), unique candidate process inventory (with statuses), and for each Measured process a table of every movement with type, data group, OOI, functional user or persistence, current spec path and exact heading/quote. Show E/X/R/W counts, per-process CFP, compatible unit subtotals and a complete overall total only when justified. State unresolved questions and absence of independent mechanical validation. No executable validation contract exists.
