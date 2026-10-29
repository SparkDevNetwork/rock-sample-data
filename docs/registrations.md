# Event registrations

Rock Solid Church's event registrations: the templates, each year's instances, and how they link to the calendar. They live in [configuration/registrations.json](../configuration/registrations.json). The loader's registrations step writes them (decision 189).

Registrations came in four slices, and all four are built. Slice 1, the setup, writes everything a church sets up before anyone registers, so every upcoming event page shows its Register button. Slice 2 adds the [students who registered](#who-registered-students) for camp, the mission trip, and the fall retreat, with their payments and placements. Slice 3 adds the [kids and adults who registered](#who-registered-kids-and-adults) for VBS, Parents' Night Out, Serve Day, Money Matters, Date Night, and the Women's Retreat. Slice 4 adds the [signed release forms](#release-forms) for camp and the mission trip, the [notes and history](#notes-and-history) Rock writes on each registration, [reminders](#reminders), and [data views](#data-views) about registrations.

## What a church gets

Ten registrations from nine templates, in every dataset. A registration for a campus event gets an instance at each campus the dataset has, so Core 1 has Phoenix's only.

| Registration | Template category | Cost | Linked group | Open on any install day |
|---|---|---|---|---|
| Student Camp, Girls and Boys, last summer and next summer | Students | $349, $100 deposit | Student Camp, one per year, shared by girls and boys | Next summer's |
| High School Mission Trip, next trip | Students | $1,850, $300 deposit | Mission Trip Team, members added as Pending | Yes |
| Student Fall Retreat | Students | $139, $50 deposit | Fall Retreat | Yes |
| Vacation Bible School, last summer and next summer, per campus | Kids | $25 per child | VBS Kids, per campus | No. Next summer's opens in spring |
| Parents' Night Out, next month, per campus | Kids | Free | None | Yes |
| Community Serve Day, per campus | Outreach | Free | Serve Day Volunteers, per campus | No. It was 35 days ago |
| Money Matters, next class, per campus | Adults | $30, set on the instance, pay later | Money Matters class | Yes |
| Marriage Date Night, next one, per campus | Adults | $40 per person | Date Night | Yes |
| Women's Retreat | Adults | $159, $50 deposit | Women's Retreat | Yes |

Seven registrations are open on every install day: camp, the mission trip, the fall retreat, Parents' Night Out, Money Matters, Date Night, and the Women's Retreat (decision 190). Each registration feature has at least one of them as its home, so anyone can try any feature on a fresh install.

| Feature | Always-open home |
|---|---|
| Free registration | Parents' Night Out |
| Cost on the template, or on the instance | Camp, or Money Matters |
| A deposit (minimum initial payment) | Camp, the mission trip, the fall retreat, the Women's Retreat |
| Pay later (no minimum) | Money Matters |
| Payment plans | Camp (monthly or twice a month), the mission trip (monthly), the Women's Retreat (monthly) |
| Full payment or a payment plan required | The mission trip |
| Payment deadline | Camp, the mission trip, the Women's Retreat |
| Required fee with choices that set the price | The Women's Retreat room |
| Optional fee with sizes | The camp T-shirt, the retreat hoodie |
| Fee with a quantity | Date Night childcare, per child |
| Fee item with a limit, hidden when none remain | Women's Retreat single rooms |
| Fee the discount does not apply to | The camp T-shirt |
| Percent discount code | FRIEND, 10% off the Women's Retreat |
| Amount discount code with limited uses | STUDENTLEADER, $40 off the fall retreat, 12 uses |
| 100% scholarship code | SCHOLARSHIP for Money Matters, 5 uses |
| Early-bird discount | Camp until April 30, then the Women's Retreat until October 31, so one is always running |
| Discount for registering several people | Camp, $25 off each sibling |
| Registrant limit and wait list | Camp girls and boys (limit), the mission trip and Parents' Night Out (limit and wait list) |
| Several people on one registration, same family | Camp siblings, Date Night couples |
| Grade, age, and gender rules | Camp and the trip (grade), Parents' Night Out (age), the Women's Retreat (women) |
| A registrar who is not a registrant | Camp: a parent registers the student |
| Placement groups, one shared by two instances, several placements per person | Camp cabins, buses shared by girls and boys, and two activities each |
| Registrant attributes and a person attribute | Camp (cabin mate, emergency contact), Parents' Night Out (allergies, saved to the child), the Women's Retreat (roommate) |
| "How did you hear about this event?" | Every registration |
| A required release with a typed signature | Camp and the mission trip |
| An eligibility data view | The mission trip: high school students who attend the student service |

## Templates

Each template has a category (Students, Kids, Adults, or Outreach), forms, and the terms Rock shows. Its emails and success text are copied from Rock's own Baptism template, so they read the way Rock writes them. Paid templates use Rock's Test Gateway with the batch prefix "Event Registration", and every instance posts to Rock's Event Registration account (decision 193).

Rock's v19 upgrade gave its "RSR - Event Registration Administration" role Edit and Administrate on every template category that existed then. The four new categories get the same.

Who may register is stored the way Rock's template screen stores it, in `AdditionalSettingsJson` under `RegistrantEligibilitySettings`. Rock refuses a registrant it cannot check, so a template that limits grade asks for grade, one that limits age asks for a birthdate, and one that limits gender asks for gender. VBS takes ages 4 to 11, not "age 4 to grade 5", because a 4-year-old has no grade and Rock would refuse them.

One camp template serves both the girls' and the boys' instances, so each has its own limit (decision 191). Rock's gender rule belongs to the template, so camp does not use one; the instance names say who each is for.

## Dates

Every date is a rule counted from the install day, through the calendar's own occurrences (decision 190). An event's dates at one campus form a list. A yearly event has one date per occurrence. A monthly event such as Parents' Night Out is one occurrence with a start each month.

Each instance rule picks an occurrence:

- **next** is the first date whose registration closes on or after the install day.
- **last** is the one before it.

Each rule opens in one of two ways:

- **rolling** opens the day after the previous date's registration closed. So the next one opens the moment the last one closes, and there is never a day without one open.
- **days-before** opens a number of days before the event.

Registration closes a number of days before the event, and the balance is due a number of days before it. An early-bird discount ends on a month and day before the template's latest event.

| Registration | Opens | Closes | Payments due | On Wednesday, October 28, 2026 |
|---|---|---|---|---|
| Camp, next (June 28, 2027) | Rolling: June 22, 2026. Early bird through April 30 | 1 day before | 14 days before | Open |
| Camp, last (June 22, 2026) | Rolling: June 23, 2025 | 1 day before | 14 days before | Closed |
| Mission trip, next (March 13, 2027) | Rolling: May 6, 2026 | 60 days before | 30 days before | Open |
| Fall retreat (November 13, 2026) | 60 days before | 3 days before | | Open, closing soon |
| Parents' Night Out (November 6, 2026) | Rolling: October 1 | 2 days before | | Open |
| Money Matters, next (January 11, 2027) | Rolling: September 21, 2026 | 1 day before | | Open |
| Date Night, next (February 12, 2027) | Rolling: October 7, 2026 | 3 days before | | Open |
| Women's Retreat, next (April 23, 2027) | Rolling: April 11, 2026. Early bird through October 31 | 14 days before | 14 days before | Open |
| VBS, last (June 8, 2026) | 60 days before | 7 days before | | Closed |
| VBS, next (June 14, 2027) | 60 days before | 7 days before | | Not open yet |
| Serve Day, last (September 19, 2026) | 42 days before | 3 days before | | Closed |

The calendar reaches 13 months ahead, so next year's camp, VBS, and Women's Retreat are always on it for their registrations to link to.

Each instance's reminder is set a few days before its event. A reminder whose day has passed is marked sent.

## Groups and linkages

Each instance links to its calendar occurrence through Rock's linkage, with a public name and a URL slug such as `student-camp-2027-girls` or `parents-night-out-2026-november-phoenix`. Slugs follow Rock's pattern, lowercase words joined by hyphens, and are unique. Rock checks both only in its screens, so the invariants check them.

The loader finds each occurrence by the key the calendar writes it with: the event, the campus or "church-wide", and the first start. Both steps take that key from one place.

Linked groups use the Event Attendees group type, under the Guid Rock reserves for it. A new Rock database does not have that type, so the loader adds it (decision 195). Placement groups use five new group types: Camp Bus, Camp Cabin, Camp Activity, VBS Crew, and Serve Day Team, each with a leader role and a default member role.

Groups with the same key are one group. So the girls' and boys' camp instances for one year share their linked group, their buses, and their activities, and each has its own cabins. Each placement group is linked to its instance the way Rock's placement screen links it: a `RelatedEntity` row with the purpose key `PLACEMENT`, qualified by the template placement's Id. A group that two instances share has a row for each.

No placement groups sit on the template (decision 192). Rock would share a template's groups with every instance in every year, so last year's campers would ride this year's buses.

| Placement groups | Core 1 | Core 3 | Full 7 |
|---|---|---|---|
| Camp buses, per year | 2 | 3 | 6 |
| Camp cabins, per year, girls and boys each | 2 | 5 | 12 |
| Camp activities, per year | 6 | 6 | 6 |
| VBS crews, per campus and year | 7 | 7 | 7 |
| Serve Day project teams, per campus | 5 | 5 | 5 |

<a id="who-registered-students"></a>

## Who registered: students

Students registered for Student Camp (last summer and next summer, girls and boys), the next High School Mission Trip, and the Student Fall Retreat. Each instance rule in `registrations.json` has a `registrants` rule that says who registers, when, and how they pay. These are designed targets, not calibration.

### The Student Path plan

Students register from the Student Path plan (decision 206). The loader plans the camps, trips, Follow Jesus decisions, and Student Leader steps once in every load, from the people, the attendance plan's student check-ins, the Cast stories, and the seed. The steps part copies those steps, and the registrations part reads them. So the registrations part needs only the calendar, as decision 189 says, and last summer's campers still match the camp steps. The steps are the same as before the plan moved: Core 3's Step rows, program completions, and baptism values are identical, row for row.

### Who

| Registration | Who registers |
|---|---|
| Camp, last summer | Exactly the students with that camp's Student Camp step, girls on the girls' instance and boys on the boys'. Everyone paid in full and is on one bus, in one cabin, and in two activities. |
| Camp, next summer | 45% of the students who could go (the Student Camp step's own rate): in grades 7 to 12 at camp, and attending the student service on install day. Early registrants already have their two activities. Nobody has a bus or cabin yet. |
| Mission trip, next | 20% of the high school students who could go (the Mission Trip step's own rate), until the limit fills. After that, 1 in 5 of the rest join the wait list. Members join the team group as Pending, as the template says. Each registers on a day they are in the trip's [eligibility data view](#data-views). |
| Fall retreat | 30% of the students who attend the student service, plus every current Student Leader. |

A student registers on a day Rock lets them: Rock checks a registrant's grade on the day they register, so a rising 7th grader registers for camp only after June 1. Every registrant is in the template's grades on that day and on the event day (decision 207).

- **Siblings** in one family register together on one registration, up to the template's limit. Camp's girls and boys have their own instances, so a brother and sister register on two registrations, made the same day.
- **The registrar** is a parent or guardian in the student's family, one with an email when there is one. A student whose parents are not in Rock registers themselves. The registration keeps the registrar's name and email, and the linkage's group and campus.
- **When.** Each family's registration day is a seeded draw across the whole window. Camp registrations bunch up in the three weeks after opening, before the early bird ends on April 30, and before the payment deadline. The trip's bunch up in its first month, and the retreat's after opening and before closing. Only registrations dated before the install day are written, so how many have registered follows how far into the window the install day is.
- **Limits.** Camp has no wait list, so a full camp stops taking registrations. Last summer's campers must all fit, so Full 7's camp limit is 300 per instance.

### Discounts, fees, and answers

Rock gives a registration one discount: the first automatic one, in the template's order, that is valid that day. So camp's sibling discount comes before its early bird, and siblings who register together get $25 off each, while anyone else who registers by April 30 gets $30 off. Current Student Leaders register for the retreat with the STUDENTLEADER code, $40 off, and the loader leaves two of its 12 uses for someone trying it on a fresh install. Jasmine Washington graduated in May, so she is no longer a Student Leader and does not register.

About 40% of campers add a camp T-shirt and 35% of retreat students a hoodie, older students in larger sizes. Each fee names its fee item, which Rock's placement board needs. Camp asks for an emergency contact, which is required: a parent or guardian other than the registrar when there is one, or the registrar. A student who registers themselves, or whose registrar has no phone, names a parent who is not in Rock, with a reserved 555 phone. Camp also asks, a third of the time, a cabin mate near the camper's grade. The trip asks about passports, and every registration answers "How did you hear about this event?"

### Payments

Each payment is an Event Registration transaction from the registrar, from the Website, through the Test Gateway, with the registrar's own card or bank account (the same wallet their gifts use). Its one detail points at the registration and posts to Rock's Event Registration account. The transaction carries the summary Rock's registration entry writes. Payments land in daily batches named the way Rock names them: "Event Registration" and the card type's batch suffix, such as "Event Registration VMD" for Visa, MasterCard, and Discover, or "Event Registration ACH". Payment plan payments come through Rock's Download Payments job, so they land in its "Online Giving VMD" batches. Each batch's control amount is its total, and a batch older than a week is closed (decision 208).

| Registration | Paid in full | Deposit, balance due | Payment plan |
|---|---|---|---|
| Camp | 45% | 30% | 25% (70% monthly, 30% twice a month) |
| Mission trip | 30% | Not allowed | 70% (monthly) |
| Fall retreat | 45% | 55% | Not allowed |

- **Deposits** are the template's minimum for each registrant. Once the payment deadline has passed, every balance is paid, most in the last three weeks. Registrations made after the deadline pay in full.
- **Payment plans** are made the way Rock's registration entry makes one: a scheduled transaction of type Event Registration with one detail that points at the registration, as many payments as fit from the start to the payment deadline (at least two), each the plan amount divided by the number of payments and rounded down to the cent, with the cents left over added to the first payment. Each payment made before the install day is a transaction of the plan. A plan whose payments are all made is inactive.
- **Payment reminders.** Rock's Event Payment Reminders job runs at 9 AM on weekdays and emails a registrar with a balance due every 14 days, until 30 days after registration closes. Each registration's last reminder is when that job last emailed it.
- **Not contributions.** Giving analytics, the giving validation, and the calibration reports count only Contribution transactions (`TransactionTypeValueId` of Rock's Contribution value, 2D607262), so registration payments change none of them. Core 3's contribution transactions, payment details, scheduled gifts, and batches are identical before and after slice 2.

### Groups and placements

Every registrant not on a wait list is a member of the instance's linked group, with the template's role and status, and their registrant row points at that member. Placed students are members of their placement groups with the group type's default role. Cabins go by grade band, the youngest in the first cabin. Buses, which the girls and boys share, and activities fill evenly.

### Cast stories

A Cast household file can list `registrations` (cast/household.schema.json): the instance rule, last or next, the members on one registration, and optionally the registrar, payment, and plan frequency. Its `expect` entries are checked on every load, except on a day the instance opened only that day.

| Who | Registration | Expected |
|---|---|---|
| Ava Brooks | Last summer's camp, girls | One bus, one cabin, two activities, paid in full. Her camp step and her Follow Jesus decision there hold too. |
| Benjamin Lee | Last summer's camp, boys | One bus, one cabin, two activities, paid in full |
| Benjamin Lee | The next mission trip | Registered by Sarah Lee, on a monthly payment plan that is on pace |
| Benjamin and Abigail Lee | Next summer's camp | Each registered by Sarah Lee, Ben on the boys' instance and Abigail on the girls', the same day |

The proposal had Ben and Abigail on one registration with the sibling discount. Camp's girls and boys have their own instances (decision 191), so a brother and sister cannot share one, and the Cast has no two sisters or brothers who are in camp's grades on every install day. Generated families who register two brothers or two sisters together show the sibling discount.

### How many

On install day October 28, 2026, at the default seed:

| Registration | Core 1 | Core 3 |
|---|---|---|
| Camp, last summer: girls, boys | 1, 2 | 2, 6 |
| Camp, next summer: girls, boys | 1, 2 | 2, 4 |
| Mission trip (on the wait list) | 1 (0) | 2 (0) |
| Fall retreat | 0 | 2 |
| Payments | 23, $2,909 | 48, $7,535 |
| Payment plans | 3 | 5 |

The small datasets have few students, so neither fills the mission trip or has two brothers or two sisters registering for next summer's camp. Full 7 was not loaded for this slice. Its Student Path has about 2,300 students and 848 camp steps over three summers (see [Steps](steps.md)), so its camp limit was raised to 300 per instance to hold last summer's campers, and its 24-spot trip should fill with a wait list. The lead's Full 7 verify gives its real numbers.

<a id="who-registered-kids-and-adults"></a>

## Who registered: kids and adults

Kids and adults go through the same steps as students: dated, registered, priced, paid, placed, and answered. Only the choosing differs (decision 209). Each instance's `registrants` rule names one audience. The audiences read the people and the small group and serving plans that every load makes, never another part's rows, so registrations still need only the calendar.

### Who

| Registration | Audience | Who registers |
|---|---|---|
| VBS | Kids | Families at the campus with kids age 4 to 11 who are in grade 5 or below on the first day: 60% of Participant families, 65% of Connected, and 70% of Engaged and Leading. A parent registers the siblings together. Phoenix fills its limit, and 60% of the families who come after it join the wait list. |
| Parents' Night Out | Kids | Kids age 2 to 11 of married parents: 20% of Known families, 60% of Connected, and 70% of Engaged and Leading. It is free. Phoenix fills its limit, and half of the rest join the wait list. |
| Serve Day | Volunteers | 30% of the people serving on a team and 15% of the small group members on the day, plus 4% of Participant and Known adults and 3% of Connected ones. 40% of their teens, age 13 to 17, come along. A family signs up together, with an adult first. |
| Money Matters | Adults | Adults 25 to 55: 5% of Known, 10% of Connected, 12% of Engaged, and 8% of Leading. 60% of the married ones bring their spouse on the same registration. |
| Date Night | Couples | Married couples, both on one registration: 8% of Known couples, 20% of Connected, 30% of Engaged, and 35% of Leading, by the more involved spouse. |
| Women's Retreat | Adults | Women 25 to 75. First, 30% of the active small groups with two or more who could go send two to four of them on one registration. Then 10% of Known women, 20% of Connected, 25% of Engaged, and 30% of Leading. |

- **Exact shares.** Each share is an exact count, rounded, figured apart for the Cast and for the generated households, so the Cast's numbers never depend on the Crowd.
- **The Cast.** A Cast person less involved than Connected registers only through a Cast story. Many of those stories are about one touch, such as Destiny Young's diaper drive, and a registration the story does not name would break it.
- **Full 7.** Its Crowd would fill a class many times over, so `scale` lowers its shares: VBS to 60%, Serve Day to 20%, Money Matters and Date Night to 15%, Parents' Night Out to 10%, and the Women's Retreat to 3%.
- **The registrar.** A parent or guardian registers kids. Adults, couples, and volunteers register themselves: the first person on the registration.
- **When.** Each registration is dated by a seeded draw across its window, with bursts after opening and before closing, and the Women's Retreat also before its early bird ends. Only the days before the install day are written. Next summer's VBS opens in spring, so it has no registrants on most install days.

### Fees, discounts, and payments

- **Childcare.** 70% of Date Night couples with children under 12 add childcare: one fee on the first registrant, with a quantity of their children, at $10 each. The childcare fee is not discounted.
- **Rooms.** Every Women's Retreat guest picks a room: quad (55%), double (30%), or single (15%). Single rooms run out after six, as Rock hides the choice once every use is taken.
- **Discounts.** Rock gives one discount: the first automatic one valid that day, or a code the person typed. The Women's Retreat early bird runs through October 31. After it ends, half of the friends who register together use FRIEND, 10% off. 8% of Money Matters registrations use SCHOLARSHIP, 100% off, leaving two of its five uses.
- **Payments.** VBS and Date Night are paid in full when registering, since neither has a minimum payment. Money Matters has no minimum, so 65% pay later and 35% pay in full. Women's Retreat guests pay in full (40%), pay the $50 deposit (35%), or start a monthly payment plan (25%). Parents' Night Out and Serve Day are free and take no payment.

### Placements and answers

- **VBS crews.** Once registration closes, each kid is in the crew for their grade on the first day of VBS, from the Pre-K crew to the 5th grade crew. A kid with no grade yet is in the Pre-K crew.
- **Serve Day teams.** Everyone on one registration is on one project team, and the teams fill evenly.
- **Answers.** VBS asks who will pick up each child, answered with a parent or guardian. Serve Day asks for a shirt size. 70% of Women's Retreat guests who came with friends ask for one of them as a roommate.
- **Allergies.** VBS and Parents' Night Out ask Rock's Allergy person attribute, which Rock saves to the child, so kids check-in prints it. About 6% of generated kids answer it, from a short list such as "Allergic to peanuts." Each child's answer is drawn once, so every form shows the same value, and it is saved once, from the first registration that asked. A child who already has a value, written by an earlier step, keeps it, so nothing is written twice. A Cast child answers only with the allergy their household file holds, such as Alex Decker's peanut allergy. The Parents' Night Out question is not required, so most families leave it blank (decision 211).

### Cast stories

| Who | Registration | Expected |
|---|---|---|
| Noah and Alex Decker | Last summer's VBS at Phoenix | Registered together by Cindy, paid in full, each in one crew, the crew for their grade. Alex keeps her peanut allergy, which the VBS form asks and Rock holds |
| Ted and Cindy Decker | The next Date Night at Phoenix | Registered together by Ted, with childcare for two, paid in full |
| Adrian and Camila Cruz | Last summer's VBS at Phoenix | Registered together by Gabriela, paid in full, each in one crew. Their household's story says the Cruzes registered them for VBS. |
| Bri Hall | The last Serve Day at Phoenix | Registered herself, on the School Supplies team, as her note says she sorted school supplies |

A Cast registration can name the placement group it joins (`placements`) and the fees it always adds (`fees`). Its `expect` entries can check the group by name (`placedIn`) and each fee's quantity (`fees`). Bri is a Participant whose only touch is that Serve Day, and the registration is part of that touch, so her story still holds.

## How many: kids and adults

On install day October 29, 2026, at the default seed. Each cell is people registered, off the wait list, with the wait list in parentheses where there is one. Core 3 has the same Cast at Phoenix as Core 1, and generated households at Peoria and Gilbert.

| Registration | Core 1 | Core 3 |
|---|---|---|
| VBS, last summer | Phoenix 15 (1), full | Phoenix 15 (1), full; Peoria 7; Gilbert 5 |
| VBS, next summer | Not open yet | Not open yet |
| Parents' Night Out | Phoenix 10 (3), full | Phoenix 10 (3), full; Gilbert 1 |
| Serve Day | Phoenix 27 on 25 registrations | Phoenix 27; Peoria 9; Gilbert 11 |
| Money Matters | Phoenix 3 | Phoenix 3; Gilbert 1, with the scholarship code |
| Date Night | Phoenix 5 couples, 2 with childcare | Phoenix 5 couples, 2 with childcare |
| Women's Retreat | 6 women on 3 registrations | 16 women on 12 registrations |
| All registrations, students included | 58 registrations, 82 registrants, 42 payments ($4,133) | 109 registrations, 137 registrants, 87 payments ($9,814) |

The early bird runs through October 31, so every Women's Retreat registration on that day has it, and none uses the friend code yet. Full 7 was not loaded for this slice. Its shares are scaled so that, by estimate, VBS fills at Phoenix but not the smaller campuses, each campus has about 25 in Money Matters and 30 couples at Date Night, and about 40 women have registered for the retreat by late October. The lead's Full 7 verify gives its real numbers.

<a id="release-forms"></a>

## Release forms

Camp and the mission trip each require a release, signed with Rock's own electronic signature (decision 212). The signature is typed, because Rock encrypts a drawn one with each install's own key (decision 194).

| Release | Template | What it says |
|---|---|---|
| Student Camp Release | Student Camp | Permission to attend and ride the bus, medical care when no one can reach the parent, the risks of camp, and camp photos |
| Mission Trip Release | High School Mission Trip | Permission to travel and serve, a valid passport, medical care, and the risks of the trip |

Rock's template screen always saves a template with the Embed action. So a person signs each registrant's release while registering, and Rock will not finish the registration without it. Every registrant has signed, on past and open registrations alike. The proposal wanted a few open ones still waiting, but Rock cannot leave one of its own releases waiting, so nothing is pending. The two templates also turn off external registration updates, as Rock's screen does, so a signed release cannot be changed later.

Each signed release is the one Rock's registration entry saves:

- **Who.** The registrar signs, and the release is assigned to them. It applies to the registrant. So a parent signs for a child, and a student who registers themselves signs their own.
- **What.** The release's Lava, rendered for the registrant, with their first name, the instance's name, and the church's name. The typed name is the registrar's full name. The email is the registrar's, or one they type when Rock has none for them.
- **When.** A minute or two before the registration was saved, on the same day. Rock's Electronic Signature Receipt emails the PDF to the signer right after.
- **Where from.** An IP address from the ranges set aside for documentation (the same one the person's email opens use), and one browser per registrar.
- **The hash.** Rock's verification hash over the signed fields, computed the way Rock's SignatureDocumentService computes it. Rock's code uses SHA-1, so the loader does too. Its port is checked against Rock's own value.
- **The PDF.** A small PDF drawn at install, in Rock's Digitally Signed Documents file type: the release's title and text, then Rock's signature box with the typed name, the signer, the date, the IP address, the agreement, and the reference code, which is the hash.

On install day October 29, 2026, at the default seed:

| Signed releases | Core 1 | Core 3 |
|---|---|---|
| Camp, last summer | 3 | 8 |
| Camp, next summer | 3 | 6 |
| Mission trip | 1 | 2 |
| Pending | 0 | 0 |

<a id="notes-and-history"></a>

## Notes and history

Every registration has the notes and history Rock's registration entry writes as it saves (decision 213), dated that moment and written by the registrar:

- **The registration's history.** "Added Registration", then the registrar's first name, last name, and email, the discount code, percent, and amount, the linked group, and the campus. A registration's campus is the linkage's, or else the registrar's family's, as Rock sets it.
- **Each registrant's history,** captioned "Registrant:" and their name: a person attribute the form saved to them, such as an allergy, each fee with its quantity and cost, and each answer.
- **Payments.** "Added Payment" for each payment made on the website. A payment plan's payments come through Rock's job, which writes none.
- **Notes.** Every template turns on Add Person Note, so each registrant gets a note of Rock's Event Registration type, "Registered for" the instance, and "by" the registrar when someone else registered them, plus a "Registered" line on their History tab. The registrar gets a note and a line naming the people they registered. Rock names the registrar by first and last name when their first name and nick name differ, even on a note for themselves, so the notes do too.

Registration notes use Rock's own Event Registration note type, which the Notes part never touches, so their types and security stay apart.

On the same day, from 58 registrations in Core 1 and 109 in Core 3:

| Notes and history | Core 1 | Core 3 |
|---|---|---|
| Person notes | 114 | 184 |
| History rows | 590 | 1,117 |

<a id="reminders"></a>

## Reminders

Reminders look as if Rock's jobs ran on schedule (decision 214):

- **Registration Reminder** runs every hour. When an instance's reminder time has passed, it emails each registration and marks the instance's reminder sent at that time.
- **Event Payment Reminders** runs at 9 AM on weekdays. Each registration's last reminder is when that job last emailed it ([Payments](#payments)).

Both jobs' emails would also leave a communication record in Rock. They are not written, the same as the confirmation and signature receipt emails, because their Lava renders the church's email header and a sign-in link only the install can make.

<a id="data-views"></a>

## Data views

Three person data views sit in a Registrations child of the church's own data view category (decision 215). Their filters are Rock's own, saved the way Rock's data view editor saves them, so each opens and edits like one a person made.

| Data view | Filters | Core 1 | Core 3 |
|---|---|---|---|
| Next Summer's Campers | Registrants of next summer's camp, girls or boys, not on a wait list | 3 | 6 |
| Students Not Registered for Camp | Grade 7 or above, at least two student service check-ins in the last 120 days, and not registered for next summer's camp | 7 | 18 |
| High School Students Attending the Student Service | Grade 9 or above, and at least two student service check-ins in the last 120 days | 6 | 14 |

The counts are for install day October 29, 2026. The last view is the mission trip's eligibility data view, so Rock lets only those students register. The loader replays it from the Student Path's check-ins, so each trip registration falls on a day the student was in it, and an invariant checks that in SQL.

The proposal also listed "Campers with a balance due." Rock has no data filter for a registration's balance, so it is left out. Rock's registrations list filters by balance due, and its Send Payment Reminder page lists who owes.

## Checks

- **Invariants** (`validation/invariants.sql`): every linkage points at an occurrence of an active, approved event on a calendar, at the occurrence's campus, with an active instance and template. Slugs match Rock's pattern and are unique. Each instance opens before it closes, and its event comes after it closes and on or after its payment deadline. Placement groups are of their placement's group type and template. Linked groups are Event Attendees groups. The admin role can edit each category. At least seven events have a registration open today. For the people: each registrant's template matches; each registrant is in the template's grades on the day they registered and on the event day; registrations fall inside their window and before today; registrants off the wait list fit the limit and are in the linked group; a wait list exists only on a full instance; placed people are registrants of a linked instance; payments are Event Registration transactions from the registrar on the instance's account, never more than what is owed, and paid up once the deadline has passed; plans use a frequency the template allows and end by the deadline; codes stay within their uses; and when the steps part loaded, the campers at every camp that has ended are exactly the people with a Student Camp step there. For release forms, notes, history, and data views: every registrant on camp and the mission trip has a signed release for them from that template, with a PDF, the registrar as signer, a signed time just before the registration on the same day, reserved values, and a verification hash the check recomputes; every registration has its "Added Registration" history, and each registrant their note and "Registered" line; data views about registrations sit under Registrations; and every mission trip registrant was in its eligibility data view on the day they registered. For kids and adults: a free registration takes no payment; a fee carries its item's cost, and a quantity only where the fee allows it; a quantity fee on a template with no minimum payment is paid in full; no fee item passes its limit, and every required fee is picked; registrants of a same-family template share a family, and each Date Night registration is one married couple; every registrant meets the template's ages and gender on both days; everyone on a closed instance is placed, VBS kids in their grade's crew, and a Serve Day registration on one team; and the Allergy attribute is written only for a registrant whose form asks it.
- **check-registrations.js**: every instance's event, template, contact, groups, and placements exist; slug patterns make valid slugs that cannot collide; templates ask for what their eligibility needs; and every feature in the table above sits on an always-open instance. For the people: each registrants rule names one audience, students name a Student Path event step or a share, a paid template's payments add up to 100 and are ones the template allows (pay later only with no minimum), a free template has none, fees, codes, and placements are the template's, and attribute fills fit their attributes. A discount that needs several registrants comes before an early bird. Each release's Lava uses only what the loader renders and starts with a title, each data view is filed under a church category and names an instance rule the loader can fill, and the mission trip's eligibility view counts exactly the student check-in areas, over a "Last N days" window, from the template's lowest grade, which is what the loader replays. Run it with `node validation/check-registrations.js`.
- **validate-cast.js**: each Cast registration names an instance rule with registrants, one of its occurrences, members of the household, and a payment, plan, placements, placement groups, fees, and discount its template and instance have.
- **Cast expectations**: the Cast registrations above, on every load.
- **loader-compat-check**: plans the registrations for install day October 28, 2026 and compares every date with the table above. It also checks the port of Rock's signature verification hash against three values computed by calling Rock 21's own Rock.dll (Rock 20's gives the same).
- **Rock lock**: `validation/rock-lock.json` watches the Rock code the release forms, notes, history, reminders, and data view filters follow, so a change in Rock flags the port.
- **Rock Cleanup referee**: Rock Cleanup must find nothing to fix.

## The registrations part

Registrations are their own loader part, `Registrations` ("Event registrations"). They need the calendar only: every linkage points at an occurrence the calendar wrote (decision 189). The step takes the calendar's plan instead of planning the calendar again. Students register from the Student Path plan every load makes, so the part does not need steps (decision 206). Kids and adults register from the people and the small group and serving plans, which every load makes too, so it needs neither the small groups nor the serving part (decision 209). A package built before registrations existed has no `configuration/registrations.json`, and the step skips. A package with no `configuration/steps.json` has no students to register, so only the student registrations are skipped. The release forms, notes, history, and data views are part of the same step. The data views are filed in `configuration/categories.json`, which every package has, and the check that each mission trip registrant was in its eligibility view runs only when the load has attendance.

## Later

1. **Refunds.** One cancelled mission trip spot with a refunded deposit.
2. **Batch history.** Rock writes financial batch history when a registration payment lands in a batch. The loader does not write it yet.
3. **Registration emails as communication history.** The confirmation, reminder, payment reminder, and signature receipt emails (decision 214).
