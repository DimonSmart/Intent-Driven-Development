# COSMIC isolated analysis and hierarchical reduction protocol

This is the **required execution protocol** for `idd-intent-cosmic`. It applies to small and large scopes. COSMIC counting rules and authoritative evidence requirements remain in `cosmic-measurement-guide.md`. This protocol is an LLM-agent workflow, **not** a scripted substitute for semantic measurement.

## Roles, isolation and capability gate

- **Coordinator**: inventory the actual current files, establish the common measurement perspective as far as sources permit, assign one independent spec-reader task per in-scope numbered spec, maintain the separate coverage ledger, schedule reductions, and issue the final report. Do not load all raw specs into its context.
- **Spec reader**: a fresh, bounded **native child-agent context for one exact source file**, never the parent conversation recycled as a child. It reads the whole source (or, for an unusually large source, delegates bounded sections for complete, overlap-checked coverage), consults COSMIC rules, and returns concise findings with verbatim evidence anchors. It does **not** author the final overall CFP.
- **Reducer**: a fresh native child-agent context for one bounded group of reader/reducer packets. It reconciles obvious duplicates, dependencies and evidence without inventing information, and returns a smaller **index** plus references to the lossless packets. Reduce recursively until the global candidate index fits the final reconciliation budget.
- **Final measurer**: reconcile candidate identities across **all** groups and dependencies, retrieve evidence on demand from lossless packets or original sources, determine movements under the shared boundary, verify completeness and produce the full human-readable COSMIC report.

Native delegation is **mandatory for every substantive spec** (one spec may be processed by an isolated existing skill fork only when that fork's full scope is exactly that one spec). Use host-native generic child agents; no custom executable runtime, simulated LLM role, or JSON parser is required. Confirm tools actually permit fresh child contexts. If child-agent isolation is unavailable, report **Execution incomplete (BLOCKED: isolated-agent capability unavailable)** and any safe inventory obtained. Do not silently perform a multi-spec read in the coordinator or claim coverage from an INDEX.

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
    Process: <local candidate name / triggering event / external user>
    Movements: E <group> [source anchor]; X <group> [anchor]; R <group> [anchor]; W <group> [anchor]
    Related: <other source IDs and why; or None>
    Open: <unknown boundary, lifecycle, duplicate or missing evidence; or None>

- Omit empty movement types, but never infer an R/W from assumed database implementation or an X from assumed UI behavior. One process can have many movement lines, each with its **own** source anchor; identify object-of-interest/data-group meaning and recipient/persistence when known. A candidate may have **no** supported movements.
- Include read-range evidence from actual read-tool results, not just a statement that the file was read. The coordinator compares the ranges with the discovered source length and must not claim full coverage when gaps remain.
- If no independent process is defined, return `No independent process: <verified reason and anchor>` instead of a fabricated empty process.
- A source can extend processes owned elsewhere; retain these facts as cross-spec dependencies, **not** extra measured processes.
- Add concise notes only for materially relevant alternatives, conflicts, exclusions and unanswered questions. Exact source location is required for material assertions. The output is **source extraction**, not a claim of complete CFP.

## Separate coverage ledger and process index

The coordinator tracks **one row per discovered in-scope numbered spec**, independent of any reducer summarization:

    Spec path | Read in full / Read failed / Not yet read | Evidence packet reference | Local process candidates or verified no-process reason | Related owners | Error / source anchor

Start every row as `Not yet read`. Change status only after receiving and checking actual reading evidence. Distinguish a tool-access failure from a child-agent budget failure; a synthesized reader answer without source access is not a read. All rows must remain addressable; an aggregate like "other 30 files" is not a ledger. Check the file inventory against INDEX and actual directory again before finalization.

Maintain a separate process/candidate index with **stable source-qualified local IDs**, triggering event, functional user, owning/related specs, evidence-packet references, cross-group duplicate/continuation hints, and status. Local IDs are not globally measured process IDs. **One spec can contain multiple processes; multiple specs can define one process.** Preserve all candidate-to-source links.

## Recursive reducer

Group packets preferably by functional area and explicit dependency; choose groups by **total input budget**, never a fixed number of files alone. Do not pass raw spec bodies to reducers. Each reducer:

1. Confirms exactly which packet IDs and spec IDs were received; rejects absent, duplicate, truncated or unresolvable packet references.
2. Produces a compact process index with source-qualified identifiers, triggering-event/user identity, cross-spec ownership, conflicts/unknowns, and references to exact reader evidence. Clearly separates **confirmed same process**, **potential overlap pending evidence**, and **distinct process**.
3. Returns the processed packet IDs and a count of candidate identities. It may merge **indexes** but never discard lossless evidence or resolve uncertain duplicates by assumption.
4. If its input exceeds budget, splits it into smaller nonempty groups; if its output exceeds the next level's budget, **recurses again**. No reducer may hide overflow by truncation. Every reducer stage uses a fresh native child context.

The coordinator verifies a complete, disjoint partition of input packet IDs at every reduction level and that every source-qualified candidate survives or has a documented, evidence-backed merge. Cross-group overlapping processes are reconciled at the final stage (or through a further focused reducer), not simply counted once per group.

## Final reconciliation and report

First check the **coverage gate**: every in-scope current spec accounted for, no accessible `Not yet read`, an evidence packet or concrete attempted-read failure for each, and an explicit no-process rationale where applicable. Then reconcile all distinct triggering events and related-owner extensions, including across reducer groups; retrieve specific detailed evidence or original source passages when needed. No CFP may rely solely on a compressed reducer assertion.

Apply the COSMIC rules to each **unique complete process**, with source-backed E/X/R/W and their data groups/OOI, and independently recount movements. Use compatible measurement units only. Do **not** sum per-file CFP or reducer subtotals (these are never authoritative). Preserve analytical uncertainty and incomplete evidence as such.

Maintain **two separate artifacts**:
1. A **compact internal handoff** for subsequent child contexts (scope, inventory X/Y, distinct process IDs/statuses, packet references, unresolved owners and totals only if justified). Retain it in session scratch or other retrievable native result storage; do **not** dump it into the user-facing report by default.
2. A **detailed user-facing Markdown report** containing the per-spec coverage ledger, unique process inventory, source-backed movement tables, defensible CFP, unresolved issues, applicability limits and explicit non-certification. If an exceptionally large ledger cannot fit the output budget, keep the complete ledger as an accessible session artifact outside the repository, provide its exact location and an area-level coverage summary in the report, or emit the report in bounded sections. Never truncate the underlying ledger or claim complete coverage based on an inaccessible artifact.

Treat `Complete`, `Analytical Partial`, `Diagnostic only`, `Execution incomplete (BLOCKED)` as defined in the main skill. An explicitly unprocessed accessible source is **BLOCKED**, not a successful Analytical Partial. If even the compact final process index cannot fit, continue hierarchical reconciliation by clusters; use a bounded final index of reconciled clusters and explicitly verify **cross-cluster duplicate identities** via focused retrieval before computing the overall total. If this cannot be established, do not claim a complete total.
