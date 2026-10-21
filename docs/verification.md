# Verification

Every check that proves the sample data is right, in one place. For each check, this page says what it does, why it exists, and how to run it.

## Why we verify

The loader writes straight into Rock's tables with bulk copy. That is what makes an install take seconds instead of minutes. It also means the data never passes through Rock's business logic: no save hooks, no validation, no defaults. If the loader gets something wrong, Rock never says so.

So the proof has to come from somewhere else. We get it from three places:

1. **Rock itself.** Its migrations, its schema, its stored procedures, and its own Rock Cleanup job tell us what correct looks like.
2. **Rules written as checks.** A rule that a script can check is a check, not a sentence in a doc. A reminder gets skipped; a failing check does not.
3. **Receipts.** Each check leaves a record that someone can read later. A package is published only with a receipt that says every check passed (decision 66).

All of this runs when the data is made, never when a church installs it. The one exception is the loader's own short safety check at install (see [When a church installs](#when-a-church-installs)).

## The checks at a glance

| Check | When it runs | What it proves |
|---|---|---|
| [Cast validator](#cast-validator) | Before every pull request | The Cast files are complete and consistent. |
| [Content standards check](#content-standards-check) | Before every pull request | The data follows the content standards a script can check. |
| [Package self-check](#package-self-check) | Every time the builder writes a package | A package that would fail to load is never written. |
| [Trusted base](#trusted-base) | Verify tool, before publishing | Every load starts from a clean copy of exactly this Rock release. |
| [Contract schema](#contract-schema) | Verify tool | Everything the loader depends on still exists in this Rock, unchanged. |
| [Contract current](#contract-current) | Verify tool | The contract still describes what the loader really writes. |
| [Load](#load) | Verify tool | The package loads into this Rock. |
| [Cast expectations](#cast-expectations) | Verify tool, and every load | The Cast givers that docs and tests rely on still show what their stories say. |
| [Invariants](#invariants) | Verify tool | Rules Rock relies on hold in the loaded data. |
| [Rock Cleanup referee](#rock-cleanup-referee) | Verify tool | Rock's own repair job finds nothing to fix. |
| [Unchanged during run](#unchanged-during-run) | Verify tool | The receipt describes the exact bytes and code that were checked. |
| [Publishing gate](#publishing-gate) | Before any upload | Only packages with a passing receipt are published. |
| [Install-time checks](#when-a-church-installs) | Every install | The package is intact and meant for this database. |
| [Realism grading](#realism-grading) | When generation changes | The data looks like a real church. |
| [Rock lock check](#rock-lock-check) | Any time, and before a release | The Rock code the loader copies has not changed. |
| [Giving parity test](#giving-parity-test) | Before each release | The loader's giving math matches Rock's own job. |

## Before a pull request

These need only Node.js, so every contributor runs them.

### Cast validator

**What it does.** `node cast/validate-cast.js` reads every Cast household and group file. It checks required fields, Guid format and uniqueness, every cross-reference (members, groups, hosts, relationships, follows, notes), connection statuses, giving expectations, small group rules, and email domains. It also checks every delivered photo: each is a 400 by 400 JPEG with an original, named for its person, and matching that person's gender.

**Why it's there.** Tests and documentation rely on Cast people by Guid. A broken reference or a reused Guid would break them quietly, long after the change that caused it.

### Content standards check

**What it does.** `node validation/check-standards.js` checks the [content standards](content-standards.md) that span folders:

- **Rule 2.** Every Cast phone is in the reserved range (555-0100 to 555-0199 in 602, 480, or 623). A number is shared only within a household, as an office line, or between duplicate records.
- **Rule 3.** Every email anywhere in the data uses `example.org` or `rocksolidchurchdemo.com`.
- **Rule 5.** The raw data folders hold nothing in git but their README and `.gitignore`.
- **Rule 6.** Calibration sources are named `source-N`, and every source's description is exactly "Calibration source". When it finds a description, it never repeats it, so the check itself cannot leak one.

**Why it's there.** A real phone number, email, or church detail in sample data could reach a real person, because the data ships with Rock and appears in demos and screenshots. [Content standards](content-standards.md#how-each-rule-is-checked) lists which script checks each rule and which rules still need a reviewer.

## When packages are built

### Package self-check

**What it does.** Before the [builder](../builder/README.md) writes a package, it opens the package the way the loader does and reads every file a load reads. It also records a SHA-256 for every file in `package.json`, and packs the same commit into byte-identical packages.

**Why it's there.** A package that cannot load should fail on the developer's machine, not on a church's server. Identical bytes mean a receipt can name a package by its hash.

## Before publishing: the verify tool

The [verify tool](../verify/README.md) proves that each package works on one exact Rock and writes a receipt next to it. Run it from `verify/` with the same built Rock checkout the packages were built from:

```
dotnet run -- --rock C:\Development\Rock
```

It runs the checks below for every dataset. A full run takes about 5 minutes.

### Trusted base

**What it does.** The tool builds an empty Rock database from nothing. First EF's own migration tool applies every Rock migration. Then `verify/rock-runner` runs Rock's own start-up steps without a web server: the hotfix (plugin) migrations, the `RockGetDate` function, the attribute value views, and the message bus. Then it runs Rock's one-time post-install job, which a new install runs on its first night: it fills the date table, the identity verification codes, and the postal codes. The tool stamps the database with its Rock version. Before each run it certifies the base: exactly the checkout's EF migrations and hotfix migrations, the post-install job's tables filled, the right version stamp, no sample data, and an empty Exception Log. If the base fails, the tool builds a new one (about 9 minutes). Every load runs on a copy, so the base itself is never touched.

**Why it's there.** A finding is only useful if it is about our data, not a stale or dirty database. EF alone is not enough: on Rock 21.0.1 it skips 189 hotfix migrations that every real install has. Patch releases often share their EF migrations and differ only in hotfixes, so both are checked. A failed hotfix logs an exception, so the empty Exception Log proves they all applied.

### Contract schema

**What it does.** `validation/rock-contract.json` lists every Rock table and column the loader writes, the full column list of those tables when it was recorded, the tables it deletes from, the Rock-provided Guids it depends on, and the Rock procedures it runs. This check compares the contract with the trusted base. It fails when a column the loader writes is gone or has a new type, a Guid or procedure is gone, or Rock added a column to a table the loader writes. It also fails if the loader writes `Person.RaceValueId` or `Person.EthnicityValueId` (content standard 8).

**Why it's there.** Because the loader skips Rock's save hooks, a new Rock column can need a value that only Rock's code would have set, and nothing would complain. This check turns that into a failure that names the column, so someone decides whether the loader must fill it.

### Contract current

**What it does.** During each verification load, the loader's database class records every bulk copy and every SQL statement it runs (`loader/database/WriteRecorder.cs`). This check compares the recording with the contract. It fails when the loader writes, deletes, or depends on something the contract does not list. When every dataset is loaded, it also fails when the contract lists something no load used. A write statement the recorder cannot read fails too.

**Why it's there.** The contract is only trustworthy if it cannot drift from the code. Nobody edits it by hand. After a reviewed change, `--record-contract` saves a new one, and its git diff shows exactly what changed.

### Load

**What it does.** Loads the package into a fresh copy of the trusted base, with check images on and Rock's real keys from the checkout's `web.config`, the way a release build does.

**Why it's there.** It proves the package installs on this exact Rock. Every check after it reads the loaded data.

### Cast expectations

**What it does.** Some Cast giving patterns say what Rock should show for that giver: a frequency, a journey stage, or alerts (`expect` in the household file). This check reads Rock's values in the loaded database and compares them. The loader also checks them on every load. See [Validation](../validation/README.md#cast-expectations).

**Why it's there.** Docs, demos, and tests say things like "the Deckers give monthly." A change to generation or to Rock can quietly break that story.

### Invariants

**What it does.** Runs every rule in `validation/invariants.sql` against the loaded dataset. Each rule returns how many rows break it, and must return zero. Rules that a foreign key or unique index already enforces are left out, because the bulk copy checks those.

**Why it's there.** These are the rules Rock's save hooks and screens rely on, which the loader must now keep by itself.

| Rule | Why |
|---|---|
| `dataset-has-people` | Every other rule looks only at this dataset's rows, so a load that wrote nothing would pass them all. |
| `person-has-one-primary-alias` | Rock finds a person through their primary alias, and every audit column and gift points at it. |
| `person-is-in-a-family` | Profile pages and check-in break for a person with no family. |
| `primary-family-is-a-family-the-person-is-in` | Rock's save hook sets the primary family; the loader sets it instead. |
| `primary-campus-is-the-primary-family-campus` | Rock shows and filters people by their primary family's campus. |
| `giving-id-matches-giving-group` | Rock groups gifts by giving Id. A wrong one splits or merges giving units. |
| `giving-leader-is-in-the-giving-unit` | The giving leader receives the statement. |
| `birth-date-parts-agree` | Rock stores a birth date three ways, and reports read all three. |
| `phones-use-the-reserved-range` | Content standard 2, for every loaded phone, generated people too. |
| `emails-use-reserved-domains` | Content standard 3, for every loaded email. |
| `no-race-or-ethnicity` | Content standard 8. |
| `home-addresses-are-mapped` | Group Finder, campus distance, and maps need a point for every home. |
| `nothing-dated-in-the-future` | A future gift or check-in shows up in "upcoming" places. |
| `transaction-has-positive-details` | Rock totals a gift from its details. |
| `transaction-is-inside-its-batch` | A gift outside its batch's date range lands in the wrong batch report. |
| `member-role-belongs-to-the-group-type` | Rock allows only roles of the group's type, and the database does not enforce it. |
| `one-current-history-row` | Rock's history tables mark exactly one current row per group, member, and meeting place. |
| `photo-is-a-stored-person-image` | A photo must point at a stored Person Image, or the profile shows a broken image. |
| `only-this-dataset` | One database holds one dataset. Another's rows mean the database was not fresh. |

To add a rule, add a `-- check:` block with a `-- why:` line that returns how many rows break it.

### Rock Cleanup referee

**What it does.** Rock's nightly Rock Cleanup job repairs what Rock's save hooks would have set: aliases, ages, salutations, search keys, and more. After a load, the tool takes a SQL Server snapshot, runs Rock's real job through `verify/rock-runner` (with Rock's message bus started first, as in a running Rock, so the job's cache messages never log bus errors), and compares every table with the snapshot, column by column, as bytes. It runs the same job on a plain copy of the trusted base too. Both databases start from the same base, so base rows have the same Ids in both. A change Rock makes to exactly the same base rows on the plain copy is background. A change to any row the load added is a finding. A change accepted on purpose goes in `validation/cleanup-accepted.json` with a reason. Two of the job's tasks need a web server, so they are noted, not failed. Any other task error fails.

**Why it's there.** This is the most direct answer to "we skip Rock's business logic." Rock itself tells us what it would have done differently. On its first runs it found real loader gaps: primary aliases, known-relationship groups, search keys, sound-alike name entries, family salutations, and inactive dates. The [roadmap](roadmap.md) lists them.

### Unchanged during run

**What it does.** Reads each package's SHA-256 and this repository's commit before the run, and checks both again at the end.

**Why it's there.** A run takes minutes. If a package or a check changed partway, the receipt would describe something that was never verified.

### Receipt

Each run writes `{dataset}.verify.json` next to each package. A receipt records the package's SHA-256 and source commit, the Rock version and migration that were checked, the trusted base's certificate and schema hash, the anchor and seed, this repository's commit, and every check with its details. A package passes only when every check passes.

### Publishing gate

**What it does.** `dotnet run -- --check-receipts ../packages` (in `verify/`) refuses to publish when any package has no receipt, its receipt is for different bytes, any check failed, the receipt is for a different Rock than the package, or either the package or the checks came from uncommitted changes.

**Why it's there.** A receipt only matters if publishing depends on it. The publish workflow runs this gate before any upload (see the [roadmap](roadmap.md#publishing)).

## When a church installs

The loader runs three short checks before it writes anything, and stops the install if any fails:

1. The package's format is one this loader reads.
2. Every file matches its SHA-256 in `package.json`, nothing is missing, and nothing was added.
3. The database's newest migration is the one the package was built for (decision 52).

During the load it also checks the Cast expectations, and it runs Rock's own journey stage procedure for the last week to confirm Rock makes exactly the changes the loader predicted. See [Loader](../loader/README.md#what-the-loader-checks-before-loading).

**Why they're there.** These are the only checks that run on a church's server, so they are fast and protect only against a damaged package or the wrong Rock. Everything slower happened before the package was published.

## Realism and Rock's math

### Realism grading

**What it does.** Runs the same calibration scripts that measure real churches against a loaded database, then compares the results with the calibration profile, within the tolerances in `validation/tolerances.json`. Today 89% of measured values pass. See [Validation](../validation/README.md).

**Why it's there.** Correct data can still look fake. This proves the data looks like a real church, and catches a generator change that makes it look less so.

### Rock lock check

**What it does.** `node validation/check-rock-lock.js` fingerprints the Rock files the loader's giving code copies, reading them from Rock's public repository, and fails when one changed.

**Why it's there.** The loader ports Rock's giving automation math. If Rock changes it, the port goes stale without any error.

### Giving parity test

**What it does.** Before each release, Rock's real Giving Automation job runs on a loaded Full 7 database, and the loader CLI's `--giving-parity` compares what Rock wrote with what the loader computed. See [Validation](../validation/README.md#rock-version-guard).

**Why it's there.** The lock check catches a change in Rock's code. The parity test proves the port gives the same answers.

## When a check fails

The [verify tool's README](../verify/README.md#when-a-check-fails) says what to do for each check. In short: fix the loader or the data, not the check. Change a check only when the check itself is wrong, and say why in the commit.
