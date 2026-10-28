# Group photo completion handback

> **Archived.** This page made the original full-group photos in this folder. Group photos now show each group's leader and spouse, from the [current group photo brief](../group-brief.md) (decision D-group-leaders-1). The member lists below were written by `photos/build-group-brief.js`, which is retired. Paths on this page are where the files lived then; they are now in this folder.

Completed October 19, 2026. All 12 Cast small-group photos from the [group photo brief](brief.md) have been generated, reviewed, saved, and committed. This includes eight active groups, two inactive groups, and two archived groups, covering 129 member appearances. Some people appear in more than one group; no members were invented.

The user reviewed Wright and Greggs first, approved the visual direction, and requested completion of the remaining ten. Each group has its own setting, with distinct architecture, furnishings, lighting, and views. Interiors follow Phoenix-area homes, and outdoor scenes include desert landscaping and local building materials.

## Delivered files

- [Originals](originals/groups/): 12 PNGs at 1536 × 1024, landscape 3:2.
- [Delivered photos](groups/): 12 JPEGs at 1200 × 800, with image metadata removed.
- [Group manifest](groups/manifest.json): group keys, ordered member lists, exact generation prompts, and correction prompts where applicable.

Images were generated with the built-in OpenAI image tool. The underlying model identifier and generation seeds were not exposed; the manifest records that limitation and uses null seeds.

## Completed groups

Each filename below has a matching full-size PNG under `originals/groups/`.

| Group | Delivered photo | Adults | Setting |
| --- | --- | ---: | --- |
| Decker | [decker-group.jpg](groups/decker-group.jpg) | 12 | Backyard lawn with folding chairs and desert landscaping |
| Wright | [gilbert-group.jpg](groups/gilbert-group.jpg) | 11 | Phoenix ranch living room with mid-century furniture and evening lamplight |
| Greggs | [greggs-group.jpg](groups/greggs-group.jpg) | 11 | Bright Arizona sunroom with terracotta tile and wicker seating |
| Lee | [lee-group.jpg](groups/lee-group.jpg) | 10 | Park ramada and picnic tables |
| Marble | [marble-group.jpg](groups/marble-group.jpg) | 12 | Vaulted great room with wood beams, stone fireplace, sectional, and ottoman |
| Foster | [petes-group.jpg](groups/petes-group.jpg) | 12 | Family den with leather sectional, bookshelves, and television turned off |
| Rivera | [rivera-group.jpg](groups/rivera-group.jpg) | 10 | Poolside patio with citrus trees |
| Silva | [silva-group.jpg](groups/silva-group.jpg) | 10 | Modern condo with tall windows and a Phoenix city view |
| Hughes | [hughes-group.jpg](groups/hughes-group.jpg) | 7 | Open kitchen and dining table with Bibles and pizza |
| Murphy | [murphy-group.jpg](groups/murphy-group.jpg) | 12 | Backyard fire pit and string lights at dusk |
| Jenkins | [jenkins-group.jpg](groups/jenkins-group.jpg) | 11 | Stucco ranch front porch and shallow entry steps |
| Russell | [russell-group.jpg](groups/russell-group.jpg) | 11 | Formal dining room with a shared meal and china cabinet |

Wright retains the existing `gilbert-group` key, and Foster retains `petes-group`.

## Review and corrections

Every final image was visually reviewed for the listed adults, portrait likenesses, clear faces, setting, Bibles, unwanted text or logos, and obvious anatomy defects. File checks confirmed all 12 originals and JPEGs, image dimensions, metadata removal, unique manifest entries, and member lists in the brief's order.

Three images received a targeted correction before delivery:

- Marble: corrected Jim Simmons's face, which initially resembled Phil Coffee.
- Silva: corrected Tom Bennett's face and added Dale Tucker, replacing a duplicate Isaac appearance and reaching ten distinct adults.
- Russell: added the missing Bob Greggs to reach eleven adults.

The approved Wright and Greggs photos were preserved. Cast records, group configuration, and the main `photos/manifest.json` were unchanged.

## Commits and completion status

- `7609343` — first two photos for direction review.
- `63d22ba` — remaining ten photos and completed group manifest.

Photo generation and file delivery are complete. Rock import and application display checks were outside this asset-production task.
