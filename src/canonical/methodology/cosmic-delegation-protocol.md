# COSMIC isolated analysis and hierarchical reduction protocol

This is the **required execution protocol** for `idd-intent-cosmic`. It applies to small and large scopes. COSMIC counting rules and authoritative evidence requirements remain in `cosmic-measurement-guide.md`. This protocol is an LLM-agent workflow, **not** a scripted substitute for semantic measurement.

## Roles, isolation and capability gate

- **Coordinator**: inventory the actual current files, establish the common measurement perspective as far as sources permit, assign one independent spec-reader task per in-scope numbered spec, maintain the separate coverage ledger, schedule reductions, and issue the final report. Do not load all raw specs into its context.
- **Spec reader**: a fresh, bounded **native child-agent context for one exact source file**, never the parent conversation recycled as a child. It reads the whole source (or, for an unusually large source, delegates bounded sections for complete, overlap-checked coverage), consults COSMIC rules, and returns concise findings with verbatim evidence anchors. It does **not** author the final overall CFP.
- **Spec consolidator**: for a large single spec, combine isolated range-reader records into one complete **owner-scoped** packet; check gaps, continuations and alternatives across its sections without treating each section as a process.
- **Reducer**: a fresh native child-agent context for one bounded group of reader/reducer packets. It assembles a **process-owner index and directed connector graph** (not a semantic duplicate finder). Reduce recursively, preserving every owner identity and connector edge.
- **Process measurer**: a fresh native child-agent for **one owned process or a bounded group**, using the owner's evidence plus referenced context where necessary. It applies COSMIC v5.0 and returns final evidence-backed E/X/R/W. Never rely on one agent to measure every process in a large project.
- **Coordinator/final reporter**: check the complete owner index, connector references, coverage and per-process measurement packets, aggregate only compatible verified CFP, and produce a user report. Its context contains concise indexes, not all movements and raw requirements at once.

