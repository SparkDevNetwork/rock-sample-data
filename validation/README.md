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

A Cast giving pattern can also design a refund with `refunds` (see [Refunds](../docs/refunds.md)):

```json
"refunds": [ { "kind": "duplicate", "near": { "daysAgo": 45 } } ]
```

Each refunds the pattern's last gift on or before `near` that its kind can refund. Every load checks that the giver has a refund with the kind's reason in `configuration/refunds.json`, made the kind's shortest delay after its gift on a weekday, of the whole gift for `duplicate`, `earlier-year`, `insufficient-funds`, and `returned-check` and part of it otherwise, for a duplicate, of a gift with a twin that day, and for a returned check, of a check. Gabriel Hernandez's duplicate gift and Javelina Wash Coffee's returned check are the Cast's refunds today.

A Cast person can say what Rock should show for their achievements with `expect` on the person:

```json
"expect": { "achievements": { "earned": [ "high-five" ], "inProgress": [ "25-club" ], "notEarned": [ "25-club" ] } }
```

- `earned` lists achievement type keys from `configuration/achievements.json` that the person has at least one success of.
- `inProgress` lists types the person has an open attempt of, which check-in shows as in progress.
- `notEarned` lists types the person has no success of.

A Cast person can also say what their steps should show:

```json
"expect": { "steps": { "complete": [ "starting-point-class" ], "inProgress": [ "serve" ], "programCompletions": [] } }
```

- `complete` lists step type keys from `configuration/steps.json` that the person has at least one Complete step of.
- `inProgress` lists types the person has an In Progress step of.
- `programCompletions` lists the keys of exactly the programs the person has a completion for, such as `discipleship-path` or `student-path`. An empty list means none. Leave it out to not check.

They cover Ted Decker (the whole Discipleship Path, with a program completion), Brian and Wendy Gilbert (Starting Point Class In Progress), Bri Hall (Starting Point Class complete, and a Serve step In Progress while she waits as a Pending greeter), Ava Brooks (Student Camp and Follow Jesus), Benjamin Lee (Student Camp and Mission Trip), Jasmine Washington (the whole Student Path, with a program completion), and Dennis Perry (Baptism, ten days before the install day).

A Cast household's `campaign` entries say how each Built on the Rock pledge must be going on install day (see [Pledges](../docs/pledges.md)):

```json
"campaign": [ { "person": "james", "pledge": 5200, "frequency": "weekly", "pledged": "commitment-sunday", "followThrough": "behind" } ]
```

The check reads the pledge and the campaign gifts of the person's giving unit, and works out the state with the rules in `loader/model/PledgeProgress.cs`: complete, ahead, on pace, behind, stopped, not started, or gifts only. The state must match `expect`, or the state `followThrough` leads to when `expect` is left out.

A Cast person can also say what their assessments should show, with the type keys from `configuration/assessments.json`:

```json
"expect": { "assessments": { "complete": [ "spiritual-gifts" ], "pending": [ "spiritual-gifts" ] } }
```

- `complete` lists the types the person has a completed result of, with its values on the person.
- `pending` lists the types whose latest request is still pending, with a requester.

