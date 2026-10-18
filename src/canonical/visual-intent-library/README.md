# Visual Intent comparison-card source library

This directory is canonical, distributable **skill asset source** for
`idd-intent-visual-brainstorm`.

Copy your verified library here:

```text
src/canonical/visual-intent-library/
  VISUAL_INTENT_CATALOG.md
  Images/
    01.png
    02.png
    ...
    50.png
  Show-Cards.ps1
```

Use the existing `E:\Private\VisualIntent\VISUAL_INTENT_CATALOG.md`
and `E:\Private\VisualIntent\Images\` as the **source** of the copy.
Keep the catalogue's actual filenames and stable V01–V50 IDs.
Do not rename already-approved image files merely to match these examples.

The generator automatically packages all files under this directory in
`skills/idd-intent-visual-brainstorm/assets/visual-intent/` in both platform
plugins. New images do not require manually editing the manifest.

The Visual Intent catalog and images are design vocabulary reference
material, **not project-specific Product Intent**. The installed skill may
use externally supplied libraries as overrides or fallbacks.

Keep images reasonably sized and ensure that they may be redistributed before
committing them to this public repository.

## Showing cards during an interview

Visual comparison questions should normally include the actual image.
Do not assume a Codex terminal or Codex desktop host can render local PNG
references inline. This folder includes an offline HTML gallery fallback:

```powershell
pwsh ./Show-Cards.ps1 -Cards V09,V15,V30
```

By default it opens a browser window with real images and corresponding
catalog questions, and prints the temporary gallery file path.
Use `-NoOpen` to generate without launching a browser, for example in CI.
The gallery is stored in the OS temp folder; the skill never changes app
source, `.idd/intent/`, or the image library when showing cards.
