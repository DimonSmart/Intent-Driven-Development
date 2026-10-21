---
name: idd-intent-visual-brainstorm
description: Clarify user-confirmed Visual Intent from screenshots or mockups using professional design interpretation and optional locally supplied comparison-card images, without changing specs or code.
---

# idd-intent-visual-brainstorm

Use this read-only skill when a user provides a visual reference (screenshot,
mockup, illustration or existing UI) and wants to discover what visual
properties should become durable Product Intent.

**Pixels are evidence. User-confirmed visual relationships are intent.**

## Required references

Read `references/visual-intent-method.md` for the interview method and
`references/visual-card-library.md` for locating and **displaying** actual
comparison images. This visual-first behavior applies automatically; the user
should not need to ask for pictures or explicitly name this skill. If a
required reference is absent, report incomplete packaging.

## Ownership and boundaries

- A reference image is not itself a specification unless the user explicitly
  chooses a fidelity target and its scope.
- Distinguish Observed (visible fact), Interpreted (professional hypothesis),
  Preferred (user choice), and Normative (confirmed product requirement).
- The user's preference outranks the model's professional taste. Never treat
  design critique, one selected comparison variant, or model confidence as
  automatically normative.
- This skill never edits `.idd/intent/`, implementation files, design tokens,
  or image assets. It does not write requirements or implement UI. A temporary,
  read-only-image HTML viewer under the OS temp directory is allowed solely
  to help the user see existing references; never write preview output to
  tracked project directories.
- Durable visual constraints belong in the current owning `.idd/intent/` spec,
  not a separate `.idd/visual-intent/` tree.
- After user confirmation, recommend `idd-intent-change` for persistence;
  do not invoke it automatically or write without a separate request. Explicitly
  confirmed meaning and scope can be handed off without another interview.

## Reference roles

Classify sources as `inspiration`, `fidelity-target`, `anti-reference`,
`current-state`, or `unclear`. Clarify only if role alters the result.
For multiple sources map the relevant visual dimensions to each reference;
do not average references or inherit unrelated content accidentally.

## Workflow

1. Resolve the user's references; verify that an image can actually be
   inspected. If an image is inaccessible, state the limitation.
2. For an existing product area, read `.idd/intent/README.md`,
   `.idd/intent/INDEX.md`, then only the likely owning spec if needed.
3. Produce a temporary Visual Observation Ledger: reference + region,
   observable fact, confidence, interpretation/hypothesis, and unknowns.
4. Select 3–7 high-impact visual hypotheses, using professional lenses
   rather than exhaustively describing every pixel.
5. Pick a few relevant comparison cards for the highest-value visual
   hypotheses. **Resolve and show each real image before asking its related
   question**, following the display/fallback procedure in
   `references/visual-card-library.md`. Never say you showed a card if you
   only read its catalog entry, or if the preview did not render/open.
6. Ask 3–5 contrastive questions per round, in plain language. Pair each
   question about a visual property with its relevant **visible** image(s).
   A card is a teaching aid, not a required fixed set of answers. If a
   genuinely non-visual question needs no image, explain it in text.
7. Accept a mixed response, 'none', 'not important', 'depends on component',
   or user-specified change to a shown example.
8. Capture user-confirmed preferences as Essential / Preferred / Free / Avoid
   with scope (global, screen, component, state, theme), agreed exceptions,
   obligation strength, permitted alternatives and rejected traits, and map
   each to the specific aspect of the relevant reference.
9. Prefer perceptual relations over CSS values. Record exact colors, fonts,
   distances or radii only when exact identity is product-significant.
10. Identify unknown dynamic properties, conflicts and gaps; do not infer
    responsive behavior, hover, focus, loading, motion, accessibility or
    localization from a single static screenshot.
11. Produce a specification-ready Visual Intent proposal with candidate
    perceptual checks and separate uncertainties; ask the user to confirm its
    meaning and scope or correct it. Stop before changing any project files.

## Contrastive interviewing

Poor: 'Do you like the layout?'

Better: 'On your screenshot, sections are separated mostly by whitespace
rather than card borders. Do you want to preserve this grouping, or prefer
more explicit containers? I can show both approaches.'

Poor: 'Choose the exact 16px radius.'

Better: 'Do you want the overall shape language to feel soft and rounded,
or are only these particular cards important?'

Translate vague words such as clean, airy, modern, premium and friendly into
several candidate properties. Do not persist a vague adjective alone when
a confirmed more precise description is available.

## Output during discovery

Give a concise professional reading of the user reference and distinguish
observations from hypotheses. For a normal visual interview, accompany
visual questions with **actual visible comparison cards**, not just IDs or
variant names. Keep the image and related question adjacent; when inline
rendering is unavailable, offer/open the generated gallery containing cards
and questions together. Ask up to five focused questions and list material
unobservable facts. If card viewing is blocked, state what failed rather
than quietly falling back to text-only comparisons.

## Specification-ready output

```md
# Specification-Ready Visual Intent

## Reference Roles / Source-to-Aspect Map
| Reference | Role | Visual aspect | Scope |
| --- | --- | --- | --- |

## Essential
- ...

## Preferred
- ...

## Free
- ...

## Avoid
- ...

## Unobservable or Dynamic Properties
- ...

## Candidate Perceptual Acceptance Criteria
- ...

## Handoff to idd-intent-change
- Current owning product area / candidate spec:
- Confirmed properties, obligation strength, scoped exceptions, permitted alternatives:
- Rejected traits and their strictness:
- Supporting durable reference (if available), role and relevant aspect:
- Candidate observable checks / meaningful verification:
- Confirmation decision (approved meaning and scope; do not infer from a card):
- Unconfirmed observations, conflicts and open questions (not normative):
```

Normative text should survive loss of a screenshot, asset path, image reader,
or UI framework. The reference image is supporting evidence except when
explicitly confirmed as a scoped fidelity target. For exact image-dependent
fidelity, flag a missing durable resolvable reference as a blocker for the
unverifiable portion only; do not persist temp gallery paths as lasting links.
A subsequent explicit request to save an approved handoff belongs to
`idd-intent-change`, without another brainstorm round.
