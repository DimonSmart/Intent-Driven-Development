# Visual comparison cards: locating and using a local library

The Visual Intent Reference Library teaches design vocabulary to people
without formal design training. It is optional, not normative.

## Typical project-supplied layout

```text
VisualIntent/
  VISUAL_INTENT_CATALOG.md
  Images/
    ... one or more images associated with V01–V50 ...
```

Example Windows location (NOT a built-in default):
`E:\Private\VisualIntent\`.

## Locate and inspect

1. Prefer a catalog/image location explicitly supplied by the user.
2. Otherwise, use an already-configured workspace library, or an unambiguous
   `VISUAL_INTENT_CATALOG.md` with adjacent `Images/` in the current project.
3. Do not browse unrelated drives, repositories or private folders looking
   for an assumed library. Do not assume every user's library has 50 assets.
4. Read the requested catalog entry, its designer term, contrast variants,
   suggested question and comparison focus. Treat variant descriptions as
   guidance, not a prescription for UI rendering.
5. Prefer an explicit image filename mapping in the catalog. Otherwise
   match the whole card ID (e.g. V09) in filenames inside the designated
   `Images/` directory; check ambiguous matches rather than guessing.
6. Read the actual image using host capabilities when available. Do not
   infer which alternative appears where from the filename alone.
7. Display the actual image using a supported local image-rendering facility.
   If this is impossible, state the limitation and use a grounded textual
   comparison; do not falsely claim an image was shown.

## Conversation pattern

Start from the user's screenshot: 'This area seems grouped mainly by space.'
Introduce a relevant term: 'Designers call this grouping by proximity.'
Show a corresponding comparison card if accessible.
Ask: 'Which principle would you like to preserve, if any?'
Then clarify scope and strength; record the semantic answer, not the
library option label or image ID alone.

Allow none, mixed alternatives, 'depends on screen' and modification.
Choose only relevant cards, generally a few per round. Do not present
50 questions or force fixed visual templates on unrelated concepts.

## Important non-assumptions

- No fixed aspect ratio, grid, demonstration UI or number of variants.
- Some concepts require several co-varying design characteristics.
- A static motion comparison cannot establish timing or easing.
- The library's catalog and approved images may evolve independently.
- The plugin must work without the library, but it must be explicit when
  it could not show requested image aids.
