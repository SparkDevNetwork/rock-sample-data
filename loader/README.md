# Loader

The loader installs a dataset package (made by the [builder](../builder/README.md)) into a fresh Rock database. It generates every date for the install day, so "last 12 months" reports are always full, and bulk-loads everything in one transaction. Rock never references this repository; it will use the same loader library.

There are two parts:

- **The loader library** (`loader/`, `RockSampleData.Loader`). `SampleDataPackage.Open` reads a package and checks every file against its checksum. `DatasetLoader.Load` installs it, with a `DatasetLoadOptions` for the anchor, seed, parts, progress, keys, and the rest. It builds for .NET 10 and for .NET Framework 4.7.2, and Rock's block uses it through the `SparkDevNetwork.Rock.SampleData.Loader` NuGet package (see [Using the library](#using-the-library)).
- **The loader CLI** (`../loader-cli/`, `rock-sample-data-load`), for developers and test harnesses.

## Loading a package

Build a package first (see the [builder](../builder/README.md)), then load it from the `loader-cli` folder:

```
dotnet run -- --package ../packages/core-1.zip --connection "Server=localhost;Database=RockSample;Integrated Security=true;TrustServerCertificate=true"
```

| Option | Meaning |
|---|---|
| `--package <file>` | The dataset package to load. Required for a load. |
| `--connection` | Connection string to a fresh Rock database. Required. |
| `--parts` | The parts to load, by name and comma separated, for example `--parts Giving,Notes,Prayer`. The names are `Photos`, `Attendance`, `SmallGroups`, `Serving`, `Giving`, `CheckImages`, `Prayer`, `Notes`, `Steps`, and `Communications`. The parts they need load too (see [Parts](#parts)). Without it, every part loads except check images. |
| `--any-rock` | Loads even when the database is not the Rock the package was built for, with a warning. For development databases (see [Which Rock a package is for](#which-rock-a-package-is-for)). |
| `--anchor` | The date every relative date counts back from (`yyyy-MM-dd`). Defaults to today. Pin it in tests for identical data. |
| `--years` | Years of attendance and giving history. Defaults to 3. |
| `--seed` | The text every random choice is seeded from. The same seed and anchor give the same data. |
| `--crowd` | Adds this many spike crowd households across the dataset's campuses, in place of the dataset manifest's generated households. Used for speed tests. Full 7's manifest already lists its Crowd. |
| `--calibrate <file>` | Loads nothing and needs no package. Runs calibration parts 1 to 3, part 5 for prayer, part 6 for notes, and part 7 for communications against the database and writes their raw output to the file. The scripts are built into the CLI from `../calibration/sql`. |
| `--check-images` | Draws scanned check images for check gifts (the `CheckImages` part). Off by default, because it is the slowest step (about 14 of Full 7's 55 seconds). Release builds turn it on. |
| `--check-image-months` | Months of check gifts that get scanned check images, overriding the dataset manifest (`../datasets/{dataset}.json`). 0 means every check. Without either, 12. Implies `--check-images`. |
| `--rock <folder>` | A Rock checkout (for example `C:\Development\Rock`). Its `RockWeb/web.config` PasswordKey and DataEncryptionKey key the check scanner fields. The `ROCK_PATH` environment variable does the same. Without either, those fields stay empty and everything else loads. |
| `--web-config <file>` | A specific Rock web.config to read the keys from, instead of `--rock`. |
| `--check-image-copies <folder>` | Also saves each check image to this folder, for looking at them. Implies `--check-images`. |
| `--check-cast` | Loads nothing. Checks the package's Cast expectations (giving, campaign, achievements, businesses, prayer, and steps) against an already loaded database (see [validation/](../validation/README.md#cast-expectations)) and exits with 1 if any fail. Every load also checks them. With `--parts`, it checks only those parts. |
| `--giving-parity` | Loads nothing. The release-time parity test: after Rock's own Giving Automation job has run on a loaded database, compares what Rock wrote with the loader's port (see [validation/](../validation/README.md#rock-version-guard)). Use with `--dataset` or `--package`. |
| `--dry-run` | Do everything, then roll it all back. |

The whole load runs in one transaction, so a failure leaves nothing behind. Every inserted row carries `ForeignKey = SampleData:<dataset>`, except `Metaphone` rows, which have no such column (see [How people load](#how-people-load)).

## Which Rock a package is for

Each package is for one exact Rock (decision 52). Its `package.json` records the Rock version (for example `21.0.1`) and that Rock's newest Entity Framework migration (for example `202609222138362_AddWorkflowBuilder`). A Rock database stores no version number, but its newest migration identifies its schema exactly. So before loading anything, the loader compares the two, and refuses a database whose newest migration is different.

On the develop branch, new migrations land often, so the check would block development loads. `--any-rock` turns the refusal into a warning. Rock's own installer never uses it.

## What the loader checks before loading

1. The package's format is one this loader reads (format 1).
2. Every file matches its SHA-256 in `package.json`, no listed file is missing, and nothing unlisted was added.
3. No other sample data load is running on the database. The load takes an exclusive SQL Server application lock (`sp_getapplock`, owned by its transaction) before it checks anything else, so two loads started at once, even from two web servers, cannot both go on.
4. The database has no people beyond the five a new Rock install creates (decision 122). They are Admin (Person Id 1, because its Guid differs between installs), Anonymous Giver, Presence, Anonymous Visitor, and System Sender. RestUser records, which are API keys, never count. A database that already has sample data fails this check too.
5. The database is the Rock the package is for (see above).

Checks 3 to 5 run inside the load's transaction, after the lock. Any failure stops the load before it writes anything. `DatasetLoader.CheckDatabase` runs checks 4 and 5 on their own, so a caller can say what is wrong before anyone presses Load.

The load runs these steps in order: configuration (with the small group, serving, connection, and building campaign setup), people (with business contacts and security roles), photos, Sunday attendance, achievements, small groups, serving, connection requests, giving (with the pledges and campaign gifts), giving analytics, giving alerts, prayer, notes, steps, Cast and business expectations, and check images. Configuration, people, and the Cast expectations always run. The others belong to [parts](#parts).

## Parts

A load can choose its parts (decision 121). Church setup and people always load. The building campaign is church setup: its fund, campus accounts, campaign dates, Create Pledge page, and statement settings load in every load (decision 126). The pledges and campaign gifts, the Cast's and the generated ones, belong to `Giving`: the giving step writes them, so they load only with giving. The fund's goal is part of the fund, so it is written in every load. `SampleDataParts.All` is the one table of parts, in load order:

| Part | Name | What it adds | Needs |
|---|---|---|---|
| `Photos` | Photos | Profile photos for the Cast and some generated families, and photos for the serving opportunities | |
| `Attendance` | Attendance and check-in | Sunday attendance and kids check-in over the whole history, with check-in streaks and achievements | |
| `SmallGroups` | Small groups | Small groups with leaders, members, meeting places, photos, history, and weekly attendance | |
| `Serving` | Serving teams | Volunteers on every serving team, with schedules, check-ins, and requests to serve | |
| `Giving` | Giving | Gifts, scheduled gifts, and batches, with giving analytics, a year of giving alerts, and the building campaign's pledges | |
| `Prayer` | Prayer requests | Prayer requests with the prayer team's prayers, comments, and answers | Small groups |
| `Notes` | Notes | Notes on people in six note types, secured to staff | |
| `Steps` | Steps | Steps on Rock's Discipleship Path and the Student Path, from baptism and classes to small groups, serving, giving, leading, camps, and mission trips, with the Baptism Date on each baptized person and program completions | Small groups, serving teams, giving |
| `Communications` | Communications | The church's texting numbers, its branded email template, and its communication lists, with members who match the people in the church | Small groups, serving teams, prayer requests |
| `CheckImages` | Check images | Scanned front and back images of recent check gifts | Giving |

`SampleDataParts.WithDependencies` adds the parts the chosen ones need, and a load always calls it. A part needs another only when its rows point at or must agree with that part's rows:

1. **Check images need giving.** Each image row points at its gift.
2. **Prayer needs small groups.** Some Cast prayer requests are shared with their small group, and who asks for prayer depends on who is in a group, which the prayer step reads from the loaded groups.
3. **Steps need small groups, serving teams, and giving** (decision 125). Each Small Group, Serve, and Lead step copies one membership, read back from the `GroupMember` rows the small groups and serving parts wrote, and points at its group. People with an open request to serve, who go first in the current Starting Point Class, come from the serving part's connection requests. Each Give step must agree with the journey stage the giving part writes. Student Path steps follow student check-ins, but they read them from the attendance plan, not the rows, so steps do not need attendance.

4. **Communications need small groups, serving teams, and prayer requests.** The Volunteers and Small Group Leaders lists copy the team and group memberships read back from the rows those parts wrote, and the Prayer Team list holds the prayer part's team. Who is involved enough for a newsletter comes from the attendance and giving plans, so communications do not need those parts.

Other parts read another part's plan, not its rows, so they need nothing. Serving and giving read the attendance plan (volunteers serve and families give on Sundays they came), steps read its student check-ins, and prayer reads the giving plan. Pledges are planned before giving in every load, because their campaign lines are part of the giving plan, so prayer reads the same gifts whether or not giving loads. The generated pledgers are drawn from the giving patterns that people carry, and people always load, so the draw needs no part. No part reads pledge rows. The loader plans every part in memory, chosen or not, and skips only the writes of the parts not chosen. Each piece of data draws from its own seeded stream, so a chosen part's rows are the same as in a full load. Group photos belong to small groups and opportunity photos to photos, so no part's choice changes the photos part's rows. Steps come last of the data parts and plan from rows (besides the student check-ins), so they plan only when they load. Nothing after them reads what they plan or write, so leaving them out changes no other part's rows.

Cast expectations check only the parts that loaded: giving stories and campaign stories need `Giving`, achievement stories need `Attendance`, prayer requests need `Prayer`, step stories need `Steps`, and staff texting numbers need `Communications`. A business's contacts are always checked, and its recent gifts only with `Giving`.

On 2026-10-21, Core 1 with `--parts Giving,Notes,Prayer` (which adds small groups) was compared table by table with a full load. Every table the loaded parts write had the same rows. The only tables that changed belong to the parts left out: photos, attendance and achievements, and serving with its connection requests. Shared tables such as `Attendance` and `GroupMember` lost exactly the rows of those parts, and their small group rows were the same. After steps joined, a full Core 1 load matched main's load table by table, and so did Core 3 and Full 7. Core 1 without `Steps` differed from the full load only in the step tables (`Step`, `StepProgramCompletion`, `StepType`, and `StepProgram`) and in the 192 Baptism Date and Baptized Here values. Core 1 with `--parts Steps`, which adds small groups, serving teams, and giving, had the same steps, step values, memberships, gifts, and small group and serving attendance as the full load. On 2026-10-22, after the Cast's pledges joined giving, a load of every part matched main's load in every table for all three datasets. Core 1 without `Giving` (and so without steps and check images) had no pledges, and differed from the full load only in the giving tables, the giving attributes and giving automation setting, the campus tithe metric, and the step tables and Baptism values. Core 1 with `--parts Giving` had the same pledges, gifts, giving attributes, and tithe metric as the full load. After the Crowd and business pledges joined, a load of every part again matched main's load in every table for all three datasets. Core 3 has Crowd households and 27 pledges. Without `Giving` it had no pledges and the same fund and goal, and it differed from the full load in the same tables as Core 1 did. Core 3 with `--parts Giving` had the same 27 pledges and the same rows in every financial table as the full load.

## Using the library

Rock's Sample Data Installer block calls the library through the `SparkDevNetwork.Rock.SampleData.Loader` NuGet package. The package holds a .NET 10 build and a .NET Framework 4.7.2 build (decision 120). `dotnet pack loader/RockSampleData.Loader.csproj -c Release -o artifacts` writes it to `../artifacts/`. The package ID names Spark as its owner, while the assembly and namespace stay `RockSampleData.Loader`.

```csharp
var package = SampleDataPackage.Open( uploadedZipStream );
var readiness = DatasetLoader.CheckDatabase( connectionString, package );
if ( !readiness.IsReady )
{
    // Show readiness.Problems, one plain sentence each.
}

DatasetLoader.Load( connectionString, package, new DatasetLoadOptions
{
    Parts = new[] { SampleDataPart.Giving, SampleDataPart.Prayer },
    Progress = progress,
    PasswordKey = passwordKey,
    DataEncryptionKey = dataEncryptionKey
} );
```

| Option | Meaning |
|---|---|
| `Parts` | The parts to load. Null loads every part. The load adds the parts they need. |
| `Progress` | An `IProgress<LoadProgressReport>` that gets every line the CLI prints. Each report has the `Message`, the `StepName` when the line starts a step (null for a detail line), and `CompletedSteps` of `TotalSteps`. `TotalSteps` counts only the steps this load runs. Reports come on the loading thread, in order. The library never writes to the console. |
| `PasswordKey`, `DataEncryptionKey` | Rock's keys, as its web.config holds them (the PasswordKey in hex). Each one is used instead of the one in `RockWebConfigPath`. Inside Rock, pass the server's own, so check scanning and note mentions work on that server. |
| `IsAnyRockVersionAllowed` | Loads on another Rock with a warning. `CheckDatabase` never honors it. Rock's block leaves it false. |

`Load` throws `DatabaseNotReadyException` (with every problem in `Problems` and the message) when the database has people or another load is running, and `RockVersionMismatchException` when the only problem is the Rock version. Nothing is written in either case. The loader bulk-copies past Rock's services, so Rock's caches are stale after a load, and the block clears them.

### The .NET Framework build

The .NET Framework build depends on the exact versions Rock ships: Microsoft.Data.SqlClient 6.1.4, System.Text.Json 10.0.0, SixLabors.ImageSharp 2.1.12, SixLabors.ImageSharp.Drawing 1.0.0, Hashids.net 1.7.0, and System.IO.Compression 4.3.0. A newer version would add a second version of an assembly to RockWeb/bin. A .NET Framework program that uses the package needs Rock's binding redirects for System.Memory and System.Numerics.Vectors, which Rock's web.config already has.

`compat/` holds the few .NET 10 features .NET Framework lacks, as internal types under `#if NETFRAMEWORK`: the init-only marker, `GetValueOrDefault` and key-value pair deconstruction, and the two key derivations behind Rock's encryption (`KeyDerivation`: PBKDF2, and HKDF written out from RFC 5869). Everything else is written a way both understand, such as `list[list.Count - 1]` instead of `list[^1]`.

The check scanner fields and note mentions are keyed by those derivations, so `../loader-compat-check/` proves both builds give the same values. It checks PBKDF2 and HKDF against the RFC 6070 and RFC 5869 test vectors, and Rock's key bytes, keyed hash, IdKey, package checksums, and seeded random draws against values from .NET 10's own methods. Run it for each build, and both must pass:

```
dotnet run --project loader-compat-check -f net10.0
dotnet run --project loader-compat-check -f net472
```

The two builds are meant to load the same rows. One difference is expected: .NET Framework rounds `DateTime.AddMinutes` and the other `Add` methods that take a fraction to the nearest millisecond, and .NET 10 does not. So a few generated times, such as when a prayer request was approved, can differ by under a millisecond. On 2026-10-21, a .NET Framework load and a .NET 10 load of attendance, small groups, and prayer matched in every table when times were compared to the second, and a later Core 1 load of every part but check images had the same steps on both builds. That later load found two gaps that are not fixed yet:

1. **Serving schedules differ.** A few volunteers are asked to serve in a different room on some Sundays (Core 1: 3,113 team occurrences on .NET Framework, 3,119 on .NET 10). `ServingLoader` picks a room with `OrderBy( load ).ThenBy( random draw ).First()`. .NET 10's `First()` draws the tie-break only for rooms that tie, and .NET Framework draws it for every room, so the two builds use the seeded stream differently. A fix draws every tie-break before sorting, which changes the serving rows on both builds.
2. **Three giving attributes differ slightly:** gift frequency mean, its standard deviation, and next expected gift date, with the giving alerts that follow from them. The cause is not traced yet.

## The package index

The hosted list of packages is `index.json` (decision 123). The [release workflow](../docs/maintaining.md#publishing-a-release) merges each release into it and uploads it last, at `https://storage.rockrms.com/sampledata/packages/index.json`, beside the versioned packages (decision 128). A prerelease changes only `index-prerelease.json` beside it (decision 129). `SampleDataPackageIndex.Parse` reads it, and it refuses text that is not JSON, another format version, or an entry without a required field.

```json
{
  "formatVersion": 1,
  "packages": [
    {
      "dataset": "core-1",
      "name": "Core 1",
      "description": "The Cast: 87 households and 185 people at one campus.",
      "people": 185,
      "campuses": 1,
      "version": "1.2.0",
      "url": "v1.2.0/rock-21.0.2/core-1.zip",
      "sha256": "b94754207879953509261c3a244e7432eab882526024aa2fd84c9647502a0650",
      "size": 15086598,
      "rockVersion": "21.0.2",
      "rockMigration": "202610141827486_CodeGenerated_20261014"
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `formatVersion` | The index format. This loader reads format 1. Required. |
| `packages` | Every package, in the order to show them. A dataset can appear once per Rock version. |
| `dataset`, `name` | The dataset key and its name. Required. |
| `description` | A sentence or two about the dataset. Optional. |
| `people`, `campuses` | About how many people the dataset loads, and its campuses. |
| `version` | The package release, from the release tag. Required. |
| `url` | Where to download the package: absolute, or relative to the index's own URL. Required. |
| `sha256`, `size` | The .zip's SHA-256 in hex (64 digits) and its size in bytes. Check a download against them before opening it. The SHA-256 is required. |
| `rockVersion`, `rockMigration` | The Rock the package is for, the same values as its `package.json`. A caller can hide packages for another Rock without downloading them. Required. |

## The dataset catalog

`SampleDataDatasets.All` lists the datasets this loader can install, in key order: Core 1, Core 3, and Full 7 (decision 124). Rock's installer shows one card for each before it downloads anything. `SampleDataDatasets.Get( "core-3" )` returns one card, or null for an unknown key.

Each `SampleDataDatasetInfo` has the `Key` and `Name`, a `Summary` of who is in it, what it is `BestFor`, two to four `Highlights`, and the `People`, `Households`, `Campuses`, and `TypicalInstallSeconds` to show. The values come from the card fields in [`datasets/*.json`](../datasets/README.md#the-dataset-card), which the loader project embeds. So the catalog ships in the same NuGet package as the loader that installs those datasets.

Two checks keep the cards true:

1. The builder refuses to write a package whose manifest is missing a card field, or whose `campuses` differs from `configuration/campuses.json`.
2. The verify tool's invariants fail a load whose people or households differ from the card by more than 2%. Only a load at the default seed is held to the card, because the card was measured at that seed.

The numbers were measured with the CLI: the default seed, 3 years of history, and every part with check images, each run on a fresh copy of the verify tool's trusted base (`RockSampleVerify-base`), which is the Rock the packages are for. Core 1 and Core 3 were measured on 2026-10-21, again after the building campaign joined the load. Full 7 was measured on 2026-10-23, after its manifest gained the Crowd (decision 136). Households are family groups, businesses included. The install time is the median of three runs of the CLI, start to finish, on a 16-core development machine.

| Dataset | People | Households | Campuses | Install time (three runs) |
|---|---|---|---|---|
| Core 1 | 185 | 87 | 1 | 10 seconds (10.4, 10.4, 10.5) |
| Core 3 | 405 | 241 | 3 | 12 seconds (12.2, 12.0, 11.4) |
| Full 7 | 25,248 | 16,967 | 7 | 55 seconds (about 57, 54.3, 54.6) |

Full 7 draws images only for the last 12 months of check gifts: 3,587 checks and 7,174 images (274 MB), about 14 seconds of its load. Every load of the same package, seed, and anchor has the same people and households, so the three runs agree exactly. Before the Crowd joined, Full 7 had only the Cast (185 people in 87 households) and loaded in about 11 seconds.

The earlier measurements used a copy of `RockSample-base`, and each install time was about a second longer there. No load finishes on that database now. It is an older Rock whose Create Pledge page has Rock's Obsidian Pledge Entry block in place of block `4898B608-4A08-4C1B-97A7-0500D708B5D2`, the one the campaign setup points at. The shorter times come from the base, not the loader, because the campaign only adds a few statements to the configuration step.

When a change to the data moves a number past 2%, verify fails. Measure again and update the card. One run:

```
rock-sample-data-load --package core-1.zip --connection "<a fresh copy of RockSampleVerify-base>" --check-images --rock C:\Development\Rock
```

Copy the base by backup and restore while holding a shared `sp_getapplock` on its name, the lock the verify tool takes, so the copy never catches the base while verify rebuilds it.

## What makes a load repeatable

A package holds the inputs, not the records: the Cast, the church setup, the dataset manifest, the resolved profile, the address pool, photos, and fonts. The loader generates every record at install. Each package also carries a `README.md` (from `../builder/package-readme.md`) that explains this, for whoever receives the package.

### The seed

The seed is a piece of text that every random choice starts from. It defaults to `rock-sample-data` (`DatasetLoadOptions.Seed`), and `--seed` changes it. It is not stored in the package or the database, and it never comes from the clock or the server.

The loader never draws from one long random stream. Each piece of data gets its own stream, named by the seed and that piece:

| Stream name | What it decides |
|---|---|
| `{seed}:crowd:{n}` | The nth generated household: size, ages, names, and statuses |
| `{seed}:business:{campus}:{n}` | The nth generated business at a campus: its name, cadence, payment method, amount, and whether a married couple are both contacts |
| `{seed}:business-contacts:{campus}` | The order of a campus's families among equals when picking business contacts |
| `{seed}:attendance:{household guid}` | Which Sundays that family came |
| `{seed}:giving:{household guid}:{person}:{persona}:{start}` | That giving pattern's gifts |
| `{seed}:wallet:{person guid}` | That giver's card, bank account, and checkbook |
| `{seed}:small-groups:...` | Group planning, joins, leaders, and group attendance |
| `{seed}:serving:...` | Who serves on which team, each volunteer's rotation and preferences, and their requests to serve |
| `{seed}:connections:{campus}` | Who asks to serve at that campus, and each request's dates, status, connector, and activities |
| `{seed}:addresses:{campus}` | The order of addresses per campus |
| `{seed}:photos:{campus}` | Which pool faces go to which households |
| `{seed}:steps:...` | Who is baptized and when, and who takes Starting Point Class and Foundations in which class |
| `{seed}:check:{transaction id}` | One check image's handwriting and stamp angle |

`generation/SeededRandom.cs` turns each name into a number with a fixed text hash (FNV-1a) and seeds .NET's `Random` with it. So the same name gives the same draws on every machine, a change to one family changes no other family, and the order of the steps does not matter.

### The anchor

The anchor is the date everything counts back from (`--anchor`, default today). The seed decides how a family behaves; the anchor decides which Sundays and holidays fall in the history window. The same seed with a different anchor gives a different history, not just shifted dates.

### What is the same, and what is not

With the same package, seed, and anchor, the data is the same on every server, row for row: people, families, addresses, attendance, gifts, groups, attribute values, alerts, and the contents of every check. The speed work on 2026-10-16 checked this with a fingerprint of 28 tables over more than ten Full 7 loads.

These differ between installs, by design:

1. **Install day.** The anchor defaults to today, so give the same `--anchor` to compare installs.
2. **Ids.** Each table's Ids start after its highest existing Id, so they match only on databases that started the same.
3. **Guids.** The Cast, generated people and families, accounts, and alert types keep stable Guids. Most other rows (attendance, gifts, attribute values, check image files) get a new Guid on every install.
4. **Created and modified times**, a family member's date added, and check image file times come from the install clock.
5. **Check scanner fields** are hashed with each server's own PasswordKey, and the encrypted bank line fields use its DataEncryptionKey with a random IV, so they differ on every install.
6. **Check image handwriting** comes from the transaction Id, so it can differ when Ids differ. The checks' contents do not.

## How people load

People are bulk-copied with Ids assigned up front (the table's current maximum plus one, and up), so every foreign key is known before anything is written. Bulk copy skips Rock's save hooks, so the loader fills in what they would set: birth date and key, age and age bracket, graduation year (using Rock's June 1 grade transition), giving Id and giving leader, primary family and campus, and formatted phone numbers. These are the "post-load fix-ups" from the plan; they happen during the load, so no separate step is needed.

It also fills in what the [verify tool](../verify/README.md)'s Rock Cleanup referee showed was missing. Each follows Rock's own code, named in a note above the loader code that ports it:

| What | How Rock sets it | How the loader sets it |
|---|---|---|
| `Person.PrimaryAliasId` and `PrimaryAliasGuid` | The person save hook, after it adds the alias. | From the alias it writes for the person. |
| Age classification | `PersonService.UpdatePersonAgeClassifications`. | Adult for anyone 18 or older or an adult in any family, otherwise Child. Jasmine Washington, 18, stays a child in her family on purpose, so she is a Child by role and an Adult by classification, the way Rock leaves a child who turns 18. |
| Family salutations (`GroupSalutation`, `GroupSalutationFull`) | `GroupService.CalculateFamilySalutation`, from the person and group member save hooks. | A port of it (`loading/FamilySalutation.cs`), for example "Ted & Cindy Decker". |
| Inactive families | Rock Cleanup marks a family inactive when every member's record is inactive, then dates it. | The same, dated at install (in the Cast, the Murphys and Jenny Greggs's family). |
| An alternate Id (`PersonSearchKey`) | The person save hook, for every new person: 7 random hex digits, a dash, and 7 more. | The same, unique among the alternate Ids already in Rock. |
| Name metaphones (`Metaphone`) | The person save hook queues them for each new first, nick, and last name. | Rock's Double Metaphone code, copied unchanged (`generation/DoubleMetaphone.cs`), for each name Rock does not have yet. SQL Server decides which names are new, as it does for Rock, so its collation matches "Strauß" to "Strauss". The table is keyed by name and shared, so these rows have no `ForeignKey`. |
| A Known Relationship group | Rock Cleanup adds one for every person who owns none, with the person as Owner. | The same, for every person. Business contacts go into these groups. |

A February 29 birthday uses the nearest leap year, so that person's age can be off by one.

## How photos load

`loading/PhotoLoader.cs` attaches profile photos as Rock person images in database storage, one file per person, and sets each person's photo. The rules are in [Photos](../photos/README.md).

- **The Cast.** Every Cast person listed in `../photos/manifest.json` gets their delivered photo. People left out on purpose and businesses are not listed, so they get none.
- **Generated households.** When the dataset manifest sets `photos.generatedHouseholdPercent` (Core 3 sets 50), about that share of each campus's generated households get faces from the 120-photo pool. A household gets faces only when one photo ethnicity has an unused face for every member's gender and age band, so a family looks like one family and no face repeats on a campus. In Core 3, these are the companion households at Peoria and Gilbert.
- **Cast small groups.** After the small groups load, each group in `../photos/groups/manifest.json` gets its photo as the group's photo, stored the way Rock stores a photo uploaded on the group's page (the Default file type). Every dataset has all 12. The configuration step turns on Show Image on Rock's Group Finder page, so the cards show them (decision 62). A package without group photos still loads.
- **Serving opportunities.** In the photos step, each opportunity in `../photos/opportunities/manifest.json` gets its photo, stored the same way, as the opportunity's photo. Every dataset has all eight, which replace the three Rock ships, and the public Serve page shows them. A package without opportunity photos still loads.

Core 1 loads 175 photos in about a third of a second.

## How the timeline is generated

Each Sunday in the history window (3 years by default) comes from `generation/SundayCalendar.cs`. It knows the holidays (Easter, Mother's Day, Memorial Day, July 4, Labor Day, fall kickoff, Thanksgiving, the Sunday before Christmas, Christmas week, and New Year), and it takes each one's attendance multiplier from the profile.

- **Attendance.** Each household gets a rate (from the Cast file, or from its connection status) and a usual service. Adults are recorded on the Weekend Service group without check-in. Kids check in through Rock's check-in template, with a security code per family per Sunday, in the room their grade or age puts them in on that date. A child on their own (usually a student) can check in without an adult.
- **Giving.** Each giving pattern has a cadence (weekly, bi-weekly, monthly, quarterly, irregular, occasional, or one-time) and a channel: scheduled (ACH or card, with a Rock scheduled transaction), online one-time, onsite (check or cash, on Sundays the family attended), or mailed checks. Gifts follow the profile's seasonality and weekday mix, round the way people give, and land in batches by day, campus, and kind: Sunday checks, Sunday cash, mailed checks, non-cash, or online. Checks are always in batches of their own, because churches run them through a check scanner (decision 54). Year-end gifts are sized from the giver's normal month.
  - **Who pays how.** Checks lean to older givers and larger gifts (`PickChannel`); a check writer who rarely comes mails their checks. Apple Pay leans young, and large online gifts lean to bank transfers. Scheduled shares are scaled so the measured share per cadence matches the profile.
  - **Rhythm and amounts.** Onsite givers often catch up after a missed Sunday with one larger check. People who give by hand online keep a usual weekday or day of the month. Hand-given gifts stray from the usual amount some of the time, schedules change amount now and then, irregular and occasional gifts spread widely, and a few givers give exact amounts. Some irregular givers have a small monthly schedule under their extra gifts. The largest givers sometimes give stock (a non-cash gift).
  - **Crowd givers.** Older givers sit higher on the annual giving curve, and the top 1% follow a long tail. A giver whose year is too small for weekly gifts gives less often instead. Givers start and stop inside the history window, which sets year-over-year retention.
- **Generated households.** `generation/CrowdGenerator.cs` samples family size, ages, gender, marital status, activity, attendance frequency, giving cadence, and annual giving from the profile. Each household takes the next address for its campus from the address pool (`generation/AddressPool.cs`). Its names are spike-only placeholders. The dataset manifest's `generatedHouseholds.perCampus` sets an exact count per campus (Core 3: 75 at Peoria and 75 at Gilbert). `--crowd` replaces that with households spread across every campus, with the main campus weighted 1.5.
- **Generated businesses.** `generation/BusinessGenerator.cs` adds businesses to each campus with generated households, after their small groups are planned, so no group changes. A campus gets enough to reach its minimum (6 on the main campus, counting the Cast's, and 2 elsewhere) or the profile's share of giving units, whichever is more. Each takes the next pool address as its work address, an adult from its campus as a contact (sometimes a married couple), and a monthly, bi-weekly, or irregular giving pattern. `loading/RelationshipsLoader.cs` writes every business contact, Cast and generated, the way Rock's `AddContactToBusiness` does: the contact in the business's Known Relationships group, and the business in the contact's.

Every random choice comes from a stream named by the seed and the household, so a rerun with the same seed and anchor gives the same data (see [What makes a load repeatable](#what-makes-a-load-repeatable)).

## How small groups load

Every small group is a plan (`model/SmallGroupPlan.cs`): its leader and members, schedule, meeting place, topic, child care, test case, and life from start to end. One planner (`generation/SmallGroupPlanner.cs`) makes plans from households and the rules in `../configuration/small-groups.json`. The design is in [Small groups](../docs/small-groups.md).

- **The Cast.** The builder's `--plan-cast-groups` plans the Cast's groups and writes them to `../cast/groups`, using this library's planner. Legacy groups keep their keys and Guids, and each new leader is marked Leading in the household files. A second run changes nothing. Its dates count back from a fixed planning year and every change is at least a year back, so the files never depend on the day you run it.
- **Generated households.** During a load, each campus's generated households get plans from the same planner (`generation/GeneratedSmallGroups.cs`). Households that still attend or give join at the profile's rate. Every campus except the main one gets its own test groups; the Cast covers the main campus. A campus too small to fill its groups takes leaders from the fallback statuses and adds the households that came closest to joining (decision 55).

The configuration step sets up small groups in Rock (`loading/SmallGroupConfigurationLoader.cs`): the soft capacity rule, the Small Group purpose, the Host role, three group attributes, the ten topics, the inactive reasons, and a parent group per campus. The small groups step (`loading/SmallGroupLoader.cs`) bulk-copies schedules, groups, meeting locations at the host's home, members, and attribute values.

Bulk copy skips Rock's save hooks, so the loader writes group history itself, the way Rock does: one row per state for each group, member, and meeting location, with the last row current. It also records weekly attendance on the church year. Groups meet in the fall and spring, stop after December 14, and meet in summer only when they say so. Holiday weeks are marked "did not meet". Spotty leaders record only some weeks. Nobody has attendance before they joined, after they left, or while their request is pending.

## How serving loads

Serving teams are set up in the configuration step from `../configuration/serving.json` (`loading/ServingConfigurationLoader.cs`): a "{campus} Serving Teams" parent per campus, every team at every campus, each team's rooms with its campus's service times and staffing, and the background check requirement. The same step sets up Rock's Involvement connection type and the eight serving opportunities from `../configuration/connections.json` (`loading/ConnectionConfigurationLoader.cs`).

`generation/ServingPlanner.cs` plans the volunteers before people load, so a generated team's new leader loads as Leading.

- **The Cast.** The Phoenix rosters are hand-picked in `../cast/groups/serving-*.json`, one file per team. Each file's Guid is the Phoenix team's Guid.
- **Generated households.** Engaged and Leading adults in households that still attend serve at the profile's `pctServe` share of active adults. Each team gets a leader and at least two people, drawing on Connected, then Known, adults when a campus is small (decision 76). Couples often serve together.

Every volunteer gets a rotation (one of Rock's four schedule templates), a join date, and usually a preferred service and room, seeded by the person. The serving step (`loading/ServingLoader.cs`) bulk-copies the members with their rotations and member history, the preferences as `GroupMemberAssignment` rows, a few blackout dates, and every request to serve as an `Attendance` row on the team's occurrence for that room, service, and Sunday. Serving history covers the whole history window, like Sunday attendance. Past requests follow the family: a volunteer served on a Sunday their family came. Otherwise they were often not scheduled at all, or they declined with a reason, never answered, or confirmed and did not come. Every time they served, they checked in at the kiosk a few minutes before the service, through Rock's Volunteer Check-in template. The next six weeks mix confirmed, pending, and declined. Every confirmation and reminder is marked sent. Kids and student volunteers get a passing background check and the requirement results Rock's job would have saved. A team too small for its configured staffing asks for about the people it usually has.

## How connection requests load

The connection requests step (`loading/ConnectionRequestLoader.cs`) follows serving, because requests end in the memberships serving just loaded. The rules are in `../configuration/connections.json` under `requests`, and the measured targets come from the profile's `connections` values. The design is decision 75.

- **Connected.** Every volunteer who joined in the last two years gets the request that put them on their team, ending the day they joined. Time to connect is log-normal around the profile's median and 90th percentile.
- **Pending.** A few people per campus connected in the last few weeks. Each gets a Pending membership on their team, which waits for the team leader to make it Active.
- **Active and inactive.** People who are not serving ask to serve in the profile's state mix around the connected requests, more often in the busy months of its month pattern. Each campus also has one future follow-up and one request transferred from another team.
- **The Cast.** Requests in the household files (`connectionRequests`) tell the Cast's stories and count toward their campus's mix.

Each request gets the activity history a connector would leave: assigned, calls and messages, meeting the team leader, shadowing, and connected. A Cast request can record a background check result on the person. Bulk copy skips Rock's save hook, so the loader sets the due date (7 days), due soon date (5 days), connected date, and on-time flag the way the hook does. It also writes the person history the hook writes (added, status changed, connected, and state changed), so each request shows on the person's History tab. Each team leader becomes the default connector for their team's opportunity at their campus and a member of Rock's Global Connector Group.

## Streaks and achievements

`loading/AchievementsLoader.cs` creates the Kids Check-in streak type and the seven achievement types in `../configuration/achievements.json` (decisions 57 to 60). Then it writes each child's streak and achievement attempts the way Rock would have computed them.

- **The streak type** is weekly and counts every check-in under the Weekly Service Check-in template. It starts on the first Sunday of the history. Its occurrence map has a bit for each Sunday with any kids check-in. Attendance is on, as Rock sets it for a new streak type, so check-ins after install keep counting.
- **Streaks.** Each child who checked in gets one streak on their primary alias. It starts on their first check-in week, and its map has a bit for each Sunday they checked in. Maps are as long as the occurrence map, as Rock's rebuild makes them. The current streak, longest streak, and engagement count are as of the anchor, because Rock's cleanup job refreshes them every day.
- **Attempts.** Rock processes achievements each time a check-in saves a child's streak. It keeps successful attempts and rebuilds the rest, so a child's attempts end up as one run on their last check-in Sunday would leave them. Types run in prerequisite order, and a type whose prerequisite has no success gets no attempts. Nothing processes a child again after they stop coming, so an attempt still open then stays open. Failed attempts stay too, as they do in Rock.
- **Settings.** Each achievement type's streak type and goal are attribute values on the type, as Rock's Achievement Type Detail block saves them. Its component settings JSON also names the streak type, and `TargetCount` holds the goal, which check-in uses to draw progress.
- **Check-in.** The template's Achievement Types setting lists all seven types, so next-gen check-in shows each child's progress and badges.

The math lives in Rock's C#, so `loading/RockStreaks.cs` is a line-for-line port: the bit maps, week alignment, the streak counts, and the consecutive and accumulative components. Its header lists the Rock files it mirrors. The step runs right after attendance. It reads the check-ins the attendance step planned, held in memory, because Attendance's indexes are not rebuilt until after small groups. No later step reads what it writes.

Speed: Full 7 with a 17,000-household crowd has 2,893 kids and about 183,000 attempts, and the step takes about 1.3 seconds. Core 1 has 49 kids and 5,009 attempts. Most attempts are failed tries at Back-to-Back, Four-Peat, and Unstoppable, because each broken streak closes one.

## Pledges

The building campaign (see [Pledges](../docs/pledges.md)) runs in three places:

1. **Configuration.** `loading/CampaignLoader.cs` gives the campaign's fund and campus accounts the campaign dates, and points Rock's two pledge pages and default contribution statement at the fund.
2. **The plan, before giving.** `loading/PledgeLoader.cs` turns each household's `campaign` entries into a pledge and a `CampaignGiving` for the giving pattern that pays it: a line factor, a stop date for a stopped pledge, and extra gifts. Then it draws generated pledgers from the Crowd and businesses until pledges reach the profile's share of giving units (decision 114), and plans theirs the same way. It writes the fund's goal into the fund's description now, in every load, because the fund is church setup (decision 126).
3. **Giving, then pledges.** `GivingLoader` plans each pattern's gifts as before, then adds the campaign. The line is the pledge's share for the time since it started, spread over the usual gifts the pattern really gave, times the line factor. Extra gifts ride on the next usual hand-given gift within a month, or are gifts of their own. The campaign uses no random numbers, so every other gift is the same as without it. After giving writes its gifts, `PledgeLoader` writes the pledges. Both writes happen only when the `Giving` part loads (decision 126).

## Giving analytics

The loader fills Rock's giving analytics as if Rock's Giving Automation job had run every night at 10pm, its default schedule. The job's last run is the night before the anchor. Rock's giving automation settings record that run, so Rock's next real run picks up only newer gifts.

The giving step also sets each campus's tithe metric, as Rock Cleanup does every night: the census median family income for each giving family's postal code (from the post-install job's `AnalyticsSourcePostalCode`), averaged per campus, times 10%. Only tax-deductible gifts in the last 365 days count. The query is Rock's, run as one update (`GivingLoader.UpdateCampusTitheMetric`).

- **Giving attributes** (`loading/GivingAnalyticsLoader.cs`), for every person in a giving unit: first and last gift, percent scheduled, preferred source and currency, gift frequency (mean, standard deviation, and label), next expected gift, median gift, its spread (IQR), and the last classification date. Rock reclassifies a unit only on a night after a new gift. So each unit's values are as of the night after its last gift, and a lapsed giver keeps the values from back then.
- **Giving bin and percentile** come from Rock's own stored procedure (`spGivingAutomation_UpdateGivingBinsAndPercentiles`), run as of the job's last run.
- **Journey stages** (Non-Giver, New, Consistent, Occasional, Lapsed, Former), with the previous stage and the change date. The loader replays Rock's weekly Tuesday runs for a year. Then it runs Rock's own procedure (`spGivingAutomation_UpdateGivingJourneyStages`) for the last Tuesday and checks that Rock made exactly the predicted changes. The load prints the result.
- **Giving alerts** (`loading/GivingAlertsLoader.cs`). The three alert types in `../configuration/giving-alert-types.json` are Large Gift ($5,000 or more), Smaller Than Usual, and Late Gift. They are created as of 12 months ago, and the loader replays Rock's nightly run since then. Each night, each new gift is checked against every alert type, using the giver's last 12 months as of that night. Then every giver who gave in the last year is checked for a gift that has not come. A warm-up month before the 12 months absorbs the burst of late-gift alerts a church sees the day it sets alerts up.

The math for attributes and alerts lives in Rock's C#, so `loading/RockGivingAutomation.cs` is a line-for-line port. It keeps Rock's types, rounding, and value formats. Its header lists the Rock files it mirrors. Values are written the way Rock writes them: attribute values as `Value` text only (Rock fills the persisted values later), and alert numbers cut to the columns' decimal places.

Speed: with Full 7 and the crowd, the attributes take about 6 seconds and the alerts about 2. Core 1 takes under 1 second for each. Full 7 raises about 1,500 alerts a year: about 300 large gifts, 300 smaller than usual, and 900 late gifts (about one per giver who stops). Core 1 raises about 20.

## Prayer requests

`generation/PrayerPlanner.cs` plans every request and what the prayer team did with it, and `loading/PrayerLoader.cs` writes it (decision 67). Every number comes from the profile's `prayer` section.

1. **Who asks.** Adults who attended, gave, or were in a small group in the last year are active. Volume is 500 requests a year per 1,000 of them, about 10 a week. Anyone on an active record can ask, but adults who are not active are about a third as likely to. Each age band and gender gets its quota from the profile's mix of people who ask, and most people ask once. About 6% of requests come from guests with no person record, who leave only a name and email.
2. **When and what.** Days follow the profile's month and weekday patterns, and times its hour bands. Each request gets a category by its weight in `configuration/prayer.json` and text from that category's pool in `content/prayer`: one request, some details, and sometimes a closing.
3. **Approval.** The Cast's Prayer Administrator approves most requests within hours. Rock's approval stamps who and when, and sets the request to expire 14 days later, stored as a date. Requests whose approval would fall after install day still wait in the queue.
4. **Prayer.** The prayer team is the Cast's Prayer Team plus about 3% of active adults, a few of them far busier than the rest. They pray through a request while it is approved and not expired, through the end of its expiration day, as Rock's prayer session shows it. Each prayer is a "Prayed" interaction in Rock's Prayer Events channel, with one interaction component per request, and `PrayerCount` is the number of them. Nobody prays for their own request.
5. **Flags, comments, and answers.** About 2% are flagged partway through, which unapproves them, as Rock's flag limit does. Some requests that allow comments get Prayer Comment notes from people who prayed, and a few requesters reply. Some requests whose prayer window has ended get an answer, and a few are closed.
6. **Daily counts.** Rock Cleanup keeps a daily count of interactions for channels with daily counts on, and Prayer Events is one. The loader runs Rock's own backfill and saves its last processed date, so Rock's first run finds nothing to do (`validation/rock-lock.json` tracks the Rock code).
7. **The Cast's own requests.** `cast/prayer-requests.json` holds hand-written requests tied to Cast stories, loaded after the generated ones. Their timing comes from their state instead of a random draw: approved two hours after entry, expiring 14 days later, and prayed for at even steps while in the prayer session. The Cast's Prayer Team prays first, taking turns, and prays again on later days when the team is small. Ten are always entered 1 to 5 days before install, so even Core 1 and Core 3 show a full prayer list and prayer session.

Speed: Full 7 with the crowd has about 11,100 requests, 67,000 prayers, and 2,500 comments, and the step takes about 3 seconds. Core 1 has about 170 requests.

## Speed

| Load | People | Attendance rows (Sunday plus group) | Gifts | Time |
|---|---|---|---|---|
| Core 1 (83 Cast households) | 181 | 12,331 plus 6,308 | 2,836 | about 7 seconds (3.5 of them check images) |
| Core 1 plus a 2,000-household crowd | 3,155 | 61,424 plus 39,961 | 12,230 | about 11 seconds |
| Full 7 plus a 17,000-household crowd | 25,316 | 377,612 plus 324,153 | 91,634 | about 29 seconds (about 44 with check images) |
| Full 7 with its Crowd (16,800 households), every part | 25,248 | 385,076 plus 322,543 | 91,250 | about 55 seconds with check images |

The last row was measured on 2026-10-23, the median of three runs. Its time goes to check images about 14 seconds, serving 7, people 5, giving 4.5, attendance 4, giving analytics 4, and every other step under 3. The spike crowd rows were measured on 2026-10-16, before serving, prayer, notes, and steps were built, best of three, on a 16-core development machine with other work running. Then, a Full 7 load with check images spent its time like this: check images about 11 seconds (drawing on every core, then storing), small groups 5, giving 4, giving analytics 4, people 3.5, attendance 3.5, and giving alerts 2. Drawing checks is the floor: it is image rendering in the ImageSharp version Rock ships, already spread across every core.

What keeps large loads fast:

1. Bulk copies take a table lock, which is safe on a fresh database inside one transaction.
2. A copy of 10,000 rows or more turns the table's nonclustered indexes off and rebuilds just those afterward. That cut FinancialTransaction (21 nonclustered indexes) from 21 seconds to under 3.
3. Sunday attendance and small group attendance both copy into Attendance, and its indexes are rebuilt once, after both.
4. Giving analytics reads the gifts the giving step planned, instead of reading them back from the database.

Trying to draw checks in the background, alongside giving analytics and alerts, made the load slower: drawing takes every core, and SQL Server runs on the same machine.

## Validation

See [validation/](../validation/README.md). The first Full 7 run passed 55% of profile values. After one tuning pass it passed 74%, and 76% after the Cast grew to 83 households. The giving tuning on 2026-10-16 took it to 90%. Small groups added four graded values. With both, 260 of 291 pass (89%); giving passes 191 of 196, and small groups 3 of 3. Businesses added two graded values on 2026-10-19 (their share of giving units and of dollars): 261 of 293 pass (89%). Prayer added 87 graded values on 2026-10-20, and all pass: 347 of 380 pass (91%). Serving and connections added graded values on 2026-10-21: 373 of 412 pass (91%). The serving values pass; two holiday multipliers for adults (fall kickoff and Labor Day) and four months of the connection month pattern are a few points out. See [Serving](../docs/serving.md#how-the-numbers-compare) for the measures skipped on purpose.

## Steps

| Step | Status |
|---|---|
| Configuration: connection statuses, service time slots, campuses, campus schedules, funds with campus child accounts, check-in rooms and Sunday service times | Done |
| People: Cast families, people, aliases, family memberships (including shared children), home addresses with map points, and phones. Ages, grades, giving units, and statuses come from the anchor date. | Done |
| Profile photos: every Cast person's delivered photo, and pool faces for generated households in Core 3, stored as Rock person images | Done |
| Sunday attendance and kids check-in, generated on the real calendar | Done |
| Small groups: the Cast's planned groups and generated groups per campus, with members, meeting places, attributes, history, and weekly attendance | Done |
| Giving history, generated from personas and the calibration profile | Done |
| Post-load fix-ups (primary family, giving leader, age brackets) | Done, during the load |
| Spike crowd for speed tests and validation | Done |
| Scanned check images, matching each gift, stored as Rock transaction images | Done |
| Validation with the calibration scripts | Done (91% of values pass) |
| Giving analytics: giving attributes, bins and percentiles, journey stages with history, and a year of giving alerts, the way Rock computes them | Done |
| Kids check-in streaks and achievements, the way Rock computes them, shown at check-in | Done |
| Serving: teams at every campus with volunteers, rotations, preferences, staffing, background checks, volunteer check-in, and scheduling history over the whole history window plus 6 weeks ahead; the Involvement connection type and eight serving opportunities | Done |
| Connection requests to serve: connected, Pending, active, inactive, future follow-up, and transferred, with activities and default connectors, in the measured mix | Done |
| Prayer requests with an active prayer team: approvals, prayers, flags, comments, answers, and Rock Cleanup's daily counts | Done |
| Notes on people: six note types with View security, hand-written Cast notes, and generated notes with replies, alerts, private and pinned notes, and mentions | Done |
| Adult steps: Rock's Discipleship Path built out to seven step types, with steps from memberships, the giving journey, the Baptism Date, and class schedules, and program completions the way Rock's job records them. See [How steps load](#how-steps-load). | Done |
| Student steps: the Student Path, a new program with four step types from student check-ins (camps, trips, the Follow Jesus decision, and student leaders), student baptisms, and its program completions | Done |
| Security roles: Cast people join the roles Rock ships, mapped in `configuration/security-roles.json`, and campus pastors join the staff and pastoral roles | Done |
| People, the rest: person attributes, logins, relationships, follows, and the Cast's general groups | Later (Phase 1) |

## Notes

`loading/NotesLoader.cs` sets up the six note types in `../configuration/notes.json`. Rock's own Personal, Pastoral, and Phone types are updated in place, and Follow-up, Counseling, and Safety Alert are added. Each type gets View rules (allow its roles, then deny all users), and the new types get matching Edit rules.

Then it writes every note in one bulk copy with explicit Ids, so replies point at their parents:

- **Cast notes** come from `../cast/notes`, exactly as written, with their own Guids.
- **Generated notes** go only on generated households, so Cast notes stay the same for tests. Each person's chance of having notes, and how many, come from `personNotes.design` in the profile. The type comes from the type's weight for the person's audience (guest, attender, or child). The day of the week and the hour come from the profile's measured mixes. The text is an opener and up to four details from `../content/notes`, aiming for a length drawn from the profile's percentiles.
- **Authors.** The main campus's notes come from the Cast staff, by each type's author weights. Other campuses' notes come from the campus pastor, except phone notes and safety alerts, which the church office writes. No one writes a note about themselves or their own family.
- **Replies, alerts, private, pinned, and mentions** follow the design rates. Counseling is confidential: its notes mention no one, and only its own authors reply.

Notes are stored the way Rock's note editor saves them: approved, with `EditedDateTime` set, a private note's caption set to Rock's "You - Personal Note", and text as HTML. A mention is Rock's `<span class="mention" data-identifier="...">` with the staff member's alias IdKey, which is keyed by the server's DataEncryptionKey. Without a web.config, mentions are plain names. `NotificationsSent` and `ApprovalsSent` are set, so Rock's Send Note Notifications job never emails staff about old sample notes.

Speed: Full 7 writes about 61,000 notes and replies in about one second.

## How steps load

`loading/StepsLoader.cs` sets up the step programs in `../configuration/steps.json`, Rock's Discipleship Path and the Student Path, and `generation/StepPlanner.cs` plans every step. The design is in [Steps](../docs/steps.md).

1. **The programs.** Rock's program and its Baptism, Starting Point Class, Serve, and Small Group types are updated in place, by Guid: order, engagement type, impact weight, organizational objective, icon, and color. Foundations, Give, and Lead are added with a description and a call to action. The completion flow becomes Linear (Preferred), and Rock's prerequisite (Serve after Starting Point Class) stays. The Student Path is added in Rock's Discipleship category with its own In Progress and Complete statuses, named and colored like Rock's, and its four types.
2. **The facts.** Small group and serving team memberships are read back from the database, so each step has its membership's dates exactly. A membership ends the day the person left, or the day the group ended while they were in it. Gifts come from the list the giving analytics replayed, so Give steps agree with the journey stage Rock shows. Active adults use the prayer step's rule. Student check-ins come from the attendance plan, the check-ins in the student service's check-in groups that the attendance step writes, so the Student Path follows whatever attendance is planned and is the same whether or not the `Attendance` part loads (decision 125).
3. **The plan.** Each step type's `source` picks one planner method: `membership` (one step per membership, In Progress while a volunteer is Pending), `giving-journey` (Rock's stage rule run every Tuesday night over the history window), `event` (Student Camp and Mission Trip on their church-wide dates), `decision` (Follow Jesus, often at camp), `school-years` (Student Leader, from August to graduation), `baptism` (the Cast's dates, generated adults at the designed rates, and students after a decision), and `class` (Starting Point Class and Foundations on their class schedules). A decision is dated from the camps, a student baptism from the decision, and classes from the other steps, so each is planned after them. A student step goes to a share of the students who could take it, counted for the Cast and the generated households on their own. Program completions follow Rock's Update Step Program Completions job, for each program.
4. **The rows.** Program completions, then steps with explicit Ids that point at their program's completion, then attribute values: the Baptism Date and Baptized Here on the person, and Baptized Here on each Baptism step. Steps carry the date keys Rock's model fills, the person's primary alias and campus, and the group as the related entity.

A Cast story can set steps in its household file (`steps`): a class by how many classes ago it was, a camp or trip by its season and how many years back it was, or `taken` to let the usual rules date the step. The planner fails the load when a story cannot hold against the person's other records, such as a Starting Point Class after their first Serve step, or a camp in a summer the student was not in the student ministry.

The load prints each program's steps by type and completions, the share of students who took each student step of those who could, and how many baptisms are students'.

Speed: Core 1 plans and writes about 380 steps in about a quarter of a second.

## Communications

`loading/CommunicationsLoader.cs` sets up how the church communicates from `../configuration/communications.json`, and `generation/CommunicationListPlanner.cs` plans who is on each list. The design is in [Communications](../docs/communications.md).

1. **Phone numbers.** Each system phone number, active and able to text, with forwarding off. A personal number is assigned to its staff member's primary alias. On the datasets the file lists, each campus pastor (`loading/CampusPastors.cs`, the same person the notes pick) gets one too. The Crowd generator never gives a generated person one of these numbers.
2. **The template.** The logo PNG becomes a Rock file, and the template's logo image points at it through `GetImage.ashx`, the way Rock's template editor saves an uploaded logo. The template carries the church's from name and address and its Lava fields.
3. **The lists.** One Communication List group per list, and one per campus for the newsletters, with Rock's Category (public lists only) and Public Name attributes. Members are active recipients, with their preference on the list and the day they joined. Volunteers and Small Group Leaders come from the `GroupMember` rows serving and small groups wrote. Each person draws from their own seeded stream for each list.
4. **Bulk email.** `generation/CommunicationCalendar.cs` dates every occasion, `generation/MessageRenderer.cs` fills each message's tokens and puts it in the template, and `generation/EmailPlanner.cs` schedules every send and walks them in date order, because a bounce or an unsubscribe changes who the later sends reach. `loading/EmailWriter.cs` writes each send as it is planned, in chunks of about 300,000 rows, with the recipient, session, interaction, and History indexes rebuilt once at the end. `loading/BulkEmailLoader.cs` then updates the people whose email bounced or who chose "No mass emails", with their History. Row Guids come from a 128-bit hash of each recipient's seed (`SeededRandom.StableGuid`), because 32-bit seeds repeat at a million rows.

## Check images

Each giver has a wallet (`generation/Wallet.cs`): one card, one bank account, and a checkbook at a fictional bank whose routing number starts with 39 and fails the routing checksum. Check numbers count up per writer in date order, so the transaction code, the payment details' last four digits, and the check image all agree.

`loading/CheckImageLoader.cs` draws the front and back of each recent check gift with `generation/CheckImageRenderer.cs` (ImageSharp, the version Rock ships) and stores them as Rock transaction images. The front shows the writer's name (both spouses for a married couple), the family's address, the check number, the date, the church as payee, the amount in numbers and words, a memo from the funds, a signature, and the bank line. The back shows the church's endorsement stamp and the bank's processing line.

Each of the five fictional banks has its own logo and paper tint. Personal checks are handwritten (three handwriting fonts and two signature fonts, in blue or black ink, one style per writer). Business checks are typed by accounting software and signed by hand; businesses that pay by check mail theirs, so they land in "Mailed Checks" batches. A giving pattern can name its payment method (`currency`) and who signs (`signer`). Every font, including Open Sans, comes from `../content/fonts`.

Check scanner fields: every check writer gets a `FinancialPersonBankAccount` row, and every check gift gets `CheckMicrHash` and a successful MICR status. Both use Rock's keyed hash with the PasswordKey from a Rock checkout's web.config (see `--rock`), which development sites run with, so scanning a sample check in that Rock site finds the person who wrote it. Each check gift also gets `CheckMicrEncrypted` (the bank line) and `CheckMicrParts` (routing, account, and check number), encrypted the way Rock's `Encryption.EncryptString` does it, with the web.config's DataEncryptionKey. `loader/database/RockKeys.cs` holds the copy; Rock's own Rock.dll decrypts its output. Real installs generate their own keys, so the loader inside Rock must use the server's keys.

Open batch for transaction matching: one recent "Sunday Offering Checks" batch at the main campus stays Open, as if it was scanned and nobody has matched it yet (decision 53). It is the Sunday with the most checks in the last six weeks. Its checks keep their amounts, check numbers, images, and bank line fields, but have no giver. The checks of Cast households with giving expectations stay matched, because one missing gift can change what Rock shows for them. Rock's Transaction Matching block decrypts `CheckMicrParts`, hashes the routing and account numbers, and finds the writer's saved bank account, so it suggests the right person for each check. Rock's giving analytics leave these gifts out until someone matches them, as Rock's job would. Without `--rock` or `--web-config`, the checks are still unmatched, but matching has nothing to suggest.

Speed: Core 1 draws its 390 or so checks in about 3.5 seconds. Full 7 draws about 3,500 checks (12 months) in about 11 seconds and stores them in about 2 seconds (269 MB). Images are color JPEGs (each bank has its own paper tint) and average 77 KB per check.

## Spike shortcuts

These are deliberate, and they go away in the real loader:

1. Configuration is written with SQL. The real loader saves configuration through Rock's services, so Rock's save hooks and caches run.
2. The spike crowd uses placeholder names. The real Crowd uses the Content name lists. Addresses already come from the address pool (`../addresses/pool.json`); a placeholder street is used only if a campus runs out.
3. The loader keeps its own copy of Rock's encryption in `RockKeys.cs`. Rock passes its own PasswordKey and DataEncryptionKey through `DatasetLoadOptions`, so the fields match the server, but inside Rock the loader could call `Encryption.EncryptString` itself.
4. The real loader runs inside Rock, so it can call Rock's own math (`GivingAutomationHelper`) instead of the port in `RockGivingAutomation.cs`. It still needs the replay, because Rock's job only computes values as of today. The stored procedures are already Rock's own. The same goes for `RockStreaks.cs`: Rock's own streak and achievement code can run as of each child's last check-in, as the achievements parity test does.
