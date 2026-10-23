# Roadmap

Where the project stands: the team's working list. Update this page whenever something finishes or a new need comes up. For the community-facing list of planned work, see [Planned work](planned.md); keep the two in step when an item starts or finishes.

## Done

- The design spec and the knowledge framework.
- Calibration scripts (parts 1 to 4) that run inside Rock's SQL editor, the normalizer, and profile v1.
- The church setup as data: 7 campuses, Sunday service times, funds with campus accounts, check-in rooms, and connection statuses.
- The Cast: 38 legacy families converted, plus 45 new families and 4 new businesses, for 87 households and 185 people. Featured families have a bio for each person, and every family has a page.
- The address pool: 18,000 real-street addresses with house numbers that do not exist, and the Cast moved onto it.
- The Phase 0 builder: configuration, people, Sunday attendance and kids check-in, giving history, a generated crowd, and validation. Core 1 loads in about 4 seconds. Full 7 with a 17,000-household crowd loads in about 29 seconds (about 44 with check images) and passes 89% of measured values.
- Photo rules, photo ethnicity for every Cast adult, and the image agent's brief.
- The public site's church page has an Initiatives section: Starting Point, the first, with its artwork, and the Built on the Rock campaign. The brand page offers initiative artwork as downloads. On phones, the top navigation folds behind a Menu button.
- Seven distinct [campus exterior photos](../photos/campuses/README.md), ready for Sunday morning without people. Goodyear meets in a high school auditorium and sets up and tears down every Sunday, Scottsdale keeps the traditional church building it had as an independent church before it merged, and Phoenix follows the existing building references. The public site's campus cards and downloads include the set (decision 135). Campus image loading into Rock remains separate work.
- One building fund. Rock's Building Fund is now the Built on the Rock campaign fund, and the separate campaign fund is gone. Building upkeep comes from the General Fund, so givers who gave to the Building Fund give the same amount there (decisions 98 and 99).
- All 45 legacy Cast portraits have been updated and approved. The temporary photo review is closed.
- Every Cast photo is delivered: 130 new people and the 45 legacy redos (175 photos), plus the 120-photo Core 3 pool. All are in `photos/manifest.json`.
- Photos load into Rock: every Cast person's delivered photo shows on their profile, and in Core 3 about half of the generated households get faces from the pool, one photo ethnicity per family, with no face repeated on a campus (decision 30).
- Small groups: the pattern in [Small groups](small-groups.md) (decisions 33 to 40). The Cast has 8 active groups, 2 inactive, and 2 archived, planned into `cast/groups`; generated households get groups per campus at load time. Groups load with members, meeting places, attributes, history, Group Finder test cases, and weekly attendance on the church year.
- Scanned check images for every check gift: five fictional banks (each with its own logo and paper color), a wallet per giver so card, bank, and check details agree, mailed business checks, bundled open fonts, and check scanner matching fields. Stored as Rock transaction images (decisions 25 to 27).
- Combined and separate giving: most couples give as one giving unit, about 10% give separately, and adult children at home give on their own (decision 42).
- Giving realism: check writers follow age and gift size, onsite givers catch up after a missed Sunday, amounts vary the way real gifts do, schedules change now and then, givers come and go, and the largest givers give stock now and then. Giving passes 191 of 196 measured values (decision 43).
- Giving analytics: Rock's giving attributes, bins and percentiles, journey stages with history, and three alert types with 12 months of alerts (decisions 44 and 45). Cast giving expectations are checked on every load (decision 46), and a version guard checks Rock on GitHub for changes to the code the loader ports (decision 47).
- The builder and loader split: the [builder](../builder/README.md) writes one package per dataset (format 1: data only, a checksum for every file, byte-identical when rebuilt), and the [loader](../loader/README.md) library and CLI install packages. Each package is for one exact Rock, checked by its newest migration (decision 52). Loads through packages matched the old direct loads exactly for all three datasets.
- Kids check-in streaks and achievements: one Kids Check-in streak type, seven achievement types from High Five to Century Club, and each child's streak and attempts the way Rock computes them. Next-gen check-in shows them (decisions 57 to 61). Cast achievement expectations are checked on every load, the version guard covers the ported code, and a parity test runs Rock's own code on loaded data. Core 1 and Full 7 match Rock exactly.
- Group photos: each of the Cast's 12 small groups has a photo of its real members, made from their profile photos from the [group photo brief](../photos/group-brief.md). Every dataset sets them as the groups' photos, and Group Finder shows them on its cards (decisions 56 and 62).
- The [verify tool](../verify/README.md) (decision 66): a trusted base made from the Rock release by its migrations and start-up steps, a contract recorded from real loads, invariants, Rock's own Rock Cleanup job as a referee, the content standards as checks, and a receipt per package with a publishing gate.
- Businesses at every campus: at least 6 on the main campus (all from the Cast) and at least 2 at each other campus, or the measured share of giving units when that is more. Every business has one or two contacts, loaded both ways as Rock known relationships, a work address, and gifts in the last 12 months. Every load checks all of it. Full 7 has 86 businesses, and both measured business values pass (decisions 63 to 65).
- Every package passes the verify tool. The loader now sets what Rock's save hooks and Rock Cleanup would: primary aliases, age classification, family salutations, inactive families, an alternate Id and name metaphones for every person, and a Known Relationship group for every person (see [How people load](../loader/README.md#how-people-load)). The trusted base also runs Rock's first-night post-install job, without which Rock Cleanup blanks every age.

