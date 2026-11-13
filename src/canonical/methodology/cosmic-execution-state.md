# COSMIC execution-state and bounded recovery contract

The COSMIC skill is read-only for the **target project**. Run state is not Product Intent. This contract extends the delegation protocol with **resumable, auditable execution** and deterministic infrastructure checks. COSMIC semantics remain LLM decisions; state tooling must never invent processes, movements, owners or links.

## State ownership and location

- Prefer a stable user/host-approved session workspace **outside the target repository**, not an ephemeral child context or a subfolder of `.idd/`. A new run creates `run.json` (host-generated; **not an LLM output contract**), `sources.json` or equivalent flat source inventory, and a directory of per-spec/per-process Markdown packets.
- Persist the exact selected scope and exclusions, software boundary, functional users, layer, granularity, COSMIC edition, **protocol revision**, and source SHA-256 hashes. A global run identity is derived from these exact inputs, not from a guessed file count or a model's summary.
- Use stable process keys `IDD-NNNN#P1` scoped to the normative owner. Do not require an LLM to generate globally unique keys; an infrastructure allocator may namespace locally supplied P1/P2 by owner. Keep previous IDs and edge references stable on resume unless the owning source changed.
- Save reader **and** reduction **and** process-measurement status, packet location/hash, source fingerprint, dependency/connector references and last failure class. Write complete packets before marking a task complete; use atomic replacement when supported, never treat an interrupted/truncated packet as success.
- Statuses must distinguish `Pending`, `Running`, `Completed`, `Retryable failure`, `Blocked` and `Invalidated` from COSMIC semantic statuses `Measured`, `Needs clarification`, `Ambiguous`, `Out of scope`, `Not a functional process`. These are separate dimensions.
- Existing input files may be read or hashed without running project code. When a trusted SHA-256/file tool is unavailable, **do not reuse old packets as current**: verify source freshness by a reliable host mechanism or process the sources again. Do not claim source fingerprints based on LLM memory.

## Resume and invalidation

At a new invocation, offer to resume a compatible saved run when its approved state is available. Do not silently merge different measurement boundaries or method editions.

1. Rediscover current files independently of INDEX; compare exact in-scope paths to saved inventory. New/removed files alter scope and require an inventory refresh.
2. Rehash sources. **Reuse a Completed task only if** its complete packet and its checksum remain retrievable, the source hash and measurement perspective match, the protocol remains compatible, and all relevant dependency references are still valid.
3. Invalidate results for changed or deleted owner specs and any dependent connector packets or measurements. Propagate invalidation through the known dependency graph to a fixed point. A changed caller spec invalidates its outgoing connector set and can change the owner's **Coupling / Change Impact** even if the owner's CFP remains unchanged. It does not automatically modify the owner's COSMIC movements.
4. Requeue only invalidated, missing and retryable tasks. Preserve unaffected successful packets and avoid re-reading unchanged specs unnecessarily. Record why each task was reused or invalidated.
5. Refresh reduction groups that include changed owner/connector IDs, then run focused process measurers where FUR affecting the measurement changed. Never reuse a process CFP across incompatible boundaries.
6. When the state path is unavailable or corrupt, report a clear limitation and start a **new** isolated run if authorized, rather than presenting unverified old results as Complete.

A checker may mechanically compare hashes, counts, IDs, packet existence and E/X/R/W arithmetic. It may not certify that an E/X/R/W assertion is semantically correct. The optional `cosmic_state.py` support script, when packaged and the host permits executing trusted bundled helpers, supports these limited operations; it is **not mandatory for every host**. Do not execute source code or validators from the target application.

## Bounded retries — no silent loss

When a reader, consolidator, reducer or measurer returns an invalid/truncated packet:

1. Preserve the rejected packet and the exact failure reason. Reject only **structural** errors mechanically (missing source anchor, invalid owner key, nonexistent reference, missing required record, impossible range, wrong source fingerprint, unfinished output).
2. Attempt **one focused correction** with a fresh native child context, asking only for the malformed/missing records and source passages. Do not have code guess semantics or synthesize missing moves.
3. If still invalid, **one smaller rerun** of the affected source section or process using a fresh context is allowed. Keep a total task retry limit of **2 after the initial attempt**; no unbounded retry or polling.
4. If still invalid, mark `Blocked` with a specific source/task and continue other independent work. Completion status is `Execution incomplete (BLOCKED)` if required accessible evidence remains unprocessed; an evidence-based semantic ambiguity after a valid read can be Analytical Partial.
5. Retain progress in the ledger: `0/3` missing packets are not treated as successful, and failures do not count as coverage.

Do not equate a process being incomplete as FUR with an agent returning an incomplete message. A missing tool, model or permission is a technical blocker, not an `Unknown` product requirement.

## Portable record examples

Model output remains simple Markdown or flat text. Example of canonicalized **infrastructure** records:

```text
OWNER | IDD-0042#P1 | IDD-0042.spec-copy.md | #Copy file
LINK | IDD-0049#P3 | IDD-0042#P1 | IDD-0049.spec-ui.md#File action
MOVE | IDD-0042#P1 | E | Copy request | IDD-0042.spec-copy.md#Copy
MEASURE | IDD-0042#P1 | system-boundary | E=1 X=1 R=1 W=1 | CFP=4
```

`OWNER` comes only from the owning spec. `LINK` refers to it from another spec. No `LINK` contributes directly to CFP. A missing target owner is an unresolved connector reference, **not** an invitation to fabricate a process. Incoming caller-process fan-in and referring-spec counts must be labeled distinctly.

## Deterministic gate (where supported)

Before claiming Complete, independently check:

- Exact source inventory and hash freshness; every spec's complete read-range coverage and packet integrity; task statuses and bounded retry history.
- Every `OWNER` has one source-qualified identity; a second normative owner describing the same process is a **structure-audit issue**, not a COSMIC deduplication task.
- Every `LINK` has a valid existing owner target or an explicit unresolved reference; every edge has provenance, no inventing callers from source filenames.
- Every owned measured process has exactly one final `MEASURE` result and supporting source-backed movements, a matching boundary, and `CFP = E+X+R+W`.
- Every complete measurement uses only compatible units, no process is summed twice, all processing partitions and cross-group connector edges are accounted for. The non-COSMIC coupling summary is not added to CFP.

If the host lacks reliable infrastructure verification, the skill must not claim mechanically verified evidence; it may perform explicit LLM self-review with caveats, or return BLOCKED if essential packets/coverage cannot be recovered.
