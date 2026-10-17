# Visual Intent comparison-card source library

This directory is canonical, distributable **skill asset source** for
`idd-intent-visual-brainstorm`.

Copy your verified library here:

```text
src/canonical/visual-intent-library/
  VISUAL_INTENT_CATALOG.md
  Images/
    V01-<descriptive-name>.png
    V02-<descriptive-name>.png
    ...
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
