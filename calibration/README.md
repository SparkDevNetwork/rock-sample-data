# Calibration

Calibration turns measured Rock data into target numbers for the builder. The same scripts also grade our generated data in `validation/`.

## Workflow

1. Run the scripts in `sql/` against a Rock database. Set `@ChurchLabel` to a neutral label (for example `source-2`), never a church name.
2. Save the raw output in `raw/`. Git ignores that folder.
3. If the output is an Excel export from Rock, convert it: `.\xlsx-to-tsv.ps1 -Path .\raw\source-2-part-1.xlsx`.
4. Normalize it: `node normalize.js source-2 "Calibration source" raw/source-2-part-1.tsv raw/source-2-part-2.tsv raw/source-2-part-3.tsv`.
5. Add the source to a profile in `profiles/` and check the result with `node resolve-profile.js profiles/sample-church.json`.
6. Rebuild the datasets and run validation.

## Part 4: distance to campus

Part 4 keeps server work light. It does no distance math. It rounds each family's home to a grid cell of about 0.7 miles and returns family counts per cell and campus, plus exact campus locations. Cells with fewer than 5 families are left out, so no home can be singled out.

All distance math happens locally:

```
.\xlsx-to-tsv.ps1 -Path .\raw\source-2-part-4.xlsx
node distance-from-cells.js source-2 raw/source-2-part-4.tsv raw/source-2-part-4-sections.tsv
node normalize.js source-2 "Calibration source" raw/source-2-part-4-sections.tsv
```

Delete the cell file after normalizing. Part 4 can run on its own. A profile can take its `distance` section from one source with `sectionBlend`, or hold coarse distance targets as an override (see `profiles/README.md`).

## Part 5: prayer

Part 5 measures how a prayer team uses Rock: who asks, how fast requests are approved, how often people pray for them, how the praying is spread across the team, and how comments and answers are used. It returns no category names and no request text.

Every per-request measure stands alone. Yearly volumes (requests and prayers per 1,000 active adults) also need part 2 from the same database, because the normalizer scales them by part 2's active adults. Without part 2 they come out empty, and a profile can take volume from another source:

```
node normalize.js source-2 "Calibration source" raw/source-2-part-5.tsv
```

## Part 6: notes on people

Part 6 measures how staff use notes on person records: which kinds of note types they use, note length, how often notes are alerts, private, pinned, or replies, when notes are written, who writes them, and who they are about. It returns no note text and no note type names. The script sorts each note type into a generic kind (care, hospital, counseling, follow-up, and so on) and returns only the kind.

Like part 5, yearly volumes need part 2 from the same database. Every other measure stands alone:

```
node normalize.js source-2 "Calibration source" raw/source-2-part-2.tsv raw/source-2-part-6.tsv
```

The normalizer writes the result to the `personNotes` section, because `notes` already holds each source file's explanations.

## Part 7: communications

Part 7 measures mass sends (25 or more recipients) and text conversations: sends by medium, month, weekday, and hour, audience size, delivery, opens, clicks, unsubscribes, preferences, app devices, replies, thread length, and reply times. It returns no names, Ids, list names, message text, phone numbers, or URLs. Clicked links are compared by checksum only.

Sends and texts use the last 12 months. Recipient outcomes, opens, clicks, and unsubscribes use the last 3, because they read the largest tables. Unsubscribe and spam rates need Rock 17, and communication flows need Rock 18; on older Rock those sections are empty. On a church with a very large communication history it can pass Rock's 180 second limit, so run it in SSMS if it times out.

Monthly volumes need part 2 from the same database, as for parts 5 and 6. The normalizer does not read part 7 yet; that comes with the first source. See [Communications](../docs/communications.md).

## Tools

| Tool | Purpose |
|---|---|
| `xlsx-to-tsv.ps1` | Converts Rock's Excel export of a calibration grid into text the normalizer reads. |
| `distance-from-cells.js` | Turns part 4's grid cells into distance bands, percentiles, nearest-campus share, and campus spacing. |
| `normalize.js` | Turns raw output into an anonymous source file of ratios in `profiles/sources/`. |
| `resolve-profile.js` | Blends sources and applies a profile's exclusions and overrides. Shows the effective targets. |

## Scripts

| Script | Use |
|---|---|
| `sql/Report_SampleDataCalibration_1_PeopleAndGiving.sql` | Rock's SQL editor or SSMS |
| `sql/Report_SampleDataCalibration_2_AttendanceAndGroups.sql` | Rock's SQL editor or SSMS |
| `sql/Report_SampleDataCalibration_3_RegistrationsPrayerCommsConnections.sql` | Rock's SQL editor or SSMS |
| `sql/Report_SampleDataCalibration_4_DistanceToCampus.sql` | Rock's SQL editor or SSMS. Returns grid-cell counts, not statistics (see above). |
| `sql/Report_SampleDataCalibration_5_Prayer.sql` | Rock's SQL editor or SSMS. Prayer in detail (see above). |
| `sql/Report_SampleDataCalibration_6_PersonNotes.sql` | Rock's SQL editor or SSMS. Notes on people (see above). |
| `sql/Report_SampleDataCalibration_7_Communications.sql` | Rock's SQL editor or SSMS. Mass sends and text conversations (see above). |
| `sql/Report_SampleDataCalibrationStats.sql` | SSMS only (parts 1 to 3 in one run; run part 4 separately) |

The scripts are read-only. They write only to temp tables, and Rock's SQL editor rolls everything back. Each one returns anonymous aggregates with no names or Ids.

## Profile rules

1. Store ratios, never raw totals.
2. Label sources neutrally (`source-1`, `source-2`). Never record a source's name, location, size, or story anywhere in the repository.
3. Leave out quirks that do not fit a typical sample church (custom statuses, list-style groups, record bloat), and note why.
