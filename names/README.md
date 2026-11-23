# Names

Generated people get **real first names for their birth decade and real surnames**, drawn by how common each name is. A grandmother born in the 1950s is more likely a Linda or a Susan, and a toddler more likely an Olivia or a Liam. The decisions are D-crowd-names-1 to D-crowd-names-3 in [Decisions](../docs/decisions.md).

## What is here

| File | What it holds |
|---|---|
| `first-names.json` | The top 200 first names for each gender and birth decade, 1930s to 2020s, each with its births in that decade. Built, never edited by hand. |
| `surnames.json` | The top 2,000 surnames, each with how many people had it. Built, never edited by hand. |
| `nicknames.json` | Common nicknames for first names in the tables, the most common first, such as Robert to Bob, Rob, or Bobby. Written by hand. |
| `build-names.js` | Builds the two tables from the source data, and checks `nicknames.json`. |
| `raw/` | Downloaded source data. Git ignores it. |

Every package carries the three JSON files.

## Source data

Both sources are public domain, from the US government. They stay in `raw/` and are never committed or shipped.

1. **Social Security Administration national baby names**, one file per birth year: `yob1930.txt` to `yob2024.txt` in `raw/ssa/`. They come in `names.zip` from the SSA's baby names page, `https://www.ssa.gov/oact/babynames/limits.html`. Each line is a name, a sex, and the number of births.
2. **Census Bureau 2010 surnames**: `Names_2010Census.csv` in `raw/census/`. It comes in `names.zip` from `https://www2.census.gov/topics/genealogy/2010surnames/`. Each line is a surname, its rank, and how many people had it.

Both government sites refuse scripted downloads from some networks. Any copy of the same files works. The tables were built from these files:

| File | SHA-256 |
|---|---|
| `Names_2010Census.csv` | `6b4933178060b4e5ac8dd9a611ec15472d35eab98fe9027474bc5ccc5b7a8708` |

Its top 1,000 rows match the Census Bureau's own `Names_2010Census_Top1000.xlsx` in name, rank, and count. The SSA files are the release that includes 2024 births.

## How a name is drawn

1. **Surname.** Each generated household draws one surname by its count. Smith is drawn about 1.7% of the time, and the 2,000th name, Kincaid, about 0.01%.
2. **First name.** Each person draws a first name from the table for their gender and birth decade, by its births. The 2020s table holds births from 2020 to 2024. Someone born before 1930 uses the 1930s table.
3. **Not a Cast name.** A full name a Cast person has, by first name or nickname, is drawn again. A demo that names a Cast person finds one record.
4. **Nickname.** 40% of the people whose first name is in `nicknames.json` go by a nickname, which is their NickName in Rock.
5. **Email.** An adult with an email gets the name they go by and their surname, in letters only, at `example.org`: `bob.obrien@example.org`. The next person with that name gets `bob.obrien2@example.org`, and so on. An address the Cast already has is skipped.

Each draw takes one number from the household's seeded stream, as the old short lists did. So a household's ages, statuses, attendance, and giving stay where they were, and only its names change. Redraws and nicknames come from each person's own stream.

Prayer requests from guests draw from the tables for people born in the 1950s to the 1990s together. A camp registrant's parent who is not in Rock draws from the decade about 30 years before the student.

The surname and first name are drawn independently of each other and of photo ethnicity (content standard 7).

## Spelling

The Census file is all capitals, with apostrophes and spaces taken out. Most names read right as one capital and lowercase letters, and names that start with "Mc" read as "Mc" and a capital, such as McDonald. `build-names.js` lists the top 2,000 names that need their apostrophe, space, or inner capital back, such as O'Brien, De La Cruz, and LeBlanc.

## Rebuilding

```
node build-names.js
node build-names.js --check
```

`--check` builds the tables in memory and fails when the committed files differ. The tables have no random choices, so the same source files always give the same tables.
