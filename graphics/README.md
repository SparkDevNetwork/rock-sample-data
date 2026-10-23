# Rock Solid Church graphics

Reusable artwork for Rock Solid Church programs, initiatives, and campaigns lives here. Architectural photos showing artwork installed in the building remain in [the church reference pack](../photos/references/church/README.md).

## Initiative folders

Use one lowercase, hyphenated folder per initiative under `initiatives/`:

```text
graphics/
  README.md
  initiatives/
    starting-point/
      starting-point-hero-v1.png
      manifest.json
    <initiative-name>/
      <initiative-name>-<format>-v1.<extension>
      manifest.json
```

[Starting Point](initiatives/starting-point/starting-point-hero-v1.png) is the first initiative. Its [manifest](initiatives/starting-point/manifest.json) records the wording, generation prompt, native dimensions, and lobby application.

## Adding graphics

- Keep original artwork and editable source files in the initiative's folder. Preserve native dimensions; name additional formats and revisions clearly, such as `starting-point-social-v2.png`.
- Keep a `manifest.json` with the initiative name, review status, file paths, dimensions, exact visible wording, and source or generation details. Record where the artwork is used.
- Retain the approved version when creating a new concept or revision. Put future screen, social, print, and wall adaptations in the same initiative folder with distinct filenames.
- Use [the approved church logo](../photos/references/church/branding/rock-solid-church-logo.png) as the brand source. Match the warm ivory, charcoal, teal, and restrained coral identity where appropriate.
- When artwork changes an architectural reference, update that photo's PNG/JPEG pair, manifest, and guide together. Link the artwork here instead of duplicating campaign masters under `photos/`.

These files are reusable design assets. Saving them here does not automatically assign them to Rock records or include them in install packages.

The public site reads every initiative folder. The church page shows the first image of each, and the brand page offers each image as a download. When you add an initiative, also add its words to the Initiatives section in `site/src/pages/church.js`.
