# Duplicates

How duplicate person records work in the sample data, and why. The decisions are 310 to 312, 314, 313, and D-crowd-names-4 to D-crowd-names-6 in [Decisions](decisions.md). The Cast's duplicate stories, and the rules for the Crowd's, live in `cast/duplicates.json`.

## The idea

Every church has duplicate records. A visitor fills out a card twice, a check-in kiosk makes a new family, or a woman's name changes and someone makes her a second record. Rock finds them, staff decide about each pair, and someone merges them. The sample data has duplicates in each of those states, so Rock's duplicate pages have something to show on install day.

## What Rock has

| Page | Where | What it shows |
|---|---|---|
| Duplicate Finder | Tools > Reporting > Data Integrity > Duplicate Finder | Each person with a possible duplicate above a confidence of 60, how many, and the best score. |
| Duplicate Detail | A person on the Duplicate Finder | The person's possible duplicates from a confidence of 40, side by side, with Not Duplicate and Ignore buttons. |
| Merge Requests | Tools > Reporting > Data Integrity > Merge Requests | Merges asked for by people who cannot merge, with the requester's note. |
| Merge People | People > Manage > Merge People | The merge itself. |
| History | A person's History tab | A Merged line for each record merged into the person. |

Rock's Calculate Person Duplicates job runs every night at 3 AM. It is a Run SQL job, and its SQL runs one stored procedure, `spCrm_PersonDuplicateFinder`. The procedure scores every pair of people who share an email, a home or mobile number, a home address, or a last name and the first two letters of a first name. A score adds points for each match, and the confidence compares the score with how much the two records could match. Each run resets every score and finds the pairs again, keeps each pair's Not Duplicate and Ignore flags, shows an ignored pair again if its score changes, and deletes pairs that no longer match. Rock 21.0.2 and Rock 20.0.10 have the same procedure, job, and pages.

## How the load fills it

The load runs Rock's procedure itself, as its last step, after every person, phone, and address is in (decision 310). So the Duplicate Finder is full on install day, and the job's first night finds nothing new. The pairs it writes carry the dataset's ForeignKey, like every row the load adds.

| Dataset | Time | Pairs | Listed in the Duplicate Finder |
|---|---|---|---|
| Core 1 | under a second | 9 | 3, all the Cast's |
| Core 3 | about a second | 16 | 3, all the Cast's |
| Full 7 | about 11 seconds | about 14,700 | 112: the Cast's 3 and 109 Crowd duplicates |

Most of Full 7's pairs are different Crowd people who share a last name and the first two letters of a first name, sometimes the whole name. The Crowd's names follow real name data (decision 357), so these pairs are as common as in a real church of that size. They score 37 to 58, below the Duplicate Finder's 60, and Rock flags them the same way in a real database. Every generated email is one person's (decision 313), so no two strangers share an address. The pairs above 60 are the real duplicates.

Households with the same address share one Location, as Rock's address editor saves them (decision 314). Rock scores Address only for people on the same Location, so Jon Smith's duplicates score their shared home.

## The Cast's duplicates

The Cast has two people with duplicate records, a different man with the same name, and a merge already made (decision 311).

| Pair | Decision | Story |
|---|---|---|
| Tom Miller and Thomas Miller | Open | A connection card made the Thomas record after Tom moved. Same email and home phone, different address. |
| Jon Smith and John Smith (mobile) | Open | One of Jon's two John Smith records, with his home phone saved as a mobile. |
| Jon Smith and John Smith (work) | Open | Jon's other John Smith record, with his home phone saved as a work number. |
| The two John Smith records | Ignored | Alisha Marble set them aside from each other, since both belong in Jon's record. |
| The other John Smith and each Jon Smith record | Not a Duplicate | A different man with only the name in common. Alisha marked all three pairs. |

So the Duplicate Finder lists Tom Miller and Jon Smith. Each open pair must score above 60, so it shows. Rock also scores two parents with a child who shares the start of their name, Marco and Mateo Alvarez and Sarah and Sam Lee. They stay as Rock scores them.

A decision is written the way Rock leaves it after a few more nights. Not Duplicate and Ignore save the pair, so Rock records who clicked. The job's later runs rewrite each pair's modified date, but keep the flags and the person, and the pair's created date stays the night the job first found it. So each decided pair names Alisha and was found at 3 AM on the day she decided, 41 days before install.

### The merge request