- Prayer requests with an active prayer team (decision 67). Every dataset gets about 10 requests a week per 1,000 active adults, mostly from adults who attended, gave, or were in a small group, in Rock's own prayer categories, with text from `content/prayer`. The Cast's Prayer Administrator approves them, and the prayer team (the Cast's Prayer Team plus about 3% of active adults) prays through each one while it is active, as "Prayed" interactions with matching prayer counts. A few are flagged back to the approval queue, and some get comments, replies, and answers. The Cast has its own requests too, covering every prayer screen state, with 10 always from the last few days so even the small datasets show a full prayer list; every load checks them. Calibration part 5 grades it: all 87 prayer values pass on Full 7 with the crowd (about 11,100 requests and 67,000 prayers), and validation overall is 347 of 380 (91%).
- Serving teams and serving opportunities at every campus (decisions 68 to 77; see [Serving](serving.md)). Rock's Involvement connection type has eight polished opportunities with photos of real Cast volunteers, each placing people on its team at their campus. Every campus has eight teams with rooms, service times, staffing that fits the campus, and a background check for kids and student volunteers. Volunteers have rotations, preferences, and scheduling history over the whole history window plus six weeks ahead, and check in to serve. Connection requests in the measured mix lead to the teams, with activity history and person history. Validation grades serving and connections.
- Notes on people (decision 78): six note types with View security (Rock's Personal, Pastoral, and Phone, plus Follow-up, Counseling, and Safety Alert), 67 hand-written Cast notes with 13 replies in 29 households, and generated notes from text pools. Calibration part 6 measures note use. The measured shape sets timing and length, and the volume and feature use are designed higher, because the sample church shows what good note use looks like. Full 7 has about 54,000 notes and 7,000 replies, and all 15 graded note values pass.
- Security roles (decision 79): Cast people join the out-of-the-box roles their stories give them, mapped in `configuration/security-roles.json`, and each campus pastor joins RSR - Staff Workers and RSR - Pastoral Workers. The load creates no roles.
- The public site (decisions 80 to 82; see [site/](../site/README.md)): why the sample data matters, Rock Solid Church and its building tour, a page for every Cast household, the datasets, how it's built, and how to contribute. It is built from the repository's own files with no packages, and the brand page offers the logo as SVG and PNG, the palette, and every photo set as a zip. The logo now has vector versions, redrawn from the approved artwork.
- The adult Discipleship Path (decisions 83 to 87 and 89, 106 to 108; see [Steps](steps.md)). Rock's program is built out to seven steps from `configuration/steps.json`, and every step comes from a record the data already has: group and team memberships, the giving journey, baptism dates, and real class dates. Classes are in progress at every campus, and Rock's program completions are written the way its job does. The Cast's stories (Ted Decker's completion, the Gilberts and Bri Hall in Starting Point) are checked on every load, and new invariants tie each step to its source. Core 1 has 438 steps, 64 baptisms, and 4 program completions.
- Pledges (decisions 97 to 105 and 109, and the ones added as it was built; see [Pledges](pledges.md)): Built on the Rock, a two-year building campaign about 38% through on install day, on Rock's Building Fund, renamed Built on the Rock, with campus accounts, both pledge pages, and statement pledge settings. The Cast has 15 pledges and 3 givers with campaign gifts and no pledge, each story checked every load. Generated givers and businesses pledge at the profile's 12% of giving units, in every follow-through state, and validation grades the rate. The pledges and campaign gifts load with the loader's Giving part (decision 126).
- The Student Path (decisions 87, 88, and 115 to 119; see [Steps](steps.md)). A second step program, filed beside the Discipleship Path, with Student Camp, Follow Jesus, Mission Trip, and Student Leader. Its steps follow the student check-ins the data already has, on church-wide camp and trip dates, for designed shares of the students who could take each step. Students who decide to follow Jesus are often baptized a few months later on the Discipleship Path's Baptism step, and Rock's program completions are written for both programs. The Cast's stories (Ava Brooks at camp, Benjamin Lee's camps and trip, and Jasmine Washington's completed path) are checked on every load, and new invariants tie each student step to the student service. Jasmine's file now sets her graduation, so she checks in through her senior year. Core 1 has 13 steps on the Student Path, 3 student baptisms, and 1 Student Path completion.
- The loader library is ready for Rock (decisions 120 to 126; see [Using the library](../loader/README.md#using-the-library)). It builds for .NET Framework 4.7.2 too, on Rock's own package versions, and packs as the `SparkDevNetwork.Rock.SampleData.Loader` NuGet package (0.1.0-alpha.9, a local file for now). A load can choose its parts, steps among them, reports progress instead of writing to the console, takes the server's PasswordKey and DataEncryptionKey, and refuses a database with people beyond a new Rock install under an application lock. `SampleDataPackageIndex` reads the hosted `index.json`, and `SampleDataDatasets` gives Rock's installer a card for each dataset, with people, households, and install times measured from real loads that verify keeps current. `loader-compat-check/` proves the key derivations give the same bytes on both builds.
- Full 7 has its Crowd (decision 136). Its manifest lists 16,800 generated households across all seven campuses, so a load has 25,248 people in 16,967 households, where it had only the Cast's 185 people before. It loads in about 55 seconds with check images, and the package stays 14.6 MB, because the Crowd is generated at install time.

## In progress

- **The Sample Data Installer block in Rock**, built in the Rock repository against the loader package. It shows Core 1, Core 3, and Full 7 as cards from the dataset catalog and downloads the chosen package from a hosted `index.json`, beside the old sample data block.
- **Communications** (decisions 90 to 96; see [Communications](communications.md)). Email, texts, and push with very large mass sends, opens and clicks, SMS conversations, one-to-one messages, and messages tied to steps. Slice 1 is done: the design, calibration part 7, and the church app stub. Source-1 has run part 7, and its communication shapes are in the profile. The plan now covers the Unsubscribe and Saturation reports (decisions 131 to 133), and part 7 measures saturation and a year of unsubscribes. Source-1 has rerun part 7, so the profile has its saturation shape, texted STOP rate, and unsubscribe timing. Slice 2, the setup, is done (decisions 137 to 141): the main and student texting lines and staff personal numbers (one per campus on Full 7), a weekly newsletter per campus and seven ministry and staff lists with members that follow involvement, the branded Rock Solid Church email template, and the message library in `content/communications` (49 emails, a newsletter built from pieces, and 32 texts and pushes). Isaac Silva is now the Student Pastor. Next: bulk email.
- **The release workflow** (decisions 127 to 130; see [Publishing a release](maintaining.md#publishing-a-release)). `.github/workflows/release.yml` builds each Rock release in `release/rock-releases.json`, writes and verifies every package, and runs the publishing gate. On a version tag it pushes the loader to nuget.org and uploads the packages, receipts, and index to Azure, the index last. A dry run on GitHub's `windows-2025` runner on 2026-10-22 built Rock 21.0.2, ran SQL Server 2025, passed verify on all three packages, and passed the gate. Its Packages job took 66 minutes (about 30 to build Rock, 6 for SQL Server, and 28 for verify). A self-hosted runner stays a fallback. The one-time setup in [Maintaining](maintaining.md#one-time-setup) is done (2026-10-22): the Azure identity and its role, nuget.org trusted publishing for `SparkDevNetwork.Rock.SampleData.Loader`, and the `release` environment with its approvers and variables. What is left:
  - A stable release (for example `v1.0.0`), which updates `index.json` for every Rock. The first prerelease, `v0.1.0-alpha.10` on 2026-10-23, published the loader to nuget.org and all three packages with `index-prerelease.json`.
  - Change the Rock 21.0.2 entry from a develop commit to its tag once Rock tags it.

## Waiting on a decision

Nothing right now. (Tempe was confirmed as the seventh campus on 2026-10-16; see decision 19.)

## Next

1. **Close validation gaps.** First-time visitors run low and teens run low.
2. **Run the giving parity test** against a running Rock: load Full 7, run Rock's Giving Automation job, and compare (see [Validation](../validation/README.md#rock-version-guard)).
3. **Per-person attendance,** so a teen can attend more often than their parents. The Student Path does not wait for it: its steps follow whatever student check-ins exist (decision 115). This work should also give recent graduates their student years: generated adults aged 18 to 20 were in high school inside the history window but never checked in as students, because only children carry a graduation year. Once they do, the Student Path shows their history with no change to the steps.
4. **Real Crowd names** from public-domain frequency data by birth decade.

## To do (parked)

Ideas we agreed on but set aside for later. Move an item into "Next" when we pick it up.

### Small groups

1. **Women's groups.** Add women's small groups led by women, with a Women's Bible Study topic. Alisha Marble's existing group becomes one of them; until then it is not part of the small group design.
2. **Leader requirements.** Use Rock's group requirements (for example, a background check for leaders), with a few leaders whose check is missing or expired so Rock shows the warning.

### People

1. **Logins for Cast staff.** The Cast files list logins, but the loader does not load them yet, so no one can sign in as a Cast staff member. It needs a decision on demo passwords.
2. **Cast group membership that disagrees with stories.** The Ortizes are active members of the Greggs group, though their story says they stopped coming six months ago, and the Ellises are in groups older than their first visit.
3. **The Cast's other known relationships.** Some Cast people list `can-check-in` and `allow-check-in-by` relationships. Only `business` loads so far.

### Serving

1. **A midweek student night.** Calibration shows about 31% of check-ins on Wednesday, and the sample church has no midweek service. Set aside on 2026-10-19 while serving is built.

### Steps

Both step programs are built (see [Steps](steps.md)). These ideas from its "Later" section wait for other work or a reason to pick them up. Keep the two lists in step.

1. **eRA steps** in Rock's Core Steps program, from the attendance the data already has.
2. **Links to learning and events.** Foundations links to a learning class, and Student Camp and Mission Trip to a registration, once learning (LMS) and event registrations are built.
3. **Student small groups,** which would add a group step to the Student Path.
4. **Step workflows,** such as a follow-up after a baptism.

### Packages and loaders

The builder and loader are split (see Done). What is left:

1. **Configuration through Rock's services.** The library now runs inside Rock (see Done and In progress), but it still writes configuration with SQL. Saving it through Rock's services would run Rock's save hooks.
2. **Download a package by version** in the loader CLI (`--version 1.2.0 --dataset core-1`), once packages are hosted (see Publishing).
3. **Record the loader version in each package.** Dataset packages should record the loader version that built them. The package format keeps its own version, so a loader can tell what it can read.

Publishing the loader to NuGet is in the release workflow (see In progress).

### Publishing

Publishing packages to Azure is in the release workflow (see In progress).

## Later (Phase 1 and beyond)

Registrations, connection requests beyond serving, learning (LMS), benevolence, reminders, and person attributes for the Crowd.

## Open questions

These come from the [design spec](design-spec.md).

1. Where dataset packages are hosted (the legacy data lived on `storage.rockrms.com`).
2. Whether the Rock loader replaces the inside of the legacy `SampleDataManager` or sits beside it.
3. Whether photos are stored in the database or through a file storage provider.
4. The final meaning of each connection status in the journey.