A story's `leaning` in `assessments` must come out on top, such as Bri Hall's gifts among her dominant gifts, and each legacy DISC result must show exactly the scores in the file (see [Assessments](../docs/assessments.md#the-casts-stories)).

A Cast person can say what eRA shows for them on install day (see [eRA](../docs/steps.md#era)):

```json
"expect": { "era": { "status": "current", "startedDaysAgo": { "min": 730 } } }
```

- `status` is `current` (Currently an eRA, an eRA Start Date, one In Progress eRA step from that day, and an Entered history line), `former` (no flag, an eRA End Date, a Complete eRA step and an Exited history line on that day), or `never` (no eRA values, steps, or history).
- `startedDaysAgo` and `endedDaysAgo` give the days before install day the start or end may be, with a `min`, a `max`, or both.

They cover Ted Decker (current for at least 730 days), Jordan Ellis (current, from 21 to 100 days), Luis Ortiz (former, ended 60 to 200 days before install day), and Bri Hall (never). They held on five install days, a Monday, a Tuesday, the 1st of a month, February, and June (decision 327).

A Cast person can say what their peer network shows (see [Peer network](../docs/small-groups.md#peer-network)):

```json
"expect": { "peerNetwork": { "includes": [ "cindy" ], "currentGroupsRising": true } }
```

- `includes` lists people in the same household who show in the person's peer network, rising.
- `currentGroupsRising` means everyone who shares one of the person's current groups, of a type with Peer Network on, shows rising.

Ted Decker has both. Every load also checks that each Cast person who left a group, or whose group closed, sees its people falling, and that at least one has (decision 332). These read whatever groups exist, so they hold when the Cast's small groups are planned again.

The Cast's duplicates in `cast/duplicates.json` are checked too (see [Duplicates](../docs/duplicates.md)). Rock's duplicate finder must score each pair, with its decision: open, Not a Duplicate, or ignored. An open pair must score above the Duplicate Finder's 60, so the list shows it. Each merge request must be a Person Merge Request that never expires, with its requester, note, and people. Each merge must leave the old record's alias on the person, one Merged line in their History, and the old last name as a previous name when it differs. These need no part, since people always load.

Every load checks them and prints the result, for the parts it loaded: giving and campaign expectations need the giving part, achievement expectations the attendance part, prayer expectations the prayer part, step and step workflow expectations the steps part, assessment expectations the assessments part, reminder expectations the reminders part, eRA expectations the attendance and giving parts, and peer network expectations the small groups and serving parts. To check a database that is already loaded, run the loader CLI with `--package <file.zip> --check-cast`; it exits with 1 when an expectation fails. The family pages show each expectation as "Rock shows ...".

The Cast giving expectations hold on every install day: the verify tool's anchor sweep checks the 60 days from each run's anchor, and `--anchor-sweep 365` a full year (decision 218). They cover the Deckers (monthly, consistent, by recurring card), Margaret Carter (monthly, consistent, by check), the Ellises (new), two lapsed givers with late-gift alerts (the Ortizes, who stopped six months ago, and Emily Gray, whose card expired seven months ago), the Murphys (former), Amanda Sullivan (bi-weekly, consistent, her own giving unit), Kristen Watson (a once-a-year giver, whom Rock shows as former), and Bill Price (large-gift alerts). For achievements they cover Sam Lee (Century Club earned), Grace Fischer (Century Club in progress), Diego Ortiz (Century Club still in progress after he stopped coming), and Mason Kelly (High Five, but not yet the 25 Club). All 22 hold at seven anchor dates from 2026-01-04 to 2027-02-20.

## Cast calendar expectations

Some calendar events carry a Cast story in `expect` in `configuration/calendar.json`: a start at a campus between some days before and after the install day. Every load checks each one against Rock's `ScheduleDate` rows, the dates Rock lists. They cover the car show Helen Evans just came to, the men's breakfast Kevin Dawson missed, the diaper drive Destiny Young came to and the next one she asked about, the serve day Bri Hall helped at, and the student retreat coming up next month. See [Calendar](../docs/calendar.md#dates).

## Cast prayer expectations

Every Cast prayer request in `cast/prayer-requests.json` names the state Rock should show: current, waiting, flagged, expired, or answered. Every load checks each one: its state, its prayers and prayer count, its answer, its urgent and private settings, its category, its group, its requester or guest, and its comments and reply. A current request must still be in Rock's prayer session on the install day, which its `daysAgo` date gives back, so a later `--check-cast` run gets the same answer. At least 8 must be, so every dataset's prayer list and prayer session have requests to pray through.

## Cast note expectations

Every load with the notes part checks each Cast note in `cast/notes/` (decision 171):

1. The note is in Rock, by its Guid, on the person it is about.
2. Its type is one the person profile's Notes block (TimeLine, Guid `0B2B550C-B0C9-420E-9CF3-BEC8979108F2`) shows: a person note type with no qualifier, and one of the block's Note Types when that setting names any. Rock's own setting is empty, which shows every person note type.

This catches what kept Rock's legacy notes on Ted Decker and others off their profiles: a note that never loads, or a type the profile hides. It does not check who may see a note. Rock shows a private note only to its author, and each type's View security decides the rest (decision 78).

## Cast mention expectations

Every load with the notes part also checks each mention in a Cast note or reply from the last 89 days (decisions 307 and 308):

1. The staff member it mentions has exactly one message from Rock's Note Mention component for that note. The author of the note never gets one.
2. The message is unread when the note is from the last three days, and read otherwise.
3. The message opens that note on the person's profile.

They cover Ted Decker's unread mention from Pete Foster, Pete's two read ones, and Daniel Peak's and Jamal Pierce's read ones. Alisha Marble has two unread ones, so signing in as admin shows them in the bell. A load without Rock's DataEncryptionKey writes mentions as plain names, so it has none to check.

## Cast tag expectations

Every load checks each tag in `configuration/tags.json` whose owner or maker it loaded (decision 302):

1. The tag is an active tag on Rock's Person entity type, where Rock's tag bar and Person Tag filter find it.
2. A personal tag belongs to its owner. An organizational tag has no owner and sits in its tag category.
3. Rock's Staff tag holds exactly the staff the dataset loads.
4. Each Cast person the tag names carries it, such as Hal Russell on Homebound and Bri Hall on Ted Decker's Young Adults Night.

## Cast reminder expectations

Every load with the reminders part checks each Cast reminder in `configuration/reminders.json` (decision 296):

1. The reminder is in Rock, by its Guid, on its owner.
2. A reminder about a person is a Personal Reminder on that person's primary alias. A reminder about a group is a Group Reminder on that group.
3. Its note is the note in the file. A repeating one has the period and the number of completions the file gives.
4. It shows what its `expect` says on the load's install day: `due`, `upcoming`, or `complete`. A load and the verify tool pass the load's anchor. A later `--check-cast` run judges it on the database server's today, so run it on the install day.

Then it checks each Cast owner's `ReminderCount`, the number Rock's bell shows. It must equal what Rock's Process Reminders job would count: their reminders of an active type that are not complete and are due. An owner with a reminder that should be due must have a count above 0 (decision 298).

They cover Ted Decker's check-in with Ashley Kelly (due, repeating every 30 days), his reminder about Josh Sanders's story (upcoming), and Bill Marble's reminder about the Marble Group's party (upcoming). The verify tool's anchor sweep checks them on the 60 install days from each run's anchor, beside the giving expectations (decision 297).

## Cast step workflow expectations

Every load with the steps part checks the step workflows in `configuration/step-workflows.json` (decision 294):

1. Each workflow type has one trigger on its step type, saved as Rock's step type editor saves one: its program, Step Completed or Manual, a blank qualifier, and the type's name. The type is active, persisted, and filed, with no age limit and no retention periods.
2. No step is linked to a Volunteer Care Check-In, because staff start it by hand.
3. Every Baptism and Follow Jesus step completed since its trigger was made started exactly one workflow.
4. The workflow went to the right person: the campus pastor of the step's campus in `configuration/staff.json` for Baptism Follow-Up, and Isaac Silva for Follow Jesus Follow-Up.
5. A workflow whose step was completed in the last 14 days is Active, and shows in its assignee's My Workflows list by the rules Rock's list uses. An older one is Completed, by its assignee.
6. Each story in the file's `cast` list shows. Josh Sanders's Baptism Follow-Up is complete, with Aaron Mitchell as Baptized By, the certificate sent, Foundations offered, and the note in the file. Dennis Perry's is open (decision 293).

The 14 days count from the load's install day. A load and the verify tool pass the load's anchor. A later `--check-cast` run judges it on the database server's today, so run it on the install day.

## Business expectations

Every load also checks the businesses, Cast and generated alike, and prints the result after the Cast expectations:

1. Each campus that has families has at least its minimum number of businesses: 6 on the main campus and 2 on every other campus.
2. Every business has at least one business contact.
3. Every business has a gift in the last 12 months, counted back from the newest gift in the database, so a later `--check-cast` run gets the same answer.

Core 1 has 6 businesses at 1 campus, Core 3 has 10 at 3 campuses, and Full 7 has 86 at 7 campuses. All checks hold.

## Rock version guard

The loader ports some of Rock's code: the giving automation math, the alert rules, the value formats, the streak and achievement math, the five assessment scoring services with the reminder job's rules, Family Analytics' eRA rule, and Calculate Peer Network's scores. It also runs some of Rock's procedures from the text Rock installed, with their clock set to the job's last run: Family Analytics' giving and attendance counts. Verify also scores every assessment answer set with Rock's own services (see [Assessments](../docs/assessments.md#how-it-is-checked)), and runs Rock's own Family Analytics and Calculate Peer Network procedures on every load (see [Verification](../docs/verification.md)). If Rock changes that code, the port goes stale. Two checks catch it.

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

The Full 7 package, with its own Crowd of 16,800 households (decision 136), passes 407 of 456 values (89%) on 2026-10-25, at the anchor 2026-10-22 and the default seed, with the adult age fixes (decisions 166 and 167). Main without them passed 404 the same day. Communications added 10 graded values since 2026-10-23. Every adult age band and every share of women by age now passes: 18 to 24 is 18.7% against 18.5%, 25 to 34 is 14.8% against 15.3%, 35 to 44 is 15.7% against 15.7%, and women aged 35 to 44 are 62.8% against 63.3%. Household sizes, the share of adults, and the child and teen bands do not change. Five connection request values that passed on main now miss: the share with a connector (32.0% against 35.2%), the 90th percentile of days to connect, and the July, September, and October month indexes. They belong to the second gap below. The Phase 0 spike (Full 7 plus a 17,000-household crowd) passed 260 of 291 values (89%), with giving at 191 of 196 and small groups at 3 of 3. The largest remaining gaps:

1. First-time visitors run low, so Easter and fall kickoff do not spike with newcomers. A visitor model comes with the real Crowd.
2. A few values come from small samples, so they swing past their tolerance with any change to who is generated. Full 7 has about 1,050 connection requests, about 90 a month, and they all come from one random stream per campus that starts with every volunteer who joined. A change to who serves reshuffles every request. At a second seed (`rock-sample-data-2`), main itself misses the same connection values that the adult age fixes miss at the default seed, and with the fixes it passes 404 values against main's 396. The holiday first-time values are the same kind (see the first gap).
3. A few holiday attendance multipliers miss. Christmas week's adult check-ins run 0.22 of a normal week against 0.30. The only adults who check in are volunteers and recent graduates. More generated adults are 18 to 20 now (decision 166), so more recent graduates stay away at Christmas. A recent graduate's student check-ins count as adult check-ins, because they are adults now, and students do not check in on Christmas week. The holiday first-time values swing a few points with each change, because few check-ins are first times (the first gap). The engagement mix (attends, gives, groups, serves) waits on serving teams.
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

Full 7 now draws 3,587 scanned checks in its 12-month window (7,174 images, 274 MB).

