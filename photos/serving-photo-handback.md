# Rock Solid Church photo and serving branch handback

Prepared October 21, 2026 for the agent integrating the `serving` branch into `main`. The user approved the delivered images and requested this handback so the receiving agent can bring in and merge the work. Photo production is complete: eight serving opportunity photos, twelve church reference photos, the approved logo, and retained logo explorations are saved in the repository. Merge the complete branch, reconcile its serving implementation with current main, and verify the result.

## Starting Point followup

After handback commit `c627f74`, the user requested a Starting Point campaign design and then asked to apply it to the lobby. The new `graphics/initiatives/starting-point/starting-point-hero-v1.png` and its manifest are saved, and the canonical inward-facing lobby PNG/JPEG pair now incorporates that design. The church guide, manifest, and this handback reflect the change. These followup files landed on main on 2026-10-21 (merge ea6bf7c). The branch hashes and divergence counts below describe the earlier handback snapshot.

## Repository and branch

| Item | Location or value |
| --- | --- |
| Source worktree | `C:\Users\JonEdmiston\repos\rock-sample-data-serving` |
| Source branch | `serving` |
| Destination worktree | `C:\Users\JonEdmiston\repos\rock-sample-data` |
| Destination branch | `main` |
| Remote | `https://github.com/SparkDevNetwork/rock-sample-data.git` |
| Last asset commit before this handback | `17f3590c94230f9ad6009c8d850cfa3735e0faea` |
| Main when checked | `9b150f7055f198faf57e8a09f5d89460c651be8d` |

Both worktrees were clean before this document was added. At that point, `main...serving` contained 19 commits unique to main and 21 unique to serving, with 145 changed files in the merge-base-to-serving comparison. These are a snapshot; recheck before merging and include the handback commit at the source branch tip. The photo agent has not pushed this work.

## Delivered files

Paths below are relative to the repository root. All twenty photo originals and their JPEG copies are 1536 by 1024 pixels, landscape 3:2. JPEG delivery copies were encoded at quality 90 with metadata removed. Church reference PNG originals intentionally live beside their JPEGs; do not relocate them to the opportunities originals folder.

```text
graphics/
  README.md
  initiatives/starting-point/
    starting-point-hero-v1.png
    manifest.json
photos/
  serving-photo-handback.md
  opportunity-brief.md
  opportunities/
    manifest.json
    greeters.jpg
    ushers.jpg
    kids-ministry.jpg
    worship-team.jpg
    production-team.jpg
    student-ministry.jpg
    prayer-team.jpg
    first-aid-team.jpg
  originals/opportunities/
    [matching eight PNG originals]
  references/church/
    README.md
    manifest.json
    front-exterior-wide.png / .jpg
    main-entrance-close.png / .jpg
    lobby-toward-entrance.png / .jpg
    lobby-toward-worship-center.png / .jpg
    worship-center-from-rear.png / .jpg
    worship-center-from-stage.png / .jpg
    stage-from-side.png / .jpg
    production-booth-wide.png / .jpg
    kids-classroom.png / .jpg
    student-room.png / .jpg
    prayer-room.png / .jpg
    first-aid-room.png / .jpg
    branding/
      rock-solid-church-logo.png
      manifest.json
    logo-options/
      manifest.json
      [twelve numbered concept PNGs]
      sources/open-cross-selected.png
```

The eight opportunity images are application assets mapped to opportunity keys by [their manifest](opportunities/manifest.json). They show the specified fictional Cast volunteers serving; guests and children follow the anonymity rules in [the opportunity brief](opportunity-brief.md). Preserve these mappings and image files.

The twelve empty church views are reusable generation references. [The church guide](references/church/README.md) is the detailed authority for visual continuity; [its manifest](references/church/manifest.json) retains exact generation and correction prompts, image paths, dimensions, and review notes. The reference pack is not a request to assign twelve more images to Rock fields or to add the pack to install packages.

## Approved appearance and final revisions

Rock Solid Church is a clean, new, contemporary Phoenix Valley church. Preserve warm-white stucco and plaster, charcoal trim, pale oak, gray floors, muted teal furnishings, and desert landscaping. Current empty-room references supersede the earlier opportunity images for room size and ceiling or roof height. The earlier opportunity images remain unchanged.

- **Logo:** the user selected option 08, the original uppercase Open Cross design. Use the exact artwork in `references/church/branding/rock-solid-church-logo.png`: four rounded forms around a negative-space cross, ROCK SOLID, and smaller tracked CHURCH. The later rock variant was not selected. Exterior dimensional signage stays fully inside one stucco wall plane with blank margins; it must not extend past or wrap the building corner.
- **Exterior:** the large, low lobby sits in front of a restrained auditorium volume set farther back. The wide view may show that rear volume. The close entrance view must show none of its roof or top. Window reflections are deliberately subtle enough to see into the lobby.
- **Lobby:** both directions share the enlarged gathering floor, targeting roughly 2.5 times the initial visual floor area. Keep normal-size furniture and doors. Toward the entrance, the welcome counter is left and the oak slat column is right; those relationships reverse toward the worship center. The worship-center wall now has three double-door pairs, six leaves total, with clear approaches.
- **Auditorium:** the final room targets 60 percent more floor area and a ceiling about 40 percent higher than the earlier version. These are visual targets, not measured dimensions or capacity. Preserve three chair banks and two aisles. Chair backs face the rear camera; chair fronts face the stage camera. Rear, stage, side-stage, booth, and exterior views reflect the enlarged envelope.
- **Production booth:** visible cables are organized in separated runs with secured slack. Audio and stage-feed screens are sharp. Keep the camera on the left when looking toward the stage, on the booth's right in the reverse view.
- **Prayer room:** twice the initial visual floor-area target, six matching teal armchairs around one oak table, generous circulation, and the approved wall graphic. No doorway, door leaf, jamb, or doorway hardware appears on either foreground edge. Keep the actual side window.
- **Student room:** twice the initial visual floor-area target, organized sofa and armchair seating, teal and coral poufs, separate table-tennis space, tidy storage, warm string lights, and the exact wall identity `Rock Solid Students`.
- **Kids and first aid:** retain the delivered classroom and care-room references, their furniture, finishes, and links to the common building palette.

