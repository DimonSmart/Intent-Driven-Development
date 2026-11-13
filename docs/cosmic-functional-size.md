# COSMIC Functional Size Measurement (IDD)

`idd-intent-cosmic` is a **read-only analytical skill** for measuring COSMIC v5.0 Function Points (CFP) from current `.idd/intent/` Functional User Requirements (FUR), independently of `idd-intent-estimate` (qualitative Business Complexity) and without an implemented product.

## Run

Ask **"Measure the current documented product in COSMIC v5.0 CFP"**, **"Count COSMIC Function Points for the order module"** or invoke `idd-intent-cosmic` directly. Optional area/IDD IDs restrict the requested scope, but all specs that jointly define its functional processes must be read.

**Native isolated child agents are required.** A fresh reader analyzes each spec, reducers combine compact owner/connector indices, and isolated process-measurer agents validate E/X/R/W within bounded contexts. There is no mandatory runtime, custom LLM engine, structured nested JSON, or target-project file edit. An optional bundled Python 3 standard-library helper provides deterministic source/run-state fingerprints, reference checks and CFP arithmetic. When supported, session-only Markdown packets and the separate coverage ledger can be stored in scratch **outside** the repository. The installed skill bundles `references/cosmic-measurement-guide.md`, `references/cosmic-delegation-protocol.md`, `references/cosmic-execution-state.md` and optional `assets/cosmic-state/cosmic_state.py`. Without native child-agent isolation the result must be `Execution incomplete (BLOCKED)`, not a silently downgraded same-context run.

## Measurement procedure

1. Inventory the actual numbered files and reconcile `INDEX.md`. Read *full* current in-scope spec contents rather than just headings, including related owners. ADRs clarify accepted context but do not introduce missing FUR.
2. Define purpose, scope, software boundary, layer, decomposition, functional-process granularity and external functional users.
3. Dispatch **every current in-scope spec** to a fresh native child reader. Each reader returns a small Markdown packet (source/read range, owner-defined PROCESS, LINK references to other owners, evidence-backed MOVE and GAP records). A process is defined once in its owning IDD spec; references to it are connectors, not other definitions. If a single spec is too large, isolate its sections and reconcile gap-free source coverage.
4. Keep a **separate per-spec coverage ledger** (actual path, read state, packet reference, candidates/no-process rationale, source anchor and errors), independent of any summaries. Source-backed packets must stay retrievable.
5. Merge small groups of **owner and connector indices** through fresh reducer agents. Use no more than approximately 6,000 input tokens or 20% of usable context per handoff, whichever is smaller, including reserve for instructions and output in the overall budget. Recurse as needed; partition every packet exactly once at each level, preserve evidence outside the compressed summary and join LINK targets across groups. Do not semantically deduplicate processes across different owners; a true duplicate normative definition belongs to Intent lint/structure audit.
6. Resolve process references against their canonical owning specs; unresolved links stay explicit. Let separate fresh process-measurer agents evaluate each owner process or bounded group under the declared boundary. Do not collapse owner processes by name similarity or sum per-file CFP.
7. Assign Entry (E), Exit (X), Read (R) and Write (W) only for supported data movements. Every Measured process needs an initiating E and at least one X or W (Rule 10c). Cite the actual current spec and short exact quote/heading for **every process and movement**.
8. Independently re-count `CFP = E + X + R + W` for each **owner-defined** compatible process. Optionally use trusted infrastructure-only checks for source hashes, packet integrity, identifiers and CFP arithmetic; these never certify COSMIC semantics. Report coverage, totals/subtotals only if justified, exclusions and applicability limitations. Return the complete **user-facing Markdown report** separately from the compact handoff used by child agents.

Source interpretation, owner-based process measurement and qualitative coupling judgments remain **semantic LLM analysis**, not independent mechanical validation. The new protocol is implemented as prompt-level native-agent orchestration, not a hard-coded deterministic pipeline. No tool can infer absent requirements. Full-product size requires reading all declared documented FUR, not merely inventorying file names. Before a finished report, the skill must show `Full-text sources reviewed: X/Y` and the per-spec ledger. An available spec simply left unread is an unfinished execution, **not an acceptable reason to return an otherwise successful Partial result**. Keep processing unless a concrete tool/context limit prevents it; when blocked, report that limit and the unread paths.

## Single owner and non-COSMIC coupling impact

IDD assumes one canonical owning specification for each functional process (one spec may own several). Other specs may reference it through `LINK` connectors. Links do not define additional processes, are not additional COSMIC data movements, and do not increase CFP. A genuine duplicate normative definition is reported for Intent structure lint/audit, not repaired or semantically deduplicated in the measurement workflow.

