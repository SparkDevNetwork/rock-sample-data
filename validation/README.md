# Validation

Validation proves the generated data is realistic. It runs the calibration scripts from `../calibration/sql/` against a freshly installed sample database, then compares the results to the calibration profile.

Realism is one question. Whether the data is correct in Rock is another, and the [verify tool](../verify/README.md) answers it. These files in this folder belong to the verify tool:

| File | What it holds |
|---|---|
| [invariants.sql](invariants.sql) | Rules that must hold in every loaded dataset, each with why it matters. |
| [rock-contract.json](rock-contract.json) | Every Rock column, Guid, and procedure the loader depends on. Recorded from real loads, never edited by hand. |
| [cleanup-accepted.json](cleanup-accepted.json) | Changes Rock Cleanup makes to sample data that we accept on purpose, each with a reason. |
| [check-standards.js](check-standards.js) | The [content standards](../docs/content-standards.md) that span folders, as a check. |

## Rules

1. Each profile value has a tolerance in [tolerances.json](tolerances.json). Percentages use points (for example, plus or minus 3). Everything else uses a share of the target (for example, within 20%).
2. The most specific rule for a path wins.
3. Any value outside tolerance fails validation.
4. Only the areas listed under `checked` are graded. Values under `skipped` are left out on purpose, and `$skipReasons` says why (for example, a feature that arrives in Phase 1).
5. A category the generator never produces passes when its target is under `missingPassesBelow` (for example, a payment type used for 0.3% of gifts).
6. Rock's own analytics are a second check, later. For example, Giving Automation should classify our weekly and monthly givers correctly.

## Running it

Load a package into a fresh database first (see [loader/](../loader/README.md)). Then, from `loader-cli/`:

```
dotnet run -- --connection "<connection string>" --calibrate raw.tsv
node ../calibration/normalize.js generated "Generated sample data" raw.tsv --out generated.json
node ../validation/validate.js generated.json
```

Keep `raw.tsv` and `generated.json` outside the repository (a temp folder is fine). `--out` keeps generated results out of `calibration/profiles/sources/`, which holds real calibration sources only.

[small-group-checks.sql](small-group-checks.sql) checks the small group rules directly in the database: leaders, ages, sizes, schedules, meeting styles, test cases, history rows, did-not-meet weeks, and attendance windows. Each result says what to expect.

`validate.js` prints a pass and fail count per area, then every value out of tolerance, worst first. Add `--all` to list every comparison, or `--profile <name>` for a different profile. It exits with 1 when anything fails.

## Cast expectations

Documentation, demos, and tests lean on certain Cast givers, for example "the Deckers give monthly." A Cast giving pattern can say what Rock should show for that giver with `expect` in its household file:

```json
"expect": { "frequency": "monthly", "journeyStage": "consistent", "alerts": [ "late-gift" ] }
```

- `frequency` is Rock's gift frequency label: weekly, bi-weekly, monthly, quarterly, erratic, or undetermined.
- `journeyStage` is Rock's giving journey stage: non-giver, new, consistent, occasional, lapsed, or former.
- `alerts` lists alert type keys from `configuration/giving-alert-types.json`. The giver must have at least one alert of each type.

A Cast person can say what Rock should show for their achievements with `expect` on the person:

```json
"expect": { "achievements": { "earned": [ "high-five" ], "inProgress": [ "25-club" ], "notEarned": [ "25-club" ] } }
```

- `earned` lists achievement type keys from `configuration/achievements.json` that the person has at least one success of.
- `inProgress` lists types the person has an open attempt of, which check-in shows as in progress.
- `notEarned` lists types the person has no success of.

Every load checks them and prints the result. To check a database that is already loaded, run the loader CLI with `--package <file.zip> --check-cast`; it exits with 1 when an expectation fails. The family pages show each expectation as "Rock shows ...".

The Cast expectations hold at every anchor date tested. They cover the Deckers (monthly, consistent, by recurring card), Margaret Carter (monthly, consistent, by check), the Ellises (new), two lapsed givers with late-gift alerts (the Ortizes, who stopped six months ago, and Emily Gray, whose card expired seven months ago), the Murphys (former), Amanda Sullivan (bi-weekly, consistent, her own giving unit), Kristen Watson (a once-a-year giver, whom Rock shows as former), and Bill Price (large-gift alerts). For achievements they cover Sam Lee (Century Club earned), Grace Fischer (Century Club in progress), Diego Ortiz (Century Club still in progress after he stopped coming), and Mason Kelly (High Five, but not yet the 25 Club). All 22 hold at seven anchor dates from 2026-01-04 to 2027-02-20.

## Cast prayer expectations

Every Cast prayer request in `cast/prayer-requests.json` names the state Rock should show: current, waiting, flagged, expired, or answered. Every load checks each one: its state, its prayers and prayer count, its answer, its urgent and private settings, its category, its group, its requester or guest, and its comments and reply. A current request must still be in Rock's prayer session on the install day, which its `daysAgo` date gives back, so a later `--check-cast` run gets the same answer. At least 8 must be, so every dataset's prayer list and prayer session have requests to pray through.

## Business expectations

Every load also checks the businesses, Cast and generated alike, and prints the result after the Cast expectations:

1. Each campus that has families has at least its minimum number of businesses: 6 on the main campus and 2 on every other campus.
2. Every business has at least one business contact.
3. Every business has a gift in the last 12 months, counted back from the newest gift in the database, so a later `--check-cast` run gets the same answer.

Core 1 has 6 businesses at 1 campus, Core 3 has 10 at 3 campuses, and Full 7 (with a 17,000-household crowd) has 87 at 7 campuses. All checks hold.

## Rock version guard