Native delegation is **mandatory for every substantive spec** (one spec may be processed by an isolated existing skill fork only when that fork's full scope is exactly that one spec). Use host-native generic child agents; no custom executable runtime, simulated LLM role, or JSON parser is required. Confirm tools actually permit fresh child contexts. If child-agent isolation is unavailable, report **Execution incomplete (BLOCKED: isolated-agent capability unavailable)** and any safe inventory obtained. Do not silently perform a multi-spec read in the coordinator or claim coverage from an INDEX.

## IDD single-owner rule — no semantic deduplication

**Assume that each functional process is defined exactly once in a current owning Product Intent spec.** A spec may own many processes. Other specs may **reference/consume** that process: these are *directed connector edges*, not repeated process descriptions, extra process candidates, or added COSMIC E/X/R/W. A connector points to the owning `IDD-NNNN` and (when the file defines multiple processes) its local process key. Preserve the exact source of the reference and known caller process/owner. An unqualified reference to a multi-process owner is unresolved until clarified; do not invent a target.

Do **not** instruct reducers or measurers to search for semantic duplicates, merge coincidentally similar processes or count the same process in multiple files. If a real second normative definition is discovered, report **`STRUCTURE_ISSUE: duplicate process ownership`** with both sources for the Intent structure audit/linter; do not repair or semantically deduplicate it within COSMIC. If it prevents a defensible CFP result, mark only the affected measurement undetermined/Analytical Partial. Same owner process ID repeated in transport is a **mechanical duplicate** that must be rejected, not a second measurement.

Cross-spec dependencies are important even without duplicate ownership. Record `LINK` edges separately and count **distinct source process identities** calling each target, never mere references repeated in prose. If only referring documents can be identified, report a distinct *referring-spec count* instead of pretending to know the number of caller processes. An unresolved connector stays unresolved and must not be counted as a confirmed call.

Publish an independent, explicitly **non-COSMIC Coupling / Change Impact** view per important owned process: confirmed inbound caller-process count (fan-in), outbound connector count (fan-out), referencing-spec count when needed, and qualitative drivers such as failure propagation, shared constraints and change blast radius. Higher confirmed fan-in can raise **qualitative integration/change difficulty** even if two processes have identical CFP; do not multiply CFP or make an unsupported person-hour estimate. Do not count each repeated invocation as a new functional process. Any actual data movement belongs to a complete FUR-defined process under the selected software boundary and must be independently justified there.

## Budget and retention

Before each handoff, choose a conservative effective token budget **including role instructions, reference material, input, reasoning reserve and output**. Keep packet input at most the smaller of approximately **6,000 tokens** and **20% of usable context**; when token counts are unavailable, use a conservative character estimate and reduce the limit for small-context models. This is a budget guard, not a claim that characters equal tokens. Do not dispatch an oversized packet. Keep output bounded, normally **150–400 tokens for one simple process**, expanding only for additional distinct requirements and their movement evidence.

All *detailed* per-spec evidence is retained in **lossless, addressable reader packets**, while reducer inputs and the coordinator use **compact indexes**, not concatenated full traces. When host-provided ephemeral artifacts or writable temporary storage are available, use a session-scoped scratch location **outside the target repository** for Markdown packets, an append-only process/evidence index and the coverage ledger; do not write `.idd/`, source files, commits or permanent project reports. Remove only scratch material owned by this run when no longer needed. Scratch is a storage transport, not a validator or an executable measurement engine.

If no safe scratch is available, small analyses can retain packets in native child results with stable references as long as all information remains retrievable and **within the budget**. If evidence/ledger no longer fits and cannot be retrieved losslessly, stop with a truthful BLOCKED status; **never solve overflow by dropping processes, anchors, unread files or unresolved conflicts**. Do not use nested JSON structures as the child contract.

## Spec-reader input

Pass exactly: the resolved file path, the declared measurement scope and boundary/layer (or an explicit unresolved boundary), known functional-user perspective, minimal related IDD paths, the location of the bundled COSMIC measurement guide, and the brief contract below. The child must inspect the complete substantive file itself. Related sources are **cross-references**, not permission to treat unread files as verified. For a source larger than the reader's context, create independently isolated section readers with explicit non-overlapping byte/line/heading ranges, reconcile all ranges and mark the spec `Read in full` **only** after gap-free coverage. When that is impossible, mark `Read failed` or `Not yet read` with the concrete cause.

## Spec-reader output: minimal Markdown (no mandatory JSON)

Use this structure; repeat a Process block for each distinct triggering-event candidate, not each endpoint or paragraph:

    Source: .idd/intent/IDD-0012.spec-orders.md
    Coverage: Read in full | Read failed | Not yet read
    Read ranges: [complete file / exact inclusive lines or sections; gaps if any]
    Evidence: [heading or short exact quote; line when available]
    PROCESS | P1 | <owned process / trigger / external user> | <source anchor>
    MOVE | P1 | E/X/R/W | <data group / OOI / recipient> | <owner source anchor>
    LINK | <caller local process key or source-only> | IDD-NNNN#P2 | <reference anchor>
    GAP | P1 or LINK | <missing evidence or unresolved target>
    No independent process: <verified reason and anchor, when applicable>

- Omit empty movement types, but never infer an R/W from assumed database implementation or an X from assumed UI behavior. One process can have many movement lines, each with its **own** source anchor; identify object-of-interest/data-group meaning and recipient/persistence when known. A candidate may have **no** supported movements.
- Include read-range evidence from actual read-tool results, not just a statement that the file was read. The coordinator compares the ranges with the discovered source length and must not claim full coverage when gaps remain.
- If no independent process is defined, return `No independent process: <verified reason and anchor>` instead of a fabricated empty process.
- References to processes owned elsewhere are **LINK connectors**, never additional PROCESS or MOVE records. An unrelated source must not amend another owner's normative process definition or contribute movements. If its text appears to redefine another process, report a structure-audit issue rather than attempting COSMIC deduplication.
- Add concise notes only for materially relevant alternatives, conflicts, exclusions and unanswered questions. Exact source location is required for material assertions. The output is **source extraction**, not a claim of complete CFP.

## Separate coverage ledger and process index

The coordinator tracks **one row per discovered in-scope numbered spec**, independent of any reducer summarization:

    Spec path | Read in full / Read failed / Not yet read | Evidence packet reference | Local process candidates or verified no-process reason | Related owners | Error / source anchor

Start every row as `Not yet read`. Change status only after receiving and checking actual reading evidence. Distinguish a tool-access failure from a child-agent budget failure; a synthesized reader answer without source access is not a read. All rows must remain addressable; an aggregate like "other 30 files" is not a ledger. Check the file inventory against INDEX and actual directory again before finalization.

Maintain **two separate lossless indices**:
- **Owner index:** stable `IDD-NNNN#local-process-key`, triggering event, user, one owning spec, source/evidence packet, state (`Measured / Needs clarification / Ambiguous / Out of scope / Not a functional process`). The key is namespaced by the owner; it is not a semantic hash, and one owning spec may define several processes.
- **Connector index:** `caller owner#process or referring spec -> target owner#process`, exact reference anchor, resolved/unresolved status, and source packet. Each connector must point to an existing owner (or stay unresolved); do not create target processes from connector text.
Preserve all source-to-process and caller-to-target links. Do not merge process identities across different owners.

## Recursive reducer

Group packets preferably by functional area and explicit dependency; choose groups by **total input budget**, never a fixed number of files alone. Do not pass raw spec bodies to reducers. Each reducer:

1. Confirms exactly which packet IDs and spec IDs were received; rejects absent, duplicate, truncated or unresolvable packet references.
2. Produces owner and connector indexes with source-qualified identifiers, triggering-event/user identities, exact owner files, unresolved target references and evidence packet references. It **does not decide semantic sameness** of separately owned processes.
3. Returns processed packet IDs plus owner-process and connector counts. Only identical source-qualified transport IDs can be mechanically collapsed, with provenance preserved. Never merge two owner keys by name similarity, and never discard source evidence.
4. If its input exceeds budget, splits it into smaller nonempty groups; if its output exceeds the next level's budget, **recurses again**. No reducer may hide overflow by truncation. Every reducer stage uses a fresh native child context.

The coordinator verifies a complete, disjoint partition of input packet IDs at every level and that every owner process and connector edge survives. **Cross-group resolution means joining LINK targets to the owner registry**, not semantic process deduplication. A missing target remains an explicit gap; a second normative owner is routed to structure audit. The final registry is complete only if all source packets and all link records can be accounted for.

## Final reconciliation and report

First check the **coverage gate**: every in-scope current spec accounted for, no accessible `Not yet read`, an evidence packet or concrete attempted-read failure for each, and an explicit no-process rationale where applicable. Then join all connector references to their owned process keys, including across reducer groups; retrieve specific detailed evidence or original source passages when needed. No CFP may rely solely on a compressed reducer assertion.

Assign **fresh bounded process-measurer agents** for owned processes; they use their own owner's evidence and only necessary external contextual references, then independently validate E/X/R/W under a consistent boundary. COSMIC interpretation stays with the LLM; a process can be Needs clarification despite a fully read owning spec. After each measurement, mechanically verify that the process ID belongs to the owner index, each movement has an anchor, there is no duplicate result for that ID, counts match its recorded E/X/R/W, and only compatible measurement units are added. Use trusted host arithmetic or an optional deterministic checker; never reinterpret the semantics in code. Do **not** sum per-file CFP or reducer subtotals, and preserve unresolved measurements as gaps rather than assigning zero.

Maintain **two separate artifacts**:
1. A **compact internal handoff** for subsequent child contexts (scope, inventory X/Y, distinct owner-process IDs/statuses, connector targets, coupling counts, measurement packet references, and totals only if justified). Retain it in session scratch or other retrievable native result storage; do **not** dump it into the user-facing report by default.
2. A **detailed user-facing Markdown report** containing the per-spec coverage ledger, owned process inventory, source-backed movement tables, defensible CFP, a **separate** non-COSMIC coupling/change-impact section, unresolved issues, applicability limits and explicit non-certification. If an exceptionally large ledger cannot fit the output budget, keep the complete ledger as an accessible session artifact outside the repository, provide its exact location and an area-level coverage summary in the report, or emit the report in bounded sections. Never truncate the underlying ledger or claim complete coverage based on an inaccessible artifact.

Treat `Complete`, `Analytical Partial`, `Diagnostic only`, `Execution incomplete (BLOCKED)` as defined in the main skill. An explicitly unprocessed accessible source is **BLOCKED**, not a successful Analytical Partial. If even the compact final owner/connector index cannot fit, continue hierarchical joining by clusters; verify **all cross-cluster LINK targets** via focused retrieval before reporting complete coverage. If this cannot be established, do not claim a complete total.
