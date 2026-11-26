# Optional numbered Markdown extraction cache

The target project stays read-only. Cache is an optional scaling/resume optimization; it is not required to estimate CFP. If native file operations or persistent external storage are unavailable, continue with in-context extraction and disclose only if relevant. Use no mandatory script, checksum, state database or custom runtime.

## Location and identity

Use a stable user-visible project-specific writable directory **outside** the target repository, e.g. `~/.cache/idd-cosmic/<project-folder-name>/` or `%LOCALAPPDATA%/IDD/Cosmic/<project-folder-name>/`. Do not share a directory between unrelated projects. A user-provided external directory takes precedence. Never save or delete Product Intent or unrelated files.

One current numbered spec corresponds to `IDD-NNNN.cosmic.md`. During extraction write `IDD-NNNN.cosmic.partial.md`; only publish by successful rename after complete source reading/consolidation. The final filename indicates completed publication; no footer is required. Incomplete or mismatched packets are not reusable. Keep a previous completed packet until a successful explicit refresh replaces it. If safe rename is unavailable, continue without publishing a new persistent packet.

## Packet format (compatible with reader-v1)

    Source ID: IDD-0042
    Source: .idd/intent/IDD-0042.spec-copy.md
    Packet format: idd-cosmic-reader-v1
    Coverage: Read in full
    Read ranges: complete file
    PROCESS | P1 | Copy file | User | # Copy
    EVENT | P1 | User requests copy | # Copy
    MOVE CANDIDATE | P1 | E | Copy request | # Copy
    MOVE CANDIDATE | P1 | R | Source file | # Copy
    MOVE CANDIDATE | P1 | W | Destination file | # Copy
    LINK | P1 | IDD-0049#P2 | # Related operation
    GAP | P1 | Confirmation not described

Record complete read ranges, source facts/anchors, owner process keys, alternative behavior, actor/group/persistence facts, and unresolved links. A connector-only or nonfunctional spec has an explicit no-independent-process reason. Candidate movements are not final verdicts. Never persist conventional assumptions, rate, CFP totals or effort as source facts. Keep separate ephemeral analysis records if needed; existing reader-v1 packets remain eligible when complete and matching.

## Reuse and controls

- Inventory current source IDs/paths and matching complete final packets. When using cache, reuse matching completed packets by default without automatic hash/mtime/diff or source reread. Report `Cached input — source freshness not verified` and fresh vs reused counts. Do not treat an old packet as proof of current source freshness.
- Read missing, incomplete or mismatched sources directly or with optional agents. Record concrete read failures and continue with other sources. Never claim an unprocessed file was read.
- Explicit per-ID/all refresh ignores the old extract for that invocation and rereads the current source. A failed refresh retains the old packet on disk but cannot silently substitute it as current. If cache persistence is unavailable, still analyze freshly read content and disclose inability to replace the packet.
- Explicit per-ID/all clear deletes only matching owned packets in the selected external cache directory; do not auto-delete. Clear is not refresh. Report any storage errors.
- Recalculate selected owner processes under current boundary/mode and rate on every invocation; never reuse cached CFP, assumptions or hours as authoritative. Referenced out-of-scope owners are dependencies, not additional sizing targets.
- Keep coverage and evidence available; the normal report shows aggregate coverage, material failures and cache freshness, not a mandatory per-file ledger. Detailed ledger on request.

No cache means no claim of persisted resume, not a blocked measurement.
