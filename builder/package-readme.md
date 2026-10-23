# {{name}} sample data package

This package installs the **{{name}}** dataset (`{{dataset}}`) of Rock Sample Data into a fresh Rock database. It is for Rock {{rockVersion}} (newest migration `{{rockMigration}}`). The loader refuses any other Rock.

All the people, families, and organizations in it are fictional.

## What is in the package

The package holds the inputs, not the records. It has no attendance rows, gifts, or generated people.

| File or folder | What it is |
|---|---|
| `cast/households/`, `cast/groups/` | The Cast: hand-designed families and their groups, the same in every dataset |
| `configuration/` | The church setup: campuses, service times, funds, check-in, small groups, and alert types |
| `datasets/{{dataset}}.json` | This dataset's settings, such as generated households and the check image window |
| `profile.json` | The target numbers the loader generates against (measured church patterns) |
| `addresses/pool.json` | Real-street addresses with house numbers that do not exist |
| `photos/` | Profile photos, and a photo of each Cast small group |
| `content/fonts/` | Fonts for the scanned check images, with their licenses |
| `package.json` | The format version, the Rock this package is for, and a SHA-256 for every file |
| `LICENSE.md` | The license for this data |

## What the loader makes from it

At install, the loader generates the rest: the generated households, three years of attendance and kids check-in, kids check-in streaks and achievements, giving, small groups, giving analytics, giving alerts, and (when turned on) scanned check images. A recent Sunday check batch at the main campus stays open, with its checks unmatched, so Rock's transaction matching has work to try. Every date counts back from an **anchor date**, so "last 12 months" reports are always full.

## The seed and the anchor

Two values decide what the loader generates.

- **The seed** is a piece of text. It defaults to `rock-sample-data`. Every random choice starts from it. The loader gives each piece of data its own stream, named by the seed and that piece, for example `rock-sample-data:attendance:{household}`. A fixed text hash (FNV-1a) turns the name into a number that seeds the random number generator. So the same name gives the same draws on every machine, and a change to one family does not change any other family.
- **The anchor** is the date everything counts back from. It defaults to install day. The anchor decides which Sundays and holidays fall in the history window.

## Is a load the same every time?

Yes, for the data, when three things match: this package, the seed, and the anchor. Every person, family, address, attendance record, gift, group membership, attribute value, and alert is the same, row for row.

Some values still differ between installs, by design:

1. **The anchor defaults to today.** Installs on different days generate different histories, not just shifted dates. To get identical data, give the same anchor.
2. **Ids** start after each table's highest existing Id, so they match only when the databases started the same.
3. **Guids** of the Cast, generated people and families, accounts, and alert types are stable. Most other rows (attendance, gifts, attribute values, check image files) get a new Guid on every install.
4. **Created and modified times** come from the install clock.
5. **Check scanner fields** are hashed with each server's own PasswordKey, and the encrypted bank line fields use its DataEncryptionKey with a random IV, so they differ on every install.
6. **Check image handwriting** is chosen per transaction Id, so it can differ when Ids differ. The checks' contents (writer, date, amount, number) are the same.

## Loading it

The loader library (`RockSampleData.Loader`) and its command line both install packages. With the command line:

```
rock-sample-data-load --package {{dataset}}.zip --connection "<connection string>" [--anchor yyyy-MM-dd] [--seed text]
```

Before it writes anything, the loader checks the format version, every file's SHA-256, that the database has no people beyond a new Rock install, and that it is Rock {{rockVersion}}. The load runs in one transaction, so a failure leaves nothing behind. Every row it writes carries `ForeignKey = SampleData:{{dataset}}`.

## More

See the Rock Sample Data repository: `loader/README.md` covers loading and repeatability in detail, and `docs/how-it-works.md` covers how the data is designed.
