# Event registrations

Rock Solid Church's event registrations: the templates, each year's instances, and how they link to the calendar. They live in [configuration/registrations.json](../configuration/registrations.json). The loader's registrations step writes them (decision 189).

Registrations come in four slices. Slice 1, the setup, is built. It writes everything a church sets up before anyone registers, so every upcoming event page shows its Register button. Slice 2 is built too: the [students who registered](#who-registered-students) for camp, the mission trip, and the fall retreat, with their payments and placements. The [later slices](#later) add kids and adults.

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
| Registrant attributes and a person attribute | Camp (cabin mate, emergency contact), Parents' Night Out (allergies, saved to the child) |
| "How did you hear about this event?" | Every registration |

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

Students register from the Student Path plan (decision D-registrations-8). The loader plans the camps, trips, Follow Jesus decisions, and Student Leader steps once in every load, from the people, the attendance plan's student check-ins, the Cast stories, and the seed. The steps part copies those steps, and the registrations part reads them. So the registrations part needs only the calendar, as decision 189 says, and last summer's campers still match the camp steps. The steps are the same as before the plan moved: Core 3's Step rows, program completions, and baptism values are identical, row for row.

### Who

| Registration | Who registers |
|---|---|
| Camp, last summer | Exactly the students with that camp's Student Camp step, girls on the girls' instance and boys on the boys'. Everyone paid in full and is on one bus, in one cabin, and in two activities. |
| Camp, next summer | 45% of the students who could go (the Student Camp step's own rate): in grades 7 to 12 at camp, and attending the student service on install day. Early registrants already have their two activities. Nobody has a bus or cabin yet. |
| Mission trip, next | 20% of the high school students who could go (the Mission Trip step's own rate), until the limit fills. After that, 1 in 5 of the rest join the wait list. Members join the team group as Pending, as the template says. |
| Fall retreat | 30% of the students who attend the student service, plus every current Student Leader. |

A student registers on a day Rock lets them: Rock checks a registrant's grade on the day they register, so a rising 7th grader registers for camp only after June 1. Every registrant is in the template's grades on that day and on the event day (decision D-registrations-9).

- **Siblings** in one family register together on one registration, up to the template's limit. Camp's girls and boys have their own instances, so a brother and sister register on two registrations, made the same day.
- **The registrar** is a parent or guardian in the student's family, one with an email when there is one. A student whose parents are not in Rock registers themselves. The registration keeps the registrar's name and email, and the linkage's group and campus.
- **When.** Each family's registration day is a seeded draw across the whole window. Camp registrations bunch up in the three weeks after opening, before the early bird ends on April 30, and before the payment deadline. The trip's bunch up in its first month, and the retreat's after opening and before closing. Only registrations dated before the install day are written, so how many have registered follows how far into the window the install day is.
- **Limits.** Camp has no wait list, so a full camp stops taking registrations. Last summer's campers must all fit, so Full 7's camp limit is 300 per instance.

### Discounts, fees, and answers

Rock gives a registration one discount: the first automatic one, in the template's order, that is valid that day. So camp's sibling discount comes before its early bird, and siblings who register together get $25 off each, while anyone else who registers by April 30 gets $30 off. Current Student Leaders register for the retreat with the STUDENTLEADER code, $40 off, and the loader leaves two of its 12 uses for someone trying it on a fresh install. Jasmine Washington graduated in May, so she is no longer a Student Leader and does not register.

About 40% of campers add a camp T-shirt and 35% of retreat students a hoodie, older students in larger sizes. Each fee names its fee item, which Rock's placement board needs. Camp asks for an emergency contact (a parent or guardian other than the registrar when there is one) and, a third of the time, a cabin mate near the camper's grade. The trip asks about passports, and every registration answers "How did you hear about this event?"

### Payments

Each payment is an Event Registration transaction from the registrar, from the Website, through the Test Gateway, with the registrar's own card or bank account (the same wallet their gifts use). Its one detail points at the registration and posts to Rock's Event Registration account. The transaction carries the summary Rock's registration entry writes. Payments land in daily batches named the way Rock names them: "Event Registration" and the card type's batch suffix, such as "Event Registration VMD" for Visa, MasterCard, and Discover, or "Event Registration ACH". Payment plan payments come through Rock's Download Payments job, so they land in its "Online Giving VMD" batches. Each batch's control amount is its total, and a batch older than a week is closed (decision D-registrations-10).

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

## Checks

- **Invariants** (`validation/invariants.sql`): every linkage points at an occurrence of an active, approved event on a calendar, at the occurrence's campus, with an active instance and template. Slugs match Rock's pattern and are unique. Each instance opens before it closes, and its event comes after it closes and on or after its payment deadline. Placement groups are of their placement's group type and template. Linked groups are Event Attendees groups. The admin role can edit each category. At least seven events have a registration open today. For the people: each registrant's template matches; each registrant is in the template's grades on the day they registered and on the event day; registrations fall inside their window and before today; registrants off the wait list fit the limit and are in the linked group; a wait list exists only on a full instance; placed people are registrants of a linked instance; payments are Event Registration transactions from the registrar on the instance's account, never more than what is owed, and paid up once the deadline has passed; plans use a frequency the template allows and end by the deadline; codes stay within their uses; and when the steps part loaded, the campers at every camp that has ended are exactly the people with a Student Camp step there.
- **check-registrations.js**: every instance's event, template, contact, groups, and placements exist; slug patterns make valid slugs that cannot collide; templates ask for what their eligibility needs; and every feature in the table above sits on an always-open instance. For the people: each registrants rule names a Student Path event step or a share, payments add up to 100 and are ones the template allows, fees and placements are the template's, and attribute fills fit their attributes. A discount that needs several registrants comes before an early bird. Run it with `node validation/check-registrations.js`.
- **validate-cast.js**: each Cast registration names an instance rule with registrants, one of its occurrences, members of the household, and a payment, plan, placements, and discount its template has.
- **Cast expectations**: the Cast registrations above, on every load.
- **loader-compat-check**: plans the registrations for install day October 28, 2026 and compares every date with the table above.
- **Rock Cleanup referee**: Rock Cleanup must find nothing to fix.

## The registrations part

Registrations are their own loader part, `Registrations` ("Event registrations"). They need the calendar only: every linkage points at an occurrence the calendar wrote (decision 189). The step takes the calendar's plan instead of planning the calendar again. Students register from the Student Path plan every load makes, so the part does not need steps (decision D-registrations-8). A package built before registrations existed has no `configuration/registrations.json`, and the step skips. A package with no `configuration/steps.json` has no students to register, and those registrations are skipped.

## Later

1. **Kids and adult registrations (slice 3).** VBS, Parents' Night Out, Serve Day, Money Matters, Date Night, and the Women's Retreat, with their wait lists and placements.
2. **Signatures and finish (slice 4).** The camp and mission trip release forms with typed signatures and a PDF drawn at install, a few ready-made data views, and the notes and history Rock writes on each registration (decision 194).
3. **Refunds.** One cancelled mission trip spot with a refunded deposit.
