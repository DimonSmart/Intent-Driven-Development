# COSMIC functional size and effort estimation (IDD)

`idd-intent-cosmic` estimates functional size from current numbered Product Intent, even before code exists. By default it uses **Approximate** COSMIC-based early sizing with conventional assumptions. Explicitly request **Strict COSMIC measurement** for evidence-only counting. It is independent of qualitative `idd-intent-estimate` and does not modify the target project.

## Usage

- "Estimate the functional size of the product."
- "Use idd-intent-cosmic for IDD-0012 and IDD-0042, at 5 person-hours per CFP."
- "Estimate the document module using 2–4 hours per CFP."
- "Strict COSMIC measurement, CFP only."
- "Show detailed movement evidence and coupling for the import process."
- "Export the assessment as a standalone HTML report."

Default boundary: the whole documented product, including internal clients/services and logical storage. An explicit selected scope/boundary takes precedence. The skill reads all accessible current normative specs, identifies owner-defined processes and counts logical Entry, Exit, Read and Write movements. References to an owner are not extra processes or movements. A quick consistency pass checks functional behavior, logical groups, external exchanges and state retained between processes (even in memory); no exhaustive audit or separate data-group catalog is required.

## Reasonable defaults

The estimate combines **Explicit** requirements, logically **Implied** behavior and minimal conventional **Assumed** behavior. For "save a document", count E + W (2 CFP) even when the storage mechanism is not specified. Opening a saved document typically means E + R + X (3 CFP). A confirmation might increase the save estimate to 3 CFP if plausible. These are patterns, not fixed counts; do not invent features or contradict the source. Keep material assumptions distinct from normative requirements. Missing details alone do not block estimation.

Use justified ranges if plausible interpretations materially change counts. An unidentifiable or contradictory process may be left unestimated without suppressing the rest. If scope is incomplete, publish the assessed subtotal, not a full-product total. Strict mode does not use conventional assumptions.

## Person-hours

Default: **3 person-hours/CFP**, an illustrative uncalibrated IDD heuristic, not an industry average. The user's positive rate or ordered positive rate range replaces the default for that request. Effort = CFP × rate. Display the actual rate; if the size or rate has bounds, propagate those bounds. No team size, elapsed duration or automatic overhead. Honor CFP-only requests. This is not a COSMIC measurement rule or a delivery commitment. Actual labor varies with algorithms, nonfunctional work, tooling and team productivity; use calibrated historical rates when available.

## Example Markdown report (illustrative)

### Document editor — Functional size assessment

*Intent-Driven Development · COSMIC v5.0 · Approximate*

**7 CFP** · **21 person-hours**

| Metric | Result |
| --- | ---: |
| Functional size range | 7–8 CFP |
| Effort range | 21–24 hours |
| Functional processes | 3 |
| Source coverage | 2/2 (fresh) |
| Effort rate | 3 hours/CFP (IDD default) |

**Scope:** Document editor, documented whole product.  
**Status:** Complete.

#### 1. Functional breakdown

| Functional area | Processes | CFP | Person-hours |
| --- | ---: | ---: | ---: |
| Document storage | 2 | 5 (5–6) | 15 (15–18) |
| Document preview | 1 | 2 | 6 |
| **Total** | **3** | **7 (7–8)** | **21 (21–24)** |

#### 2. Measurement profile

| Entry (E) | Exit (X) | Read (R) | Write (W) | Total CFP |
| ---: | ---: | ---: | ---: | ---: |
| 3 | 2 | 1 | 1 | **7** |

**Evidence:** 4 explicit · 2 implied · 1 assumed movements (illustrative).

#### 3. Functional processes

| Owner | Process | Logical movements | CFP |
| --- | --- | --- | ---: |
| IDD-0001 | Save | E document; W document | 2 (2–3) |
| IDD-0001 | Open | E request; R document; X document | 3 |
| IDD-0002 | Preview uploaded file | E file; X preview | 2 |
| **Total** | | | **7 (7–8)** |

#### 4. Assumptions and limitations

Save assumes ordinary persistent storage; optional human confirmation explains the upper bound. No extra versioning/audit is assumed. Effort uses an uncalibrated heuristic, not a calendar commitment.

The [report template](../src/canonical/methodology/cosmic-report-template.md) is packaged with the skill independently of the counting rules. Ordinary Markdown is the default; an external Markdown or standalone HTML export is available on explicit request. HTML uses the same numbers, with optional charts, system light/dark theme and print styling. No report is automatically written inside the target project.

## Scaling, caching and limitations

Native child agents, bounded reduction and an external project-specific numbered Markdown cache are optional; a small project can be analyzed directly. For large projects, preserve source coverage and owner identities across batches. Missing agent/cache capabilities alone never block an assessment.

When using cache, reuse completed `IDD-NNNN.cosmic.md` extraction packets with matching ID/path. Write new extracts through `.cosmic.partial.md` and publish by rename. Packets contain facts and candidates, never normative assumptions or authoritative CFP/hours. Recompute each request; reused packets carry the warning `Cached input — source freshness not verified`. Explicitly refresh or clear selected/all packets when desired. No hashes, watchers or executable validator are required. If cache unavailable, analyze directly.

Read all accessible sources; state fresh/reused coverage, actual unread/missing paths and unestimated functionality. Use Complete for fully assessed documented scope (not proof that the product is fully specified), Partial for an assessed subtotal, Diagnostic only if no defensible numeric result exists. Missing sources are not zero size. Algorithms can be small in CFP and large in implementation difficulty. Coupling belongs primarily to the separate qualitative assessment; it can be requested as a detailed non-COSMIC appendix and never increases CFP.
