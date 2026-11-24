# Verification

Every check that proves the sample data is right, in one place. For each check, this page says what it does, why it exists, and how to run it.

## Why we verify

The loader writes straight into Rock's tables with bulk copy. That is what makes an install take seconds instead of minutes. It also means the data never passes through Rock's business logic: no save hooks, no validation, no defaults. If the loader gets something wrong, Rock never says so.

So the proof has to come from somewhere else. We get it from three places:

1. **Rock itself.** Its migrations, its schema, its stored procedures, and its own Rock Cleanup job tell us what correct looks like.
2. **Rules written as checks.** A rule that a script can check is a check, not a sentence in a doc. A reminder gets skipped; a failing check does not.
3. **Receipts.** Each check leaves a record that someone can read later. A package is published only with a receipt that says every check passed (decision 66).

All of this runs when the data is made, never when a church installs it. The one exception is the loader's own short safety check at install (see [When a church installs](#when-a-church-installs)).

## The checks at a glance

| Check | When it runs | What it proves |
|---|---|---|
| [Cast validator](#cast-validator) | Before every pull request | The Cast files are complete and consistent. |
| [Content standards check](#content-standards-check) | Before every pull request | The data follows the content standards a script can check. |
| [Registrations check](#registrations-check) | Before every pull request that changes registrations | Every registration names real events, templates, and groups, makes valid unique slugs, every feature has an always-open home, every registrants rule is one the template allows, releases use only the Lava the loader renders, and data views are filed and replayable. |
| [Reporting check](#reporting-check) | Before every pull request that changes data views or categories | Every data view is filed under a church category, points only at views that exist, never loops, names real step types, and sits under the same lock as any locked view it builds on, every flow audience in Sample Data carries the email guard, and every personalization segment on a locked view is locked the same way. |
| [Adaptive messages check](#adaptive-messages-check) | Before every pull request that changes adaptive messages | Every adaptation names real segments, each message ends with a fallback that has none, and its text has the right fields, lengths, links, and approved images. |
| [Staff updates check](#staff-updates-check) | Before every pull request that changes the staff homepage | Every staff update piece names a real occasion, an approved image, and a staff author with a photo, and fits Rock's homepage. |
| [Package self-check](#package-self-check) | Every time the builder writes a package | A package that would fail to load is never written. |
| [Trusted base](#trusted-base) | Verify tool, before publishing | Every load starts from a clean copy of exactly this Rock release. |
| [Contract schema](#contract-schema) | Verify tool | Everything the loader depends on still exists in this Rock, unchanged. |
| [Contract current](#contract-current) | Verify tool | The contract still describes what the loader really writes. |
| [Load](#load) | Verify tool | The package loads into this Rock. |
| [Cast expectations](#cast-expectations) | Verify tool, and every CLI load | The Cast givers that docs and tests rely on still show what their stories say. |
| [Invariants](#invariants) | Verify tool | Rules Rock relies on hold in the loaded data. |
| [Rock metrics](#rock-metrics) | Verify tool | Rock's own metric SQL, run as of a past run, gives every value the load wrote for Rock's shipped metrics. |
| [Rock Cleanup referee](#rock-cleanup-referee) | Verify tool | Rock's own repair job finds nothing to fix. |
| [Address services](#address-services) | Verify tool | Rock's Location Services Verify job would send no loaded address to an address service, on any night. |
| [Person duplicates](#person-duplicates) | Verify tool | Rock's own Calculate Person Duplicates job finds no new pair and keeps every decision. |
| [Sign-in](#sign-in) | Verify tool | Rock's own Database login accepts the sample logins' password and refuses a wrong one. |
| [Data views](#data-views) | Verify tool | Rock's own engine runs every data view in the load without an error. |
| [Metrics](#metrics) | Verify tool | Each path metric's newest values, by campus, match Rock's count of its stage view. |
| [Access](#access) | Verify tool | Rock's own security check gives every answer in the security walkthroughs, tags included. |
| [Reports](#reports) | Verify tool | Rock reads every report column and renders every Lava column. |
| [Assessment scores](#assessment-scores) | Verify tool | Rock's own scoring gives every saved answer set the answer data and person attribute values the load wrote. |
| [Adaptive messages](#adaptive-messages) | Verify tool | Rock's own adaptivemessage Lava command shows each Cast person the adaptation their story gives them. |
| [Email sections](#email-sections) | Verify tool | Rock's own save rules accept every email section the load wrote. |
| [Staff homepage](#staff-homepage) | Verify tool | Rock's staff homepage shows this week's update from a year of them, with current metric tiles and the church's Quick Links. |
| [Calculate Metrics](#calculate-metrics) | Verify tool | Rock's own Calculate Metrics job, run after the load, changes no loaded value and fills in no past run. |
| [Segments](#segments) | Verify tool | Rock's own segment update finds nothing to add or remove in any personalization segment. |
| [Signals](#signals) | Verify tool | Rock's own top signal rule gives every person with a signal the flag the load wrote. |
| [Personal links](#personal-links) | Verify tool | Rock's own bookmark menu shows each person their sections and links in order, and refreshes a cached empty menu. |
| [Notifications](#notifications) | Verify tool | Rock's own bell counts each note author's unread mentions, and their newest mention opens its note. |
| [Process Reminders](#process-reminders) | Verify tool | Rock's own Process Reminders job changes no reminder and nobody's reminder count, the number Rock's bell shows. |
| [Step workflow launch](#step-workflow-launch) | Verify tool | Rock's own code launches each step workflow as the load wrote it: the same name, assignee, person, and waiting form. |
| [Process Workflows](#process-workflows) | Verify tool | Rock's own Process Workflows job leaves every workflow as it was, apart from the times it processed them. |
| [Family Analytics](#family-analytics) | Verify tool | Rock's own Family Analytics procedures, run on install day, move no family in or out of eRA and change no count, gift date, or eRA value. |
| [Peer network](#peer-network) | Verify tool | Rock's own Calculate Peer Network procedures, run on install day, add, remove, and rescore no peer network row. |
| [Group sync](#group-sync) | Verify tool | Rock's own group sync finds nothing to add or remove, so nothing sends at install. |
| [Benevolence workflow launch](#benevolence-workflow-launch) | Verify tool | Rock's own launch of the Request Started workflow gives each new benevolence request to the case worker. |
| [Group requirements](#group-requirements) | Verify tool | Rock's own Calculate Group Requirements job inserts, deletes, and changes no requirement result. |
| [Data automation](#data-automation) | Verify tool | Rock reads each Data Automation rule as the load saved it, all off, and its own Data Automation job changes no person and no family. |
| [Anchor sweep](#anchor-sweep) | Verify tool, on Core 1 | Every Cast giving and reminder story holds on each of the 60 install days from the run's anchor, not only on the day verify runs. |
| [Unchanged during run](#unchanged-during-run) | Verify tool | The receipt describes the exact bytes and code that were checked. |
| [Publishing gate](#publishing-gate) | Before any upload | Only packages with a passing receipt are published. |
| [Install-time checks](#when-a-church-installs) | Every install | The package is intact, meant for this Rock, and going into a database with no people yet. |
| [Loader compatibility check](#loader-compatibility-check) | When the loader library changes | The .NET Framework build Rock runs computes the same keys and values as the .NET 10 build. |
| [Realism grading](#realism-grading) | When generation changes | The data looks like a real church. |
| [Rock lock check](#rock-lock-check) | Any time, and before a release | The Rock code the loader copies has not changed. |
| [Giving parity test](#giving-parity-test) | Before each release | The loader's giving math matches Rock's own job. |

## Before a pull request

These need only Node.js, so every contributor runs them.

### Cast validator

**What it does.** `node cast/validate-cast.js` reads every Cast household and group file. It checks required fields, Guid format and uniqueness, every cross-reference (members, groups, hosts, relationships, follows, notes), that each relationship type is one the loader maps, stated once for each pair, between people on one campus whose ages fit it (decision 365), that each check-in supervisor in `configuration/checkin.json` has their own PIN and lives on the campus they supervise (decision 367), connection statuses, giving expectations, campaign pledges and gifts, small group rules (a women's or men's group's leader and members are that gender), email domains, and that a celebration is on a request someone has reached and has a connector. Logins must be lowercase letters and digits, unique, never `admin`, and only on adults, and everyone with a Rock security role must have one (decision 179). Every person attribute key in a Cast file must be a Rock person attribute that `configuration/person-attributes.json` lists, a staff attribute, or Date Engaged. Exactly two Cast givers have Do Not Send Giving Statement, and every Cast business contact's Employer is their business (decision 250). In `cast/duplicates.json`, every person is a Cast person with that name, each pair is two people listed once, a decided pair names a staff member and a date, and a merged record's Guid is no Cast person's. It also checks every delivered photo: each is a 400 by 400 JPEG with an original, named for its person, and matching that person's gender. It checks `configuration/tags.json` too (decision 302). Each tag has a unique key and Guid, and a name Rock accepts that is unique for its owner. A tag is personal, with a staff member as its owner, or organizational, in a tag category from `configuration/categories.json`, never both. Everyone a tag names is a Cast person, by Guid and name. A tag's maker, its tagger, and the Cast people it names live on the main campus, so the tag is whole in every dataset. Only an owner may live on another campus, because the tag loads only with its owner. An organizational tag's maker and tagger must have a role that may tag people under its category's security. A draw gives a percent or a count, its campus is the main campus or the owner's, and its data view and connection statuses are real. Every Person Tag filter in a data view names a tag as Rock saves it: 1 for a personal tag or 2 for an organizational one, a bar, and the tag's Guid in lowercase. Every access row names a Cast person, a tag, a Rock action, and an answer.

**Why it's there.** Tests and documentation rely on Cast people by Guid. A broken reference or a reused Guid would break them quietly, long after the change that caused it.

### Content standards check

**What it does.** `node validation/check-standards.js` checks the [content standards](content-standards.md) that span folders:

- **Rule 2.** Every Cast phone is in the reserved range (555-0100 to 555-0199 in 602, 480, or 623, the local codes in `configuration/phones.json`). A number is shared only within a household, as an office line, or between duplicate records. The church's texting numbers in `configuration/communications.json` are reserved too, each is unique, and none is a Cast person's number. Every area code in `configuration/phones.json` is a geographic code, never toll-free or special use, listed once, under a US state or Washington, D.C.
- **Rule 3.** Every email anywhere in the data uses `example.org` or `rocksolidchurchdemo.com`.
- **Rule 5.** The raw data folders hold nothing in git but their README and `.gitignore`.
- **Rule 6.** Calibration sources are named `source-N`, and every source's description is exactly "Calibration source". When it finds a description, it never repeats it, so the check itself cannot leak one.

**Why it's there.** A real phone number, email, or church detail in sample data could reach a real person, because the data ships with Rock and appears in demos and screenshots. [Content standards](content-standards.md#how-each-rule-is-checked) lists which script checks each rule and which rules still need a reviewer.

### Registrations check

**What it does.** `node validation/check-registrations.js` reads `configuration/registrations.json` with the calendar, accounts, campuses, and datasets. Every instance names a real event, template, contact, placement, and group type; its slug and group keys carry the year, month, and campus they need, so no two instances share one, and its slug matches Rock's pattern; every template asks for the fields its grade, age, or gender rule needs; and every registration feature sits on an instance that is open on any install day (decision 190). For the people who register: each registrants rule names exactly one audience (students, kids, couples, adults, or volunteers) with real connection statuses, ages, and shares; students name a Student Path event step or a share; a paid template's payments add up to 100 and are ones the template allows, pay later only with no minimum payment, and a free template has none; its fees, fee weights, per-child fees, codes, and placements are the template's; attribute and person attribute fills fit their attributes; and a discount that needs several registrants comes before an early bird, since Rock gives only the first one valid (decision 207). Each release names a template's `signatureDocument`, and its Lava uses only the four outputs the loader renders and starts with a title for the PDF. Each data view sits in a data view child of the church's category, a registration filter names an instance rule and occurrence that make exactly one instance, and the mission trip's eligibility view counts student service check-ins in exactly the student check-in areas over a "Last N days" window, from the template's lowest grade, which the loader replays from the Student Path (decision 215).

**Why it's there.** Rock checks slugs only in its screens and refuses a registrant it cannot check for eligibility. A broken reference or a feature with no open home would only show up after someone installed the data.

### Reporting check

**What it does.** `node validation/check-reporting.js` reads `configuration/reporting.json` with the categories, security roles, steps, Cast, and the other files that hold data views. Data view keys and Guids are unique across every file. Every view sits in a church data view category, and every category's roles are real. A view points only at views that exist, and no views loop. A steps filter names a real program and matches at least one step type by stage or key. A persisted view names one real schedule or one interval. A view built on a view under a lock, such as Finance, sits under the same lock (the consumer rule, docs/reporting.md section 1.8). Every flow audience in Sample Data includes Reserved or No Email (decision 227). Every access row names a Cast person, a real category or view, and a Rock action. Each personalization segment in `configuration/adaptive-messages.json` has its own key and Guid and a real person data view. A segment on a locked view carries a View rule that allows no more than that lock and denies All Users, because Rock secures a segment only by its own rules. When any segment is locked, the segment Results page carries such a rule too, because Rock's Results page checks no segment's security (decision 273).

**Why it's there.** Rock checks none of this when a view runs: a report or data view built on a locked view is open to anyone who can see its own category (section 1.4 of the design).

### Adaptive messages check

**What it does.** `node content/adaptive-messages/check-messages.js` reads `configuration/adaptive-messages.json` and the text in `content/adaptive-messages/messages.json`. Keys and Guids are unique. Every adaptation names segments the file has. Each message's last adaptation is a fallback with no segments, and no other adaptation lacks them, since Rock shows the first adaptation that fits and one with no segments fits everyone. So everyone in a message's season sees a card from it (decision 343). Every segment is used. Dates rules, saturation, pages, and Cast rows are real. Every adaptation has text and no text is left over. A call to action is 3 to 30 characters, a summary 40 to 160, and each of one to three details paragraphs 20 to 200. A link is a route Rock ships on its External Website or a page in the configuration. An image is an approved calendar event image. Text uses plain characters, no Lava, no full web addresses, and reserved email domains, and no two summaries in one message start with the same five words. When `photos/messages/manifest.json` exists, each of its images belongs to one real adaptation with no event image, and its file is there. It also lists the adaptations that wait for the message image brief.

**Why it's there.** An adaptation with no segments placed early would hide every adaptation after it, and Rock would never say so. A broken link or a missing image only shows on a page after someone installs the data.

### Staff updates check

**What it does.** `node content/internal-news/check-news.js` reads `content/internal-news/updates.json` and `configuration/staff-homepage.json`. Keys are unique. Every dated piece names an occasion the loader dates, or a calendar event, and a week offset from -4 to 12. Every feature shows an approved event image, and every article's author is a staff member with a staff photo. Titles are 5 to 60 characters and paragraphs 20 to 250, in plain characters with no Lava, HTML, or web addresses. There are enough evergreen features and authors to fill a year. The settings have Rock's Guids, an https link, article counts of one to three, and Quick Links that are Rock routes.

**Why it's there.** The loader writes the text into Rock's homepage as it is, so a mistake here shows on every staff member's screen.

## When packages are built

### Package self-check

**What it does.** Before the [builder](../builder/README.md) writes a package, it opens the package the way the loader does and reads every file a load reads. It also records a SHA-256 for every file in `package.json`, and packs the same commit into byte-identical packages. It refuses a dataset manifest without its full [dataset card](../datasets/README.md#the-dataset-card), or with a campus count that differs from the church setup.

**Why it's there.** A package that cannot load should fail on the developer's machine, not on a church's server. Identical bytes mean a receipt can name a package by its hash.

## Before publishing: the verify tool

The [verify tool](../verify/README.md) proves that each package works on the Rock release it was built on and writes a receipt next to it. A package serves every dot release of that Rock's major release (decision 52), so a release builds on the newest dot release, and each new dot release gets a verify run before churches use it (see [Add or change a Rock release](maintaining.md#add-or-change-a-rock-release)). Run it from `verify/` with the same built Rock checkout the packages were built from:

```
dotnet run -- --rock C:\Development\Rock-21
```

Run it against a Rock checkout kept only for verify, at the `ref` in `release/rock-releases.json`, not the one you develop Rock in (see the [verify README](../verify/README.md#running-it)).

It runs the checks below for every dataset. A full run takes about 8 minutes, about 3 of them for Full 7.

### Trusted base

**What it does.** The tool builds an empty Rock database from nothing. First EF's own migration tool applies every Rock migration. Then `verify/rock-runner` runs Rock's own start-up steps without a web server: the hotfix (plugin) migrations, the `RockGetDate` function, the attribute value views, and the message bus. Then it runs Rock's one-time update jobs, which a new install runs on its first evening, including the ones that swap WebForms blocks for Obsidian ones, and Rock's one-time post-install job, which runs on its first night: it fills the date table, the identity verification codes, and the postal codes (decision 170). The tool stamps the database with its Rock version. Before each run it certifies the base: exactly the checkout's EF migrations and hotfix migrations, no update job left waiting, the post-install job's tables filled, the right version stamp, no sample data, and an empty Exception Log. If the base fails, the tool builds a new one (about 9 minutes). Every load runs on a copy, so the base itself is never touched.

**Why it's there.** A finding is only useful if it is about our data, not a stale or dirty database. EF alone is not enough: on Rock 21.0.1 it skips 189 hotfix migrations that every real install has. Patch releases often share their EF migrations and differ only in hotfixes, so both are checked. A failed hotfix logs an exception, so the empty Exception Log proves they all applied. Without the update jobs, the base kept WebForms blocks that every Rock that has run a night has swapped, so a loader bug that only showed on the Obsidian Pledge Entry block passed verify (decision 109).

### Contract schema

**What it does.** `validation/rock-contract.json` lists every Rock table and column the loader writes, the full column list of those tables when it was recorded, the tables it deletes from, the Rock-provided Guids it depends on, and the Rock procedures it runs. This check compares the contract with the trusted base. It fails when a column the loader writes is gone or has a new type, a Guid or procedure is gone, or Rock added a column to a table the loader writes. It also fails if the loader writes `Person.RaceValueId` or `Person.EthnicityValueId` (content standard 8).

**Why it's there.** Because the loader skips Rock's save hooks, a new Rock column can need a value that only Rock's code would have set, and nothing would complain. This check turns that into a failure that names the column, so someone decides whether the loader must fill it.

### Contract current

**What it does.** During each verification load, the loader's database class records every bulk copy and every SQL statement it runs (`loader/database/WriteRecorder.cs`). This check compares the recording with the contract. It fails when the loader writes, deletes, or depends on something the contract does not list. When every dataset is loaded, it also fails when the contract lists something no load used. A write statement the recorder cannot read fails too.

**Why it's there.** The contract is only trustworthy if it cannot drift from the code. Nobody edits it by hand. After a reviewed change, `--record-contract` saves a new one, and its git diff shows exactly what changed.

### Load

**What it does.** Loads the package into a fresh copy of the trusted base, with check images on and Rock's real keys from the checkout's `web.config`, the way a release build does. It loads logins with a test password that Rock's own Database login hashed through `verify/rock-runner`, the way Rock's installer block hashes the one a person types (decision 180). It gives the loader the Rock version from the checkout's `RockWeb\bin\Rock.Version.dll`, as Rock's installer gives its own, and fails first if that differs from the checkout's `Directory.Build.props`, which means `RockWeb\bin` was built from other code.

**Why it's there.** It proves the package installs on this Rock, through the same Rock version check a church's install runs. Every check after it reads the loaded data.

### Cast expectations

**What it does.** Some Cast giving patterns say what Rock should show for that giver: a frequency, a journey stage, or alerts (`expect` in the household file). Some Cast people say what their achievements or steps should show, and Cast households with a part in the building campaign say how their pledge is going (`campaign` in the household file). Each staff texting number in `configuration/communications.json` must be active and assigned to its staff member, and each scripted Cast unsubscribe there must show at its level, with the person Inactive in the list or on "No mass emails". Each Cast text thread in `cast/sms-threads.json` must show its texts and its state: unread, read, or stopped. A staff text counts when it is a reply, which Rock names "From: " and the sender. A class reminder from the same line, such as Starting Point's to a Cast person with a generated class step, shows in the thread too but is not part of its story. Each Cast staff email in `cast/staff-emails.json` must show once, delivered, from the staff member, with its subject, on its day. The church's branded template must pass the Communication Entry Wizard's template query (active, not Legacy, no usage type), and its Legacy version must stay Legacy (decision 269). Each Cast known relationship must show on both people, a kiosk search for the family of each person who may check in a child from another family must find the child, as Rock's search does, and each child a Cast person brings on Sundays must have check-ins that person made (decisions 365 and 366). Each Cast person must show the person attribute values their file holds, such as an allergy, an employer, a legal note, or Do Not Send Giving Statement. Each Cast family with a start or end to its attendance must have no small group membership or request before four weeks after it started coming, and none past six weeks after it stopped (decision 320). Each pin in `configuration/small-groups.json` must hold: the pinned leader leads the group, and each pinned household's adults are in it or have asked to join, as Cast stories, bios, notes, and prayer requests say (decision 321). Each Cast note in `cast/notes/` must be on its person, with a type the person profile's Notes block shows. Each Cast reminder in `configuration/reminders.json` must be on its owner, as a Personal Reminder on its person's primary alias or a Group Reminder on its group, with its note and repeat. It must show due, upcoming, or complete as its story says on the load's install day, so the check takes the load's anchor. Each Cast owner's reminder count, which Rock's bell shows, must be what Rock's Process Reminders job would count, and an owner with a reminder due must have a count above 0. Each step workflow type in `configuration/step-workflows.json` must have one trigger on its step type, saved as Rock's step type editor saves one, on a type that is active, persisted, filed, and keeps its workflows. Every Baptism and Follow Jesus step completed since its trigger was made must have started exactly one workflow, assigned to the campus pastor of the step's campus or the named staff member. When the step was completed in the last 14 days, the workflow must wait at its form in its assignee's My Workflows list. Otherwise its assignee must have completed it. No step may have a Volunteer Care Check-In, because staff start it by hand. Josh Sanders's Baptism Follow-Up must be complete with the answers and note in the file, and Dennis Perry's must be open (decision 293). The 14 days count from the load's install day, so this check takes the load's anchor too. A Cast staff connector with a `minimum` must connect at least that many active requests, and with a `celebrationMinimum`, have celebrations they wrote on at least that many (decisions 178 and 219). Each Cast celebration must be on the person's request for its team, written by the request's connector, and in the Celebrations Report's default view: its connection type has celebrations on, and the request was made in the last 8 months. Each calendar event's `expect` entries in `configuration/calendar.json` must show a start at their campus on the days their Cast story needs. Each refund in a Cast giving pattern's `refunds` list must show: a refund with its kind's reason, made the kind's shortest delay after its gift, for a duplicate a gift with a twin that day, and for a returned check a check (decisions 264 and 288). Each stock gift in a Cast campaign entry must be a non-cash gift of Stocks And Bonds, for its amount, with a description (decision 289). Each Cast registration in a household's `registrations` list must show its members on one registration of the instance, off the wait list, with the registrar, placements, payment state, and discount it expects. A registration whose instance opened only that day is not checked. Each Cast assessment story must show its completed results and pending requests, a story's leaning must come out on top (Bri Hall's gifts that fit a greeter), and each legacy DISC result must show exactly the scores in its household file. Each tag in `configuration/tags.json` whose owner or maker loaded must be an active tag on people, with its owner or its category. Rock's Staff tag must hold exactly the staff the dataset loads, and each Cast person a story tags must carry the tag, such as Hal Russell on Homebound (decision 302). When notes load, each mention in a Cast note from the last 89 days must have left its staff member exactly one Note Mention message that opens the note. It is unread when the note is from the last three days and read otherwise, so Ted Decker has one unread mention and Pete Foster two read ones (decision 308). Each Cast pair in `cast/duplicates.json` must be scored by Rock's duplicate finder with its decision, and an open pair must score above the Duplicate Finder's 60, so the list shows it. Each Cast merge request must be a Person Merge Request that never expires, with its requester, note, and people. Each Cast merge must leave the old record's alias on the person, with no Person row left for the old record, one Merged line in the person's History, and the old last name as a previous name when it differs (decisions 311 and 312). Each Cast eRA story must show its state on the load's install day: a current eRA with its start date, its In Progress eRA step, and its Entered history line, a former one with its end date, its Complete step, and its Exited line, or never an eRA (decision 327). Ted Decker's peer network must show Cindy and everyone in his current groups and teams, rising, and every Cast person who left a group must see its people falling (decision 332). Each Data Integrity story in `configuration/data-integrity.json` must show too: every Cast record's date, no later than its person's first record, each Pending record's form and Record Source, each self-inactivation's reason, notes, and History lines, each connection status change, each Person Data Error and Photo Request workflow, each couple's shared email, each shared phone number, each failed address, the NCOA settings and each Cast move, and each Data Automation rule off (decisions D-integrity-1 to D-integrity-13). This check reads Rock's values in the loaded database and compares them. The loader also checks them on every CLI load; Rock's block skips them (decision 217). See [Validation](../validation/README.md#cast-expectations).

**Why it's there.** Docs, demos, and tests say things like "the Deckers give monthly." A change to generation or to Rock can quietly break that story.

### Invariants

**What it does.** Runs every rule in `validation/invariants.sql` against the loaded dataset. Each rule returns how many rows break it, and must return zero. Rules that a foreign key or unique index already enforces are left out, because the bulk copy checks those. A rule about this dataset's people covers the rows marked with the dataset's ForeignKey and Rock's admin person (Person Id 1), which the load turns into Alisha Marble without that ForeignKey (decision 177).

**Why it's there.** These are the rules Rock's save hooks and screens rely on, which the loader must now keep by itself.

| Rule | Why |
|---|---|
| `dataset-has-people` | Every other rule looks only at this dataset's rows, so a load that wrote nothing would pass them all. |
| `admin-is-a-cast-person` | Signing in as admin signs in as Alisha Marble, who keeps Rock's admin login and Rock Administrators, lives in her Cast family, and has no ForeignKey, so removing sample data can never remove the site's admin (decision 177). |
| `person-has-one-primary-alias` | Rock finds a person through their primary alias, and every audit column and gift points at it. |
| `person-is-in-a-family` | Profile pages and check-in break for a person with no family. |
| `primary-family-is-a-family-the-person-is-in` | Rock's save hook sets the primary family; the loader sets it instead. |
| `primary-campus-is-the-primary-family-campus` | Rock shows and filters people by their primary family's campus. |
| `giving-id-matches-giving-group` | Rock groups gifts by giving Id. A wrong one splits or merges giving units. |
| `giving-leader-is-in-the-giving-unit` | The giving leader receives the statement. |
| `birth-date-parts-agree` | Rock stores a birth date three ways, and reports read all three. |
| `phones-use-the-reserved-range` | Content standard 2, for every loaded phone, generated people too: 555-0100 to 555-0199 in an area code from the package's `configuration/phones.json`. |
| `phones-are-not-shared-across-households` | Each number belongs to one family. People in different families share one only as an office's main line (both work phones), as duplicate records of one person (the same last name and email), or when one record is inactive, a number the phone company gave a new family. Rock turns off texting on every phone with a number that texts STOP, and check-in and the duplicate finder match on the number (decisions 259 and D-integrity-12). |
| `emails-are-not-shared-across-households` | Each email belongs to one person. People in different families share one only as duplicate records of one person: the same last name, and a shared home or phone number. Rock's duplicate finder scores a shared email highly, so strangers with one name and one email filled the Duplicate Finder (decision 313). |
| `emails-use-reserved-domains` | Content standard 3, for every loaded email. |
| `no-race-or-ethnicity` | Content standard 8. |
| `home-addresses-are-mapped` | Group Finder, campus distance, and maps need a point for every home. |
| `loaded-addresses-are-checked` | Every home, business, and campus Location the load wrote carries the marks of a successful check by Rock's address service, and a geocoded one has its map point, so no sample address ever goes to one (decision 315). It covers campuses, which have no street yet, so the address services check cannot see them. |
| `staff-updates-are-complete` | Every staff update has its feature title, text, link, and stored image, one to three articles with a title and content, a global key, and one slug, as Rock's editor and save hook leave them (decision 345). |
| `campus-has-its-photo` | Every campus the dataset has shows its exterior photo on its Location, stored as a Location Image file with its content (decision 317). |
| `nothing-dated-in-the-future` | A future gift, check-in, or step shows up in "upcoming" places. |
| `transaction-has-positive-details` | Rock totals a gift from its details. A refund is the one transaction with negative details. |
| `transaction-is-inside-its-batch` | A gift outside its batch's date range lands in the wrong batch report. |
| `batch-control-amount-is-its-total` | Rock adds each gift, payment, and refund to its batch's control amount, so a batch balances in Rock's batch list. |
| `refund-is-rocks` | A refund has its gift's type, person, gateway, source, and payment method, one of Rock's refund reasons, and a date after the gift and by install day. Rock never refunds a refund or a foreign currency gift (decision 261). |
| `refund-returns-what-was-paid` | A refund's lines are negative and on its gift's own lines, and no account is refunded more than was paid. |
| `refund-batch-holds-only-refunds` | Rock puts a refund in a " - Refund" batch, and nothing else lands there. |
| `refund-is-of-a-gift` | Giving refunds return gifts. Only Registration Follow-ups may refund a registration payment (decision 262). |
| `returned-check-is-a-whole-check` | A refund with Rock's Insufficient Funds or Returned Check reason is a check the bank returned: it returns the whole check and has no gateway or gateway code (decision 288). |
| `non-cash-gift-is-rocks` | An in-kind gift is paid with Non-Cash Asset, has one of Rock's Non-Cash Asset Types and a summary for the statement, no gateway, and no refund. Only a non-cash gift has an asset type (decision 289). |
| `member-role-belongs-to-the-group-type` | Rock allows only roles of the group's type, and the database does not enforce it. |
| `one-current-history-row` | Rock's history tables mark exactly one current row per group, member, and meeting place. |
| `photo-is-a-stored-person-image` | A photo must point at a stored Person Image, or the profile shows a broken image. |
| `only-this-dataset` | One database holds one dataset. Another's rows mean the database was not fresh. |
| `no-high-schooler-in-adult-groups` | Nobody joins or leads a small group or serving team while still in high school: each membership starts after June 1 of the person's graduation year, the day after Rock's last day of grade 12. |
| `step-matches-its-membership` | A Small Group, Serve, or Lead step records one membership, with its dates, and every membership that counts has its step. |
| `step-looks-saved-by-rock` | Rock fills each step's date keys from its dates, and a Complete step has a completion date. Every step is on the person's primary alias and campus. |
| `baptism-step-agrees-with-baptism-date` | The Baptism step and the Baptism Date attribute always agree, and Baptized Here matches on the step and the person. |
| `serve-step-follows-starting-point` | Rock's prerequisite: every Serve step has a Starting Point Class completed before it. |
| `give-step-agrees-with-journey-stage` | A giver Rock shows as Consistent has an open Give step, and an open Give step belongs to a Consistent or Occasional giver. |
| `step-has-its-person-history` | Every step has one "Added" line on its person's History tab, dated the day it started, and a step completed after it started has its status change. Every step history line points at a step of that person. |
| `assessment-row-has-its-state` | Decision 266. A pending assessment has a requester and a request date and no result, a completed one has its completion date, and every completed one has its answers, except a legacy DISC result taken on the person's own. |
| `assessment-results-are-the-persons-attributes` | Decision 265. A person has a type's results exactly when they completed that type, and DISC and Spiritual Gifts keep the latest completion as their save date. The [assessment scores](#assessment-scores) check compares each value. |
| `assessment-retake-waits-its-minimum-days` | A person's completed results of one type are at least the type's MinimumDaysToRetake apart, 365 days for all five. |
| `assessment-only-adults` | Only an adult in their family, 18 or older and out of high school, is asked or takes an assessment. No student or child has one. |
| `assessment-not-after-install-day` | Every assessment was requested, taken, and reminded by install day, and never taken before it was requested. Only a due date can come later. |
| `assessment-reminder-follows-the-job` | Decision 267. A reminder is on a request, at 8 AM, after the day it was requested and before install day, while it was still pending, and no latest pending request is left overdue for Rock's reminder job. |
| `leader-has-disc-and-conflict-profile` | Every current small group and serving team leader who is an adult has a completed DISC and Conflict Profile. |
| `assessment-has-no-workflow` | The Assessment rows fill every screen, so Rock's DISC Request and Request Assessment workflow types stay unused. |
| `reminder-count-is-what-process-reminders-sets` | Decision 298. Rock's bell shows each person's reminder count, and Rock's Process Reminders job sets it to their reminders of an active type that are not complete and are due. A count that differs would change on the job's first run. |
| `reminder-is-as-rocks-blocks-save-it` | Decision 295. A reminder is on its owner's primary alias, about a person's primary alias or a group that exists. Its repeat count starts at 0 and never passes its limit, and only a repeating reminder has a period. A completed reminder with repeats left would have moved to its next date instead. A missing person or group makes the Process Reminders job report an error. |
| `reminder-never-comes-due-during-install-day` | Decision 297. Every reminder that is not complete is due by midnight or dated tomorrow or later, so the count the loader set does not rise when the job next runs. |
| `program-completions-follow-rocks-rule` | Rock's Update Step Program Completions job finds nothing to add or fix after install, in any program the dataset has steps in except Rock's Core Steps, whose eRA steps follow the next two rules. |
| `era-flag-and-step-agree` | Decision 323. Every In Progress eRA step belongs to someone who is Currently an eRA, with an eRA Start Date on or before it and no eRA End Date, and nobody has two. A child in two homes follows each family on their own, as Rock's job does, so only people in one family are held to it. Every Complete eRA step ended the day it was completed. |
| `era-completion-follows-rocks-rule` | Decision 323. A person's first exit from eRA gave them one Core Steps completion, dated from that eRA step, and every eRA step of theirs points at it, as Rock's step save hook and its Update Step Program Completions job leave them. |
| `follows-come-from-staff` | Decision 330. Every follow the load writes is a staff member's, on the sample church's domain, and Rock's Send Following Event Notification job stays off, so if a church turns it on, its notices reach only staff. |
| `student-step-belongs-to-a-student` | Every Student Path step belongs to someone in grades 7 to 12 on its start who had checked in to the student service by then. Mission Trip is high school only, and Student Leader is juniors and seniors only. |
| `student-path-decisions-and-leaders` | Follow Jesus and Student Leader happen at most once per person. Student Leader ends at graduation in May, or stays open while the student is still in school. |
| `student-baptism-follows-the-student-service` | A child's baptism is here, on a Sunday, after they started coming to the student service. |
| `attendance-check-in-status-follows-its-dates` | Every attendance row has the check-in status Rock's save hook gives it: Checked Out with an end time, Present with a present time, and Not Present otherwise. Check-in screens read it to show who is in a room (decision 260). |
| `young-kid-checks-in-with-the-family` | A kid from the nursery to 6th grade checks in only with an adult who was in the service that Sunday: one of their family's, or one whose Known Relationships hold the kid as Can check in or Grandchild, such as Karen Morgan with her Kelly grandchildren. A child whose families have no living, active adult checks in on their own (decisions 146, 366, and 372). |
| `known-relationship-has-its-other-half` | Every known relationship the load wrote has its inverse in the other person's group, the role Rock's InverseRelationship attribute names, as Rock writes both halves (decision 365). |
| `test-schedules-have-no-attendance` | No loaded attendance or occurrence uses Rock's two all-day test schedules, so a tester's check-ins are the only ones there (decision 370). |
| `test-group-in-every-kids-and-student-room` | Every room at every campus that holds a kids or student check-in group holds Rock's Test Group on both test schedules (decision 370). |
| `every-kiosk-offers-the-test-group` | Every check-in kiosk's locations reach a room that holds the Test Group on both test schedules, so any kiosk can test check-in any day, at any time (decision 370). |
| `volunteer-test-area-is-in-the-volunteer-template` | The Volunteer Test Area sits under Rock's Volunteer Check-in template, so a volunteer kiosk can offer its test groups (decision 371). |
| `volunteer-test-group-in-every-serving-room` | Every room a serving team serves in holds its campus's volunteer test group on both test schedules (decision 371). |
| `no-serving-team-room-has-a-test-schedule` | No serving team's room has a test schedule, so Rock's Group Scheduler, Schedule Toolbox, and auto-schedule never offer one (decision 371). |
| `onsite-gift-on-a-family-sunday` | Every gift in a Sunday Offering batch falls on a Sunday an adult of the giver's family came. A student on their own never makes a family Sunday (decision 146). |
| `student-check-in-fits-the-grade` | Every student check-in fits the student's grade that day, so a recent graduate's student check-ins end after June 1 of their graduation year, when Rock moves them out of grade 12 (decisions 148 and 205). |
| `kiosk-and-printer-addresses-are-reserved` | Decision 360. Every check-in kiosk and label printer, Rock's renamed ones included, has its own address in 192.0.2.0/24, so a label never reaches a real printer. |
| `check-in-kiosk-is-at-its-campus` | Decision 360. Every check-in names a check-in kiosk whose rooms are all at the campus of the check-in. |
| `check-in-has-its-session` | Decision 361. Every kiosk check-in points at a session at that kiosk, with its address. A security code and its session go together, and the search found the family of the person who checked in. |
| `student-night-is-for-students` | Decisions 363 and 364. The only weekday check-in is Wednesday Student Night, in the student rooms, on its schedule, by the student at a student kiosk, in a week that ended by install day. |
| `young-kid-is-checked-out-by-who-brought-them` | Decision 362. Kids from the nursery through 6th grade are checked out the same day by the adult who checked them in. Nobody else is checked out. |
| `communication-list-members-allow-mass-email` | Nobody who chose "No mass emails" or "Do not email" is on a communication list (decision 133). |
| `newsletter-members-belong-to-the-campus` | A campus newsletter holds only that campus's adults with an email that is active, or that bounced after they joined. |
| `volunteers-list-follows-the-teams`, `small-group-leaders-list-follows-the-groups` | The Volunteers list is exactly the people active on a serving team, and the Small Group Leaders list exactly the leaders of active small groups. |
| `small-group-gender-topics` | Decision 318. Every member of a Women's Bible Study group is a woman, and every member of a Men's Bible Study group a man, in any state, including a pending request. |
| `lead-a-group-converts` | Decision 322. A load with the Lead a Group flow has at least one person who took the Lead step within its goal's window, because most launch groups are led by someone who was Ready to Lead the day the flow ran. |
| `group-sync-targets-a-sync-group` | Every sync targets a group whose type Rock lets sync, with one of that type's roles and a person data view, so it opens and edits in Rock (decision 186). |
| `group-sync-refreshed-at-install` | Every sync shows a last refresh from the load, as the job leaves it after a run (decision 187). |
| `church-data-view-is-filed` | Every data view the church adds sits in a child of the Rock Solid Church category (decision 185). |
| `data-view-filter-tree-is-whole` | Every church data view has a root filter, every filter belongs to its view, and each single filter names its component and selection, so Rock can read the view. |
| `communication-is-sent-and-never-sends-again`, `no-recipient-waits-to-send` | Decision 95: every communication is Approved with its send time and date key, and no recipient is Pending or Sending, so Rock's send job finds nothing to send. The one scheduled communication is the only exception (decision 248). |
| `scheduled-communication-waits-for-its-date` | Decision 248. At most one communication has no send time. It is an Approved and reviewed bulk email with a future send date and no list, and every recipient is Pending with no send times and an email on example.org or none. |
| `email-recipient-states-agree` | Each email recipient's status agrees with its send, delivery, and open times and its status note. Only a bounce fails after Rock sent it. |
| `opens-and-clicks-follow-an-open` | Every open and click is on the communication's component, points at an opened recipient, comes after their first open, and a click goes to the church's site. |
| `unsubscribe-matches-the-person` | Decision 131: a list unsubscribe leaves the person Inactive in that list, and a Bulk one leaves them on "No mass emails" or stricter. |
| `sent-email-has-its-history` | Every sent email recipient has Rock's "Sent" line on their History. |
| `sent-text-is-delivered-with-its-text`, `sent-text-has-its-history` | SMS Conversations shows a text only when it is Delivered and reads its words from SentMessage, and Rock writes a "SMS message" History line for each text it sends. A text Rock could not send failed with its note. |
| `open-flow-run-is-allowed` | Decision 220. A flow run still sending at install is in an active On-Demand flow, its next message is due today or later, and every message the load wrote went out before install day. |
| `open-flow-run-reaches-only-reserved-contact-points` | Decision 220. Everyone still Active in an open run has an email on example.org or none, and only reserved 555-01xx phones, because Rock's job sends their remaining messages after install. |
| `flow-form-entry-has-its-workflow` | Decision 235. Everyone in a run of a flow a workflow type activates started that workflow themselves, and it finished on the run's start date. |
| `workflow-is-finished-as-rock-leaves-it` | Decision 235. Each sample workflow a person started from a form is Completed by the person who started it, with one completed activity, every action completed, and the person in its Person attribute. A workflow a step started has its own rules, below (decision 294). |
| `step-workflow-is-as-rock-launches-it` | Decision 292. When a step is completed, Rock launches each Step Completed trigger's workflow with the step as its entity and nobody as its initiator. One StepWorkflow row links the step, the trigger, and the workflow. The workflow is the trigger's type, its step is of the trigger's step type and complete, and it started when the step was completed or later. |
| `completed-step-has-its-workflow` | Decision 292. Rock launches a Step Completed trigger's workflow for every step of its type completed after the trigger was made, so each such step has one. |
| `step-workflow-waits-at-its-form-or-is-complete` | Decision 292. A step workflow has one activity, assigned to a person. An open one waits at its form: every action before the form is complete, the form is processed but not complete, and nothing after it has run, so Rock's My Workflows list shows it. A completed one has every action complete and its form submitted with a button, by its assignee. |
| `step-workflow-type-keeps-its-workflows` | Decision 294. Rock Cleanup completes a workflow older than its type's age limit, and deletes completed workflows and logs after their retention periods. A form email would send when Process Workflows first runs the form. So every workflow type a step triggers is active, persisted, and filed, with none of those limits and no form email, and each trigger is saved as Rock's step type editor saves one. |
| `recurring-flow-audience-has-the-guard` | Decision 220. Every recurring flow has a schedule and an audience view that includes Reserved or No Email, so no future run can reach a real address. |
| `attribute-value-has-rock-derived-columns` | Decision 249. Every attribute value a load wrote has the columns Rock's save hook derives from Value, which attribute filters read: the yes-no answer for a value of 1 to 5 characters, the number whenever SQL Server reads Value as one, the date whenever Value is an ISO date, and the person behind a Person attribute. The seven values Rock's own Family Analytics procedures write keep the columns those procedures set (decision 326). |
| `one-time-flow-is-finished` | Decision 247. A One-Time flow has a single-date schedule, an audience view, and one run on that date, before install, with messaging and goal tracking done, and its own messaging closed, so Rock shows it Completed and its job never reads it. |
| `flow-send-reaches-its-instance` | Each flow email, text, or push is a bulk send from its message's flow template, to people in the run: everyone still Active when the exit rule is the last message, and never anyone who left before it, by an exit rule or by unsubscribing. |
| `flow-unsubscribe-leaves-the-flow` | Decision 246. A person who leaves a flow from an email's link has that run's row marked Unsubscribed with the recipient it came from, and is Inactive in every later run of the flow. |
| `sent-push-is-delivered-with-its-history` | Rock Mobile Push marks a sent push Delivered with its times and writes a "Push Notification" History line, and a person with no device that takes notifications fails with Rock's note. |
| `push-goes-to-a-device-that-takes-notifications` | A delivered push had an active device with notifications on and a token when it went out, and a failed one had none. |
| `app-devices-never-reach-a-phone` | Decision 95. Every device token starts with `sample-data-`, which Firebase never issues. |
| `serving-request-is-not-in-the-future` | A request to serve, its answer, and its last change all happened by install day. |
| `serving-flag-has-its-email` | Before install day, each confirmation or reminder marked sent has its email, and only volunteers who confirmed were reminded. |
| `serving-job-has-nothing-to-send` | Decision 95. Every request for a coming Sunday to a volunteer Rock can email is marked confirmed and reminded, so Rock's scheduling job sends nothing. |
| `serving-email-follows-a-request` | Each serving confirmation and reminder links to Rock's system communication, has one recipient, and has the request it came from: asked that day, or serving two days later and not declined. |
| `one-to-one-email-skips-do-not-email` | Rock sends a personal email to someone on "No mass emails", but never to someone on "Do not email". |
| `texted-stop-turns-off-texting` | A STOP marks a level All unsubscribe and turns off and opts out texting on the person's mobile, and every opted-out phone came from a STOP. |
| `text-reply-comes-from-a-known-phone` | Every text a person sent comes from their own mobile to an active church number, so SMS Conversations finds them. |
| `system-phone-numbers-are-reserved-and-unused` | The church's texting numbers are active, can text, have forwarding off, use the reserved range, and are no loaded person's number (decision 93). |
| `dataset-has-logins` | Verify always loads with a test password, so a load with no logins means logins broke (decision 179). |
| `login-is-a-database-login-rock-can-check` | Every login is a Database login with a BCrypt hash Rock reads, on a person, never locked out or sent to change its password, and last used only after it was made and only when confirmed (decision 180). The one other kind is a check-in supervisor's PIN Authentication login: its user name is the PIN, in digits, with no password (decision 367). |
| `login-username-is-plain` | Usernames are lowercase letters and digits only, and never Rock's own `admin`. |
| `login-belongs-to-an-adult` | Only adults 18 and older have logins, never a child or a business. |
| `background-check-requirement-agrees-with-check` | Decision 333. Each Background Check Required result agrees with the person's check on the night it was last checked: Meets when Rock's still-valid view holds, Meets With Warning in a Pass's last 60 days for a person 18 or older by the birth date Rock has, and Not Met otherwise, with the dates Rock's job sets. |
| `finished-background-check-has-report` | Decision 336. Every finished check has its report: a PDF in Rock's Background Check file type, with its bytes, linked as the response document. A check in progress has none. |
| `background-check-agrees-with-person` | Decision 336. A person's Background Checked, date, and result are what Checkr's workflow set from their latest finished check, and every check is one Rock's Checkr provider wrote. |
| `background-check-in-progress-waits-on-its-request` | Decision 333. A check in progress belongs to an active request in the Background Check status, and a finished check linked to a request finished after the request. |
| `benevolence-request-is-worked-by-a-case-worker` | Decision 350. Every request's case worker is an active member of RSR - Benevolence, the role Rock's case worker list reads. |
| `benevolence-request-matches-its-requester` | Decision 350. Each request holds what Rock's block copies from the requester: name, email, connection status, campus, and home location, with its date key, and no government ID. |
| `benevolence-results-follow-the-status` | Decision 352. Pending requests have no result or summary, Approved ones have a result, Denied ones give nothing with an amount, and every amount is more than zero and saved after the request. |
| `benevolence-document-is-a-stored-pdf` | Decision 353. Each request document is a PDF with its bytes in Rock's Benevolence Request Documents file type, on an Approved request. |
| `benevolence-trigger-sends-no-email` | Decision 355. Each church type has one Request Started trigger, saved as Rock saves it, to an active workflow that is not persisted and has no action that sends an email, a text, or a communication. |
| `benevolence-type-is-locked` | Decision 351. Every benevolence type allows View and Edit only to RSR - Benevolence and RSR - Rock Administration. |
| `security-role-member-can-sign-in` | Everyone the load puts in a security role can sign in to see what the role allows (decision 79). |
| `app-user-can-sign-in` | A church app device tied to a person means they signed in to the app. |
| `login-has-its-history` | Every login has the "User Login" line Rock's save hook writes on the person's History tab (decision 181). |
| `account-protection-matches-rock` | Every loaded person's account protection profile is what Rock's Process Elevated Security job would set (decision 182). Rock Cleanup does not set it, so the referee cannot catch it. |
| `registration-linkage-points-at-a-listed-occurrence`, `registration-linkage-campus-matches-occurrence`, `registration-linkage-instance-and-template-are-active` | A registration shows on its event page only through a linkage to an occurrence of an active, approved event on a calendar, with an active instance and template, at the occurrence's campus (decision 189). |
| `registration-slug-matches-rocks-pattern`, `registration-slug-is-unique` | The event page finds a registration by its slug. Rock checks the pattern and uniqueness only in its screens. |
| `registration-dates-are-in-order` | An instance opens before it closes, and its event comes after it closes and on or after its payment deadline. |
| `placement-group-matches-its-placement`, `linked-group-is-an-event-attendees-group` | Rock places people only in groups of the placement's type and adds registrants to a linked group of the template's type. |
| `registration-category-has-the-admin-role` | Rock's registration admin role can manage templates in the new categories, as Rock's v19 rollup set for older ones. |
| `seven-registrations-are-open-today` | Seven registrations are open on every install day, each linked to its occurrence, so every feature can be tried (decision 190). |
| `registrant-template-matches` | A registrant, its registration, and the registration's instance name one template, whose forms, fees, and attributes Rock reads. |
| `registrant-is-in-one-instance-per-event` | Nobody is a registrant in two instances of one template with the same dates, such as VBS at two campuses the same week. A Cast family registers only at its own campus. |
| `registrant-required-answers-are-filled` | Every registrant answers each required registrant question its form shows, as Rock's form requires before it saves. |
| `registrant-is-in-the-grades` | Rock checks a registrant's grade on the day they register, and the event is for those grades, so every registrant is in the template's grades on both days (decision 207). |
| `registration-is-inside-its-window` | A registration is made while its instance is open and before the install day, with the date key Rock writes. |
| `registrants-fit-the-limit-and-join-the-group`, `wait-list-only-when-full` | No more registrants than the limit are off the wait list, each of them is in the linked group with the template's role and status, and a wait list exists only on a full instance. |
| `placed-person-is-a-registrant` | Everyone in a placement group is a registrant, off the wait list, of an instance linked to that group. |
| `registration-payment-is-rocks`, `registration-is-never-overpaid` | A registration payment is an Event Registration transaction from the registrar, on the instance's account, pointing at the registration. Nobody pays more than Rock's discounted cost, and once the payment deadline passes every balance is paid (decision 208). |
| `payment-plan-fits-its-template`, `discount-code-within-its-uses` | A payment plan uses a frequency the template allows, points at its registration, and ends by the payment deadline. A code is used no more times than its limit. |
| `free-registration-has-no-payments` | A free template takes no payment and makes no payment plan. |
| `registrant-fee-matches-its-item`, `quantity-fee-is-in-the-balance` | A fee carries its item's cost, and a quantity above one only on a fee that allows it, such as Date Night childcare per child. A registration with a quantity fee on a template with no minimum payment paid exactly its cost, quantity included (decision 210). |
| `fee-item-within-its-usage-limit`, `required-fee-is-chosen` | No instance uses a fee item, such as a single room, more times than its limit, and every registrant off the wait list picks each required fee. |
| `registrants-share-a-family-when-the-template-says-so`, `date-night-registers-a-married-couple` | Everyone on a registration of a same-family template shares a family, and each Date Night registration is one married couple. |
| `registrant-meets-the-age-and-gender-rules` | Every registrant meets the template's ages and gender on the day they registered and on the event day, so VBS kids are 4 to 11 and Women's Retreat guests are women. |
| `closed-instance-registrants-are-placed`, `vbs-crew-matches-the-grade`, `together-placement-keeps-a-registration-together` | Once registration closes, everyone is placed: last summer's campers, VBS kids in the crew for their grade, and Serve Day volunteers on one team per registration. |
| `allergy-and-legal-notes-are-on-kids` | Kids check-in prints Allergy and shows Legal Notes, so they are only on a child in a family, or, for Allergy, on a registrant whose form asks for it. No child gets a stray note on their label (decisions 211, 251, and 254). |
| `person-attribute-value-points-at-a-person` | A person attribute value finds its person by an Id with no foreign key, so every value the load wrote is on a person in the dataset (decision 250). |
| `excluded-person-attributes-stay-empty` | Nothing writes Membership Date, social media, a driver's license number, Last DISC Request Date, or Special Needs (decision 250). Assessment results come only from the assessments step, and the assessment checks cover them (decision 265). First Visit and Second Visit come only from the visit dates step (decision D-integrity-11). |
| `adult-attributes-are-on-adults` | Employer, Position, Source of Visit, Previous Church, Date Engaged, Envelope Number, and Do Not Send Giving Statement are only on adult people, never a child or a business. |
| `defined-value-attribute-names-a-value-of-its-type` | School, Ability Level, and Source of Visit each name an active value of the attribute's own defined type, or Rock shows nothing. |
| `school-is-on-kids-and-students` | School is only on a kid or student in kindergarten to grade 12 the day it was saved, and names a school for their grade band (decision 252). |
| `ability-level-fits-the-check-in-room` | Rock's nursery and preschool groups filter by ability level, so a kid's Ability Level is the one of the group their latest check-in was in, and check-in keeps putting them in the room they already use (decision 255). |
| `date-engaged-is-the-first-group-team-or-gift` | Every Engaged or Leading adult, and no one else, has Date Engaged: their first small group or serving team membership or their giving unit's first contribution, or a day before the history window when they have none (decision 257). |
| `envelope-number-is-unique-for-a-check-writer` | Rock finds a giver by envelope number, so each number belongs to one person, and only to a person who wrote a check gift (decision 258). |
| `business-contact-works-at-the-business` | A business contact owns or runs the business, so their Employer is the business (decisions 63 to 65 and 253). |
| `last-camp-matches-camp-steps` | When the steps part loaded, the campers at every camp that has ended are exactly the people with a Student Camp step there (decision 206). |
| `release-is-signed-for-every-registrant` | On a template that requires a release, saved with Rock's Embed action and no external updates, every registrant links to a signed release from that template for that person. None is pending (decision 212). |
| `signed-release-is-whole` | Each signed release has its PDF in the release's file type, the registrar as signer and the one it is assigned to, a typed name, a reserved email and IP address, a signed time just before the registration on the same day, and a verification hash that matches when recomputed in SQL the way Rock computes it. |
| `registration-has-its-notes-and-history` | Every registration has Rock's "Added Registration" history by its registrar, and each registrant a note of Rock's Event Registration type and a "Registered" history line tied to the registration (decision 213). |
| `registration-data-view-is-filed-under-registrations` | A data view that filters on a registration instance or is a template's eligibility rule sits in the church's Registrations category. |
| `registrant-is-in-the-eligibility-data-view` | Every registrant of a template with an eligibility data view was in it on the day they registered: its grade filter and its check-in count over its window, replayed in SQL. A load without attendance skips it (decision 215). |
| `celebration-is-on-a-worked-request` | A celebration is a Celebration Note the request's connector wrote after reaching the person, on a request whose connection type turns celebrations on, with text, and only one per request, since the Connections Hub shows only the first (decision 219). |
| `top-signal-is-own-signal`, `signal-without-end-sets-top-signal` | A person's top signal, which the Top Person Signal badge draws, is one of their own signals with its type's color and icon, and a signal that never ends sets one (issue 45). Whether it is the right open one depends on the day, so the [signals check](#signals) asks Rock. |
| `tag-is-saved-like-rock` | Decision 302. Rock's tag bar and Tag Report find a tag on people by Rock's Person entity type, with no qualifier, while it is active. A personal tag belongs to the owner who made it and has no category. An organizational tag sits in a tag category, whose security decides who can see and use it. |
| `tag-name-is-unique-for-its-owner` | Rock finds a tag by its name and owner, so two tags on people with one name and owner would send the tag bar to either one. |
| `tagged-item-is-on-a-dataset-person` | A tagged item finds its person by Guid, with no foreign key. So each one is on a person in this dataset, once for each tag, as Rock's tag bar adds it. |
| `tagged-item-is-made-by-someone-who-may-tag` | Rock lets only a personal tag's owner use it, and records who tagged each person. Each person was tagged after the tag was made and before install day. |
| `mention-message-is-rocks` | Decision 307. Each message has every part Rock's Note Mention component saves, from its type, title, and text to the data its link reads, and never goes to the note's author. The bell counts it and opens the note only when every part is Rock's. |
| `mention-message-is-read-by-its-date` | A mention from a note in the last three days waits unread in the bell. The person has opened every older one, which Rock's notification list marks read. |
| `recent-mention-has-one-message` | Every note from the last 89 days that mentions a staff member left them exactly one message, so no mention is missing from the bell. No message is older, so none expires on install day, when Rock Cleanup would delete it. |
| `profile-workflow-is-as-rock-leaves-it` | A workflow staff start from a person's profile, such as Person Data Error or Photo Request, has that person as its entity and in its Person attribute. A completed one has every activity completed with it, and an open one waits on exactly one assigned activity, so it shows in My Workflows (decisions D-integrity-5 and D-integrity-6). |
| `photo-request-reply-has-a-photo` | Verify Photos shows each Pending or Active member's photo, so a reply has one (decision D-integrity-6). |
| `self-inactivated-record-has-its-note` | A Self Inactivated record is inactive with its reason, both notes, and when its status changed, as Rock's Email Preferences page leaves it (decision D-integrity-3). |
| `data-automation-rules-are-off` | Every Data Automation rule the load saves is off, so Rock's job changes nobody (decision D-integrity-7). |
| `person-has-a-record-source` | Every record shows where it came from (decision D-integrity-10). Rock's admin person keeps the record Rock made (decision 177). |
| `person-is-made-before-their-first-record` | No record is made after its first check-in, group membership, gift, request, registration, note, login, workflow, or demographic History line, since Rock writes those only once the record exists (decisions D-integrity-1 and D-integrity-10). |
| `ncoa-move-is-processed-as-rock-does` | Every NCOA History row is Complete or waits for a staff member. A processed move gave the family a new Home, the mailing address, at the row's address, and kept the old home as Previous. A move left for a staff member changed nothing (decision D-integrity-13). |
| `ncoa-move-away-is-inactivated` | A family the NCOA run moved at least the saved minimum distance is inactive with the run's reason and Rock's note, and has no group membership, open connection request, or communication flow that Rock's person save hook would have inactivated (decision D-integrity-13). |
| `visit-dates-are-as-rock-writes-them` | First Visit and Second Visit are system values with the date in Value and ValueAsDateTime, never empty, since Rock Cleanup deletes the empty First Visit the job writes for an adult whose family has no child's check-in, and a Second Visit comes with a First Visit (decision D-integrity-11). |
| `people-match-the-dataset-card`, `households-match-the-dataset-card` | Rock's installer shows the people and households from the dataset card in `datasets/{key}.json`. A default-seed load more than 2% away from either means the card is stale (decision 124). |

To add a rule, add a `-- check:` block with a `-- why:` line that returns how many rows break it.

### Rock metrics

**What it does.** Checks the history of the metrics Rock ships, from `configuration/rock-metrics.json`. For each SQL metric's newest run, and its newest run at least 90 days before that, it sets the loaded database back to that moment inside a transaction it rolls back: check-ins, prayer requests, prayers, and gifts dated after it are hidden from Rock's queries, the people are as they were then, by the loader's rule for who was here and how old they were, and so are their eRA values. Rock's eRA job writes a history line each time it moves a person in or out of eRA, so the lines give each person's Currently an eRA, eRA Start Date, and eRA End Date at any moment. Then it runs the metric's SQL from Rock's Metric table, with Rock's clock set to the run, and every value must match the load, campus by campus. It also checks that every typed Total Adult Attendance value is the people the load has in that service, and that each such service has a Salvations value. Today's eRA values must be the ones the history lines give, so setting them back rests on Rock's own record. It runs before the [Rock Cleanup referee](#rock-cleanup-referee), so it judges the database as loaded: Rock Cleanup's first-night fixes to Rock's own records, such as a primary campus or an age bracket, would move Rock's Insights counts by a person.

**Why it's there.** The loader works out each metric's past values itself, because running Rock's SQL once per run would be slow on Full 7. Rock's own SQL is the proof that each value is the one Rock's job would have saved, and setting the database back makes it work on any day, not only a metric's run day (decision 381). Without the eRA values set back, a person who left eRA, or came back, after a run would drop out of Rock's eRA SQL for that run. On Full 7, a run a quarter back lost up to 8 of a campus's 20 wins that way.

### Rock Cleanup referee

**What it does.** Rock's nightly Rock Cleanup job repairs what Rock's save hooks would have set: aliases, ages, salutations, search keys, and more. After a load, the tool takes a SQL Server snapshot, runs Rock's real job through `verify/rock-runner` (with Rock's message bus started first, as in a running Rock, so the job's cache messages never log bus errors), and compares every table with the snapshot, column by column, as bytes. It runs the same job on a plain copy of the trusted base too. Both databases start from the same base, so base rows have the same Ids in both. A change Rock makes to exactly the same base rows on the plain copy is background. A change to any row the load added is a finding. A change accepted on purpose goes in `validation/cleanup-accepted.json` with a reason. Two of the job's tasks need a web server, so they are noted, not failed. Any other task error fails.

**Why it's there.** This is the most direct answer to "we skip Rock's business logic." Rock itself tells us what it would have done differently. On its first runs it found real loader gaps: primary aliases, known-relationship groups, search keys, sound-alike name entries, family salutations, and inactive dates. The [roadmap](roadmap.md) lists them.

**The moving window.** Rock Cleanup rebuilds `ScheduleDate` from now minus two years to now plus two years, to the minute. The plain copy and each loaded copy are cleaned minutes apart, so the tool leaves out the rows within a day of either edge, in both runs, and compares the rest exactly. Before, an occurrence at an edge sometimes fell inside one run and outside the other, and rock-cleanup failed on base rows the load never touched (11,080 against 11,079 changed rows).

**The clock.** The loader sets each event occurrence's next start from the clock at load time, the way Rock's save hook does, and Rock Cleanup sets it again from the clock when it runs, minutes later. An occurrence whose next start passes in between moves to its following start, which once failed rock-cleanup (3 rows on a Core 3 run). So a change to `EventItemOccurrence.NextStartDateTime` is not judged when the old value had already passed when Rock Cleanup started, and Rock only moved it later or cleared it. The tool reports those rows as "Not judged". Any other change to the column, such as a next start the loader set wrong, still fails.

**When it is not judged.** Rock Cleanup recomputes ages and daily counts for today, from SQL Server's `GETDATE`. So the tool judges it only while the database server and the machine running Rock are both on the loads' anchor, checked before and after the job (decision 152). Otherwise the check fails with both dates and says to run again. A run that crosses midnight, or an `--anchor` that is not today, lands here instead of reporting false age changes.

### Address services

**What it does.** After the referee, the tool runs the selection of Rock's Location Services Verify job (`Rock/Jobs/LocationServicesVerify.cs`) against the loaded database: active Locations with a street that are not locked and lack a standardized or geocoded date. It sets the job's retry date in the far future, so it judges every later night, not only tonight. It fails if the selection picks any Location the load wrote: this dataset's rows and every campus's Location. It never runs the job, so verify never calls an address service. Rock's own addresses that every new Rock already sends, such as its Organization Address, are noted, not failed. The selection is the same in Rock 20.0.10 and Rock 21.0.2, and `validation/rock-lock.json` pins it (decision 316).

**Why it's there.** Rock ships with its Smarty Streets service on, and the job sends up to 1,000 unchecked addresses a night. Sample addresses never go to an address service, so every loaded address must carry the marks of a successful check (decision 315). An address with only an attempted date would pass a check of tonight alone, then go out when the job's 200-day retry period ends. The two Cast homes that failed verification on purpose, the Ellises' and the Ramirezes', are locked, as a staff member locks one with Location Locked, so the job and this check skip them (decision D-integrity-4).

### Person duplicates

**What it does.** After the referee, `verify/rock-runner` runs Rock's own Calculate Person Duplicates job once, as its own stored job, the way Rock's scheduler runs it at 3 AM: a Run SQL job that runs Rock's duplicate finder procedure with the timeout its settings hold. The tool reads every duplicate pair before and after, with its people, scores, confidence, Not Duplicate and Ignore flags, created date, last modifier, and Guid. Any pair added, removed, or changed fails. Each run rewrites every pair's modified date, so that column alone is not compared (decision 310).

**Why it's there.** The load runs the same procedure as its last step, so the Duplicate Finder is full on install day. If a later step changed a person, phone, or address after the procedure ran, or a Cast decision did not stick, the job's first night would rescore pairs or show a set-aside pair again. This check proves the job finds nothing new.

### Sign-in

**What it does.** After the referee, `verify/rock-runner` signs in through Rock's own Database login, the check Rock's login block makes: the login exists, is confirmed, and Rock accepts the password. It signs in as Ted Decker (`tdecker`) and as the load's newest confirmed Database login with the test password, and as Ted with a wrong password, which Rock must refuse (decision 180). A check-in supervisor's PIN is a PIN Authentication login that a kiosk checks, so it is never one of these (decision 367). It runs after the referee, because a sign-in can rewrite a hash at the site's cost setting.

**Why it's there.** The loader writes the hash with SQL, so only Rock itself can prove the hash is one it accepts, and that a person installing could sign in as the Cast.

### Adaptive messages

**What it does.** `verify/rock-runner` renders Rock's own `{% adaptivemessage %}` Lava command, with the command enabled and views not tracked, for each Cast row in `configuration/adaptive-messages.json`: the person and the message. Rock finds the person's segments in the database, as it does on a page with no segment cookie, and shows the first adaptation that fits. Each must be the row's adaptation, with its call to action. Rock's answer for Ted Decker, Jordan Ellis, Margaret Carter, and the other Giving layer stories proves each lands in its layer by Rock's own rule. Then it renders the External Website homepage block for each of them, as the block renders it for the signed-in person, and the page must show the row's call to action. A row is judged only on an install day its card can show: in its season, such as Baptism Sunday's, and not cut from the homepage's four cards, as Student Camp is in baptism season. On other days the check notes the row and skips it, and so do the Cast expectations (decisions 343 and 344).

**Why it's there.** The loader writes the segments, adaptations, and attribute values with SQL. Only Rock's own Lava engine proves a page shows each person what the design says.

### Email sections

**What it does.** `verify/rock-runner` loads every email section with Rock's own EmailSectionService and checks it the way Rock saves one from its email editor: the model's own validation, such as a name of at most 100 characters, then the string validation Rock's editor runs on a section's markup, with Rock's Lava and basic HTML rules. Each section must pass, and every section in `content/communications/email-sections.json` must be there (decision 301).

**Why it's there.** The loader writes a section's markup with SQL, and Rock never checks it then. A section Rock would refuse to save could not be edited and saved again in the editor. Only Rock's own rules prove a staff member can change it.
### Staff homepage

**What it does.** Checks Rock's staff homepage (decision 349). The load wrote an update on each of the configured 52 Mondays in a row. The item Rock's Internal Communication View block picks, the newest approved one whose start has passed, is this week's update. `verify/rock-runner` renders it with the block's own Body Template through Rock's Lava engine, and the page must show its feature title and image and each article. Rock 21's article list builds its image links from the web request, which the runner does not have, so on Rock 21 the articles are noted and the `staff-updates-are-complete` invariant checks them. Each homepage metric's newest value must be its last run, from this week. A daily metric's newest value must equal Rock's own SQL when that run was today, and no campus's Active Families value may be above Rock's count today, or zero where Rock's is not. The block must list the Ministry Path metrics, and the Quick Links must hold the church's links.

**Why it's there.** The loader writes the items, matrices, and metric values with SQL. Only Rock's own Lava engine and Rock's own metric SQL prove the homepage a staff member opens is full and current.

### Calculate Metrics

**What it does.** Runs Rock's own Calculate Metrics job once, through `verify/rock-runner`, as Rock's scheduler runs it every 15 minutes after install. Every value the load wrote must still be there, unchanged, and any value the job adds must be for a run after its metric's last run. The values it adds are noted.

**Why it's there.** Each metric's last run tells Rock's job where to start. One left too early would make the job's first run after install save copies of past runs, and one left too late would skip a run (decision 381).

### Segments

**What it does.** `verify/rock-runner` runs Rock's own segment update (`PersonalizationSegmentService.UpdatePersonAliasPersonalizationDataForSegment`, what the Update Personalization Data job calls) on every personalization segment, whatever its schedule. Rock evaluates each segment's data view with its real filters and compares it with the segment's people. The check passes when Rock updates every segment the load wrote and adds and removes nobody. Like group sync, it is judged only when today is the loads' anchor.

**Why it's there.** The loader finds each segment's people with its own data view evaluator, which follows Rock's filters in SQL (decision 271). Only Rock's own update proves the loader picked exactly the people Rock picks.

### Signals

**What it does.** `verify/rock-runner` runs Rock's own `Person.CalculateSignals`, the method Rock's signal list and its nightly Calculate Person Signals job run, for everyone with a signal, and saves nothing. It passes when Rock would change no one's top signal: its Id, color, and icon. A signal's end counts from now, so like segments it is judged only while today is the loads' anchor.

**Why it's there.** The Top Person Signal badge and Rock's signal report column draw from three columns on the person, which Rock sets only when a signal is saved or its job runs. The loader writes them itself (issue 45), so only Rock's own rule proves each person shows the right flag, and that an ended signal shows none.

### Personal links

**What it does.** `verify/rock-runner` asks Rock for each bookmark menu in `menus` in `configuration/personal-links.json`, with `PersonalLinkService.GetPersonalLinksData`, the call Rock's bookmark block makes. Each person's sections must be the listed ones, in order, each with its links from the file. For each person who owns a section, Rock's `AddMissingPersonalLinkSectionOrders` must find no section order missing. Rock's version of the shared links, and each owner's version of their own, must not be midnight today.

**Why it's there.** Rock checks who sees a section, orders sections by each person's own orders, and caches the menu in the browser by a version built from the rows' ModifiedDateTime. Midnight today is the version of a database with no links, so a browser that showed an empty menu earlier that day would keep it (issue 46). Only Rock's own code proves the menu opens filled.

### Notifications

**What it does.** `verify/rock-runner` asks Rock what each note author's top-bar bell shows (decision 309). For each staff member who writes notes, Rock's own unread count (`NotificationMessageService.GetUnreadMessagesForPerson`, which the bell reads) must equal their mentions in the last three days' notes. The tool finds those mentions from the notes themselves, not from the messages. Then Rock's Note Mention component builds the link for each person's newest mention, and it must open the note on the person's profile. Like segments and group sync, it is judged only when today is the loads' anchor, because the bell counts from now. A load that wrote no mention messages, such as one without the notes part, has nothing to check.

**Why it's there.** The loader writes the messages with SQL (decision 307). Only Rock's own code proves the bell counts them and each one opens the right note.

### Process Reminders

**What it does.** `verify/rock-runner` runs Rock's own Process Reminders job once, as its stored job with the settings Rock's scheduler gives it (`--task process-reminders`). Before and after, the tool reads every reminder's due date, completion, repeat counts, and last change, and every person's reminder count. The check fails when the job changes any of them, or reports any error other than a failed email. The job emails each owner their reminders due, and the trusted base has no mail server, so failed sends are noted, not failed. A load that wrote no reminders passes. Like segments, it is judged only when today is the loads' anchor, checked before and after the job, because reminders come due by the day. A run that crosses midnight is not judged.

**Why it's there.** The loader writes each reminder and each owner's count with SQL (decisions 297 and 298). The job stays on in a new install, so only Rock's own run proves its first run changes nothing, and Rock's bell shows the right count from the start. It also catches a reminder whose person or group is missing, which the job reports as an error.

### Step workflow launch

**What it does.** `verify/rock-runner` launches step workflows with Rock's own code (`--task launch-step-workflows`), the way Rock does when a step becomes complete. For each step, it activates the trigger's workflow type with the trigger's name, and processes it with the step as its entity. It marks the type not persisted for the run, so Rock processes every action as it would and saves nothing. It tries the step of every open step workflow, and up to ten steps with completed ones for each trigger. Rock's workflow must have the name, assignee, and person the load wrote, and wait at its form. Any error Rock reports fails the check. A load that wrote no step workflows passes.

**Why it's there.** The loader writes each workflow type, its Lava, and every workflow with SQL (decision 294). Only Rock's own launch proves the types work when Rock runs them: their Lava finds the step's person and its campus's leader, and their actions assign the task. So a step a tester completes after install gets its task too.

### Process Workflows

**What it does.** `verify/rock-runner` runs Rock's own Process Workflows job once (`--task process-workflows`). It runs the stored job with the settings Rock's scheduler registers, as Rock runs it every ten minutes. Before and after, the tool reads every workflow the load wrote: its status, name, and completion, its activities and who has them, its actions and their form answers, and its step link. Rock processes each open workflow again, so an open workflow's processed and modified times are left out. A completed workflow must keep those times too. The check fails when the job changes anything else, or reports an error or an exception. A load that wrote no step workflows passes. How Rock Cleanup treats the same workflows is the [Rock Cleanup referee](#rock-cleanup-referee)'s job.

**Why it's there.** The job stays on in a new install. Rock's own jobs must leave open tasks open and completed ones completed after install (decision 294). Only Rock's own run proves that an open task still waits at its form for the same person. That includes the Person Data Error reports waiting for Alisha Marble (decision D-integrity-5).

### Family Analytics

**What it does.** Runs Rock's own Family Analytics procedures as Rock installed them, unchanged, on the loaded database, inside a transaction it rolls back: `spCrm_FamilyAnalyticsEraDataset`, then `spCrm_FamilyAnalyticsGiving`, `spCrm_FamilyAnalyticsAttendance`, and `spCrm_FamilyAnalyticsUpdateVisitDates`. That is everything the job runs with Set Visit Dates on. The check fails when the eRA procedure returns a family the job would move into eRA or out of it, or when any of the four would change an eRA value, a giving or check-in count, a First or Last Gave, or a First or Second Visit. A value counts as changed only when its text, date, or number differs, since Rock reads an empty value as no value, and the visit dates procedure writes empty First Visit values that Rock Cleanup deletes every night. It also checks that the job is on with Set Visit Dates on. Like Rock Cleanup, it is judged only when today is the loads' anchor, because the procedures count from today.

**Why it's there.** The loader replays the job's weekly runs with a port of Rock's rule (decisions 323 to 326). Only Rock's own procedures prove the port left each family where Rock would, so the job's first run after install changes nothing.

### Peer network

**What it does.** Runs Rock's own Calculate Peer Network procedures as Rock installed them, `spPeerNetwork_UpdateFollowing` then `spPeerNetwork_UpdateGroupConnections`, which is everything the job runs, inside a transaction it rolls back. The check fails when they would add or remove a row, or change a row's dates, score, or caption. A re-run always resets each trend, so trends are left out. It also checks that the job stays on. It is judged only when today is the loads' anchor.

**Why it's there.** The loader writes the job's last two monthly runs with a port of Rock's math (decisions 328 and 329). Rock's score depends only on the month, so Rock's own run on install day must give every score the load wrote.

### Group sync

**What it does.** Last, `verify/rock-runner` runs Rock's own group sync (`GroupSyncService.SyncGroups`, what the Group Sync job calls) on every sync the load wrote, whatever its interval. Rock evaluates each data view with its real data filters and compares it with its group. The check passes when Rock syncs every sync and adds, removes, and refuses nobody, with no warnings. Like Rock Cleanup, it is judged only when today is the loads' anchor, because the views count from today.

**Why it's there.** The loader fills each synced group by evaluating the data views in SQL (decision 187). Only Rock's own run proves the loader picked exactly the people Rock picks. Nothing changing also proves that the syncs' welcome and exit emails and logins wait for a person to join or leave later, so a new install sends nothing (decision 95).

### Benevolence workflow launch

**What it does.** Last of the load's checks, the tool clears the case worker on up to 25 of the load's benevolence requests in its throwaway copy, as on a request entered without one. Then `verify/rock-runner` launches each request's Request Started workflow with Rock's own code, the way Rock's BenevolenceRequestChangeTransaction does when a request is added, and the tool reads back the case worker Rock saved. The check passes when every request has the configured case worker again and Rock reported no error.

**Why it's there.** The workflow is not persisted, so a load leaves nothing of its runs to compare (decision 355). Only Rock's own run proves the type's entity type, its Lava, and Rock's Entity Property Set action work together, on both Rock builds.

### Group requirements

**What it does.** After group sync, `verify/rock-runner` runs Rock's own Calculate Group Requirements job once, with its default settings. The tool reads every requirement result before and after, and the check passes when the job inserted, deleted, and changed none of them. The job also stamps its last-checked time on every result it looks at, so that one column is left out. Like group sync, it is judged only when today is the loads' anchor, because Rock's requirement data views count from today.

**Why it's there.** The loader writes each kids and student volunteer's and each small group leader's requirement result the way the job would leave them after running every night (decision 333). Rock's scheduler and group screens read the stored result, not the rule, so only the real job proves every result is right, on both Rock builds.

### Data automation

**What it does.** After group requirements, `verify/rock-runner` reads each of the six Data Automation rules with Rock's own settings class and writes it back with Rock's own serializer. The check expects exactly the JSON the load saved, and every rule off. Then the runner runs Rock's Data Automation job once, as its own stored job, the way Rock's scheduler runs it every Tuesday at 4 AM. The tool reads every person's gender, statuses, reasons, campus, family, and modified time, and every family's campus, status, and members, before and after, and the check passes when the job changed none of them.

**Why it's there.** The load saves the rules configured and off (decision D-integrity-7). A property Rock's class does not know would read back as its default with no error, so only Rock's own classes prove each rule reads as saved. With every rule off, the job runs only its gender auto-fill, so its first run must find no one to fill.

### Data views

**What it does.** `verify/rock-runner` runs every data view in the load, the church's and Rock's own, through Rock's own engine (`DataViewCache.GetEntityIds`), the call Rock's data view screens and jobs make. Any error fails the check, and each church view's count and time are printed. A persisted view runs live, since Rock's job has not persisted it yet.

**Why it's there.** The loader writes each filter's selection string itself. Only Rock can prove it reads every one, on both Rock builds (decision 229).

### Metrics

**What it does.** For each path metric in `configuration/reporting.json`, reads its newest values, one for each campus, and when they were taken. It counts, in SQL, the active people with a completed step of the stage, and how many of them first reached the stage after that value, by the campus Rock's job counts each person under. The SQL count must equal Rock's count of the stage view from the data views check, the newest values must add up to Rock's count less the later ones, and each campus's value must be its own people less its own later ones.

**Why it's there.** Rock counts a view only as it is today, so the loader reads each week's value back from the steps. Tying the newest values to Rock's own count proves the loader used the view's rule and Rock's campus split (decisions 234 and 379).

### Access

**What it does.** `verify/rock-runner` asks Rock's own security check (`Authorization.Authorized`) each question in `access` in `configuration/reporting.json`, `configuration/adaptive-messages.json`, `configuration/signals.json`, `configuration/tags.json`, and `configuration/benevolence.json`: may this Cast person view or edit this category, data view, report, personalization segment, page, signal type, or benevolence type, or view or use this tag. Rock walks a data view's categories up to the global default, as it does on every page. It asks an SMS snippet in `access` in `content/communications/snippets.json` through the snippet's own check, as SMS Conversations does, which shows a personal snippet only to its owner (decision 299). For a tag, the runner loads the tag and asks the tag itself, as Rock's tag bar does. That lets a personal tag's owner do anything with it and keeps everyone else out, before the tag category's rules (decision 303). Each answer must match.

**Why it's there.** The security walkthrough in the design is a claim about Rock's code. This makes it a test, so a change to a category's rules, or to Rock, that changes who can see finance data or the Homebound tag fails here.

### Reports

**What it does.** `verify/rock-runner` checks every report the way Rock's report grid reads it: a property column names one of Rock's reportable fields for the entity, an attribute column's attribute exists, and a data select column's component builds its expression from the stored selection. Each Lava column is rendered through Rock's own Lava engine, with the first row its data view finds, and any Lava error fails the check.

**Why it's there.** A property or attribute Rock cannot find is quietly left out of the grid, and a component that cannot read its selection stops the whole report. Only Rock can say which, on both builds (decision 231).

### Assessment scores

**What it does.** `verify/rock-runner` scores every assessment's saved answers with Rock's own five scoring services (`DiscService`, `SpiritualGiftsService`, `ConflictProfileService`, `EQInventoryService`, and `MotivatorService`), and writes each answer set again with Rock's own serializer, the way each assessment block saves it. The answer data must be exactly what the load wrote, and each person's latest result of each type must give exactly the person attribute values the load wrote. A legacy DISC result has no answers, so the Cast expectations check it instead (decisions 265 and 268).

**Why it's there.** The loader scores answers with its own port of Rock's scoring. Only Rock's code can prove the port right on each Rock release, as the giving parity test does for giving.

### Anchor sweep

**What it does.** After Core 1's other checks, the verify tool loads Core 1's giving and reminders parts into one copy of the trusted base once for each of the 60 install days from the run's anchor, as a dry run that rolls back, with the Cast expectations checked inside each load. Reminders bring the notes and small groups they need. It fails when any Cast giving or reminder expectation fails on any of those days, and names the days. `--anchor-sweep [days]` runs only the sweep, for as many days as you ask, such as a full year.

**Why it's there.** Every date in a load counts back from its install day, so a Cast story can hold on one day and not the next. Margaret Carter's monthly giving held when verify ran on 2026-10-29 and failed on an install the next day, because that day's draw put two of her gifts a week apart. A year's sweep found her failing on 37 of 365 days and every other Cast giver holding on all of them. She now keeps her check in the first week of each month (`isOnTime`), and the sweep keeps every Cast giving story honest on any install day (decision 218). Each Cast reminder is due, upcoming, or complete by dates that count from the install day too, so the sweep checks the reminders and their owners' counts on every day it sweeps (decision 297).

### Unchanged during run

**What it does.** Reads each package's SHA-256 and this repository's commit before the run, and checks both again at the end.

**Why it's there.** A run takes minutes. If a package or a check changed partway, the receipt would describe something that was never verified.

### Receipt

Each run writes `{dataset}.verify.json` next to each package. A receipt records the package's SHA-256, source commit, and minimum loader version, the Rock major release and the exact Rock version and migration that were checked, the trusted base's certificate and schema hash, the anchor and seed, this repository's commit, and every check with its details. A package passes only when every check passes. It also records the load's step timings and peak space, which no check reads (see [Performance](performance.md)).

### Publishing gate

**What it does.** `dotnet run -- --check-receipts ../packages` (in `verify/`) refuses to publish when any package has no receipt, its receipt is for different bytes, any check failed, the receipt is for a different Rock than the package, the package names no minimum loader version or the receipt names another, or either the package or the checks came from uncommitted changes.

**Why it's there.** A receipt only matters if publishing depends on it. The release workflow runs this gate in each Packages job, and again in the Publish job on the exact files it uploads (see [Publishing a release](maintaining.md#publishing-a-release)).

## When a church installs

The loader runs five short checks before it writes anything, and stops the install if any fails:

1. The package's format is one this loader reads, and this loader is at least the oldest loader the package names (decision 158).
2. Every file matches its SHA-256 in `package.json`, nothing is missing, and nothing was added.
3. No other sample data load is running on the database. The load holds an exclusive SQL Server application lock for its whole transaction.
4. The database has no people beyond the five a new Rock install creates, so it holds no real people and no sample data yet (decision 122).
5. The database's Rock, read from Rock's `Rock.Version.dll`, is the package's major release (decision 52).

Checks 3 to 5 run inside the load's transaction, so two loads started at once cannot both pass. During a CLI or verify load it also checks the Cast expectations of the parts it loaded (Rock's block skips them, decision 217), and it runs Rock's own journey stage procedure for the last week to confirm Rock makes exactly the changes the loader predicted. See [Loader](../loader/README.md#what-the-loader-checks-before-loading).

**Why they're there.** These are the only checks that run on a church's server, so they are fast and protect only against a damaged package, a loader too old for it, the wrong database, or the wrong Rock major release. Everything slower happened before the package was published. They check nothing else about Rock's schema: Rock makes no model changes in a dot release, and the contract was checked on the Rock the package was built on.

## When the loader library changes

### Loader compatibility check

**What it does.** `dotnet run --project loader-compat-check -f net10.0`, then the same with `-f net472`. It checks the loader's key derivations (PBKDF2 and HKDF) against the RFC 6070 and RFC 5869 test vectors, and Rock's key bytes, keyed hash, IdKey, package checksums, and seeded random draws against values from .NET 10's own methods. It also opens the configuration of every published release, saved in `loader-compat-check/published/`, and checks that a file the loader cannot read gives a plain message (decision 168). Last, it plans the repository's calendar and registrations for install day October 28, 2026, and checks every registration's event, opening, closing, and payment days and both early birds against the example dates in [Event registrations](registrations.md#dates) (decision 190). It checks the port of Rock's signature verification hash against three values computed by calling Rock 21's own Rock.dll, which Rock 20's matches. It answers and scores 40 people on each of the five assessments and hashes every score and answer data text, so Rock's build scores and writes them exactly as the build verify checks against Rock does (decision 265). It plays a failed load whose rollback fails too, and checks that the load's own error is still the one thrown, with the rollback's error attached, even when .NET Framework cannot serialize that error (decision 278). Both runs must pass. See [Loader](../loader/README.md#the-net-framework-build).

**Why it's there.** Rock runs the loader's .NET Framework build, which fills .NET 10's missing key derivation with its own code (decision 120). Check scanning and note mentions in Rock work only if those bytes are exactly right, and nothing else would notice if they were not.

## Realism and Rock's math

### Realism grading

**What it does.** Runs the same calibration scripts that measure real churches against a loaded database, then compares the results with the calibration profile, within the tolerances in `validation/tolerances.json`. Today 89% of measured values pass. See [Validation](../validation/README.md).

**Why it's there.** Correct data can still look fake. This proves the data looks like a real church, and catches a generator change that makes it look less so.

### Rock lock check

**What it does.** `node validation/check-rock-lock.js` fingerprints the Rock files the loader's code copies, such as the giving automation math, the five assessment scoring services, Family Analytics' eRA rule, and Calculate Peer Network's scores, reading them from Rock's public repository, and fails when one changed.

**Why it's there.** The loader ports Rock's giving automation math, its assessment scoring, and its eRA and peer network rules. If Rock changes them, the port goes stale without any error.

### Giving parity test

**What it does.** Before each release, Rock's real Giving Automation job runs on a loaded Full 7 database, and the loader CLI's `--giving-parity` compares what Rock wrote with what the loader computed. See [Validation](../validation/README.md#rock-version-guard).

**Why it's there.** The lock check catches a change in Rock's code. The parity test proves the port gives the same answers.

## When a check fails

The [verify tool's README](../verify/README.md#when-a-check-fails) says what to do for each check. In short: fix the loader or the data, not the check. Change a check only when the check itself is wrong, and say why in the commit.
