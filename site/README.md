# Site

The public site for Rock Sample Data: why the sample data exists, Rock Solid Church, the Cast, the datasets, how it's built, the brand kit and photo downloads, and how to contribute. GitHub Pages publishes it from `main` (decision 80) at [sampledata.rockrms.com](https://sampledata.rockrms.com/). The domain is a CNAME in rockrms.com's DNS (DNSimple) pointing to `sparkdevnetwork.github.io`, set as the custom domain in the repository's Pages settings. The old address, sparkdevnetwork.github.io/rock-sample-data, redirects to it.

The site is built from the repository's own files, so it never keeps a copy of the Cast or the church. Change a household in `cast/households/`, and its page changes on the next build.

Campus cards use the seven exterior photos in `photos/campuses/manifest.json`. The brand page offers the JPEG set as a zip and each image as a JPEG or original PNG. The twelve-view building tour describes Phoenix; other campus exteriors have distinct venue types.

The church page has a Staff section, built from `configuration/staff.json`, with a card for each of the 23 staff linking to their household page, and each campus card names its campus pastor. A staff member whose profile photo is still to be made shows a monogram, and their page says the photo is coming soon. Each staff member's household page shows their staff page bio and favorite quote from `configuration/staff.json`, and a staff card shows the staff photo once it is delivered (`photos/staff/manifest.json`). The Staff section's departments match the org chart in `staff.json`. The church page also has an Initiatives section, and the brand page offers each initiative's artwork as a PNG. Both read every folder in `graphics/initiatives/`, so new artwork appears on the next build. The words for each initiative are in `src/pages/church.js`. A folder with no words there still shows, with its artwork's concept as the text.

On phones, the top navigation folds behind a Menu button. On tablets, the links wrap onto their own row. Without script, phones get the wrapped row.

## Build and look at it

You need Node.js 22 or later. No packages are installed.

```
node site/build.js
```

That writes the site to `site/dist/` (git ignores it). Open `site/dist/index.html` in a browser. Every link is relative, so the site works straight from disk. The fonts load from Google Fonts, so you need an internet connection to see them.

## Publishing

The [Publish site](../.github/workflows/pages.yml) workflow runs the same build and publishes `site/dist` when `main` changes the site, the Cast, the configuration, the photos, the graphics, or the two docs the site reads. To turn it on once, set **Settings > Pages > Source** to **GitHub Actions**.

## What is where

| Path | What it holds |
|---|---|
| `build.js` | Copies the photos, builds the downloads and their zips, and writes every page. |
| `src/data.js` | Reads `cast/`, `configuration/`, `photos/`, and `graphics/` into one model. It never reads `calibration/` (decision 82). |
| `src/layout.js` | The header, navigation, and footer every page shares. |
| `src/components.js` | Shared pieces: section headings, portraits, household cards, downloads, and zoomable photos. |
| `src/pages/` | One file per page. `cast.js` also writes one page per household. `contribute.js` reads the issue questions and the roadmap from `docs/`. |
| `src/zip.js` | Writes the download zips with Node's built-in zlib. |
| `src/assets/` | The stylesheet, the small script, and the Rock RMS logos. |

## Rules for the site

- Never show a Cast address, email, or phone number (decision 82).
- Never name or describe a calibration source (decision 8). Describe calibration only as a method.
- Say that every person and photo is fictional, and that the photos are AI-generated.
- Write plain, short sentences with no em dashes, like the rest of the repository.
- The Rock Solid Church logo files live in `photos/references/church/branding/`. The site copies them; it does not keep its own.
