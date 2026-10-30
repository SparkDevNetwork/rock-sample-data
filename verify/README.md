# Verify

The verify tool proves that each dataset package works on the Rock release it was built on, and writes a receipt that says so. A package serves every dot release of that Rock's major release (decision 52), so each new dot release gets a verify run before a church uses it. It runs when packages are made, never when a church installs one (decision 66).

The loader bulk-copies around Rock's business logic. That is what makes it fast, but it also means Rock never gets a chance to say "this is wrong." So the proof has to come from somewhere else. The verify tool gets it from Rock itself: a database made only by Rock's own migrations and start-up steps, Rock's own column list, and Rock's own Rock Cleanup job.

This page is about running the tool. For every check in the repository, what it does, and why it is there, see [Verification](../docs/verification.md).

## Running it

Build the packages first (see the [builder](../builder/README.md)), from the same built Rock checkout. Then, from this folder:

```
dotnet run -- --rock C:\Development\Rock-21
```

Use a Rock checkout kept only for this, at the `ref` in [`release/rock-releases.json`](../release/rock-releases.json) for its major release, such as `C:\Development\Rock-21` and `C:\Development\Rock-20`, built with Visual Studio's MSBuild. Do not use the checkout you develop Rock in. Pulling develop there changes its migrations while `RockWeb\bin` still holds the old build, so the trusted base fails until you rebuild, and a run already going reads a mix of both.

It needs SQL Server on `localhost` with Windows sign-in, Node.js, and a built Rock checkout (`RockWeb\bin` must hold `Rock.dll`). It makes and drops its own databases, all named `RockSampleVerify-<run tag>-...`, and never touches any other database. The run tag is the worktree's folder name without "rock-sample-data-" (the main checkout is `main`), so runs from different worktrees can share a server. They share the trusted base, which a run locks while it uses it (see [Working in parallel](../docs/maintaining.md#shared-machine-resources)).

| Option | Meaning |
|---|---|
| `--rock <folder>` | A built Rock checkout: a release tag, or develop. The `ROCK_PATH` environment variable does the same. Required. |
| `--dataset` | One dataset (`core-1`, `core-3`, or `full-7`), or `all` (the default). |
| `--packages <folder>` | Where the packages are. Defaults to `../packages`. Receipts are written next to them. |
| `--server` | The SQL Server. Defaults to `localhost`. |
| `--base` | The trusted base's name. Defaults to `RockSampleVerify-base`. |
| `--run <tag>` | The run tag in this run's database names. Defaults to the worktree's name. |
| `--anchor`, `--seed` | The install day and seed for the verification loads. Default to today on the database server and `rock-sample-data`. Rock Cleanup is judged only when the anchor is today (see `rock-cleanup` below). |
| `--record-contract` | Records `validation/rock-contract.json` from these loads, instead of only checking it. Needs every dataset. |
| `--keep` | Keeps each loaded database (`RockSampleVerify-<run tag>-core-1` and so on) for a look afterward. |
| `--check-receipts [folder]` | Runs nothing else. The publishing gate: exits with 1 unless every package has a passing receipt. |

A full run takes about 8 minutes, about 3 of them for Full 7. Making a new trusted base adds about 9 minutes, once for each Rock release.

## What it checks

