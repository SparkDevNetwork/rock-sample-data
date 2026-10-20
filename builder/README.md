# Builder

The builder reads this repository and writes one dataset package per dataset: `core-1.zip`, `core-3.zip`, and `full-7.zip`. It never touches a database. The [loader](../loader/README.md) installs packages.

## Writing packages

From the `builder` folder:

```
dotnet run -- --rock C:\Development\Rock
```

That writes every dataset's package to `../packages/` (git ignores it). Each package is for the Rock in the checkout you name.

| Option | Meaning |
|---|---|
| `--rock <folder>` | A Rock checkout. The package records its version (from `Directory.Build.props`) and its newest migration (from `Rock.Migrations/Migrations`). The `ROCK_PATH` environment variable does the same. Required unless both values below are given. |
| `--rock-version` | The Rock version, for example `21.0.1`. Wins over the checkout's. |
| `--rock-migration` | The newest migration Id, for example `202609222138362_AddWorkflowBuilder`. Wins over the checkout's. |
| `--dataset` | One dataset (`core-1`, `core-3`, or `full-7`), or `all` (the default). |
| `--out <folder>` | Where to write packages. Defaults to `../packages`. |
| `--profile` | The calibration profile in `../calibration/profiles/`. Defaults to `sample-church`. |
| `--root` | The repository folder, if it cannot be found automatically. |

## What a package holds

A package is a zip of data, never SQL (decision 41). Files keep their paths from this repository, so the loader reads a package the same way it once read the repository.

| Path | What it holds |
|---|---|
| `package.json` | The format version, the dataset, the source commit, the one Rock it is for (decision 52), and the SHA-256 of every other file. |
| `configuration/*.json` | The church setup, unchanged. |
| `cast/households/*.json`, `cast/groups/*.json` | The Cast and its small group plans, unchanged. |
| `datasets/{key}.json` | The dataset's settings. |
| `profile.json` | The calibration profile, already resolved from its sources, so a load never blends sources itself. |
| `addresses/pool.json` | The address pool for the dataset's campuses only. Each campus's list is unchanged, so generated households get the same addresses. |
| `photos/manifest.json`, `photos/cast/`, `photos/pool/` | The Cast photos, plus the pool when the dataset gives generated households faces (Core 3). |
| `content/fonts/` | The check image fonts, with their licenses. |
| `LICENSE.md` | The Rock Community License. |

No timeline is stored: the loader generates every gift, attendance, and alert for the install day. Text files use LF line endings, and every zip entry has the same fixed time, so packing the same commit twice gives byte-identical packages.

Before writing a package, the builder opens it the way the loader does and reads every file a load reads, so a package that would fail to load is never written.

## Other commands

| Command | What it does |
|---|---|
| `--plan-cast-groups [--seed text]` | Plans the Cast's small groups and writes them to `../cast/groups`. See [How small groups load](../loader/README.md#how-small-groups-load). |
| `--deliver-photos <batch.json>` | Keeps new generated photos' originals in `../photos/originals`, saves their 400-pixel copies, and adds them to `../photos/manifest.json`. See the [photo brief](../photos/brief.md). |

## Proving a change

After you build packages, run the [verify tool](../verify/README.md) with the same Rock checkout. It loads each package into a trusted Rock database, checks it against Rock itself, and writes a receipt next to each package. Packages are published only with a passing receipt (decision 66).

The split into builder and loader was proven by loading each dataset both ways: straight from the repository with the old builder, and through a package. At the same seed and anchor, every table's row count, the money totals, and fingerprints of people, addresses, gifts, attendance, groups, attribute values, alerts, and stored files matched exactly, including Full 7 with a 3,000-household crowd.