The loader ports some of Rock's code: the giving automation math, the alert rules, the value formats, and the streak and achievement math. If Rock changes that code, the port goes stale. Two checks catch it.

### The lock check (any time)

`rock-lock.json` lists the Rock files the port mirrors, with a fingerprint of each. For large files it fingerprints only the methods that matter. `check-rock-lock.js` reads those files from Rock's public repository on GitHub, so it works the same on every machine and needs no Rock checkout.

```
node validation/check-rock-lock.js                 # against Rock's develop branch
node validation/check-rock-lock.js --ref 19.1.0    # against a release tag, branch, or commit
```

It exits with 1 when a file changed and prints a link to the file's history and the loader files to update. Set `GITHUB_TOKEN` if GitHub's rate limit gets in the way. After reviewing a change and updating the port, run it with `--update` to record the new fingerprints.

The lock was last recorded against `develop` at commit b34e15fc21 (Rock 21) on 2026-10-19. Older Rock versions fail the check, because Rock 19 rewrote giving automation.

The loader adds a third check on every load: it runs Rock's own journey stage procedure for the last week and warns if Rock's result differs from the replay.

### The parity test (before each release)

The parity test runs Rock's real Giving Automation job on loaded sample data and compares what Rock writes with what the port computes.

1. Load the Full 7 package into a Rock development database with the loader CLI.
2. Mark the job's filters as changed, so the job reclassifies every giving unit. Run this SQL, then clear Rock's cache (Admin Tools, then Clear Cache):

   ```sql
   UPDATE [Attribute]
   SET [DefaultValue] = JSON_MODIFY( [DefaultValue], '$.GivingClassificationSettings.FiltersChanged', CAST( 1 AS BIT ) )
   WHERE [Key] = 'core_GivingAutomationConfiguration' AND [EntityTypeQualifierColumn] = 'SystemSetting';
   ```

3. In Rock, run the Giving Automation job (Jobs Administration, then Run Now).
4. Compare: `dotnet run -- --connection "<the database>" --dataset full-7 --giving-parity` from `loader-cli/`. It recomputes every unit the job classified, as of the job's run time, and lists any value that differs. It exits with 1 on any difference.
5. Run `node validation/check-rock-lock.js --update` and commit `rock-lock.json`.

The parity test covers the giving attributes. Bins, percentiles, and journey stages already come from Rock's own procedures. The alert rules are covered by the lock check and by reading the port against Rock's code, because Rock's job only raises alerts for the last week.

### The achievements parity test (before each release)

The achievements parity test runs Rock's own streak and achievement code, not the port, on loaded sample data. It sets Rock's clock to the moment Rock would have run each piece, the way Rock's integration tests do, and compares the results with the loader's rows:

1. **Streak counts.** Rock's GetStreakData as of the anchor, against each streak's current streak, longest streak, and engagement count.
2. **Attempts.** It deletes the loader's attempts, then lets Rock process each child's streak as of their last check-in Sunday, as a live check-in would. Rock's attempts must match the loader's exactly.
3. **Rebuild.** Rock's streak type rebuild from attendance (the Rebuild button on Streak Type Detail), as of the anchor. Rock's occurrence map, engagement maps, and enrollment dates must match the loader's byte for byte.

Steps 2 and 3 rewrite rows, so run it on a copy of a loaded database, never one you want to keep.

1. Build Rock's integration tests in a Rock checkout of the same version (`Rock.Tests.Integration`). The test needs its output folder, which holds Rock.dll and everything it uses.
2. Load a package into a fresh database with the loader CLI, and note the anchor. Make a copy of the database.
3. From `validation/achievement-parity/`, build and run it against the copy:

   ```
   dotnet build -p:RockBin="C:DevelopmentRockRock.Tests.IntegrationinDebug
et472"
   binDebug
et472RockSampleData.AchievementParity.exe --connection "<the copy>" --anchor 2026-10-19 --changes-database
   ```

4. It prints each difference and exits with 1 if there are any.

On 2026-10-19 it passed for Core 1 (49 streaks and 5,009 attempts) and for Full 7 with a 17,000-household crowd (2,893 streaks and 182,762 attempts). Full 7 takes about 6 minutes.

## Where it stands

The Phase 0 spike (Full 7 plus a 17,000-household crowd) passes 260 of 291 values (89%). Giving passes 191 of 196, and small groups 3 of 3. The largest remaining gaps:

1. First-time visitors run low, so Easter and fall kickoff do not spike with newcomers. A visitor model comes with the real Crowd.
2. Teens (ages 12 to 17) run low.
3. A few holiday attendance multipliers miss, and the engagement mix (attends, gives, groups, serves) waits on serving teams.
4. Giving's last misses are small: the largest gifts (99th percentile) run high, a few too many gifts are odd amounts, and last year's ACH share of gifts runs a little high.

Small group size, leader share, and attendance are design targets, not gaps. They are recorded as overrides in `calibration/profiles/sample-church.json`, with reasons, so validation grades them against what we chose (see [small groups](../docs/small-groups.md)). The share of active adults in a group follows the measured rate and passes.

The giving tuning on 2026-10-16 took the spike from 215 to 257 (of 287, before small groups added four values). Before and after, for the check targets:

| Value | Target | Before | After |
|---|---|---|---|
| Checks, share of gifts | 8.7% | 3.7% | 10.7% |
| Checks, share of dollars | 17.8% | 4.1% | 18.6% |
| Cash, share of gifts | 4.1% | 1.7% | 2.1% |
| Cash, share of dollars | 2.2% | 1.9% | 1.8% |
| On-site source, share of gifts | 13.4% | 5.4% | 12.8% |
| On-site source, share of dollars | 21.5% | 6.1% | 20.6% |

Full 7 now draws 3,533 scanned checks in its 12-month window (7,066 images, 269 MB).

