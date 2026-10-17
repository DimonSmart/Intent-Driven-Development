# Visual Intent discovery method

This methodology is reusable designer vocabulary and interview guidance.
It is NOT product intent for any particular application.

## Professional lenses

- Composition: focal point, visual hierarchy, weight, balance, grid, proportion, optical alignment.
- Grouping: proximity, similarity, common region, containerization and scan path.
- Space: margins, padding, whitespace, visual density, spacing rhythm.
- Typography: typeface character, hierarchy, weight, leading, line length and reading alignment.
- Color: palette, chroma, tonal contrast, temperature, accent strategy and functional roles.
- Shapes: geometric/organic language, corner treatment, borders and contours.
- Surfaces: flatness, layers, elevation, translucency, shadows and texture.
- UI cues: affordances, action prominence, control scale, iconography, image framing.
- UX structure: information hierarchy, progressive disclosure, scannability.
- Motion/states/responsiveness: discuss only when visible or user-supplied.

## Observation-ledger example (temporary)

```yaml
reference: screenshot-A
observed:
  - region: content
    fact: Sections are separated mainly by whitespace.
    confidence: high
interpreted:
  - Grouping through proximity may create the calm appearance.
unknown_from_static_reference:
  - responsive reflow
  - focus state
```

**Observed is not interpreted; interpreted is not preferred; preferred is
not normative until the user confirms scope and importance.**

## Discovery protocol

Identify a small number of high-value hypotheses. Refer to specific regions.
Phrase questions in everyday language and offer alternatives.
Treat terms such as 'airy' as potentially caused by low visual density,
generous outer margins, large group spacing, light text, or low
containerization. Check rather than assume.

When a user selects option B on comparison card V15, do not store
'V15 B' as product truth. Ask what about B matters, where it applies,
and whether an equivalent implementation would be acceptable.

Classify confirmed properties as Essential, Preferred, Free or Avoid.
Negative reference images may define explicit anti-patterns. For multiple
references, attach each confirmed aspect to a source and scope.

## Durable handoff

Record only confirmed visual properties that affect whether a future
implementation would still be the desired product.
Prefer statements such as 'primary actions visibly dominate secondary
actions, with a restrained palette' to copied values like '#4377ee'.

An owning spec may have an OPTIONAL `## Visual Intent` section.
Related UX behavior belongs in Behavior; visual prohibitions can be
Non-Goals; perceptual criteria belong in Acceptance Criteria and Verification.
Do not replace durable written meaning with a filename or screenshot.
