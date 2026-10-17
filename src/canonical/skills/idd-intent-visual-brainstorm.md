# idd-intent-visual-brainstorm

Use this read-only skill when a user provides a visual reference (screenshot,
mockup, illustration or existing UI) and wants to discover what visual
properties should become durable Product Intent.

**Pixels are evidence. User-confirmed visual relationships are intent.**

## Required references

Read `references/visual-intent-method.md` for the interview method. When visual
comparisons would help, read `references/visual-card-library.md` for locating
the user's catalog and existing card images. If a required reference is absent,
report incomplete plugin packaging; do not invent its instructions.

## Ownership and boundaries

- A reference image is not itself a specification unless the user explicitly
  chooses a fidelity target and its scope.
- Distinguish Observed (visible fact), Interpreted (professional hypothesis),
  Preferred (user choice), and Normative (confirmed product requirement).
- The user's preference outranks the model's professional taste. Never treat
  design critique, one selected comparison variant, or model confidence as
  automatically normative.
- This skill never edits `.idd/intent/`, implementation files, design tokens,
  or image assets. It does not write requirements or implement UI.
- Durable visual constraints belong in the current owning `.idd/intent/` spec,
  not a separate `.idd/visual-intent/` tree.
- After user confirmation, recommend `idd-intent-change` for persistence;
  do not invoke it automatically.

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
5. Ask 3–5 contrastive questions per round, in plain language. Explain the
   professional term using the user's own screenshot.
6. Where helpful, use actual comparison images from the user-provided
   Visual Intent Reference Library to clarify alternatives. A card is an
   educational aid, not a mandatory questionnaire or design system.
7. Accept a mixed response, 'none', 'not important', 'depends on component',
   or user-specified change to a shown example.
8. Capture user-confirmed preferences as Essential / Preferred / Free / Avoid
   with scope (global, screen, component, state), and map to their references.
9. Prefer perceptual relations over CSS values. Record exact colors, fonts,
   distances or radii only when exact identity is product-significant.
10. Identify unknown dynamic properties, conflicts and gaps; do not infer
    responsive behavior, hover, focus, loading, motion, accessibility or
    localization from a single static screenshot.
11. Produce a specification-ready Visual Intent proposal and ask the user
    to confirm or correct it. Stop before changing any project files.

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

Give a concise professional reading of the user reference, distinguish
observations from hypotheses, mention relevant card IDs only when actually
used, ask up to five focused questions, and list material unobservable facts.

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
- Confirmed visual relationships and scopes:
- Supporting reference locations:
- Conflicts and open questions:
```

Normative text should survive loss of a screenshot, asset path, image reader,
or UI framework. The reference image is supporting evidence except when
explicitly confirmed as a scoped fidelity target.
