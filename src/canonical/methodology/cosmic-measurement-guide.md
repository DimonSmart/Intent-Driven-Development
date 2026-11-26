# COSMIC v5.0 — practical functional sizing from Product Intent

Normative reference: [COSMIC Measurement Manual v5.0](https://cosmic-sizing.org/wp-content/uploads/2022/11/Combination-of-Parts-v5.0-Sep-2024.pdf), combined September 2024. IDD's default is an **approximate COSMIC-based estimate**, not certified measurement. The strict mode applies the source-backed rules below without conventional assumptions. No implementation inspection or executable validator is required.

## Counting rules (both modes)

- Strategy (Rules 1–9): use one declared boundary, layer and granularity. Default whole product includes internal clients/services and logical persistent storage. External functional users can be humans, other software/hardware or a clock.
- Process (Rule 10): a cohesive response to an external triggering event; at least one initiating Entry and one Exit or Write. Count independent user capabilities, not endpoints, buttons, paragraphs, internal agent calls, retries or success/error alternatives as separate processes. A callback is a process only if it independently triggers a required functional response at the boundary.
- Data group (Rule 11): logical attributes of an object of interest meaningful to the functional user; not each field, DTO, SQL table or screen row.
- Movements (Rules 12–20): E enters from an external functional user, X exits to one, R reads logical persistent data, W changes/deletes persistent data. Temporary working memory, transformations, computations and internal communication are not movements. Storage implementation (files, SQL, blob) is irrelevant if it has the same logical role. An independently required exchange with a genuinely external system is E/X, not automatically R/W.
- Repetitions: count each distinct movement type/data group/functional user as applicable once per process unless FUR requires distinct movements. Loops, record counts, calls, retries and frequency do not multiply CFP. Alternative human error/confirmation messages normally share one special X; substantive business data X is separate.
- Ownership: each process belongs to one normative current spec. LINK connectors contribute no extra CFP. A duplicate normative definition is a structure-audit issue; do not merge distinct owners by semantic similarity or double count a known conflict. A reference with no defined owner is not an invented independent process.
- Aggregation (Rules 21–23): CFP = E+X+R+W; assign each distinct included owner process to one functional area. Sum compatible units only; never add a component to its containing product. Coupling is a separate optional change-impact metric.
- Reporting (Rule 25 informs strict reports): record scope, boundary, source coverage, process/movement basis, result and limits. Detailed source anchors remain available; no mandatory verbose audit trail in the standard report.

## Approximate mode: minimal conventional assumption

Classify each central-estimate movement once, in this precedence:

1. **Explicit:** the source directly states the movement's functional meaning.
2. **Implied:** logically necessary to the stated behavior (e.g. opening a saved document reads it and supplies it to the user).
3. **Assumed:** a plausible minimal conventional behavior is needed to complete the estimate (e.g. an underspecified save uses ordinary logical persistent storage). State the assumption and why it fits.

Prefer the simplest common interpretation compatible with stated requirements. Choose logical movements, not a speculative technology stack. Do not assume features such as versioning, backups, audit, conflict resolution, extra actors, notifications or extra CRUD operations merely because common products have them. Never override an explicit prohibition (e.g. no persistence). Do not fabricate facts from an inaccessible source or infer an entirely new process from an unresolved LINK. If a described caller needs a typical persistence step, estimate that step in the caller only when it is not double-counting a separately owned capability. Do not ask routine clarifying questions just to resolve implementation details.

These examples are **starting patterns**, not fixed counts or automatic feature lists. Apply only to an identified requirement, adapt groups/recipients/alternatives to evidence:

| Known capability | Minimal typical movements | CFP |
| --- | --- | ---: |
| Create / save supplied record | E record; W record | 2 |
| Open / read saved record | E request; R record; X record | 3 |
| Update existing record with current state needed | E update; R current record; W record | 3 |
| Delete saved record | E identifier; W deletion | 2 |
| Search / list stored records | E query; R records; X results | 3 |
| Import submitted data to persistent storage | E data; W data | 2 |
| Export saved data to user | E request; R data; X result | 3 |
| Calculate without persistent data | E input; X result | 2 |
| Scheduled notification based on stored data | E clock trigger; R data; X notification | 3 |

Only include an extra confirmation/error X if stated, necessarily implied or a justified conventional alternative. For a plain save, use 2 CFP centrally; if confirmation is a credible material alternative, 2–3 CFP with rationale is acceptable. Do not automatically assume a read for an update that simply replaces supplied data. Record multiple logical groups when supported. Mark substantial ambiguity as a justified range, not a fabricated accuracy percentage.

## Strict mode

Use explicit or logically necessary, source-backed functional movements only. Anchor each process/movement to the actual source path and substantive heading/quote. Include required success/error alternatives and check Rule 10c. If a material gap prevents a defensible complete process, mark it unmeasured; keep independently measured processes. A partial subtotal is not a full product measurement. No conventional assumptions in a strict count. Explain boundaries and detailed evidence when requested.

Examples: the documented Add a Professor COSMIC example (Part 3d §4.1) is E professor details, R existing professor details, W new professor, X human error/confirmation = 4 CFP. A temporary image resize can be E image + X image = 2 CFP despite substantial computation.

## Effort model (separate from COSMIC)

IDD's **3 person-hours/CFP** default is an illustrative, uncalibrated heuristic, not a standard or empirical productivity claim. A user-provided positive finite scalar or ordered positive range overrides it. Compute effort = CFP × rate, applying both bounds if present; use midpoint rate for a central estimate of a rate range. Report rate and provenance, assessed scope, and one short caveat. No team size, duration, currency or invented complexity multiplier. No number if no CFP; honor CFP-only requests. A partial size yields partial effort. Algorithms, security, infrastructure, testing and other nonfunctional work may not scale with CFP; actual rates should be calibrated to comparable historical work.
