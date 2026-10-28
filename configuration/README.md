# Configuration

Configuration is the church setup the data depends on: campuses, connection statuses, group types, financial accounts, check-in areas, schedules, and similar. The loader saves these rows through Rock's services, because there are few of them and they need full model behavior.

## Files

| File | What it defines |
|---|---|
| `campuses.json` | Campuses, the datasets each appears in, and which service time slots each offers. |
| `schedules.json` | The shared Sunday service time slots and the campus schedule types (Weekend Service, Student Service). |
| `accounts.json` | Funds. The General Fund and the Built on the Rock campaign fund have one child account per campus. |
| `checkin.json` | Check-in areas, groups, rooms, and which services each group meets at. |
| `connection-statuses.json` | The six connection statuses. |
| `giving-alert-types.json` | The giving alert types: Large Gift, Smaller Than Usual, and Late Gift. Rock ships none. |
| `achievements.json` | The Kids Check-in streak type and its seven achievement types. |
| `serving.json` | The serving teams at every campus: their rooms, service times, and staffing, the background check requirement, volunteer check-in, and the rules for who serves and how scheduling history is generated. |
| `connections.json` | Rock's Involvement connection type, its request statuses, the eight serving opportunities, and the rules for generating requests to serve. These are request statuses, not the person connection statuses in `connection-statuses.json`. |
| `prayer.json` | A weight for each of Rock's own prayer categories. Rock ships the categories; none are added or renamed. |
| `security-roles.json` | How the Cast's role names map to the security roles Rock ships. The sample church adds no roles. |
| `communications.json` | The church's texting numbers, who sends mass emails and texts, who welcomes first-time guests, who approves sends, Rock's communication topics, the branded email template, and the communication lists with the rules for who is on them. See [Communications](../docs/communications.md#the-setup). |
| `notes.json` | The six note types on person records, their security, the staff who write notes, and each type's share of generated notes. |
| `campaign.json` | The Built on the Rock building campaign: its fund, description, and dates, and the Rock settings that point at it. See [Pledges](../docs/pledges.md). |
| `steps.json` | The step programs: the adult Discipleship Path and the Student Path. Each has its completion flow, statuses, step types in path order, and prerequisites, and each type says where its steps come from, with the class schedules, the camp and trip dates, and the designed rates. |
| `logins.json` | Who among generated adults has a Rock Database login: each reason and its designed chance, the inactive and older-adult shares, and the unconfirmed share (decision 179). The Cast's logins are in their own files. Logins load only when the person installing gives a password. See [How logins load](../loader/README.md#how-logins-load). |
| `calendar.json` | The church's calendar: its events on Rock's Public and Internal calendars, with audiences, campuses, places, contacts, and the rule for each event's dates. See [Calendar](../docs/calendar.md). |
| `church-app.json` | The Rock Solid Church App, the sample church's mobile app: its site and layouts, the share of active adults with it and their devices, and how often pushes are opened. See [Communications](../docs/communications.md#push). |
| `categories.json` | Every category the church adds to Rock, in one tree per kind of thing it holds. See [Categories](#categories). |
| `group-syncs.json` | The church's data views, with their filters in Rock's own data filter terms, and the group syncs that keep a list or security role in step with them, with each sync's interval, emails, and create login setting, and the Cast people each synced group must hold. See [Group sync](../docs/communications.md#group-sync). |

## Categories

Every category the church adds is in `categories.json` (decision 185), so its rows never mix with what Rock ships and later work knows where to file new rows. Categories always load, with the church setup.

| Holds | Tree | Used by |
|---|---|---|
| Data views | Rock Solid Church, then Group Sync | The five group sync data views |
| Communication lists | Care | The New Here and We Miss You lists |

To add rows of a new kind or for a new purpose:

1. **Data views.** Add a child of Rock Solid Church for the purpose, such as Communications, Care, or Reporting, then file the views under it. Never put a church view at the top level or in a category Rock ships.
2. **Communication lists.** Keep list categories flat, as Rock's own Public category is. A public list uses Rock's Public category. A private list may use a church category such as Care, through its `categoryGuid` in `communications.json`.
3. **Another kind of row,** such as system communications or reports. Add a top-level category named Rock Solid Church for that kind, with children by purpose, as the data views have.

Each category names the Rock entity it holds, its parent's key, and, for a group category, the group type it is for.

## Service times

There are no Saturday services. Every campus uses the same Sunday time slots: 9:00am, 10:30am, and 12:00pm. Campuses differ only in which slots they offer. Service times are set through Rock's campus schedules.

The student service meets at Sunday 10:30am at every campus. On each campus, Sunday 10:30 is listed twice in campus schedules: once as "Weekend Service" and once as "Student Service."

| Campus | Sunday services | Student service |
|---|---|---|
| Phoenix | 9:00, 10:30, 12:00 | 10:30 |
| Peoria | 9:00, 10:30 | 10:30 |
| Gilbert | 9:00, 10:30 | 10:30 |
| Scottsdale | 9:00, 10:30 | 10:30 |
| Chandler | 9:00, 10:30, 12:00 | 10:30 |
| Goodyear | 9:00, 10:30 | 10:30 |
| Tempe | 10:30, 12:00 | 10:30 |

## Funds

The church has four funds: General Fund, Built on the Rock, Mission Fund, and Event Registration. All four are funds Rock ships. The loader sets each fund's name and public name from `accounts.json`.

General Fund and Built on the Rock each have a child account per campus (for example "Phoenix General Fund"), with Rock's `UsesCampusChildAccounts` turned on. Gifts post to the giver's campus account. Mission Fund and Event Registration have no child accounts.

Built on the Rock is the building campaign's fund (decision 98). It is Rock's Building Fund, renamed, so it keeps Rock's Guid. The church has no standing building fund: upkeep comes from the General Fund (decision 99), so the fund has no gifts from before the campaign. Its start and end dates are the campaign's: Commitment Sunday is the Sunday 40 weeks before install day, and the campaign lasts two years. Its campus accounts share those dates. Rock's giving page hides an account outside its dates, so the fund takes gifts only during the campaign.

The loader also points two Rock settings at the fund (decision 100). Rock has two pledge pages: (Public) Create Pledge in the Rock admin site, and Pledge under Give on the external website. The Pledge Entry block on each uses the fund and the campaign dates, and shows the pledge frequency as an optional choice. Rock ships the WebForms Pledge Entry block, and a Rock job swaps it for the Obsidian one after Rock starts, so the loader finds the block on each page by either block type. Rock's default contribution statement template shows pledges to the fund and counts gifts to its campus accounts toward them.

## Notes

People have six note types (decision 78). Three are Rock's own, updated in place: Personal Note, Pastoral Note, and Phone Note. Rock ships Pastoral and Phone hidden from the "add a note" picker, so they are made selectable. Three are new: Follow-up, Counseling, and Safety Alert.

| Type | Who can view | Used for |
|---|---|---|
| Personal Note | Staff | Everyday staff notes. |
| Pastoral Note | Staff | Visits, hospital stays, and care calls. |
| Follow-up | Staff | Next steps after a visit, a class, or a conversation. |
| Phone Note | Staff | Call logs. |
| Counseling | Pastoral workers only | Counseling sessions, in general terms. |
| Safety Alert | Staff | Pickup restrictions, serious allergies, and serving restrictions. Every one is an alert. |

"Staff" means Rock's Staff Workers and Staff Like Workers roles, plus Rock Administration. Everyone else is denied, so notes never show to someone who is not staff.

Pete Foster, Ted Decker, Alisha Marble, and Daniel Peak write the main campus's notes. Every other campus has a campus pastor: its oldest Leading adult, or its oldest Engaged adult if it has none. Campus pastors join RSR - Staff Workers and RSR - Pastoral Workers, so they can view the notes they write, Counseling included. The church office (Alisha) still writes every campus's phone notes and safety alerts.

## Check-in

Check-in uses Rock's built-in Weekly Service Check-in template, its areas, its groups, and their age and grade rules, unchanged. Every campus gets the same building and room names. Kids groups meet at every Sunday service their campus offers. Jr High and High School check in at the Sunday 10:30am student service. Adult worship attendance uses Rock's Service Attendance template, located at each campus with its weekend services.

## Kids achievements

One weekly streak type, Kids Check-in, counts every check-in under the Weekly Service Check-in template, from the nursery through grades 9-12. Seven achievement types read it. Week-in-a-row types show Rock's trophy, and the others show its medal. The template's Achievement Types setting lists all seven, so next-gen check-in shows them.

| Achievement | Goal | Earned |
|---|---|---|
| High Five | 5 check-ins | Once |
| Back-to-Back | 2 weeks in a row | Again and again |
| Four-Peat | 4 weeks in a row | Again and again |
| Unstoppable | 10 weeks in a row | Once |
| 25 Club | 25 check-ins | Once |
| Half Century | 50 check-ins, after the 25 Club | Once |
| Century Club | 100 check-ins, after Half Century | Once |

Each entry in `achievements.json` has a key, a Guid, a name, a description for kids, a component (`consecutive` or `accumulative`), a target, how many times it can be earned (`null` is unlimited), an optional prerequisite, a Font Awesome icon, and a highlight color. A prerequisite must come before the types that need it. Rock's own Weekly Attendance streak type and its two achievement types stay as Rock ships them.

## Serving and connections

Every campus gets eight serving teams under a "{campus} Serving Teams" parent, and each team has a serving opportunity in Rock's Involvement connection type. The main campus teams take their Guids from the Cast rosters in `cast/groups/serving-*.json`; every other team's Guid comes from its campus and key. See [Serving](../docs/serving.md) for the design and the reasons.

## Prayer categories

Prayer requests use the 19 categories Rock ships under All Church, unchanged (decision 67). Each has a weight: its share of generated requests. General holds 65%, and the top 5 hold about 89%, which matches the measured shape. The rest share what is left, from Finances/Job at 2.2% down to Current Events and Nation at 0.3%. Suicidal Tendencies has weight 0, because that text is too heavy for sample data shown in demos. The category stays in Rock, empty. Each category's text pool is in [content/prayer](../content/prayer/README.md).

## Campuses

Campuses are named by city only, with no "Campus" suffix. All are in the Phoenix area.

| Campus | Datasets | Notes |
|---|---|---|
| Phoenix | Core 1, Core 3, Full 7 | Rock's built-in main campus, renamed. North Phoenix, where the Cast lives. |
| Peoria | Core 3, Full 7 | West Valley. |
| Gilbert | Core 3, Full 7 | East Valley. |
| Scottsdale | Full 7 | Older, higher-income households. It was an independent church until it merged with Rock Solid Church and became a campus. |
| Chandler | Full 7 | Young families; large kids ministry. |
| Goodyear | Full 7 | Fast-growing far west; more first-time guests. It meets in a high school auditorium, so volunteers set up and tear down every Sunday. |
| Tempe | Full 7 | Urban; singles and young adults. |

## Connection statuses

The sample church uses six statuses. Rock code refers to the system values by Guid, so we rename them instead of deleting them. Every code path that assigns a system status lands on one of these six.

| Rock system value | Sample data name | Meaning |
|---|---|---|
| Prospect | Prospect | Created from a web or mobile form. Contact details only. |
| Participant | Participant | Took part in one thing in person. Not really known yet. |
| Visitor | Known | Has visited. We know who they are. |
| Attendee | Connected | Attends regularly. |
| Member | Engaged | In a group, serving, or giving. |
| (new value) | Leading | Leads a group or a team. |

## Steps

Rock's Discipleship Path is the adult step program, built out to seven step types (decisions 83 to 89). Rock's four types keep their Guids and are updated in place; Foundations, Give, and Lead are new. Each type's `source` names the record its steps come from: memberships (Small Group, Serve, Lead), the giving journey (Give), the Baptism Date (Baptism), or a class (Starting Point Class, Foundations), with the class schedule and its designed share of active adults. See [Steps](../docs/steps.md) for the design and the reasons.

The Student Path is a second program in the same file (decisions 88 and 115 to 119). It is new, so it has its own Guid, its category (Rock's Discipleship category), its icon, and its own In Progress and Complete statuses. Its `students` rule names the student service's check-in groups from `checkin.json` and when a student has been attending. Its four types read student check-ins: `event` (Student Camp and Mission Trip, with each season's dates), `decision` (Follow Jesus, often at camp), and `school-years` (Student Leader, from a school year's start to graduation). Each has a `percentOfStudents`, the share of the students who could take it. The Baptism type's `students` rule says how students are baptized. Step type keys are unique across programs.