### Exact environmental wording

The lobby mission graphic has the small heading `OUR MISSION`, followed by three separate lines:

```text
Love God.
Love people.
Share Jesus.
```

The lobby connection graphic reads:

```text
YOU BELONG HERE.
STARTING POINT
From a first hello to a place you belong.
Enroll in Starting Point
```

The right wall adapts the new Starting Point campaign to a more upright layout: charcoal typography on warm ivory, teal and sea-glass curved forms gathering around a coral point, and a dark-teal invitation strip. Its key text is above the lounge seating; campaign fine print and the duplicate church logo are omitted. This replaces the earlier solid-teal panel and Turn Sunday visits into meaningful connections sentence. Do not restore the discarded New Here / Connection Event / Sign Up Today wording. Both lobby graphics stay outside the three-pair door bank, above seating, without covering doors, frames, or openings. The standalone campaign master is 1672 by 941 pixels; the twelve architectural photo pairs remain 1536 by 1024.

The prayer-room graphic reads:

```text
Find strength in prayer.
“I can do all things through Christ who strengthens me.” — Philippians 4:13
```

Retain the teal script emphasis on `strength` and the remaining charcoal lettering. Future photo briefs may prohibit visible text; frame out or obscure graphics in those future photos instead of changing these approved masters.

## Work included beyond the photos

The complete `serving` branch includes serving teams and configuration, volunteer schedules and check-in, connection requests and history, Cast data and family pages, package and loader integration, validation rules, and documentation. It also contains merges from earlier main revisions. Read [the serving design](../docs/serving.md) and inspect the current branch diff before resolving conflicts. A photo-only cherry-pick would omit part of the requested branch integration.

In particular, commit `b90aa25` adds opportunity-photo packaging and loading. The builder includes the opportunity manifest and images, validates their keys, and the loader stores the images and sets `ConnectionOpportunity.PhotoId`. Preserve that integration along with the eight delivered images. Shared conflict areas may include `builder/PackageWriter.cs`, the loader entry points and photo models, Cast files, configuration, decision numbering, validation, and READMEs.

Keep newer main changes and serving behavior together. Reconcile shared files by intent; do not replace whole shared files with one branch's version. Preserve the current decision numbering and cross-references consistently if main has added decisions since the source branch last merged it.

## Merge and verification

1. Read any current repository instructions. Recheck both worktrees for other agents' uncommitted work, and inspect the current `main...serving` history and diff. Preserve any such work before proceeding.
2. Merge the local `serving` branch into `main` in `C:\Users\JonEdmiston\repos\rock-sample-data`. Since both branches have unique commits, use a normal merge that preserves both histories. Do not reset main to serving. Include this handback document and the latest image revisions, not just the initial asset commits.
3. Resolve source, data, and documentation conflicts, preserving serving-photo packaging and loading. Retain all delivered binary assets and manifests at their documented paths.
4. Run `node cast/validate-cast.js` and `node validation/check-standards.js` from the merged repository root. Build and verify the integrated serving changes using the current [builder](../builder/README.md), [loader](../loader/README.md), and [verify](../verify/README.md) instructions with the configured Rock checkout and disposable verification databases. Rebuild packages after the merge so they contain the merged inputs.
5. Confirm the opportunity manifest still has eight unique real opportunity keys and all eight JPEGs and PNG originals. Confirm the church manifest still has twelve unique views and all twelve PNG/JPEG pairs, with the approved logo and supporting manifests present.
6. Review the public Serve page and opportunity records after a fresh verified load: all eight photos should be assigned and display correctly. Spot-check the latest church reference views against this document, especially six prayer-room chairs, three lobby door pairs, exact wall copy, logo clearance, and the close exterior roofline.
7. Finish the merge commit and report its hash, checks run, and any remaining failures. Push or publish according to the receiving task's instructions; preparing this handback did not push or publish anything.

The handback preparation checked all forty photo files by opening them and confirming 1536 by 1024 dimensions; all four opportunity, church, branding, and logo-option manifests parsed successfully. Final images were visually reviewed during production, with the user accepting the latest revisions. This handback task did not rerun database loads or the application verification suite. Historical serving validation results are recorded in the branch documentation and commit history; they do not replace verification of the merged result.

## Useful asset commits

| Commit | Delivered change |
| --- | --- |
| `a96daa6` | All eight serving opportunity photos and originals |
| `b90aa25` | Opportunity photos integrated into packages and Rock loading |
| `57d487b` | Initial church architectural references |
| `ecb1fac` | Approved logo and exterior application |
| `5e6b39f` | Exterior logo safely inset within the wall |
| `f6dd6a8` | Complete twelve-view reference pack and larger lobby |
| `fc48489` | Organized booth wiring and sharper displays |
| `5d417c0` | Expanded auditorium and higher ceiling across views |
| `d6f3022` | Approved lobby graphics, exterior massing, and softer reflections |
| `0edcc3c` | Expanded prayer and student rooms with wall graphics |
| `17f3590` | Six prayer-room chairs and removal of doorway framing |

Use the current branch tip for the complete integration; the table is for tracing approved asset changes. Existing Cast portraits, group photos, and separate small-group scene work should remain intact. No additional photo generation is required for this handback.
