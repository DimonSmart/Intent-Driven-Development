# COSMIC Functional Size Measurement (IDD)

`idd-intent-cosmic` is a **read-only analytical skill** for measuring COSMIC v5.0 Function Points (CFP) from current `.idd/intent/` Functional User Requirements (FUR), independently of `idd-intent-estimate` (qualitative Business Complexity) and without an implemented product.

## Run

Ask **"Measure the current documented product in COSMIC v5.0 CFP"**, **"Count COSMIC Function Points for the order module"** or invoke `idd-intent-cosmic` directly. Optional area/IDD IDs restrict the requested scope, but all specs that jointly define its functional processes must be read.

**Native isolated child agents are required for new extraction and bounded measurement.** Each source has a reusable, complete Markdown reader packet named `IDD-NNNN.cosmic.md` in a persistent, project-specific folder **outside** the target repository. A packet contains source-backed functional facts, owner-defined processes and LINK connectors, **not a final CFP number**. A later request reuses matching completed packets and reads only missing or explicitly refreshed source IDs. Every requested measurement produces a **new** COSMIC calculation for the current scope and boundary.

No Python, Node.js, hashing tool, extra validator, custom runtime, structured nested JSON, or modification of target-project files is needed. All cache operations use native file tools. The installed skill bundles `references/cosmic-measurement-guide.md`, `references/cosmic-delegation-protocol.md` and `references/cosmic-execution-state.md`. Without native child-agent isolation for missing sources the result must explain `Execution incomplete (BLOCKED)`, not silently downgrade to same-context bulk reading.

## Measurement procedure

1. Inventory the actual numbered files and reconcile `INDEX.md`. Read *full* current in-scope spec contents rather than just headings, including related owners. ADRs clarify accepted context but do not introduce missing FUR.
2. Define purpose, scope, software boundary, layer, decomposition, functional-process granularity and external functional users.
3. Compare selected current source IDs with completed cache packets named `IDD-NNNN.cosmic.md`. **Reuse complete matching packets by default**; run a fresh native Reader only for missing, incomplete or explicitly refreshed sources. A Reader saves source/read ranges, owner `PROCESS`, connector `LINK`, `MOVE CANDIDATE`, exact evidence and GAP records to `IDD-NNNN.cosmic.partial.md`; after completing the full extraction, it renames the file to `IDD-NNNN.cosmic.md`. Very large specs use independent section Readers and gap-free consolidation.
4. Keep a per-ID coverage ledger (exact current path, `Reused packet` or `Newly read`, packet path, process/connector/no-process rationale and errors). A cached packet is accepted when it has the **final `.cosmic.md` filename** with matching source ID and recorded source path. No completion line inside the file is needed. Reused extraction is not counted as a fresh source read.
5. Merge small groups of **owner and connector indices** through fresh reducer agents. Use no more than approximately 6,000 input tokens or 20% of usable context per handoff, whichever is smaller, including reserve for instructions and output in the overall budget. Recurse as needed; partition every packet exactly once at each level, preserve evidence outside the compressed summary and join LINK targets across groups. Do not semantically deduplicate processes across different owners; a true duplicate normative definition belongs to Intent lint/structure audit.
6. Resolve process references against their canonical owning specs; for a selected subset, load external owner packets only as needed dependencies, not automatic extra measurement targets. Let fresh process-measurer agents size **selected** owned processes under the current boundary; never treat cached reader packets as previous CFP verdicts.
7. Assign Entry (E), Exit (X), Read (R) and Write (W) only for supported data movements. Every Measured process needs an initiating E and at least one X or W (Rule 10c). Cite the actual current spec and short exact quote/heading for **every process and movement**.
8. Independently re-count `CFP = E + X + R + W` once per **selected** compatible owner process using ordinary arithmetic and file inspection. No custom code or hash checking is involved. Report full requested-scope coverage, newly-read/reused packet counts, totals/subtotals only if justified, and explicit cached-source freshness limitations. Return the complete **user-facing Markdown report** separately from the compact handoff used by child agents.

