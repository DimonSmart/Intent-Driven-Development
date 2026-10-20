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

## Persistence and maintenance of confirmed visual requirements

Product Intent can include durable perceptual constraints without a functional
behavior change. This method supports discovery; the **separately requested**
`idd-intent-change` workflow owns normative writing and later maintenance.
An approved specification-ready proposal does not require a second interview
merely to be recorded. Unconfirmed image details or card choices do not count
as approval of colors, layout, states or all properties of a screenshot.

For every candidate record its confirmed property, `Essential` / `Preferred`
/ `Free` / `Avoid` strength, scope and explicit exceptions, admissible
alternatives, avoided traits, source role and specific aspect, observable
verification and unresolved questions. Keep unknowns separate from product
truth; if approval or scope is unclear, ask a minimal focused question.

- `Essential` means a mandatory product invariant in its agreed scope.
- `Preferred` means an intended direction with justified alternatives, not a
  mandatory pass/fail criterion.
- `Free` declares freedom and usually needs no persistence unless it prevents
  over-constraining another requirement.
- `Avoid` retains its confirmed strength, from discouraged to prohibited.
  `Non-Goals` is for actually excluded product goals, not every disliked style.

An agent without chat history should be able to find the owning spec and
interpret current visual meaning, strength, scope, exceptions and freedom.
Use an existing product-area owner, not an automatically created visual spec.
A new owner is justified only via the existing internal document-creation gate
when no current spec owns the durable constraint. `## Visual Intent` remains
optional; interaction states belong with Behavior, and important observations
and checks with Acceptance Criteria / Verification. Avoid conflicting copies
of cross-area rules.

Preserve user-significant exact red, round, or numeric identity if *exactness*
is itself confirmed. Otherwise state perceptual relationships rather than
invented styling tokens, CSS/XAML values or implementation geometry. Keep
engineering-only rules separate from Product Intent; use existing ADR/spike
routing for genuine architectural choices or unresolved research.

References are evidence, not normative substitutes. Identify the source role
(`inspiration`, `fidelity-target`, `anti-reference`, `current-state`),
the precise aspect and scope. For a fidelity target, specify allowed deviations
and a durable accessible source identifier; do not store ephemeral previews,
gallery files or OS temp paths. If precise fidelity cannot be independently
verified without a disappearing asset, block the inexpressible portion until
a stable source is supplied, while recording other confirmed independent
properties.

Later `modify`/`remove` replaces or weakens the current normative property
and reconciles scoped exceptions, acceptance criteria, verification and
dependent references. Do not leave old assertions beside updated meaning;
preserve unrelated UX, accessibility, compatibility and behavior. The history
is in Git, not an archival spec or new Visual Intent state machine.