As a separate **Coupling / Change Impact** assessment, show confirmed distinct *caller process* fan-in and *referring-specification* counts independently, together with verified failure/dependency risks. For example, a 3-CFP operation referenced by one process and one referenced by 100 processes remain 3 CFP each; the latter may have greater change/integration impact. The counts have no automatic conversion to effort or CFP. Do not pretend that 100 document mentions prove 100 independent callers.

## Resume and bounded recovery

A run may persist source and evidence packet hashes, measurement strategy, task statuses and directed connector references in safe scratch **outside** the target project, then resume only compatible and current results. Changed owner specs invalidate dependent results; a changed caller spec can change the coupling summary without changing the callee's normative CFP. A valid child packet must exist before its task becomes Completed. On malformed or truncated responses, allow at most one focused correction and one smaller fresh-context retry; do not silently drop failed records. If the source inventory or measurement strategy changes, start a new run rather than mixing incompatible results.

An **optional, non-mandatory** support script is packaged at `assets/cosmic-state/cosmic_state.py` to fingerprint inputs/strategy, keep a safe whole-scope checkpoint, mechanically verify exact owner/link IDs and CFP arithmetic, and invalidate known dependents. It requires Python 3 when invoked, but the COSMIC skill itself does not require Python. It does **not** prove source semantics or that a reader really inspected each range. In hosts without safe writable scratch, transparent incomplete execution is preferable to a false claim of resumability.

## Complete, Partial, Diagnostic only

- **Complete** — all declared *documented* FUR were read and mapped; measurement boundaries and aggregation are justified; method applicability established. Publish `Overall size: N CFP (v5.0)`. This does not guarantee that all real-world requirements are documented.
- **Analytical Partial** — after processing or genuinely attempting the entire requested source scope, at least one complete, well-supported independent process is measured but some requirements remain unavailable/ambiguous or methodology applicability limited. Publish `Measured subtotal: N CFP (v5.0)`, `Overall size: Not determined`. Do not extrapolate.
- **Diagnostic only** — the source analysis was performed but there is no defensibly measured process or initial stable boundary; report blockers without a numeric count.
- **Execution incomplete (BLOCKED)** — accessible sources remain unprocessed due to a genuine host/tool/context blocker. Report the real blocker, read X/Y and the remaining paths; do not present a one-process subtotal as a finished whole-product measurement.
- **0 CFP** — only for an expressly evidenced empty documented scope, never as a substitute for missing analysis.

Separate different software pieces/layers or incompatible measurement units rather than adding system and component sizes. A partial result must show what was not measured.

## COSMIC examples

The official [COSMIC Measurement Manual v5.0, combined September 2024](https://cosmic-sizing.org/wp-content/uploads/2022/11/Combination-of-Parts-v5.0-Sep-2024.pdf), Part 3d §4.1, describes the C-Reg Add a Professor process:

| Movement | Supported data movement |
| --- | --- |
| E | Professor details from registrar |
| R | Existing professor details from persistent storage |
| W | Professor details to persistent storage |
| X | Error/confirmation to registrar |

This **specific** documented process is **4 CFP (v5.0)**, not a generic creation template. If another function such as Synchronize Payment lacks a supported event/boundary, report 4 CFP as the measured subtotal and the overall size as undetermined.

COSMIC measures movements, not internal algorithm or AI complexity. A small CFP number does not imply easy development or predictable effort.

## Sources and evaluation

- `src/canonical/skills/idd-intent-cosmic.md` — canonical read-only skill.
- `src/canonical/methodology/cosmic-measurement-guide.md` — rule mapping and manual analytical self-review.
- `src/canonical/methodology/cosmic-delegation-protocol.md` — owner readers, LINK connectors, process measurers and bounded reduction.
- `src/canonical/methodology/cosmic-execution-state.md` — fingerprints, resumability, invalidation and retry limits.
- `src/canonical/cosmic-state-support/` — optional deterministic helper with isolated mechanics tests.
- `tests/Idd.Generation.Tests/CosmicFunctionalSizeContractTests.cs` — generated skill/metadata packaging and read-only tooling requirements.
- `tests/Idd.Intent.LiveTests/Scenarios/cosmic-cases.json` and `CosmicFunctionalSizeLiveTests.cs` — optional **real LLM** semantic checks of process and movement inventories; opt in via `IDD_RUN_LIVE_COSMIC_EVALS=1`. Not run in regular CI.
- [IDD scope diagnostic example](examples/cosmic-idd-diagnostic.md).

Also see the [official measurement process](https://cosmic-sizing.org/cosmic-sizing/intro/measurement-process/). The skill is not COSMIC certification, an implementation audit or a cost/schedule forecast.
