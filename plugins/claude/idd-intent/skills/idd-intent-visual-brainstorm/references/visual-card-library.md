# Visual comparison cards: mandatory preview for visual questions

The Visual Intent Reference Library teaches professional design vocabulary.
Cards are optional **topics** (not every interview asks about every topic),
but when a visual topic is asked and its reference image is available,
**show the actual image before the question**. Do not substitute a catalog
ID, text description or inferred variant name for a viewable image.

## Library resolution

Prefer the user's explicit catalog/image directory when supplied. Otherwise
use the bundled library relative to this skill:

```text
skills/idd-intent-visual-brainstorm/
  SKILL.md
  references/visual-card-library.md
  assets/visual-intent/
    VISUAL_INTENT_CATALOG.md
    Images/01.png ... Images/50.png
    Show-Cards.ps1
```

An explicitly supplied project library overrides bundled cards.
Do not scan unrelated directories or hardcode a user's drive letter.
Source files are maintained at `src/canonical/visual-intent-library/` in
the IDD repository and packaged under `assets/visual-intent/`.

## Map IDs to actual image files

Read the catalog entry for a concept and identify its stable ID.
In the present bundled library, **V09 maps to `Images/09.png`**;
**V15 to `Images/15.png`**, and so on, up to V50/`50.png`.
Match filenames in this order:

1. Any explicit asset mapping supplied by the catalog.
2. Exact zero-padded numeric basename (`09.png`) or exact ID basename
   (`V09.png`), regardless of supported image extension.
3. A single filename beginning with the whole ID followed by a separator
   (`V09-density.png`). Never mistake `V09` for `V090`.

If zero or multiple images match, report the ambiguity; do not invent a file.
Inspect the actual image with available image-reading tools before explaining
its variants. **Never assert that variant C is compact solely because the
catalog says so**, unless the visual content has also been verified.

## Show pictures together with questions

For every round, choose a small number of high-impact visual properties
(typically 2–4 cards for 3–5 questions). Resolve the images and display
them in the same response, immediately before or beside the questions.
Example:

```markdown
### V09 — Visual Density
![Actual V09 card](resolved_image_uri)
Which density works best for the History list in your app?

### V15 — Grouping
![Actual V15 card](resolved_image_uri)
How would you like related controls and panels to be grouped?
```

The `resolved_image_uri` above is a placeholder, not a literal URL. Substitute
a verified image URI or use a native image attachment/preview facility.
Do not assume every Codex host will render local Markdown images inline.

## Host fallback: one local gallery, not invisible IDs

When host-native inline images are unsupported or render as broken
placeholders, use the **packaged** `assets/visual-intent/Show-Cards.ps1`
if PowerShell 7 and a browser are available:

```powershell
pwsh <absolute-path-to-skill>/assets/visual-intent/Show-Cards.ps1 -Cards V09,V15,V30
```

Resolve `<absolute-path-to-skill>` from the installed skill location; this
is a path example, not literal shell syntax. The script opens a local HTML
gallery with full reference images and their associated catalog questions.
It writes only a temporary HTML file under the OS temp folder; it does
not modify `.idd/intent/`, application code or image assets.
The script also prints the path so the user can open the gallery directly
when automatic browser opening is blocked. Prefer to present a clickable
file link if supported, otherwise the exact path and instructions to open it.
On hosts without pwsh or a graphical browser, offer native image previews
or verified per-image links/paths. Clearly state if displaying pictures
is impossible; do not pretend it succeeded.

After showing the images or gallery, pose the **same grounded questions in
chat** so the user can continue the interview naturally.

## Conversation pattern

Start from an observed property of the user's own screenshot. Name a
professional concept and select the matching card. Show the card or gallery.
Then ask which visual property matters, where it applies and how strongly.
Allow mixed approaches, none, not important and per-screen exceptions.
Store the confirmed semantic preference, not a card ID or variant label.

## Limits

- Do not force a 50-question interview or one static layout for all cards.
- Do not infer interaction, animation timing or responsive states from static images.
- Never publish a temporary preview into tracked product files.
- If the user explicitly declines pictures, use text comparisons instead.
