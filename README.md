# Rock Sample Data

Rock Sample Data builds the sample data for [Rock RMS](https://www.rockrms.com): fictional church people, families, giving, attendance, check-in, and more. Our goal is sample data that **installs in seconds, looks like a real church, covers every feature, and never goes stale**.

This repository holds everything that goes into that data: the people, the church setup, the measured patterns that make it realistic, the tools that build it, and every decision we made along the way. If you want to know why the data looks the way it does, the answer is written down here.

## Who uses it

- **Core developers** who need realistic data to build and debug features.
- **Automated tests** that need the same known people every time.
- **Demo sites and sales** that need a church that looks real.
- **Plugin developers** who need data to test against.

## Get up to speed

Read these in order. Together they take about 35 minutes.

1. [Strategy](docs/strategy.md): the problem, the goal, and the big ideas behind the design.
2. [How it works](docs/how-it-works.md): how the data files become a Rock database, step by step.
3. [Meet the Cast](cast/families/README.md): the families we use in testing, with photos and backstories.
4. [Decisions](docs/decisions.md): every decision we made, with the date and the reason.
5. [Content standards](docs/content-standards.md): the rules every piece of data must follow.
6. [Verification](docs/verification.md): every check that proves the data is right, what each one does, and why it is there.
7. [Planned work](docs/planned.md): features and data we plan to add but have not built yet.
8. [Roadmap](docs/roadmap.md): the team's working list: what is done, what is next, and what we are waiting on.
9. [Contributing](docs/contributing.md): how to suggest a use case or point out a gap.

Changing the repository yourself, or with AI agents? Read [AGENTS.md](AGENTS.md) first: several agents often work here at once, and its rules keep them from colliding. [Working with AI agents](#working-with-ai-agents) below says what to tell them.

Need a word explained? See the [glossary](docs/glossary.md). Curious how generated people behave? See [The Crowd and personas](docs/crowd.md). Want the original design? See the [design spec](docs/design-spec.md).

## Working with AI agents

AI agents such as Claude Code read [AGENTS.md](AGENTS.md) on their own and follow its rules. This section gives you prompts to copy and paste. The full process is in [Working in parallel](docs/maintaining.md#working-in-parallel).

**Set up once per clone.** Run the two `git config` lines in [AGENTS.md](AGENTS.md#setup-once-per-clone). They turn on the hooks that keep `main` merge-only.

### Pick a way to work

- **Start here (Claude desktop app):** paste the coordinator prompt. It is the only prompt you paste and the only session you talk to. It starts the integrator, and a feature session for each piece of work, by itself. Feature sessions hand finished branches to the integrator, and only the integrator merges into `main`. Every session sends the coordinator its questions and status, and it brings you only the real decisions (see [The coordinator](docs/maintaining.md#the-coordinator)).
- **One session alone:** use solo mode. The session does the work and then lands it itself.
- **In a terminal:** a terminal session cannot start other sessions. Start the integrator yourself, then one feature session per piece of work, with the fallback prompts below.

Replace each `<...>` before you paste a prompt.

### Prompt: coordinator (start here)

```text
You are the coordinator for the rock-sample-data work, and the only session I talk
to. Read AGENTS.md (rule 19) and "Working in parallel" in docs/maintaining.md.

1. Start an integrator session with the integrator prompt in the README's "Working
   with AI agents" section. Fill in <path to your Rock checkout>, and name yourself
   as its coordinator.
2. For each piece of work I give you, start a feature session with the feature
   prompt, its own branch name, and the work, and name yourself as its coordinator.
3. Answer anything the decision log, AGENTS.md, or my earlier direction already
   settles. Bring me only real choices, numbered, each with one recommendation, and
   forward my answers in my own words.
4. Keep a short list of what is in flight, what the next release waits on, and what
   is waiting on me. Tell me before any click I must make in another window.

The work: <describe each piece of work, or "none yet">.
```

### Fallback prompts

The coordinator uses these as the briefs for the sessions it starts. In a terminal, paste them yourself: the integrator first, then one feature session per piece of work. If no coordinator is running, leave out the coordinator line.

### Prompt: integrator

Keep this session open while the others work.

```text
You are the integrator for the rock-sample-data repository. Read AGENTS.md and the
"Working in parallel" section of docs/maintaining.md; they define your process.
Confirm this clone has core.hooksPath=.githooks and branch.main.mergeoptions=--no-ff.

Your only job is landing other sessions' finished branches on main, one at a time,
in the order they arrive. You never write features.

When a session tells you a branch is ready, land it in that branch's own worktree:
1. Merge main into the branch and resolve conflicts. Take either side of a
   generated file.
2. Run node docs/number-decisions.js on the branch.
3. Rebuild the packages with the builder for <path to your Rock checkout>. Run the
   verify tool with --record-contract, then again without it. Every receipt must
   pass. A branch that changes only docs can skip this step.
4. Commit on the branch, merge it into main from the main checkout, and push.
5. Tell every other session that main moved, so they merge it.

If a branch fails a check, send it back to its session with the failure instead of
fixing it yourself. Push main after each landing. Ask me before anything that removes
or rewrites history.

Send your questions and status to the coordinator session, <coordinator name>.
```

### Prompt: feature session

Use one of these for each piece of work while an integrator is running.

```text
Read AGENTS.md. Make a worktree for a new branch named <branch> from main, and do all
of your work there: <describe the work>.

Give any new decision a placeholder such as D-<branch>-1. Keep the docs and the
public site current (AGENTS.md rule 15).

When the work is done and its checks pass, message the integrator session with the
branch, the worktree, what changed, and the checks you ran. Do not merge into main.

Send your questions and status to the coordinator session, <coordinator name>.
```

### Prompt: one session alone (solo mode)

> **Warning:** Use solo mode only when no other session is changing the repository. An agent cannot see other sessions, so it trusts you on this.

```text
You are the only session working in the rock-sample-data repository, so you are also
its integrator (solo mode, AGENTS.md rule 12). Read AGENTS.md and the "Working in
parallel" section of docs/maintaining.md.

Make a worktree for a new branch named <branch> from main, and do the work there:
<describe the work>.

When the work is done, land it yourself with the integrator's steps: merge main into
the branch, run node docs/number-decisions.js, rebuild the packages with the builder
for <path to your Rock checkout> and run the verify tool with --record-contract and
then again without it (skip both for a docs-only change), commit, merge into main,
and push. Ask me before anything that removes or rewrites history.
```

### What to expect

- The integrator pushes `main` only when your prompt says it may. The prompts above let it push after each landing.
- It brings you any judgment call, such as dropping a branch that `main` already covers, or a check that fails for a reason outside the branch. With a coordinator running, those calls reach you through the coordinator.
- Some clicks still happen in other windows: tool permission prompts, and the yes for a tag, a push to another repository, or a squash. The coordinator tells you when one is coming.

## At a glance

| Dataset | People | Campuses | Load time |
|---|---|---|---|
| **Core 1** | The Cast (87 households, 185 people) | 1 | About 15 seconds |
| **Core 3** | The Cast plus 75 generated households at each of 2 more campuses | 3 | Under 20 seconds |
| **Full 7** | The Cast plus the Crowd (about 25,000 people) | 7 | About 4 minutes |

- **The Cast** is hand-designed, the same in every dataset, and safe for tests to rely on by Guid. Every Cast person has a profile photo, except a few left without one on purpose.
- **The Crowd** is generated from measured church patterns to give volume.
- **Every campus has businesses** that give, each with one or two contacts. The main campus has at least 6 and every other campus at least 2.
- **Dates are generated at install**, counted back from install day, so "last 12 months" reports are always full.
- **Realism is measured.** The same scripts that measure real churches grade our data. Today 91% of measured values fall within tolerance, including serving, connections, and prayer.
- **Correctness is proven against Rock itself.** Before a package is published, the verify tool loads it into a clean copy of the Rock release it is built on, checks it against Rock's schema, runs rule checks, and has Rock's own Rock Cleanup job look for anything to fix. See [Verification](docs/verification.md).
- **People have notes** in six note types, secured to staff. Cast notes are hand-written and tied to each family's story, and they show more and better note use than most churches have today.
- **A building campaign is under way.** Built on the Rock is about 38% through on install day, with pledges from people and businesses in every state Pledge Analytics can show.
- **Giving analytics work on install.** Giving attributes, journey stages, and a year of giving alerts are filled in the way Rock computes them, and a version guard catches Rock changing that math.

## What is where

| Folder | What it holds |
|---|---|
| [site/](site/README.md) | The public site: why the sample data matters, the church, the Cast, the datasets, and the brand kit and photo downloads. GitHub Pages publishes it from `main` at [sampledata.rockrms.com](https://sampledata.rockrms.com/). |
| [docs/](docs/) | Strategy, how it works, decisions, planned work, roadmap, standards, contributing, maintaining, glossary, small groups, serving, steps, communications, the calendar, the Crowd and personas, and the design spec. |
| [cast/](cast/README.md) | The hand-designed households and groups, their schemas, the validator, and the [family pages](cast/families/README.md). |
| [configuration/](configuration/README.md) | The church setup: campuses, service times, funds, check-in, and connection statuses. |
| [calibration/](calibration/README.md) | Scripts that measure Rock databases, the normalizer, and the profile of target numbers. |
| [addresses/](addresses/README.md) | The address pool: real streets with house numbers that do not exist. |
| [photos/](photos/README.md) | The photo rules, the manifest, and the delivered Cast and pool photos. |
| [graphics/](graphics/README.md) | Reusable church initiative and campaign artwork, beginning with Starting Point. |
| [builder/](builder/README.md) | The .NET tool that turns this repository into one dataset package per dataset. |
| [loader/](loader/README.md), `loader-cli/`, `loader-compat-check/` | The loader library that installs a package into a Rock database (for .NET 10 and for Rock's .NET Framework, as the `SparkDevNetwork.Rock.SampleData.Loader` NuGet package), its command line for developers and test harnesses, and the check that both builds compute the same keys. |
| [validation/](validation/README.md) | Tolerances and the script that grades generated data, plus the invariants, contract, and standards check the verify tool uses. |
| [verify/](verify/README.md) | The tool that proves each package works on the Rock it is built on and writes its receipt. Publishing needs a passing receipt. |
| [release/](release/README.md) | The Rock releases each release builds for, and the script that merges a release into the hosted package index. `.github/workflows/release.yml` publishes the packages and the loader on a version tag. How to release, what has shipped, and how to fix a failed run: [Publishing a release](docs/maintaining.md#publishing-a-release). |
| [datasets/](datasets/README.md) | One manifest per dataset, with its settings. |
| [content/](content/README.md) | Fonts for check images, and later the text pools. |

## Quick start

You need the .NET 10 SDK, Node.js 20 or later, and SQL Server with a freshly installed Rock database.

1. Check the Cast and the content standards:

   ```
   node cast/validate-cast.js
   node validation/check-standards.js
   ```

2. Build the packages for your Rock checkout. They land in `packages/`.

   ```
   cd builder
   dotnet run -- --rock C:\Development\Rock
   ```

3. Load Core 1 into a fresh Rock database. `--rock` names your built Rock checkout, so the loader can read its version from `RockWeb\bin\Rock.Version.dll`. Add `--dry-run` to roll everything back at the end. If your database is on another Rock major release than the checkout, add `--any-rock`.

   ```
   cd ../loader-cli
   dotnet run -- --package ../packages/core-1.zip --rock C:\Development\Rock --connection "Server=localhost;Database=RockSample;Integrated Security=true;TrustServerCertificate=true"
   ```

4. Grade the result against the profile. See [Validation](validation/README.md) for the three commands.

5. Before you publish, prove every package works on its Rock. The verify tool makes its own databases, loads each package, and writes a receipt next to it (see [Verification](docs/verification.md)):

   ```
   cd ../verify
   dotnet run -- --rock C:\Development\Rock-21
   ```

   Point `--rock` at a Rock checkout kept only for this, at the `ref` in `release/rock-releases.json`, not the one you develop Rock in. Pulling develop there breaks the trusted base until you rebuild Rock (see the [verify README](verify/README.md#running-it)).

6. After you change a Cast file, rebuild the family pages:

   ```
   node cast/build-pages.js
   ```

## Ground rules

Everything here is fictional: people, churches, businesses, phone numbers, emails, and house numbers. Real church data never enters this repository, and calibration sources are never named or described. Read the [content standards](docs/content-standards.md) before you add anything.

## License

The code and data are licensed under the [Rock Community License](LICENSE.md), the same license as Rock. The bundled fonts in `content/fonts/` keep their own open licenses (see [Fonts](content/fonts/README.md)).

## History

This project started in the Rock repository under `Dev Tools/SampleData` and moved here on 2026-10-15 with its full commit history.
