# Verify

The verify tool proves that each dataset package works on one exact Rock, and writes a receipt that says so. It runs when packages are made, never when a church installs one (decision 66).

The loader bulk-copies around Rock's business logic. That is what makes it fast, but it also means Rock never gets a chance to say "this is wrong." So the proof has to come from somewhere else. The verify tool gets it from Rock itself: a database made only by Rock's own migrations and start-up steps, Rock's own column list, and Rock's own Rock Cleanup job.

This page is about running the tool. For every check in the repository, what it does, and why it is there, see [Verification](../docs/verification.md).

## Running it

Build the packages first (see the [builder](../builder/README.md)), from the same built Rock checkout. Then, from this folder:

```
dotnet run -- --rock C:\Development\Rock
```

It needs SQL Server on `localhost` with Windows sign-in, Node.js, and a built Rock checkout (`RockWeb\bin` must hold `Rock.dll`). It makes and drops its own databases, all named `RockSampleVerify...`, and never touches any other database. Run one verification at a time on a server: two runs would drop each other's databases.

| Option | Meaning |
|---|---|
| `--rock <folder>` | A built Rock checkout: a release tag, or develop. The `ROCK_PATH` environment variable does the same. Required. |
| `--dataset` | One dataset (`core-1`, `core-3`, or `full-7`), or `all` (the default). |
| `--packages <folder>` | Where the packages are. Defaults to `../packages`. Receipts are written next to them. |
| `--server` | The SQL Server. Defaults to `localhost`. |
| `--base` | The trusted base's name. Defaults to `RockSampleVerify-base`. |
| `--anchor`, `--seed` | The install day and seed for the verification loads. Default to today and `rock-sample-data`. |
| `--record-contract` | Records `validation/rock-contract.json` from these loads, instead of only checking it. Needs every dataset. |
| `--keep` | Keeps each loaded database (`RockSampleVerify-core-1` and so on) for a look afterward. |
| `--check-receipts [folder]` | Runs nothing else. The publishing gate: exits with 1 unless every package has a passing receipt. |

A full run takes about 5 minutes. Making a new trusted base adds about 9 minutes, once for each Rock release.

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

### The trusted base

The base is an empty database made from nothing in three steps:

1. EF's own migration tool (`ef6.exe`, from the Rock checkout's packages) applies every Rock migration, the same way a Rock developer runs Update-Database.
2. `rock-runner` runs Rock's own start-up steps that write to the database: the `RockGetDate` function, the plugin and hotfix migrations, and the attribute value views. EF alone skips these. On Rock 21.0.1 that is 189 hotfix migrations, so a base made only by EF is not what a church has after Rock starts.
3. `rock-runner` runs Rock's one-time post-install job (`PostInstallDataMigrations`), which a new install runs at 2am on its first night. It fills `AnalyticsSourceDate`, the identity verification codes, and `AnalyticsSourcePostalCode` (from `RockWebApp_DataFormatted_Census_Data.xlsx`, so Rock's AppDomain uses the RockWeb folder as its base, as under IIS). Rock Cleanup reads every person's age from `AnalyticsSourceDate`, so without this step Rock Cleanup sets every age to null, which no church that has run one night sees.

Nothing else ever touches the base; every load runs on a copy.

Before each run, the tool checks the base: its `__MigrationHistory` must hold exactly the Rock checkout's migrations (read from each migration's designer file, since a file name is not always its Id), Rock's start-up steps and post-install job must have run (with the postal codes filled), and it must have no sample data and an empty Exception Log. A plugin migration that fails logs an exception, so the empty log also proves every hotfix applied. If the base is missing or does not match the checkout, the tool makes a new one. The receipt records the base's schema hash, so two receipts with the same hash were checked against the same schema.

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

The job runs outside IIS through `rock-runner/`, a small .NET Framework program that loads `Rock.dll` from the checkout's `RockWeb\bin` in its own AppDomain and starts Rock the way EF's design-time tools do. It has no compile-time reference to Rock, so one build works with any Rock version. Three of the job's tasks need a web server (orphaned entity metadata, the campus tithe metric, and the connected services manifest). They fail the same way on the plain base, so they are noted, not failed.

Rock's job also tidies things in any fresh database. So it runs once on a plain copy of the base too. A change Rock also makes there, to rows that are not sample data, is background and is reported but does not fail. A change we accept on purpose goes in `validation/cleanup-accepted.json` with a reason.

### Content standards

The verify tool runs `cast/validate-cast.js` and `validation/check-standards.js`. Both need only Node, so contributors run them too. The [content standards](../docs/content-standards.md) say which rule each script checks, and which rules still need a person.

## Receipts and the publishing gate

Each run writes `{dataset}.verify.json` next to each package. A receipt records the package's SHA-256, its source commit, the Rock version and migration, the trusted base's certificate (including its schema hash), the anchor and seed, this repository's commit, and every check with its details. A package passes only when every check passes.

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
| `rock-cleanup` | The loader skipped something Rock's save hooks set. Fill it in the loader, or accept it in `validation/cleanup-accepted.json` with a reason. |
| `content-standards` | The data breaks a content standard. Each line names the rule. |
