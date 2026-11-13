# COSMIC Functional Size Measurement (IDD)

`idd-intent-cosmic` is a **read-only analytical skill** for measuring COSMIC v5.0 Function Points (CFP) from current `.idd/intent/` Functional User Requirements (FUR), independently of `idd-intent-estimate` (qualitative Business Complexity) and without an implemented product.

## Run

Ask **"Measure the current documented product in COSMIC v5.0 CFP"**, **"Count COSMIC Function Points for the order module"** or invoke `idd-intent-cosmic` directly. Optional area/IDD IDs restrict the requested scope, but all specs that jointly define its functional processes must be read.

**No Node.js validator, execution permission, shell command, temporary JSON/model file or additional runtime is needed.** The skill reads current Intent documents and returns a Markdown report; it never edits project files or executes product code. The installed skill has one bundled reference: `references/cosmic-measurement-guide.md`.

## Measurement procedure

1. Inventory the actual numbered files and reconcile `INDEX.md`. Read *full* current in-scope spec contents rather than just headings, including related owners. ADRs clarify accepted context but do not introduce missing FUR.
2. Define purpose, scope, software boundary, layer, decomposition, functional-process granularity and external functional users.
3. For a large scope, process **every current in-scope spec** in repeated manageable groups. Maintain a coverage ledger (exact path, actually read full text, owned/related process IDs or specific no-process rationale, source anchor). Reconcile cross-spec overlap before counting. The default whole-product request is **exhaustive**, not a convenient partial sample.
4. Identify triggering events, unique complete functional processes, objects of interest and data groups. Include alternatives.
5. Assign Entry (E), Exit (X), Read (R) and Write (W) only for supported data movements. Every Measured process needs an initiating E and at least one X or W (Rule 10c). A movement normally counts once per type/group/recipient within a process unless distinct required semantics are supported.
6. Cite the current spec path and short exact quote/heading for **every process and movement**. Independently re-count `CFP = E + X + R + W` in the displayed per-process breakdown.
7. Reconcile duplicate and ambiguous candidates and report coverage, compatible totals, exclusions and applicability limitations.

The analysis uses **semantic LLM self-review**, not an independent mechanical validation. No tool can infer absent requirements. Full-product size requires reading all declared documented FUR, not merely inventorying file names. Before a finished report, the skill must show `Full-text sources reviewed: X/Y` and the per-spec ledger. An available spec simply left unread is an unfinished execution, **not an acceptable reason to return an otherwise successful Partial result**. Keep processing unless a concrete tool/context limit prevents it; when blocked, report that limit and the unread paths.

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
- `tests/Idd.Generation.Tests/CosmicFunctionalSizeContractTests.cs` — generated skill/metadata packaging and read-only tooling requirements.
- `tests/Idd.Intent.LiveTests/Scenarios/cosmic-cases.json` and `CosmicFunctionalSizeLiveTests.cs` — optional **real LLM** semantic checks of process and movement inventories; opt in via `IDD_RUN_LIVE_COSMIC_EVALS=1`. Not run in regular CI.
- [IDD scope diagnostic example](examples/cosmic-idd-diagnostic.md).

Also see the [official measurement process](https://cosmic-sizing.org/cosmic-sizing/intro/measurement-process/). The skill is not COSMIC certification, an implementation audit or a cost/schedule forecast.
