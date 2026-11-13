# COSMIC numbered reader cache — simple resume and explicit refresh

This is the required file-based reuse protocol for `idd-intent-cosmic`. It replaces execution-state databases, hashes, generated JSON manifests and the optional `cosmic_state.py` helper. The skill uses only the host's **native file read/list/write/rename/delete abilities and native agents**. No dedicated script, validator, Python, Node.js, custom LLM runtime, checksum command, polling service or background process is needed. The target Product Intent remains read-only.

## Location and naming

Choose **one user-visible, persistent, writable cache directory outside the target project/repository**, scoped to that project (never share a numbered cache between unrelated projects). Use a **stable default** such as `~/.cache/idd-cosmic/<project-folder-name>/` on macOS/Linux or `%LOCALAPPDATA%\\IDD\\Cosmic\\<project-folder-name>\\` on Windows, or the user's explicitly chosen location; do not create a new timestamped/random directory on each invocation. If the default name conflicts with a different project, choose/ask for a distinct readable folder name. If host sandbox permissions prohibit writes there, request a writable external location or report the capability limit, rather than silently modifying the target repository. Reuse that same folder on later invocations, including after a new conversation, when available. The user may override the location. Do not put reader packets into `.idd/intent/`, commit them, or treat them as a new source of product truth. A temporary run folder that disappears after the session is not a resumable cache.

Example:

```text
<user-approved-cache-for-this-project>/
  IDD-0001.cosmic.md
  IDD-0002.cosmic.md
  IDD-0042.cosmic.md
```

**One current numbered Intent source ID -> one completed reader packet**. The suffix is `.cosmic.md`; do not use the full original filename as the cache key. A rename of `IDD-0042.spec-old.md` to `IDD-0042.spec-new.md` is detectable because the packet records its exact source path. Multiple current normative files with the same ID are a structure-audit issue, not a reason to overwrite a packet unpredictably.

Do **not** require a per-run `run.json`, `sources.json`, timestamp database, file fingerprint or process checkpoint. Enumerating the current numbered sources and the corresponding `IDD-NNNN.cosmic.md` packets is the recovery ledger. Do not persist every reducer/measurement agent's state by default; they can recompute from reader packets.

## Completed reader packet format

A packet stores **source-backed extraction**, not a finished COSMIC CFP measurement. A fixed file ID permits reuse for another declared measurement subset and, where the extraction remains useful, another measurement boundary. The final measurer must reconsider boundary-dependent E/X/R/W rather than inherit an old total.

```text
Source ID: IDD-0042
Source: .idd/intent/IDD-0042.spec-copy.md
Packet format: idd-cosmic-reader-v1
Coverage: Read in full
Read ranges: 1-180 (or explicit complete source length and gap-free sections)
PROCESS | P1 | Copy file | User | # Copy
EVENT | P1 | User requests a copy | # Copy
MOVE CANDIDATE | P1 | E | Copy request | # Copy
MOVE CANDIDATE | P1 | R | Source file | # Copy
MOVE CANDIDATE | P1 | W | Destination file | # Copy
LINK | P1 | IDD-0049#P2 | # Related operation
GAP | P1 | No evidence of an external confirmation
Packet complete: IDD-0042
```

Every substantive source assertion must retain an exact heading, line/section or short quote; use additional simple Markdown lines for required alternatives, actors, functional-user distinctions, persistence, unknowns and source-only connectors. A connector-only spec has a `LINK` and a source-backed `No independent process` explanation; a nonfunctional spec has an explicit reason. **Do not discard source evidence to make a packet small**; the packet lives on disk, not in the coordinator's context.

The opening `Source ID`, exact `Source`, `Packet format`, complete read-range coverage, `Coverage: Read in full` and final `Packet complete: IDD-NNNN` line are all required. A truncated or partially written file is **not a completed packet** just because its name exists. Treat it as missing until corrected; do not count it towards coverage.

The packet must be written **only after full source inspection and any local range consolidation**. Prefer creating an intermediate `IDD-0042.cosmic.partial.md` and moving/renaming it to `IDD-0042.cosmic.md` via native file operations only after verification. If the host cannot rename, it may write the entire completed content to the final filename using its native write capability, but the final sentinel and range check must still pass before reuse. A `.partial.md` file is never a cache hit.

## Default: reuse by existence, not freshness checks

At the start of **every** request:

1. Discover actual current `.idd/intent/IDD-NNNN.spec-*.md` sources (do not rely solely on INDEX) and establish the requested scope: all current Intent by default, or explicitly selected IDs/functional area.
2. List `IDD-NNNN.cosmic.md` files in the project-specific cache folder. For a requested ID, reuse its existing **complete matching packet** without rereading its full original source. A matching packet has the same ID and exact current source path, validated completion marker, ranges and extraction schema.
3. For a missing, incomplete, mismatched or explicitly refreshed ID, start a **fresh native isolated Reader**, read its full source (split large specs into range readers), validate the complete packet and save the completed filename. Never infer coverage from filenames that fail these basic checks.
4. Keep a compact **coverage ledger** of the requested IDs: `Reused packet / Newly read / Failed / Not processed`, packet path, owner processes or connector-only reason, and any source-access error. Reconcile requested source IDs against completed packet IDs before final measurement. A reused completed packet counts as **previously full-read**, not **freshly read in this run**.
5. Build owner and connector indexes from the selected packets, using bounded fresh reducers. Resolve necessary external owner references using their existing matching packets or read the referenced owner source if relevant to the requested measurement; explicitly distinguish **requested IDs** from **dependency IDs**. A selected subset must not silently expand to the whole project.
6. Run fresh bounded **process measurers** for the selected owned processes and compute a **new** CFP report from selected packets under the **current declared boundary**. Previous run totals, reducer summaries and process-measurement verdicts are not authoritative cached results.

