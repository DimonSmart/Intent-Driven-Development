# COSMIC report template

Presentation only: render the already assessed result; do not change the estimate or repeat the measurement to fit this layout. Translate labels into the user's language. Use ordinary portable Markdown, a single title, bold headline figures and compact tables. No required charts, badges, decorative ASCII art, JSON or host-specific markup. Replace placeholders with actual values and omit inapplicable fields/sections.

## Default layout (Markdown)

```markdown
# {Project} — Functional size assessment

*Intent-Driven Development · COSMIC v5.0 · {Approximate / Strict}*

**{CFP} CFP** · **{effort} person-hours**

| Metric | Result |
| --- | ---: |
| Functional size range | {low–high CFP, when justified} |
| Effort range | {low–high hours, when justified} |
| Functional processes | {included; unestimated if known} |
| Source coverage | {assessed/total; fresh/cached} |
| Effort rate | {rate and provenance} |

**Scope:** {documented scope and boundary}  
**Status:** {Complete / Partial / Diagnostic only}  
**Source revision:** {if readily available}

## 1. Functional breakdown

| Functional area | Processes | CFP | Person-hours |
| --- | ---: | ---: | ---: |
| {area} | {count} | {central (range)} | {central (range)} |
| **Total** | **{count}** | **{CFP}** | **{hours}** |

## 2. Measurement profile

| Entry (E) | Exit (X) | Read (R) | Write (W) | Total CFP |
| ---: | ---: | ---: | ---: | ---: |
| {E} | {X} | {R} | {W} | **{CFP}** |

**Evidence:** {explicit} explicit · {implied} implied · {assumed} assumed movements.

## 3. Functional processes

| Owner | Process / trigger | Logical movements | CFP |
| --- | --- | --- | ---: |
| {IDD-NNNN} | {process} | {E group; R group; X group; …} | {central (range)} |
| **Total** | | | **{CFP}** |

## 4. Assumptions and limitations

{Material assumptions, why bounds differ, actual gaps, and significant limitations.}
```

Use the headline as the primary result; do not repeat it in a prose introduction. Order functional areas by size or functional flow. Keep movement descriptions specific enough to verify the count, including material external users, and place short assumption notes in the corresponding row or section 4. Link source IDs if useful. All totals, evidence counts and ranges must match the assessment. For very large scopes, group the summary and provide a retrievable detailed Markdown appendix when feasible; never silently drop counts or evidence.

For **Partial**, label headline and totals as the assessed subtotal (measured subtotal in Strict), list actual missing/unread paths and unestimated processes. **Complete** means assessed documented scope, not proof of product completeness. For **Diagnostic only**, show the diagnostic and gaps without invented CFP or effort. Show zero only with positive evidence. Omit effort headline, range and columns for CFP-only or when effort cannot be calculated; explain an invalid rate or unavailable estimate when relevant. Omit unjustified ranges rather than repeat central values. If cache was reused, include `Cached input — source freshness not verified`. Explain uncalibrated rates and relevant algorithmic/nonfunctional limitations once. Detailed evidence anchors and coupling/change impact are on request (required Strict evidence remains available); coupling never increases CFP.

## Optional HTML export

Only on explicit request, render the same result in a self-contained HTML file outside the target repository. Use a restrained report style: headline metric cards, readable tables, optional proportional area/evidence bars, responsive layout, light/dark system theme, and print CSS. Keep numeric labels visible without relying on color. Use inline CSS, no remote assets, JavaScript or generator dependency; escape source text. Omit unavailable metrics exactly as in Markdown. If no writable external location is available, explain and return Markdown. Do not automatically generate any file.
