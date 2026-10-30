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
| [Package self-check](#package-self-check) | Every time the builder writes a package | A package that would fail to load is never written. |
| [Trusted base](#trusted-base) | Verify tool, before publishing | Every load starts from a clean copy of exactly this Rock release. |
| [Contract schema](#contract-schema) | Verify tool | Everything the loader depends on still exists in this Rock, unchanged. |
| [Contract current](#contract-current) | Verify tool | The contract still describes what the loader really writes. |
| [Load](#load) | Verify tool | The package loads into this Rock. |
| [Cast expectations](#cast-expectations) | Verify tool, and every CLI load | The Cast givers that docs and tests rely on still show what their stories say. |
| [Invariants](#invariants) | Verify tool | Rules Rock relies on hold in the loaded data. |
| [Rock Cleanup referee](#rock-cleanup-referee) | Verify tool | Rock's own repair job finds nothing to fix. |
| [Sign-in](#sign-in) | Verify tool | Rock's own Database login accepts the sample logins' password and refuses a wrong one. |
| [Group sync](#group-sync) | Verify tool | Rock's own group sync finds nothing to add or remove, so nothing sends at install. |
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

**What it does.** `node cast/validate-cast.js` reads every Cast household and group file. It checks required fields, Guid format and uniqueness, every cross-reference (members, groups, hosts, relationships, follows, notes), connection statuses, giving expectations, campaign pledges and gifts, small group rules, and email domains. Logins must be lowercase letters and digits, unique, never `admin`, and only on adults, and everyone with a Rock security role must have one (decision 179). It also checks every delivered photo: each is a 400 by 400 JPEG with an original, named for its person, and matching that person's gender.

**Why it's there.** Tests and documentation rely on Cast people by Guid. A broken reference or a reused Guid would break them quietly, long after the change that caused it.

### Content standards check

**What it does.** `node validation/check-standards.js` checks the [content standards](content-standards.md) that span folders:

- **Rule 2.** Every Cast phone is in the reserved range (555-0100 to 555-0199 in 602, 480, or 623). A number is shared only within a household, as an office line, or between duplicate records. The church's texting numbers in `configuration/communications.json` are reserved too, each is unique, and none is a Cast person's number.
- **Rule 3.** Every email anywhere in the data uses `example.org` or `rocksolidchurchdemo.com`.
- **Rule 5.** The raw data folders hold nothing in git but their README and `.gitignore`.
- **Rule 6.** Calibration sources are named `source-N`, and every source's description is exactly "Calibration source". When it finds a description, it never repeats it, so the check itself cannot leak one.

**Why it's there.** A real phone number, email, or church detail in sample data could reach a real person, because the data ships with Rock and appears in demos and screenshots. [Content standards](content-standards.md#how-each-rule-is-checked) lists which script checks each rule and which rules still need a reviewer.

### Registrations check

**What it does.** `node validation/check-registrations.js` reads `configuration/registrations.json` with the calendar, accounts, campuses, and datasets. Every instance names a real event, template, contact, placement, and group type; its slug and group keys carry the year, month, and campus they need, so no two instances share one, and its slug matches Rock's pattern; every template asks for the fields its grade, age, or gender rule needs; and every registration feature sits on an instance that is open on any install day (decision 190). For the people who register: each registrants rule names exactly one audience (students, kids, couples, adults, or volunteers) with real connection statuses, ages, and shares; students name a Student Path event step or a share; a paid template's payments add up to 100 and are ones the template allows, pay later only with no minimum payment, and a free template has none; its fees, fee weights, per-child fees, codes, and placements are the template's; attribute and person attribute fills fit their attributes; and a discount that needs several registrants comes before an early bird, since Rock gives only the first one valid (decision 207). Each release names a template's `signatureDocument`, and its Lava uses only the four outputs the loader renders and starts with a title for the PDF. Each data view sits in a data view child of the church's category, a registration filter names an instance rule and occurrence that make exactly one instance, and the mission trip's eligibility view counts student service check-ins in exactly the student check-in areas over a "Last N days" window, from the template's lowest grade, which the loader replays from the Student Path (decision 215).

**Why it's there.** Rock checks slugs only in its screens and refuses a registrant it cannot check for eligibility. A broken reference or a feature with no open home would only show up after someone installed the data.

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

**What it does.** Some Cast giving patterns say what Rock should show for that giver: a frequency, a journey stage, or alerts (`expect` in the household file). Some Cast people say what their achievements or steps should show, and Cast households with a part in the building campaign say how their pledge is going (`campaign` in the household file). Each staff texting number in `configuration/communications.json` must be active and assigned to its staff member, and each scripted Cast unsubscribe there must show at its level, with the person Inactive in the list or on "No mass emails". Each Cast text thread in `cast/sms-threads.json` must show its texts and its state: unread, read, or stopped. Each Cast staff email in `cast/staff-emails.json` must show once, delivered, from the staff member, with its subject, on its day. Each Cast note in `cast/notes/` must be on its person, with a type the person profile's Notes block shows. Each calendar event's `expect` entries in `configuration/calendar.json` must show a start at their campus on the days their Cast story needs. Each Cast registration in a household's `registrations` list must show its members on one registration of the instance, off the wait list, with the registrar, placements, payment state, and discount it expects. A registration whose instance opened only that day is not checked. This check reads Rock's values in the loaded database and compares them. The loader also checks them on every CLI load; Rock's block skips them (decision D-expectations-1). See [Validation](../validation/README.md#cast-expectations).

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
| `phones-use-the-reserved-range` | Content standard 2, for every loaded phone, generated people too. |
| `emails-use-reserved-domains` | Content standard 3, for every loaded email. |
| `no-race-or-ethnicity` | Content standard 8. |
| `home-addresses-are-mapped` | Group Finder, campus distance, and maps need a point for every home. |
| `nothing-dated-in-the-future` | A future gift, check-in, or step shows up in "upcoming" places. |
| `transaction-has-positive-details` | Rock totals a gift from its details. |
| `transaction-is-inside-its-batch` | A gift outside its batch's date range lands in the wrong batch report. |
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
| `program-completions-follow-rocks-rule` | Rock's Update Step Program Completions job finds nothing to add or fix after install, in any program the dataset has steps in. |
| `student-step-belongs-to-a-student` | Every Student Path step belongs to someone in grades 7 to 12 on its start who had checked in to the student service by then. Mission Trip is high school only, and Student Leader is juniors and seniors only. |
| `student-path-decisions-and-leaders` | Follow Jesus and Student Leader happen at most once per person. Student Leader ends at graduation in May, or stays open while the student is still in school. |
| `student-baptism-follows-the-student-service` | A child's baptism is here, on a Sunday, after they started coming to the student service. |
| `young-kid-checks-in-with-the-family` | A kid from the nursery to 6th grade checks in only with an adult of their family who was in the service that Sunday. A child whose families have no living, active adult checks in on their own (decision 146). |
| `onsite-gift-on-a-family-sunday` | Every gift in a Sunday Offering batch falls on a Sunday an adult of the giver's family came. A student on their own never makes a family Sunday (decision 146). |
| `student-check-in-fits-the-grade` | Every student check-in fits the student's grade that day, so a recent graduate's student check-ins end after June 1 of their graduation year, when Rock moves them out of grade 12 (decisions 148 and 205). |
| `communication-list-members-allow-mass-email` | Nobody who chose "No mass emails" or "Do not email" is on a communication list (decision 133). |
| `newsletter-members-belong-to-the-campus` | A campus newsletter holds only that campus's adults with an email that is active, or that bounced after they joined. |
| `volunteers-list-follows-the-teams`, `small-group-leaders-list-follows-the-groups` | The Volunteers list is exactly the people active on a serving team, and the Small Group Leaders list exactly the leaders of active small groups. |
| `group-sync-targets-a-sync-group` | Every sync targets a group whose type Rock lets sync, with one of that type's roles and a person data view, so it opens and edits in Rock (decision 186). |
| `group-sync-refreshed-at-install` | Every sync shows a last refresh from the load, as the job leaves it after a run (decision 187). |
| `church-data-view-is-filed` | Every data view the church adds sits in a child of the Rock Solid Church category (decision 185). |
| `data-view-filter-tree-is-whole` | Every church data view has a root filter, every filter belongs to its view, and each single filter names its component and selection, so Rock can read the view. |
| `communication-is-sent-and-never-sends-again`, `no-recipient-waits-to-send` | Decision 95: every communication is Approved with its send time and date key, and no recipient is Pending or Sending, so Rock's send job finds nothing to send. |
| `email-recipient-states-agree` | Each email recipient's status agrees with its send, delivery, and open times and its status note. Only a bounce fails after Rock sent it. |
| `opens-and-clicks-follow-an-open` | Every open and click is on the communication's component, points at an opened recipient, comes after their first open, and a click goes to the church's site. |
| `unsubscribe-matches-the-person` | Decision 131: a list unsubscribe leaves the person Inactive in that list, and a Bulk one leaves them on "No mass emails" or stricter. |
| `sent-email-has-its-history` | Every sent email recipient has Rock's "Sent" line on their History. |
| `sent-text-is-delivered-with-its-text`, `sent-text-has-its-history` | SMS Conversations shows a text only when it is Delivered and reads its words from SentMessage, and Rock writes a "SMS message" History line for each text it sends. A text Rock could not send failed with its note. |
| `no-active-flow-instance` | Decisions 95 and 96. Every flow instance is complete and each of its emails went out before install day, so Rock's flow job has nothing left to send. |
| `flow-email-reaches-its-instance` | Each flow email is a bulk email from its message's flow template, to exactly the instance's people. |
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
| `login-is-a-database-login-rock-can-check` | Every login is a Database login with a BCrypt hash Rock reads, on a person, never locked out or sent to change its password, and last used only after it was made and only when confirmed (decision 180). |
| `login-username-is-plain` | Usernames are lowercase letters and digits only, and never Rock's own `admin`. |
| `login-belongs-to-an-adult` | Only adults 18 and older have logins, never a child or a business. |
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
| `person-attribute-answer-comes-from-a-registration` | Rock's Allergy person attribute is written only for a registrant whose form asks for it, so no child gets a stray allergy on their check-in label (decision 211). |
| `last-camp-matches-camp-steps` | When the steps part loaded, the campers at every camp that has ended are exactly the people with a Student Camp step there (decision 206). |
| `release-is-signed-for-every-registrant` | On a template that requires a release, saved with Rock's Embed action and no external updates, every registrant links to a signed release from that template for that person. None is pending (decision 212). |
| `signed-release-is-whole` | Each signed release has its PDF in the release's file type, the registrar as signer and the one it is assigned to, a typed name, a reserved email and IP address, a signed time just before the registration on the same day, and a verification hash that matches when recomputed in SQL the way Rock computes it. |
| `registration-has-its-notes-and-history` | Every registration has Rock's "Added Registration" history by its registrar, and each registrant a note of Rock's Event Registration type and a "Registered" history line tied to the registration (decision 213). |
| `registration-data-view-is-filed-under-registrations` | A data view that filters on a registration instance or is a template's eligibility rule sits in the church's Registrations category. |
| `registrant-is-in-the-eligibility-data-view` | Every registrant of a template with an eligibility data view was in it on the day they registered: its grade filter and its check-in count over its window, replayed in SQL. A load without attendance skips it (decision 215). |
| `people-match-the-dataset-card`, `households-match-the-dataset-card` | Rock's installer shows the people and households from the dataset card in `datasets/{key}.json`. A default-seed load more than 2% away from either means the card is stale (decision 124). |

To add a rule, add a `-- check:` block with a `-- why:` line that returns how many rows break it.

### Rock Cleanup referee

**What it does.** Rock's nightly Rock Cleanup job repairs what Rock's save hooks would have set: aliases, ages, salutations, search keys, and more. After a load, the tool takes a SQL Server snapshot, runs Rock's real job through `verify/rock-runner` (with Rock's message bus started first, as in a running Rock, so the job's cache messages never log bus errors), and compares every table with the snapshot, column by column, as bytes. It runs the same job on a plain copy of the trusted base too. Both databases start from the same base, so base rows have the same Ids in both. A change Rock makes to exactly the same base rows on the plain copy is background. A change to any row the load added is a finding. A change accepted on purpose goes in `validation/cleanup-accepted.json` with a reason. Two of the job's tasks need a web server, so they are noted, not failed. Any other task error fails.

**Why it's there.** This is the most direct answer to "we skip Rock's business logic." Rock itself tells us what it would have done differently. On its first runs it found real loader gaps: primary aliases, known-relationship groups, search keys, sound-alike name entries, family salutations, and inactive dates. The [roadmap](roadmap.md) lists them.

**The moving window.** Rock Cleanup rebuilds `ScheduleDate` from now minus two years to now plus two years, to the minute. The plain copy and each loaded copy are cleaned minutes apart, so the tool leaves out the rows within a day of either edge, in both runs, and compares the rest exactly. Before, an occurrence at an edge sometimes fell inside one run and outside the other, and rock-cleanup failed on base rows the load never touched (11,080 against 11,079 changed rows).

**The clock.** The loader sets each event occurrence's next start from the clock at load time, the way Rock's save hook does, and Rock Cleanup sets it again from the clock when it runs, minutes later. An occurrence whose next start passes in between moves to its following start, which once failed rock-cleanup (3 rows on a Core 3 run). So a change to `EventItemOccurrence.NextStartDateTime` is not judged when the old value had already passed when Rock Cleanup started, and Rock only moved it later or cleared it. The tool reports those rows as "Not judged". Any other change to the column, such as a next start the loader set wrong, still fails.

**When it is not judged.** Rock Cleanup recomputes ages and daily counts for today, from SQL Server's `GETDATE`. So the tool judges it only while the database server and the machine running Rock are both on the loads' anchor, checked before and after the job (decision 152). Otherwise the check fails with both dates and says to run again. A run that crosses midnight, or an `--anchor` that is not today, lands here instead of reporting false age changes.

### Sign-in

**What it does.** After the referee, `verify/rock-runner` signs in through Rock's own Database login, the check Rock's login block makes: the login exists, is confirmed, and Rock accepts the password. It signs in as Ted Decker (`tdecker`) and as the load's newest confirmed login with the test password, and as Ted with a wrong password, which Rock must refuse (decision 180). It runs after the referee, because a sign-in can rewrite a hash at the site's cost setting.

**Why it's there.** The loader writes the hash with SQL, so only Rock itself can prove the hash is one it accepts, and that a person installing could sign in as the Cast.

### Group sync

**What it does.** Last, `verify/rock-runner` runs Rock's own group sync (`GroupSyncService.SyncGroups`, what the Group Sync job calls) on every sync the load wrote, whatever its interval. Rock evaluates each data view with its real data filters and compares it with its group. The check passes when Rock syncs every sync and adds, removes, and refuses nobody, with no warnings. Like Rock Cleanup, it is judged only when today is the loads' anchor, because the views count from today.

**Why it's there.** The loader fills each synced group by evaluating the data views in SQL (decision 187). Only Rock's own run proves the loader picked exactly the people Rock picks. Nothing changing also proves that the syncs' welcome and exit emails and logins wait for a person to join or leave later, so a new install sends nothing (decision 95).

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

Checks 3 to 5 run inside the load's transaction, so two loads started at once cannot both pass. During a CLI or verify load it also checks the Cast expectations of the parts it loaded (Rock's block skips them, decision D-expectations-1), and it runs Rock's own journey stage procedure for the last week to confirm Rock makes exactly the changes the loader predicted. See [Loader](../loader/README.md#what-the-loader-checks-before-loading).

**Why they're there.** These are the only checks that run on a church's server, so they are fast and protect only against a damaged package, a loader too old for it, the wrong database, or the wrong Rock major release. Everything slower happened before the package was published. They check nothing else about Rock's schema: Rock makes no model changes in a dot release, and the contract was checked on the Rock the package was built on.

## When the loader library changes

### Loader compatibility check

**What it does.** `dotnet run --project loader-compat-check -f net10.0`, then the same with `-f net472`. It checks the loader's key derivations (PBKDF2 and HKDF) against the RFC 6070 and RFC 5869 test vectors, and Rock's key bytes, keyed hash, IdKey, package checksums, and seeded random draws against values from .NET 10's own methods. It also opens the configuration of every published release, saved in `loader-compat-check/published/`, and checks that a file the loader cannot read gives a plain message (decision 168). Last, it plans the repository's calendar and registrations for install day October 28, 2026, and checks every registration's event, opening, closing, and payment days and both early birds against the example dates in [Event registrations](registrations.md#dates) (decision 190). It checks the port of Rock's signature verification hash against three values computed by calling Rock 21's own Rock.dll, which Rock 20's matches. Both runs must pass. See [Loader](../loader/README.md#the-net-framework-build).

**Why it's there.** Rock runs the loader's .NET Framework build, which fills .NET 10's missing key derivation with its own code (decision 120). Check scanning and note mentions in Rock work only if those bytes are exactly right, and nothing else would notice if they were not.

## Realism and Rock's math

### Realism grading

**What it does.** Runs the same calibration scripts that measure real churches against a loaded database, then compares the results with the calibration profile, within the tolerances in `validation/tolerances.json`. Today 89% of measured values pass. See [Validation](../validation/README.md).

**Why it's there.** Correct data can still look fake. This proves the data looks like a real church, and catches a generator change that makes it look less so.

### Rock lock check

**What it does.** `node validation/check-rock-lock.js` fingerprints the Rock files the loader's giving code copies, reading them from Rock's public repository, and fails when one changed.

**Why it's there.** The loader ports Rock's giving automation math. If Rock changes it, the port goes stale without any error.

### Giving parity test

**What it does.** Before each release, Rock's real Giving Automation job runs on a loaded Full 7 database, and the loader CLI's `--giving-parity` compares what Rock wrote with what the loader computed. See [Validation](../validation/README.md#rock-version-guard).

**Why it's there.** The lock check catches a change in Rock's code. The parity test proves the port gives the same answers.

## When a check fails

The [verify tool's README](../verify/README.md#when-a-check-fails) says what to do for each check. In short: fix the loader or the data, not the check. Change a check only when the check itself is wrong, and say why in the commit.