**The cache is trusted by user choice, not verified for freshness.** No hash, mtime, diff, Git comparison, background watcher or implicit source reread is required to prove that its contents match possibly edited Intent. State clearly when prior extraction packets were reused and that edits since their creation are not detected automatically. Do not claim a complete *up-to-date* product measurement solely on unverified reused packets. `Complete` means complete over the **selected accepted packet snapshot**; label the report `Cached input — source freshness not verified` when any cached packets were reused. When the user says Intent changed, refresh the relevant IDs or all before reporting a current-state measurement. A changed source **name/ID** is the only mismatch detected by simple identity checks.

## Explicit user controls (natural language, no new CLI or code)

- **Reuse (default):** "Measure the full product" or "measure IDD-0012, IDD-0034 and IDD-0042". Reuse complete matching reader packets; read missing packets. Always **recompute** final measurements for the selected subset and boundary.
- **Refresh specific:** "Re-read IDD-0042" / "recalculate IDD-0012 and IDD-0042". Ignore their previous packets, fully re-read those sources in isolated contexts and replace only their packets. Existing unrelated packets stay untouched.
- **Refresh all:** "The Intent changed substantially; re-read everything" / "recalculate from scratch". Ignore all existing packets for the requested full-project scope and replace them after complete readings. Never pretend that a reused packet has just been refreshed.
- **Clear specific/all:** "Delete cached COSMIC extracts for IDD-0042" or "clear the COSMIC cache". Remove only the explicitly selected `IDD-NNNN.cosmic.md` files (or all *owned* packets in the selected project folder) after the user's explicit deletion request. Do not delete real Intent files or other cache folders. Clearing is not the same as refreshing.
- **After reporting:** state the cache location and how many source packets were reused/newly created. Briefly offer to **keep** them for later scoped measurements (default) or **clear** them. Do not delete automatically.

For a refresh, retain the old successful packet until its replacement is confirmed; do not replace it with malformed output. During a refresh invocation, an old packet requested to be refreshed **must not** satisfy that invocation's completion gate if refresh failed. Report the failure and keep the old packet for a future explicit reuse decision.

## Scope, dependencies and count separation

- Distinct **canonical process owners** are fixed by source ID. A `LINK` is a connector, not duplicate process definition or a COSMIC movement. Do not use semantic deduplication to merge different owners.
- If a caller spec refers to an owner outside the user-selected scope, read/reuse that owner's packet **only when needed to understand or size the selected function**; record it as a dependency, not as another selected measurement target. The user-selected subset remains explicit.
- For a partial-scope report, fan-in is **observed in the examined packet set only**. Never imply full-project fan-in from a subset, even if many unrelated cached packets exist.
- CFP is recalculated for the selected owner processes only. Referencing a process never causes it to be summed a second time; selected process IDs are counted once within a consistent boundary.
- Compact reducers and process measurers use references to these per-ID packets; the full evidence stays on disk. If the cache index is too large for one context, split/reduce it in fresh native contexts, never truncate.
- `Complete` and `Analytical Partial` still require the full **declared** requested scope to be accounted for. A scope narrowed by the user can be Complete for that scope, not for the whole product.

## Failures and bounded retries

An invalid or truncated result is not written as a completed cache packet. Allow **one focused correction** to repair just the missing evidence/records, followed if necessary by **one smaller rerun** with a fresh context. Do not loop indefinitely or fabricate COSMIC assertions. Continue other independent sources. A persistent read/tool/storage failure is `Execution incomplete (BLOCKED)`, with the affected IDs and completed cache count stated. An unresolved functional requirement in an otherwise valid packet is a **semantic** gap, not a technical failure.

No separate retry counter service is required. A native-agent coordinator tracks attempts for the current invocation. When the host cannot write or reread the required cache files safely, report the specific limitation; do not claim a resumable cache was saved.

## Final checks

Before giving CFP, compare the current requested IDs (plus explicitly needed dependency IDs) with completed `IDD-NNNN.cosmic.md` packets, check completion markers and source paths, ensure every `PROCESS` belongs to one owner, every `LINK` resolves or is identified as unresolved, and every Measured owner process has evidence-backed E/X/R/W satisfying Rule 10c. Sum `CFP = E + X + R + W` once for each compatible selected owner process; these are ordinary arithmetic and coverage checks, **not an executable semantic validator**. Report unresolved gaps honestly and keep non-COSMIC coupling separate.
