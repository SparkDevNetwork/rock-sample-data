# Campus exterior photo handoff

October 23, 2026. Seven campus exteriors are complete and saved for user review in `C:\Users\JonEdmiston\repos\rock-sample-data-campus-exteriors`, branch `codex/campus-exteriors`. The work starts from main commit `ce702e2` and remains uncommitted. No merge or push has been made.

## Delivered assets

- [Seven full-size JPEGs and manifest](../photos/campuses/README.md) in `photos/campuses/`.
- Seven unchanged generated PNG originals in `photos/originals/campuses/`.
- All photos are 1536 by 1024 pixels. JPEGs use quality 90 with metadata removed. The manifest records each campus key and Guid, exact prompts, references, and review notes.
- Phoenix preserves the established main-campus building. Peoria has a courtyard, Gilbert a converted retail building, Scottsdale a traditional sanctuary with light brand updates, Chandler a contemporary family campus, Goodyear a high school auditorium with portable church banners, and Tempe an urban warehouse conversion.
- No people are visible in the final photos. Tempe received one cleanup to remove distant street activity and possible pedestrian-like shapes. Seven final photos were delivered from eight generation calls.

The public site's campus cards, individual JPEG/PNG downloads, and campus-photo zip use the new set. Supporting photo guides, site text, roadmap, and decision placeholder `D-campus-exteriors-1` were updated. Campus records, addresses, schedules, package layout, and loader behavior are unchanged. Campus photo fields in Rock are not assigned by this change.

## Checks and integration

Image checks pass for all seven campus mappings, fourteen photo files, native dimensions, unchanged PNG copies, and JPEG metadata removal. Both public pages contain seven campus images, and their image/download references resolve. The campus download zip contains seven JPEGs. The site builds 95 pages. The Cast validator reports zero errors or warnings, and content standards report zero problems.

All three packages built for Rock 21.0.2 and passed the verify tool on October 23, 2026: Core 1, Core 3, and Full 7. The checks include Cast expectations, all 39 invariants, Rock Cleanup, and the existing schema contract. Ignored receipts are in this worktree's `packages/` folder. The run used the isolated `campus-exteriors` database tag; it did not load any other session's database. This proves the existing install packages remain valid; the new campus photo set is a repository and public-site asset.

Browser rendering review of the site remains for the integrator: the browser tool blocks local file URLs. The generated photos themselves were visually reviewed.

Review and commit the new `photos/campuses/`, `photos/originals/campuses/`, this handoff, and changes in `photos/README.md`, `photos/references/church/README.md`, `docs/decisions.md`, `docs/roadmap.md`, and `site/`. Follow AGENTS.md for merging current main, numbering the decision placeholder, and landing through the integrator. Keep any unrelated work that arrives after this handoff.

## Campus facts

The same branch records two campus facts the photos show:

- Scottsdale was an independent church until it merged with Rock Solid Church and became a campus. It keeps its traditional church building.
- Goodyear meets in a high school auditorium. It is a set-up and tear-down campus, so volunteers set up and tear down every Sunday.

Both facts are in each campus's `notes` in `configuration/campuses.json`, the campus table in `configuration/README.md`, the campus photo README and manifest, decision D-campus-exteriors-1, the roadmap, and the public site's church page. The loader does not read campus notes, so nothing in Rock changes. No serving team for setup and teardown exists yet.

## Initiatives and phone navigation

Added on the same branch at the user's request:

- The church page has an Initiatives section. Starting Point, the first initiative, shows its artwork and what the sample data does with the class. Built on the Rock shows a card until its campaign graphic exists. The site reads every folder in `graphics/initiatives/`.
- The brand page offers the Starting Point artwork as a PNG download.
- On phones (760 pixels and narrower), the top navigation folds behind a Menu button with large tap targets. Escape closes it. On tablets, the links wrap onto their own row instead of scrolling sideways.
- The Publish site workflow now also runs when `graphics/` changes.

Checked in a browser at 375, 768, and 1280 pixels wide, served from localhost: the menu opens and closes, no page scrolls sideways, and the artwork shows uncropped.
