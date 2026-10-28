# Event registrations

Rock Solid Church's event registrations: the templates, each year's instances, and how they link to the calendar. They live in [configuration/registrations.json](../configuration/registrations.json). The loader's registrations step writes them (decision D-registrations-1).

Registrations come in four slices. Slice 1, the setup, is built. It writes everything a church sets up before anyone registers, so every upcoming event page shows its Register button. Nobody has registered yet. The [later slices](#later) add the people.

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

Seven registrations are open on every install day: camp, the mission trip, the fall retreat, Parents' Night Out, Money Matters, Date Night, and the Women's Retreat (decision D-registrations-2). Each registration feature has at least one of them as its home, so anyone can try any feature on a fresh install.

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

Each template has a category (Students, Kids, Adults, or Outreach), forms, and the terms Rock shows. Its emails and success text are copied from Rock's own Baptism template, so they read the way Rock writes them. Paid templates use Rock's Test Gateway with the batch prefix "Event Registration", and every instance posts to Rock's Event Registration account (decision D-registrations-5).

Rock's v19 upgrade gave its "RSR - Event Registration Administration" role Edit and Administrate on every template category that existed then. The four new categories get the same.

Who may register is stored the way Rock's template screen stores it, in `AdditionalSettingsJson` under `RegistrantEligibilitySettings`. Rock refuses a registrant it cannot check, so a template that limits grade asks for grade, one that limits age asks for a birthdate, and one that limits gender asks for gender. VBS takes ages 4 to 11, not "age 4 to grade 5", because a 4-year-old has no grade and Rock would refuse them.

One camp template serves both the girls' and the boys' instances, so each has its own limit (decision D-registrations-3). Rock's gender rule belongs to the template, so camp does not use one; the instance names say who each is for.

## Dates

Every date is a rule counted from the install day, through the calendar's own occurrences (decision D-registrations-2). An event's dates at one campus form a list. A yearly event has one date per occurrence. A monthly event such as Parents' Night Out is one occurrence with a start each month.

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

Linked groups use the Event Attendees group type, under the Guid Rock reserves for it. A new Rock database does not have that type, so the loader adds it (decision D-registrations-7). Placement groups use five new group types: Camp Bus, Camp Cabin, Camp Activity, VBS Crew, and Serve Day Team, each with a leader role and a default member role.

Groups with the same key are one group. So the girls' and boys' camp instances for one year share their linked group, their buses, and their activities, and each has its own cabins. Each placement group is linked to its instance the way Rock's placement screen links it: a `RelatedEntity` row with the purpose key `PLACEMENT`, qualified by the template placement's Id. A group that two instances share has a row for each.

No placement groups sit on the template (decision D-registrations-4). Rock would share a template's groups with every instance in every year, so last year's campers would ride this year's buses.

| Placement groups | Core 1 | Core 3 | Full 7 |
|---|---|---|---|
| Camp buses, per year | 2 | 3 | 6 |
| Camp cabins, per year, girls and boys each | 2 | 5 | 12 |
| Camp activities, per year | 6 | 6 | 6 |
| VBS crews, per campus and year | 7 | 7 | 7 |
| Serve Day project teams, per campus | 5 | 5 | 5 |

## Checks

- **Invariants** (`validation/invariants.sql`): every linkage points at an occurrence of an active, approved event on a calendar, at the occurrence's campus, with an active instance and template. Slugs match Rock's pattern and are unique. Each instance opens before it closes, and its event comes after it closes and on or after its payment deadline. Placement groups are of their placement's group type and template. Linked groups are Event Attendees groups. The admin role can edit each category. At least seven events have a registration open today.
- **check-registrations.js**: every instance's event, template, contact, groups, and placements exist; slug patterns make valid slugs that cannot collide; templates ask for what their eligibility needs; and every feature in the table above sits on an always-open instance. Run it with `node validation/check-registrations.js`.
- **loader-compat-check**: plans the registrations for install day October 28, 2026 and compares every date with the table above.
- **Rock Cleanup referee**: Rock Cleanup must find nothing to fix.

## The registrations part

Registrations are their own loader part, `Registrations` ("Event registrations"). They need the calendar only: every linkage points at an occurrence the calendar wrote (decision D-registrations-1). The step takes the calendar's plan instead of planning the calendar again. A package built before registrations existed has no `configuration/registrations.json`, and the step skips.

## Later

1. **Student registrations (slice 2).** Camp for both years, the mission trip, and the fall retreat: registrants, payments through the Test Gateway, payment plans, balances, the linked group's members, and people placed on buses, in cabins, and in activities. Last summer's campers are exactly the students with last summer's Student Camp step.
2. **Kids and adult registrations (slice 3).** VBS, Parents' Night Out, Serve Day, Money Matters, Date Night, and the Women's Retreat, with their wait lists and placements.
3. **Signatures and finish (slice 4).** The camp and mission trip release forms with typed signatures and a PDF drawn at install, a few ready-made data views, and the notes and history Rock writes on each registration (decision D-registrations-6).
4. **Refunds.** One cancelled mission trip spot with a refunded deposit.
