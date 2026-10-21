# Starting Point graphics handoff

October 21, 2026. Changes are saved and **uncommitted** in `C:\Users\JonEdmiston\repos\rock-sample-data-serving`, branch `serving`, based on `c627f74`. Please review and commit this asset update; nothing has been pushed.

The lobby's right-hand wall now uses the Starting Point design with ivory, teal ribbons, and a coral point. Its mission wall and three double-door pairs remain clear. The shared library for future initiative artwork is `graphics/initiatives/<initiative-name>/`, documented in [graphics/README.md](../graphics/README.md).

## Files to include

- `graphics/README.md`
- `graphics/initiatives/starting-point/starting-point-hero-v1.png` and `manifest.json`
- `photos/references/church/lobby-toward-worship-center.png` and `.jpg`
- `photos/references/church/manifest.json` and `README.md`
- Root `README.md`, `photos/README.md`, and `photos/serving-photo-handback.md`
- This handoff: `docs/starting-point-handoff.md`

The campaign master is 1672 by 941 pixels. The lobby originals and quality-90 JPEG are 1536 by 1024; JPEG metadata is removed. Manifests retain the exact generation/edit prompts and current artwork paths. Starting Point was moved out of the temporary `photos/programs/` location; use the new graphics library.

## Commit and integration

Review `git status --short` and `git diff`, stage the files above, run `git diff --cached --check`, and commit with a message such as `Add initiative graphics library and Starting Point lobby design`. Preserve any unrelated work that appears after this handoff. For integration into main, follow [the serving branch handback](../photos/serving-photo-handback.md), including its wider serving implementation review.

Checks completed: image dimensions, complete church reference pairs, JSON parsing, current file references, document links, and diff whitespace. No application code changed in this update.