Jamal Pierce, the groups pastor, found Jon's three records two days before install and tried to merge them. He cannot merge people, so Rock saved his selection as a Person Merge Request with his note and the Notify Me When Merged box. It waits on the Merge Requests page.

Rock's grid saves the selection as an entity set that expires in 5 minutes, and the merge page turns it into a merge request that never expires. The load writes the finished request: the entity set with Rock's Person Merge Request purpose, no expiration, Jamal as its creator, his note, the box in its settings, and one item for each person.

### A merge already made

Sarah Simmons's first record was Sarah Jones, from when she came with Ben. After she married Jim, she got a second record as Sarah Simmons in his family. About two years before install, Alisha merged the old Sarah Jones record into it (decision 312). Rock's merge leaves three things, and the load writes each the way Rock does:

1. **The old alias.** Rock points the old record's alias at Sarah. Its AliasPersonId is the old record's Id, which no person has any more, and its AliasPersonGuid is the old record's Guid.
2. **A Merged History line.** Rock's merge block writes "Sarah Jones [ID: n]" in Sarah's History, by Alisha, with the verb Merge.
3. **A previous name.** Rock's merge procedure saves the old record's last name, Jones, as Sarah's previous last name, since it differs from Simmons.

The merge also deletes the old record's phones and family memberships, so nothing else is left.

## The Crowd's duplicates

About 1% of generated people have a second record (decisions D-crowd-names-4 and D-crowd-names-5). The rules are `crowd` in `cast/duplicates.json`.

A second record is what a connection card, a registration, or a kiosk makes when it does not find someone already in Rock:

1. **Pending.** A Prospect in a family of its own, at the person's campus.
2. **The name they go by.** Bob, when Robert goes by Bob. Half the people who go by their first name but have a usual nickname write the nickname, when they give both an email and a mobile.
3. **No birth date,** address, or marital status.
4. **The email or mobile they gave.** Someone with both gives both half the time, the email alone 30%, and the mobile alone 20%.

Only adults with an email or a mobile get one. A duplicate record takes no part in church life: it never attends, gives, serves, or joins a group or list, and the load gives it no note, tag, step, text, photo, or login (decision D-crowd-names-6). Rock's own jobs and data views still see it.

### How Rock scores them

Rock's confidence works out to 100 times the square root of the score over 29, so a pair needs a score of 11 to pass the Duplicate Finder's 60. A Crowd pair scores these:

| Match | Points |
|---|---|
| Email | 4 |
| Mobile | 4 |
| First name or nickname | 3 |
| Last name | 3 |
| Gender | 1 |
| Campus | 1 |
| Last name and the first two letters of the first name | 1 |

One shared contact and the name the person goes by score 12 or 13, a confidence of 64 or 67. A nickname the person never gave Rock does not match the first name, so that record gives both contacts and scores 13 or 14. In Full 7, every Crowd pair scores 12 to 17.

### What staff did

| State | Share | What Rock shows |
|---|---|---|
| Open | 50% | The Duplicate Finder lists the person. |
| Set aside | 15% | Alisha Marble clicked Ignore on a weekday in the last year. Rock shows the pair again if its score changes. |
| Merged | 35% | Alisha merged the record in the last two years. Its alias is on the person, with the Id just after theirs, since the record was made later, and their History has a Merged line. |

| Dataset | Duplicate records | Open | Set aside | Merged |
|---|---|---|---|---|
| Core 1 | none, since it has no Crowd | | | |
| Core 3 | 1 | 0 | 0 | 1 |
| Full 7 | 249 | 109 | 40 | 100 |

Counts are at the default seed. Core 3's companions are about 225 people, so 1% is about 2, and the draw gave 1.

## How it is checked

1. **validate-cast.js** checks `cast/duplicates.json`: every person is a Cast person with that name, each pair is listed once, a decided pair names a staff member and a date, and a merged record's Guid is no Cast person's.
2. **Cast expectations** (every CLI load and the verify tool) check each pair's decision, that the Duplicate Finder lists each open pair, the merge request, and Sarah's alias, History line, and previous name. A missing pair or a wrong decision fails. They also check that Rock pairs each Crowd duplicate record with its person, listed above 60 or set aside.
3. **Invariants** let two people in different families share an email or a number only as duplicate records of one person: the same last name, and a shared contact or one of them a Pending record from a form.
4. **The verify tool's `person-duplicates` check** runs Rock's own Calculate Person Duplicates job after each load, as its scheduler does, and compares every pair before and after. A pair added, removed, or rescored, or a decision lost, fails. See [Verification](verification.md).
