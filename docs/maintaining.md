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
| Make a decision | Add a row to [decisions.md](decisions.md). |

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
- [ ] New decisions added to [decisions.md](decisions.md).
- [ ] [Roadmap](roadmap.md) and [cast/INVENTORY.md](../cast/INVENTORY.md) updated if a gap opened or closed.