| Check | What it proves |
|---|---|
| `trusted-base` | The database every load starts from was made only by this Rock's migrations. |
| `content-standards` | The [content standards](../docs/content-standards.md) a script can check all hold. |
| `contract-schema` | Every column, Guid, and procedure the loader depends on exists in this Rock, and Rock added no column the loader has not reviewed. |
| `contract-current` | `validation/rock-contract.json` still describes what the loader writes. |
| `load` | The package loads into a copy of the trusted base. |
| `cast-expectations` | Every Cast giver whose story says what Rock should show still shows it ([validation](../validation/README.md#cast-expectations)). |
| `invariants` | Every rule in `validation/invariants.sql` holds in the loaded data. |
| `rock-cleanup` | Rock's own Rock Cleanup job finds nothing to fix in the sample data. |
| `sign-in` | Rock's own Database login signs in as `tdecker` and the load's newest confirmed login, and refuses a wrong password. |
| `data-views` | Rock's own engine runs every data view in the load, the church's and Rock's, without an error. It prints each church view's count and time. |
| `access` | Rock's own security check gives every answer in the security walkthrough (`access` in `configuration/reporting.json`): which Cast person may view or edit which category or data view. |
| `group-sync` | Rock's own group sync, run on every sync the load wrote, adds, removes, and refuses nobody. Judged only when today is the anchor. |

### The trusted base

The base is an empty database made from nothing in four steps:

1. EF's own migration tool (`ef6.exe`, from the Rock checkout's packages) applies every Rock migration, the same way a Rock developer runs Update-Database.
2. `rock-runner` runs Rock's own start-up steps that write to the database: the `RockGetDate` function, the plugin and hotfix migrations, and the attribute value views. Then it starts Rock's message bus, as Rock's startup does next. EF alone skips these. On Rock 21.0.1 that is 189 hotfix migrations, so a base made only by EF is not what a church has after Rock starts.
3. `rock-runner` starts Rock's Lava engine, then runs Rock's one-time update jobs (the "Rock Update Helper" jobs, `Rock.Jobs.Post...`), which a new install runs at 9pm on its first evening (decision 170). Each runs as its own stored job, so it reads its settings and deletes itself, oldest first. Among them are the jobs that swap WebForms blocks for Obsidian ones, such as Pledge Entry, so the base has the blocks a Rock that has run a night has. The swap jobs first register their new block types' settings; Rock's web start-up has already done that for WebForms (`.ascx`) block types, which only compile inside IIS, so the runner marks those verified first. On Rock 21.0.2 that is 87 jobs.
4. `rock-runner` runs Rock's one-time post-install job (`PostInstallDataMigrations`), which a new install runs at 2am on its first night. It fills `AnalyticsSourceDate`, the identity verification codes, and `AnalyticsSourcePostalCode` (from `RockWebApp_DataFormatted_Census_Data.xlsx`, so Rock's AppDomain uses the RockWeb folder as its base, as under IIS). Rock Cleanup reads every person's age from `AnalyticsSourceDate`, so without this step Rock Cleanup sets every age to null, which no church that has run one night sees.

Nothing else ever touches the base; every load runs on a copy.

Before each run, the tool checks the base: its `__MigrationHistory` must hold exactly the Rock checkout's migrations (read from each migration's designer file, since a file name is not always its Id), Rock's start-up steps, every one-time update job, and the post-install job must have run (with the postal codes filled), and it must have no sample data and an empty Exception Log. A plugin migration that fails logs an exception, so the empty log also proves every hotfix applied. If the base is missing or does not match the checkout, the tool makes a new one. The receipt records the base's schema hash, so two receipts with the same hash were checked against the same schema.

The tool writes its own config file for EF and for Rock, pointed at its own database. The checkout's `web.ConnectionStrings.config` is never read.

### The contract

`validation/rock-contract.json` lists every Rock table and column the loader writes, the full column list of those tables when it was recorded, the tables it deletes rows from, the Rock-provided Guids it looks up, and the Rock procedures it runs.

Nobody writes it by hand. During each verification load, the loader's database class records every bulk copy and every SQL statement it runs (`loader/database/WriteRecorder.cs`). `--record-contract` saves that recording. A write the recorder cannot read fails the check, so nothing slips past it.

Two kinds of drift fail the check:

1. **Rock changed.** A column the loader writes is gone or has a new type, a Guid or procedure is gone, or Rock added a column to a table the loader writes. A new column needs a decision: should the loader fill it the way Rock's save hook would?
2. **The loader changed.** It writes, deletes, or depends on something the contract does not list.

Either way, review the change, run with `--record-contract`, and commit the new contract. Its diff shows exactly what changed. The check also refuses `Person.RaceValueId` and `Person.EthnicityValueId` (content standard 8).

### Invariants

`validation/invariants.sql` holds rules that must hold in every dataset on every Rock: every person has one primary alias, the primary family is a family the person is in, giving Ids match giving groups, phones and emails use reserved values, home addresses are mapped, nothing is dated in the future, every gift sits inside its batch, member roles belong to the group's type, and more. Each rule says why it matters. Rules a foreign key already enforces are left out, because the bulk copy checks those.

To add a rule, add a `-- check:` block that returns how many rows break it.

### Rock Cleanup as a referee

Rock's nightly Rock Cleanup job repairs what Rock's save hooks would have set: missing aliases, ages, salutations, and more. After a load, the tool runs that job and compares the database before and after, column by column, using a SQL Server snapshot. Whatever Rock fixes in the sample data, the loader missed.

