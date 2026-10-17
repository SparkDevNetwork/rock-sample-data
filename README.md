# Rock Sample Data

Rock Sample Data builds the sample data for [Rock RMS](https://www.rockrms.com): fictional church people, families, giving, attendance, check-in, and more. Our goal is sample data that **installs in seconds, looks like a real church, covers every feature, and never goes stale**.

This repository holds everything that goes into that data: the people, the church setup, the measured patterns that make it realistic, the tools that build it, and every decision we made along the way. If you want to know why the data looks the way it does, the answer is written down here.

## Who uses it

- **Core developers** who need realistic data to build and debug features.
- **Automated tests** that need the same known people every time.
- **Demo sites and sales** that need a church that looks real.
- **Plugin developers** who need data to test against.

## Get up to speed

Read these in order. Together they take about 30 minutes.

1. [Strategy](docs/strategy.md): the problem, the goal, and the big ideas behind the design.
2. [How it works](docs/how-it-works.md): how the data files become a Rock database, step by step.
3. [Meet the Cast](cast/families/README.md): the families we use in testing, with photos and backstories.
4. [Decisions](docs/decisions.md): every decision we made, with the date and the reason.
5. [Content standards](docs/content-standards.md): the rules every piece of data must follow.
6. [Planned work](docs/planned.md): features and data we plan to add but have not built yet.
7. [Roadmap](docs/roadmap.md): the team's working list: what is done, what is next, and what we are waiting on.
8. [Contributing](docs/contributing.md): how to suggest a use case or point out a gap.

Need a word explained? See the [glossary](docs/glossary.md). Curious how generated people behave? See [The Crowd and personas](docs/crowd.md). Want the original design? See the [design spec](docs/design-spec.md).

## At a glance

| Dataset | People | Campuses | Install time |
|---|---|---|---|
| **Core 1** | The Cast (83 households, 181 people) | 1 | About 4 seconds |
| **Core 3** | The Cast plus 75 generated households at each of 2 more campuses | 3 | Seconds |
| **Full 7** | The Cast plus the Crowd (about 25,000 people) | 7 | 20 to 40 seconds |

- **The Cast** is hand-designed, the same in every dataset, and safe for tests to rely on by Guid. Every Cast person has a profile photo, except a few left without one on purpose.
- **The Crowd** is generated from measured church patterns to give volume.
- **Dates are generated at install**, counted back from install day, so "last 12 months" reports are always full.
- **Realism is measured.** The same scripts that measure real churches grade our data. Today 89% of measured values fall within tolerance.
- **Giving analytics work on install.** Giving attributes, journey stages, and a year of giving alerts are filled in the way Rock computes them, and a version guard catches Rock changing that math.

## What is where

| Folder | What it holds |
|---|---|
| [docs/](docs/) | Strategy, how it works, decisions, planned work, roadmap, standards, contributing, maintaining, glossary, small groups, the Crowd and personas, and the design spec. |
| [cast/](cast/README.md) | The hand-designed households and groups, their schemas, the validator, and the [family pages](cast/families/README.md). |
| [configuration/](configuration/README.md) | The church setup: campuses, service times, funds, check-in, and connection statuses. |
| [calibration/](calibration/README.md) | Scripts that measure Rock databases, the normalizer, and the profile of target numbers. |
| [addresses/](addresses/README.md) | The address pool: real streets with house numbers that do not exist. |
| [photos/](photos/README.md) | The photo rules, the manifest, and the delivered Cast and pool photos. |
| [builder/](builder/README.md) | The .NET tool that turns this repository into one dataset package per dataset. |
| [loader/](loader/README.md), `loader-cli/` | The loader library that installs a package into a Rock database, and its command line for developers and test harnesses. |
| [validation/](validation/README.md) | Tolerances and the script that grades generated data. |
| [datasets/](datasets/README.md) | One manifest per dataset, with its settings. |
| [content/](content/README.md) | Fonts for check images, and later the text pools. |

## Quick start

You need the .NET 10 SDK, Node.js 20 or later, and SQL Server with a freshly installed Rock database.

1. Check the Cast:

   ```
   node cast/validate-cast.js
   ```

2. Build the packages for your Rock checkout. They land in `packages/`.

   ```
   cd builder
   dotnet run -- --rock C:\Development\Rock
   ```

3. Load Core 1 into a fresh Rock database. Add `--dry-run` to roll everything back at the end. If your database is on a different migration than the checkout, add `--any-rock`.

   ```
   cd ../loader-cli
   dotnet run -- --package ../packages/core-1.zip --connection "Server=localhost;Database=RockSample;Integrated Security=true;TrustServerCertificate=true"
   ```

4. Grade the result against the profile. See [Validation](validation/README.md) for the three commands.

5. After you change a Cast file, rebuild the family pages:

   ```
   node cast/build-pages.js
   ```

## Ground rules

Everything here is fictional: people, churches, businesses, phone numbers, emails, and house numbers. Real church data never enters this repository, and calibration sources are never named or described. Read the [content standards](docs/content-standards.md) before you add anything.

## License

The code and data are licensed under the [Rock Community License](LICENSE.md), the same license as Rock. The bundled fonts in `content/fonts/` keep their own open licenses (see [Fonts](content/fonts/README.md)).

## History

This project started in the Rock repository under `Dev Tools/SampleData` and moved here on 2026-10-15 with its full commit history.