Source interpretation, owner-based process measurement and qualitative coupling judgments remain **semantic LLM analysis**, not independent mechanical validation. The new protocol is implemented as prompt-level native-agent orchestration, not a hard-coded deterministic pipeline. No tool can infer absent requirements. Full-product size requires reading all declared documented FUR, not merely inventorying file names. Before a finished report, the skill must show `Full-text sources reviewed: X/Y` and the per-spec ledger. An available spec simply left unread is an unfinished execution, **not an acceptable reason to return an otherwise successful Partial result**. Keep processing unless a concrete tool/context limit prevents it; when blocked, report that limit and the unread paths.

## Single owner and non-COSMIC coupling impact

IDD assumes one canonical owning specification for each functional process (one spec may own several). Other specs may reference it through `LINK` connectors. Links do not define additional processes, are not additional COSMIC data movements, and do not increase CFP. A genuine duplicate normative definition is reported for Intent structure lint/audit, not repaired or semantically deduplicated in the measurement workflow.

As a separate **Coupling / Change Impact** assessment, show confirmed distinct *caller process* fan-in and *referring-specification* counts independently, together with verified failure/dependency risks. For example, a 3-CFP operation referenced by one process and one referenced by 100 processes remain 3 CFP each; the latter may have greater change/integration impact. The counts have no automatic conversion to effort or CFP. Do not pretend that 100 document mentions prove 100 independent callers.

## Numbered cache, scope reuse, refresh and cleanup

Example folder structure:

```text
<cache-folder-for-this-project>/
  IDD-0001.cosmic.md
  IDD-0002.cosmic.md
  IDD-0042.cosmic.md
```

The cache folder is a separate, user-visible persistent directory outside the repository (for example, `~/.cache/idd-cosmic/<project-folder-name>/` on macOS/Linux, or a user-chosen writable directory). Reuse the same folder on later runs; if the host's sandbox cannot write there, obtain permission or choose another external directory rather than changing Product Intent. The filenames use the same ID as `.idd/intent/IDD-NNNN.spec-*.md`, so the source inventory itself serves as the work queue. During extraction, write to `.cosmic.partial.md`; after success, rename to `.cosmic.md` (preferably atomically). The **final filename alone indicates completion**; there is no footer. Leftover `.partial.md` files are ignored. No hashes, timestamps, file-change watchers, manifests or separate state engine are required.

Natural-language usage:

- **Resume / reuse:** “Measure all Product Intent” processes completed packets and reads only missing sources.
- **Selected scope:** “Measure IDD-0001, IDD-0002 and IDD-0042” reuses those extracts, reads any needed missing references, and recalculates CFP for that scope only.
- **Refresh selected:** “Re-read IDD-0042” ignores its existing extract and replaces it after a successful complete read.
- **Refresh all:** “I changed Intent; re-read everything” regenerates all requested extracts.
- **Clear selected or all:** “Clear cached COSMIC extracts for IDD-0042” or “Clear the COSMIC cache” removes only explicitly requested cache files. Clearing is never automatic.

**Existence is a user-controlled reuse decision, not proof of freshness.** If the source contents changed without changing ID/path, cached extracts may be stale. The skill does not compare hashes or reread originals automatically; the report says `Cached input — source freshness not verified` whenever reused packets were involved. The user explicitly requests refresh when documents materially change. A scoped report must not claim whole-project coverage or full-project connector fan-in based on its selected packets.

After reporting, offer to retain the cache for subsequent measurement (default) or clear it. A broken reader packet gets one focused correction and one bounded smaller retry; it is never marked complete just because a partial file exists. If safe native cache storage is unavailable, report the limitation rather than claim resumable execution.

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
- `src/canonical/methodology/cosmic-execution-state.md` — per-IDD completed Markdown reader packet cache, scoped reuse, explicit refresh/clear and bounded retries.
- `tests/Idd.Generation.Tests/CosmicFunctionalSizeContractTests.cs` — generated skill/metadata packaging and read-only tooling requirements.
- `tests/Idd.Intent.LiveTests/Scenarios/cosmic-cases.json` and `CosmicFunctionalSizeLiveTests.cs` — optional **real LLM** semantic checks of process and movement inventories; opt in via `IDD_RUN_LIVE_COSMIC_EVALS=1`. Not run in regular CI.
- [IDD scope diagnostic example](examples/cosmic-idd-diagnostic.md).

Also see the [official measurement process](https://cosmic-sizing.org/cosmic-sizing/intro/measurement-process/). The skill is not COSMIC certification, an implementation audit or a cost/schedule forecast.