The job runs outside IIS through `rock-runner/`, a small .NET Framework program that loads `Rock.dll` from the checkout's `RockWeb\bin` in its own AppDomain and starts Rock the way EF's design-time tools do. It also starts Rock's in-memory message bus before the job, as Rock's own startup does. Without it, each cache message the job publishes could log a bus error to the Exception Log, depending on timing, which made this check fail now and then for no data reason. It has no compile-time reference to Rock, so one build works with any Rock version. Two of the job's tasks need a web server (orphaned entity metadata and the connected services manifest). They fail the same way on the plain base, so they are noted, not failed. The campus tithe metric task runs for real once the base has its postal codes, so the loader sets that metric too.

Rock's job also tidies things in any fresh database. So it runs once on a plain copy of the base too. A change Rock also makes there, to rows that are not sample data, is background and is reported but does not fail. A change we accept on purpose goes in `validation/cleanup-accepted.json` with a reason.

Rock Cleanup rebuilds `ScheduleDate` for a window of now plus or minus two years, to the minute, and the plain copy and each loaded copy are cleaned minutes apart. So the comparison leaves out the `ScheduleDate` rows within a day of either edge of that window, in both runs, and compares the rest exactly. Before, an occurrence at an edge now and then fell inside one run and outside the other, and the check failed on base rows the load never touched.

An event occurrence's next start can pass between the load and Rock Cleanup, and Rock then moves it to the following start. So a change to `EventItemOccurrence.NextStartDateTime` is left out when the old value had already passed when Rock Cleanup started and Rock only moved it later or cleared it. Those rows show as "Not judged". Any other change to the column still fails.

### Logins and sign-in

Every load makes its logins with the test password `sample-data-verify`, hashed by Rock's own Database login through `rock-runner --task hash-password`, the way Rock's installer block hashes the one a person types (decision 180). It is not a secret: it signs in only to the run's throwaway databases. After the referee, `rock-runner --task sign-in` signs in through Rock's Database login, the check Rock's login block makes, and the `sign-in` check fails unless Rock accepts the sample logins and refuses a wrong password. It runs after the referee because a sign-in can rewrite a hash at the site's cost setting.

### Content standards

The verify tool runs `cast/validate-cast.js` and `validation/check-standards.js`. Both need only Node, so contributors run them too. The [content standards](../docs/content-standards.md) say which rule each script checks, and which rules still need a person.

## Receipts and the publishing gate

Each run writes `{dataset}.verify.json` next to each package. A receipt records the package's SHA-256, its source commit, its minimum loader version (decision 158), the Rock major release and the exact Rock version and migration, the trusted base's certificate (including its schema hash), the anchor and seed, this repository's commit, and every check with its details. A package passes only when every check passes. Its `load` section records how long the load took, step by step (every timing the loader reports), and the space it used: the peak used data and log space, sampled once a second, the file sizes, the file growths the default trace saw, the recovery model, and whether instant file initialization is on. No check reads it; it is there so every run leaves a timing record (see [Performance](../docs/performance.md)).

Publishing runs the gate first:

```
dotnet run -- --check-receipts ../packages
```

It refuses to publish when a package has no receipt, its receipt is for different bytes, any check failed, the receipt is for a different Rock than the package, or either the package or the checks came from uncommitted changes.

## When a check fails

| Check | What to do |
|---|---|
| `trusted-base` | The tool rebuilds the base on its own. If it still fails, the checkout's `RockWeb\bin` was built from different code than its migrations folder. Rebuild Rock. |
| `contract-schema` | Rock changed under the loader. Read each line, update the loader if needed, then re-record. |
| `contract-current` | The loader changed. Review the change, then re-record. |
| `invariants` | The loader wrote data that breaks a rule. Fix the loader, or fix the rule if the rule is wrong. |
| `rock-cleanup`, "Rock Cleanup was not judged" | Today's date is not the loads' anchor: the run crossed midnight, `--anchor` is not today, or the database server and this machine keep different time zones. Run it again, without `--anchor`. |
| `rock-cleanup` | The loader skipped something Rock's save hooks set. Fill it in the loader, or accept it in `validation/cleanup-accepted.json` with a reason. |
| `group-sync` | A synced group does not hold exactly what its data view finds in Rock. Fix the view's filters in `configuration/group-syncs.json` or how `loader/loading/DataViewEvaluator.cs` reads them. |
| `content-standards` | The data breaks a content standard. Each line names the rule. |
| `data-views` | Rock could not read a data view's filter. Fix its selection in the configuration file that holds it, or how `loader/loading/ReportingLoader.cs` builds it. |
| `access` | Rock's answer differs from the walkthrough. Fix the category's `security` in `configuration/categories.json`, or the row in `configuration/reporting.json` if the walkthrough is wrong. |
