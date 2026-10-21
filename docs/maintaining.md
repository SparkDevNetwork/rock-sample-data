# Maintaining

How the team changes the sample data. To suggest a change from outside the team, see [Contributing](contributing.md).

Before you start, read the [content standards](content-standards.md). Every change must follow them.

## Where changes go

| You want to... | Start here |
|---|---|
| Add a Cast family or person | See "Add a Cast household." |
| Change the church setup (campuses, funds, rooms, times) | Edit `configuration/` and explain why in the decision log. |
| Add a calibration source | Run the calibration scripts on a database you may use. See [Calibration](../calibration/README.md). |
| Change how data is generated | Change the builder, then show the validation result did not get worse. |
| Make a decision | Add a row to [decisions.md](decisions.md) with a placeholder number. See "Working in parallel." |

## Working in parallel

Several people and AI agents often change this repository at the same time. The short rules are in [AGENTS.md](../AGENTS.md), which agents read automatically. This section explains how the pieces fit.

### One worktree per piece of work

Each piece of work gets its own branch in its own folder (a git worktree), so no one edits another's files:

```
git worktree add -b notes ../rock-sample-data-notes main
```

The main checkout, `rock-sample-data`, only takes merges. The hooks in `.githooks` enforce it: `pre-commit` refuses an ordinary commit on `main`, and `pre-merge-commit` refuses a merge into `main` that still has a placeholder decision. Turn them on once per clone (see [AGENTS.md](../AGENTS.md#setup-once-per-clone)). A person who truly needs a direct commit on `main` can set `ALLOW_MAIN_COMMIT=1` for that one command.

Merge `main` into your branch often: when you start, before each verify run, and before you hand off. Small pieces that land the same day rarely conflict.

### Decision numbers

Two branches that each take "the next number" collide. So a branch never takes one. A new decision gets a placeholder: `D-`, a slug for the branch, and a counter.

```
| D-notes-1 | 2026-10-21 | People have notes. ... | ... |
```

Cite it the same way everywhere, for example "(decision D-notes-1)". Just before the branch merges, the integrator runs:

```
node docs/number-decisions.js --dry-run
node docs/number-decisions.js
```

It numbers the placeholders from the next free number, in the order they appear in the log, and rewrites every mention in the repository's tracked text files. `--check` exits with 1 when a placeholder is left; the merge hook runs it.

### Generated files

Some files are written by tools, never by hand. Two branches each write a correct version, and git sees a conflict. Never merge them line by line. Take either side, finish the merge, then rebuild:

| File | Rebuild with |
|---|---|
| `validation/rock-contract.json` | The verify tool with `--record-contract` |
| `cast/families/*.md` | `node cast/build-pages.js` |
| `packages/` (ignored by git) | The builder |

The contract is the list of every Rock table, column, Guid, and stored procedure the loader writes or depends on, recorded from real loads. The verify tool checks it against each Rock release, so a Rock change that would break the loader is caught before a package ships.

### Shared machine resources

The verify tool names its own databases after the worktree it runs from (`RockSampleVerify-notes-core-1`, or `RockSampleVerify-main-core-1` from the main checkout), so runs from different worktrees never touch each other. They all copy from one trusted base, `RockSampleVerify-base`. A run holds a shared lock on it while it runs, and takes an exclusive lock only to rebuild a stale base, so a rebuild waits for every running verification. `--run <tag>` sets the tag by hand.

Scratch databases for your own loads follow the same idea: name them after your branch, and never touch one another session made.

### The integrator

One session or person, the integrator, lands work on `main`. Feature work ends with a message to the integrator: the branch, what changed, and the checks that ran. The integrator lands one branch at a time:

1. Merge `main` into the branch, and resolve conflicts. Take either side of a generated file.
2. Run `node docs/number-decisions.js` on the branch.
3. Rebuild the packages, run the verify tool with `--record-contract`, then run it again without it for a clean pass.
4. Commit on the branch, and merge it into `main`. Push once the person running the integrator agrees.
5. Tell the other sessions that `main` moved, so they merge it.

Start an integrator whenever two or more sessions will change the repository at once. It handles the steps above, but a person still makes the judgment calls, such as dropping a branch that `main` already covers.

When only one session is working, it can be its own integrator (solo mode, rule 12 in [AGENTS.md](../AGENTS.md#handing-off)). The person turns it on by saying so. A session never assumes it, because it cannot see other sessions. A branch that changes only documentation (`.md` files outside `cast/` and `content/`) can skip the package build and verify, since docs cannot break a package.

## Add a Cast household

1. **Copy a similar household** in `cast/households/` and give it a new file name. The file name is the household `key` (lowercase, hyphens).
2. **Make new Guids** for the household and every person. Guids are uppercase and never change once committed.
3. **Write the story and test purposes.** The first test purpose is the main reason the family exists. It shows on the family pages.
4. **Fill in each person:** names, gender, family role, age (`birth.age` plus month and day), marital status, connection status, and, for featured people, a short `bio`.
5. **Pick names first.** Then set `photo.ethnicity` for each adult so the Cast stays close to the mix in `photos/ethnicities.json`, with about 15% of couples mixed. Children inherit from their parents. Set a child's value only when the blend would be wrong, for example an adoption.
6. **Use only reserved contact data.** Phones from 555-0100 to 555-0199 that no one else in the Cast uses, and emails at `example.org`.
7. **Set attendance and giving patterns** in words the builder understands. See `cast/household.schema.json` for every option. Use `daysAgo` for anything recent.
8. **Leave the address out.** Run `node addresses/build-pool.js --cast`, and it adds an address near the main campus.
9. **Check and publish:**

   ```
   node cast/validate-cast.js
   node cast/build-pages.js
   ```

   The validator must show 0 errors. Read your new family page in `cast/families/`.

10. **Load it.** Build the Core 1 package with the builder (`--dataset core-1`), then load it with the loader CLI and `--dry-run` against a fresh Rock database, and confirm it loads.
11. **Photos.** New people need photos. The validator warns about each person without one. Give an image model the [photo brief](../photos/brief.md), then deliver the results with the builder's `--deliver-photos` command.

## Change a Cast person

Tests and documentation rely on Cast people. Treat a change like a code change:

- Never change a Guid.
- Do not change a name, age, or relationship that a test or help article may rely on, unless you also fix what depends on it.
- Keep the status rules: a Leading person leads a group; an Engaged person gives, serves, or is in a group (a child counts when an adult at home does).

## Change the builder or loader

- Keep the load to one transaction, and keep `--dry-run` working.
- Keep every random choice seeded, so the same seed and anchor give the same data.
- Keep packages data only. A file the loader needs goes into the package through the builder, and the loader reads it through the package, never from the repository.
- If a change alters the package layout in a way an older loader cannot read, raise the package format version.
- Run validation on Full 7 before and after your change. A change should not lower the share of values within tolerance without a written reason. See [Validation](../validation/README.md).

## Commits

Write commit messages that start with a past-tense verb and say what changed, for example "Added a blended family with half siblings." Keep each commit to one idea.

## Review checklist

- [ ] Follows the [content standards](content-standards.md).
- [ ] `node cast/validate-cast.js` and `node validation/check-standards.js` show 0 errors.
- [ ] For a builder or loader change, the [verify tool](../verify/README.md) passes, and any change to `validation/rock-contract.json` is explained. See [Verification](verification.md).
- [ ] Family pages rebuilt if a Cast file changed.
- [ ] The Core 1 package builds and loads with `--dry-run`.
- [ ] New decisions added to [decisions.md](decisions.md), with placeholder numbers on a branch.
- [ ] [Roadmap](roadmap.md) and [cast/INVENTORY.md](../cast/INVENTORY.md) updated if a gap opened or closed.
