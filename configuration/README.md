# Configuration

Configuration is the church setup the data depends on: campuses, connection statuses, group types, financial accounts, check-in areas, schedules, and similar. The loader saves these rows through Rock's services, because there are few of them and they need full model behavior.

## Files

| File | What it defines |
|---|---|
| `campuses.json` | Campuses, the datasets each appears in, and which service time slots each offers. |
| `schedules.json` | The shared service time slots, Sunday mornings and Wednesday Student Night, and the campus schedule types (Weekend Service, Student Service). |
| `accounts.json` | Funds. The General Fund and the Built on the Rock campaign fund have one child account per campus. |
| `checkin.json` | Check-in areas, groups, rooms, and which services each group meets at, with the template's settings, each area's labels, room limits, test check-in at every campus, supervisor PINs, and the Crowd's check-in adults. |
| `connection-statuses.json` | The six connection statuses. |
| `giving-alert-types.json` | The giving alert types: Large Gift, Smaller Than Usual, and Late Gift. Rock ships none. |
| `refunds.json` | The refunds staff make on generated households' gifts: who refunds them, the rate when the profile has none, and four kinds, each with a Rock refund reason, a share, a delay, and the summaries staff type. See [Refunds](../docs/refunds.md). |
| `achievements.json` | The Kids Check-in streak type and its seven achievement types. |
| `small-groups.json` | Every small group rule: the group type, roles, and parents, the topics (Women's Bible Study and Men's Bible Study planned by share), size, leaders, hosts, schedule, the church year, attendance, membership with each household's attendance window, launch groups led from Ready to Lead, test cases, and the Cast's pins. See [Small groups](../docs/small-groups.md). |
| `peer-network.json` | The group types with Rock's Peer Network on, Small Group (close) and Serving Team (casual), with their strength and growth, and who follows whom: campus pastors follow their small group leaders, and staff follow up to 25 people in their care. See [Peer network](../docs/small-groups.md#peer-network). |
| `serving.json` | The serving teams at every campus: their rooms, service times, and staffing, the background check requirement (the checks themselves are in `background-checks.json`), volunteer check-in, and the rules for who serves and how scheduling history is generated. |
| `connections.json` | Rock's Involvement connection type, its request statuses, the eight serving opportunities, and the rules for generating requests to serve and their celebrations. These are request statuses, not the person connection statuses in `connection-statuses.json`. |
| `prayer.json` | A weight for each of Rock's own prayer categories. Rock ships the categories; none are added or renamed. |
| `security-roles.json` | How the Cast's role names map to the security roles Rock ships. The sample church adds no roles. |
| `phones.json` | The area codes of generated phone numbers: the Phoenix codes the Cast and the church use, which generated numbers use up first, then other US geographic codes by state. Every number is 555-0100 to 555-0199, and each generated person and business gets one no one else has (content standard 2, decision 259). |
| `kiosks.json` | Each campus's check-in kiosks and label printers: kids kiosks, a student kiosk, and a volunteer kiosk, each with a printer, at addresses in 192.0.2.0/24, which no real printer has (decision 360). |
| `communications.json` | The church's texting numbers, who sends mass emails and texts, who welcomes first-time guests, who approves sends, Rock's communication topics, the branded email template in both of Rock's formats, the communication lists with the rules for who is on them, and the one communication scheduled to send after install. See [Communications](../docs/communications.md#the-setup). |
| `notes.json` | The six note types on person records, their security, the staff who write notes, and each type's share of generated notes. |
| `reminders.json` | The reminders staff and leaders set for themselves: Rock's two shipped reminder types, the Cast's hand-written reminders with the state each shows on any install day, the rules for generated reminders, and the notes they use. See [Reminders](#reminders). |
| `campaign.json` | The Built on the Rock building campaign: its fund, description, and dates, and the Rock settings that point at it. See [Pledges](../docs/pledges.md). |
| `steps.json` | The step programs: the adult Discipleship Path and the Student Path. Each has its completion flow, statuses, step types in path order, and prerequisites, and each type says where its steps come from, with the class schedules, the camp and trip dates, and the designed rates. |
| `step-workflows.json` | The staff follow-up workflows that steps start: Baptism Follow-Up, Follow Jesus Follow-Up, and Volunteer Care Check-In, each with its trigger, who gets its task, its form, and what a completed task holds, and the Cast's follow-up stories. See [Step workflows](#step-workflows). |
| `logins.json` | Who among generated adults has a Rock Database login: each reason and its designed chance, the inactive and older-adult shares, and the unconfirmed share (decision 179). The Cast's logins are in their own files. Logins load only when the person installing gives a password. See [How logins load](../loader/README.md#how-logins-load). |
| `calendar.json` | The church's calendar: its events on Rock's Public and Internal calendars, with audiences, campuses, places, contacts, and the rule for each event's dates. See [Calendar](../docs/calendar.md). |
| `staff.json` | The church's 23 staff, every one a Cast person, with their title, department, campus, supervisor, start date, bio, and favorite quote, and the named campus pastor of every campus (decision 198). It also defines the eight Staff person attributes, their security, and the block that shows them (decision 201), and the org chart in Rock's Organization Unit groups (decision 202). Notes, the calendar, first-visit welcomes, and campus texting numbers read the campus pastors from it. Each campus pastor is also their campus's leader in Rock, so a baptism's follow-up task goes to them (decision 291). |
| `person-attributes.json` | The person attributes Rock ships that the sample data fills, by key and Guid, with the keys Cast files load and the ones nothing writes. It defines the Date Engaged attribute the loader adds, the invented schools near each campus, the invented employers, and the rules that fill School, Employer, Position, Legal Notes, Ability Level, Source of Visit, Previous Church, and Envelope Number for generated people (decisions 250 to 258). See [How person attributes load](../loader/README.md#how-person-attributes-load). |
| `registrations.json` | The church's event registrations: template categories, group types for linked and placement groups, templates with forms, fees, discounts, placements, and eligibility, the rules that make each year's instances from calendar events and who registers, the camp and mission trip releases, and the data views about registrations. See [Event registrations](../docs/registrations.md). |
| `church-app.json` | The Rock Solid Church App, the sample church's mobile app: its site and layouts, the share of active adults with it and their devices, and how often pushes are opened. See [Communications](../docs/communications.md#push). |
| `categories.json` | Every category the church adds to Rock, in one tree per kind of thing it holds. See [Categories](#categories). |
| `reporting.json` | The church's layered data views: foundations, the Ministry by Design stage views built from steps, next-step views filed by Ministry System, the communication flows' audiences, Safety, and Finance, with the named schedules that persist the slow ones and run the metrics, the 25 reports with their columns, a `usage` run history where one must differ from the usual rule, the four path metrics, and the security walkthrough verify asks Rock. See [Data views and reports](../docs/reporting.md). |
| `flows.json` | The church's Communication Flows: each flow's Ministry by Design category, how people enter, its messages from the message library, its goal, its exit rule, and how far back its runs reach, plus the one email domain that can be in a run Rock still sends after install, and the workflow types whose forms put people in a flow. See [Communication flows](../docs/communications.md#communication-flows). |
| `assessments.json` | Rock's five assessments: when the church adopted them, how long each takes, and who takes them. Staff take all five when they start, Starting Point and Foundations ask their classes on the first night, every current leader takes DISC and Conflict Profile before leading, and some active adults take one on their own, with who asks, when each is due, how many finish, and what leaders' answers lean toward. See [Assessments](../docs/assessments.md). |
| `adaptive-messages.json` | The church's 22 personalization segments, each on a person data view, with the Finance lock on the ones built on Finance views; its five adaptive messages with their adaptations in order, dates rules, and saturation; the External Website pages a link may name; the lock on Rock's segment Results page; the access questions verify asks Rock; and the Cast people each adaptation must reach. The text is in `content/adaptive-messages`. See [Adaptive messages](../docs/adaptive-messages.md). |
| `signals.json` | The church's four person signal types, in Rock's order, with their colors, icons, and who may see and add each; the Cast's six signals with their owners, notes, and ends; and how many Care Team Follow-ups each campus pastor has. See [How signals load](../loader/README.md#how-signals-load). |
| `personal-links.json` | The bookmarks in Rock's bookmark menu: the shared sections and their roles, the named staff's personal sections, each campus pastor's My Campus section, the Rock page, parameter, and load part behind each link, and the menus verify checks. Every link is a Rock page. See [How personal links load](../loader/README.md#how-personal-links-load). |
| `tags.json` | The church's tags on people: organizational tags in their tag categories, staff members' personal tags, and Rock's own Staff tag, with the Cast people each story tags, the rules that draw generated people, and the access questions verify asks Rock. See [Tags](#tags). |
| `background-checks.json` | Rock's background check requirement on small group leaders, the mix of generated leaders' checks, the Cast people with no check, the Cast's check stories that every load checks, the Background Check request status, and the Checkr package and report searches (decisions 333 to 336). |
| `benevolence.json` | The benevolence types and the security each gets, the case worker, the Cast's requests with their dates and amounts, the rate and mix of generated requests, their text, the assistance agreement, the Request Started workflow, and the access walkthrough's benevolence questions (decisions 350 to 355). |
| `data-integrity.json` | Every designed problem on Rock's Data Integrity pages: when Cast records were made, the forms behind Pending records with their Record Source, two self-inactivations with notes, the Cast's connection status changes, Person Data Error reports, photo requests, couples who share an email, Cast homes that failed address verification, and the six Data Automation rules, configured and off (decisions D-integrity-1 to D-integrity-9). `validate-cast.js` checks its people. See [Data Integrity](../docs/data-integrity.md). |
| `rock-metrics.json` | The metrics Rock ships that get history: each weekly and Tithing Overview metric with how many weeks it gets, Rock's Insights categories with how many monthly runs, and how staff type the weekend's adult counts and salvations into Rock's Service Metrics Entry block. Rock's homepage metrics are in `staff-homepage.json`, and the path metrics are in `reporting.json`. See [How it works](../docs/how-it-works.md). |
| `staff-homepage.json` | Rock's staff homepage: who posts the weekly staff updates and when, the link every feature carries, Rock's three homepage metrics and their schedules, the Ministry Path metrics on the block, and the church's Quick Links. The updates' text is in `content/internal-news`. See [Staff homepage](../docs/staff-homepage.md). |
| `group-syncs.json` | The church's group sync data views, with their filters in Rock's own data filter terms, and the group syncs that keep a list or security role in step with them, with each sync's interval, emails, and create login setting, and the Cast people each synced group must hold. See [Group sync](../docs/communications.md#group-sync). |

## Categories

Every category the church adds is in `categories.json` (decision 185), so its rows never mix with what Rock ships and later work knows where to file new rows. Categories always load, with the church setup.

| Holds | Tree | Used by |
|---|---|---|
| Data views | Rock Solid Church, locked to staff (decision 223) | Every church data view, in the children below |
| Data views | Rock Solid Church, then Foundations, Path, Notice, Belong, Equip, Contribute, Lead, and Re-engage | The layered views in `reporting.json`, filed by Ministry System |
| Data views | Rock Solid Church, then Group Sync | The five group sync data views |
| Data views | Rock Solid Church, then Registrations | The three registration data views, one of them the mission trip's eligibility rule, and Women Who Attend |
| Data views | Rock Solid Church, then Sample Data | Reserved or No Email, and each recurring flow's guarded audience |
| Data views | Rock Solid Church, then Safety, locked to the safety and background check roles | Kids and Student Teams, Kids and Student Volunteers, Small Group Leaders, and Kids With an Allergy |
| Data views | Rock Solid Church, then Finance, locked to the finance roles | The giving and pledge views, including the Giving message's layers |
| Data views | Rock Solid Church, then Staff Workspace, where Staff Workers can edit | Men's Retreat Invites, which no one has run in over a year, two views built on tags, Dee Navarro's Homebound Adults and Jamal Pierce's My Possible Group Leaders (decision 306), and room for staff to build |
| Data views | Foundational Views (Shipped), Rock's own category renamed by Guid (decision 224) | Rock's 8 shipped example views, unchanged |
| Reports | Rock Solid Church, locked to staff, then Leadership (locked to pastors), Notice, Belong, Equip, Contribute, Lead, Re-engage, Safety and Finance (each locked to its roles), and Staff Workspace | The 25 reports in `reporting.json`; Staff Workspace is empty |
| Metrics | Ministry Path | The four stage metrics in `reporting.json`, Belong Stage to Lead Stage |
| Communication lists | Care | The New Here and We Miss You lists |
| Person attributes | Staff | The eight staff attributes (decision 201) |
| Workflow types | Rock Solid Church, then Equip and Contribute | In Equip, the Baptism Interest workflow (decision 235), Baptism Follow-Up, and Follow Jesus Follow-Up. In Contribute, Volunteer Care Check-In (decision 290). |
| Communication Flows | Rock Solid Church, then Notice, Belong, Equip, Contribute, Lead, and Re-engage | Each flow, filed by the Ministry by Design system it serves first (decision 221) |
| Adaptive messages | Rock Solid Church, under Rock's All Church category (`parentGuid`) | The five adaptive messages in `adaptive-messages.json` |
| SMS snippets | Guests, Kids Check-in, Students, Care, and Events, each at the top level, as SMS Conversations' snippet picker groups them | The snippets in `content/communications/snippets.json` (decision 299) |
| Email sections | Rock Solid Church, beside Rock's Starter Sections | The four sections in `content/communications/email-sections.json` (decision 301) |
| Tags | Care, where staff, pastoral workers, and Rock administrators may view and tag. Events, where staff, staff-like workers, and Rock administrators may (decision 303) | Homebound in Care, and Car Show Volunteers in Events, from `tags.json` |

To add rows of a new kind or for a new purpose:

1. **Data views.** Add a child of Rock Solid Church for the purpose, such as Communications, Care, or Reporting, then file the views under it. Never put a church view at the top level or in a category Rock ships.
2. **Communication lists.** Keep list categories flat, as Rock's own Public category is. A public list uses Rock's Public category. A private list may use a church category such as Care, through its `categoryGuid` in `communications.json`.
3. **Person attributes.** Keep attribute categories flat, as Rock's own Employment and Membership are. Give the category `qualifierEntityType` (such as `Rock.Model.Person`), which Rock stores as its EntityTypeId qualifier. The person profile shows a category only through an Attribute Values block, so add one, as `staff.json` does for Staff.
4. **Tags.** Keep tag categories flat, and give each its own `security` for View and Tag. A tag's security comes from its category, and Rock's global default lets everyone view and tag (decision 303).
5. **Another kind of row,** such as system communications or reports. Add a top-level category named Rock Solid Church for that kind, with children by purpose, as the data views have.

Each category names the Rock entity it holds, its parent's key or, under a category Rock ships, that category's Guid as `parentGuid`, and, for a group category, the group type it is for, or, for an attribute category, the entity its attributes belong to.

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

Pete Foster, Ted Decker, Alisha Marble, Daniel Peak, Dee Navarro (the Care Director, who writes most Counseling notes), Jamal Pierce, and Aaron Mitchell, the Phoenix campus pastor, write the main campus's notes. Every other campus's notes come from its named campus pastor in `staff.json` (decision 198). Campus pastors are in RSR - Staff Workers and RSR - Pastoral Workers, so they can view the notes they write, Counseling included. The church office (Alisha) still writes every campus's phone notes and safety alerts.

## Tags

`tags.json` lists the church's tags on people (decision 302). They load in every dataset. See [Tags](../loader/README.md#tags) for how.

| Tag | Kind | Who keeps it | Who carries it |
|---|---|---|---|
| Staff | Rock's own, found by Guid | Alisha Marble tags each one | Every staff member the dataset loads |
| Homebound | Organizational, in Care | Dee Navarro | Hal Russell, and a share of generated adults 80 and older who live alone (decision 304) |
| Car Show Volunteers | Organizational, in Events | Jenny Michaels | Cast adults who help at the car show, and a draw of generated main campus adults |
| Possible Group Leaders | Personal | Jamal Pierce | Cast people who are Ready to Lead, and a draw of generated main campus people from Ready to Lead |
| Young Adults Night | Personal | Ted Decker | Cast young adults, Bri Hall among them, and a draw of generated main campus adults up to 29 |
| Office Volunteers | Personal | Alisha Marble | Cast office volunteers, and a draw of generated main campus Engaged or Leading adults 60 and older |
| Coffee This Month | Personal, one for each campus pastor | That campus's pastor | A draw of that campus's adults whose family first came in the last year, tagged in the last 30 days. Aaron Mitchell's holds four Cast newcomers instead |

Each tag says how it is made and who carries it:

1. **Kind.** An organizational tag names its `category`, a tag category in `categories.json`, and `createdBy`, who made it. A personal tag names its `owner`, a staff member, and has no category. `rockShips` marks Rock's Staff tag, which the loader never changes.
2. **When.** `createdDaysAgo` is when the tag was made. `taggedBy` tags each person, and defaults to the owner or the maker. `taggedWithinDays` keeps each person's tag within that many days of install day.
3. **Who.** `staff` tags every staff member the dataset loads. `cast` lists the Cast people a story tags. `draw` adds generated people, never Cast people, who match every rule it gives: `campus` (main, or owner for the owner's campus), `minAge`, `maxAge`, `livesAlone`, `statuses`, `firstCameWithinDays`, and `dataView`. Then `percent` tags each match by that chance, or `count` tags that many.
4. **Access.** `access` lists the questions verify asks Rock's own security check, such as whether Hal Russell may see Homebound (decision 303).

A tag whose owner or maker a dataset does not load is skipped, so a campus pastor's Coffee This Month loads only with their campus.

## Reminders

`reminders.json` holds the reminders staff and leaders set for themselves (decisions 295 to 298). It uses Rock's two shipped reminder types by Guid and adds none: `personReminderType` for a Personal Reminder about a person, and `groupReminderType` for a Group Reminder about a group.

Each entry in `cast` is one Cast reminder, with its own key and Guid. `owner` is the person who set it. A Personal Reminder names its person in `person`, and a Group Reminder names its group in `group`. `set` is when it was set, and `date` is when it is due: `daysAgo`, `daysAhead`, or `beforeNextBaptismSunday`, that many days before the next Easter or July baptism Sunday, and never before tomorrow. A repeating reminder has `repeat`, with `everyDays` and how many times it has been `completed`. `expect` is what Rock shows on any install day: `due`, `upcoming`, or `complete`. Every load checks it (see [Validation](../validation/README.md#cast-reminder-expectations)).

`generated` holds the rules for reminders in the generated datasets. `person` gives each note type's share of notes that get a reminder, how far back those notes go, how many days after it is set a reminder is due (`daysAfter`), and the share that repeat and their periods. `group` gives the share of active generated small groups whose leader sets one, how far back they set it, and the same timing and repeats. `duePercent` and `dueWithinDays` keep some recent reminders due, and `completedDaysLate` says how late owners complete the rest. `repeatTimes` lists how many times a repeating reminder may repeat, where `null` means no limit.

`notes` holds the short notes generated reminders use. In a person's note, `{name}` is the person's nickname or first name. How the loader uses the file is in [How reminders load](../loader/README.md#how-reminders-load).

## Check-in

Check-in uses Rock's built-in Weekly Service Check-in template, its areas, its groups, and their age and grade rules, unchanged. Every campus gets the same building and room names. Kids groups meet at every Sunday service their campus offers. Jr High and High School check in at the Sunday 10:30am student service. Adult worship attendance uses Rock's Service Attendance template, located at each campus with its weekend services.

The rest of `checkin.json` is set on purpose, the way a church sets up check-in (decisions 367 to 372):

| Part | What it sets |
|---|---|
| `templateSettings` | The template's own settings, by Rock attribute key: supervisor mode on, checkout at the kiosk and in the Check-in Manager, and duplicate check-in prevented. |
| `labels` | Rock's next-gen labels by key. Each area's `labels` lists what it prints: Child, Parent, and Note labels for kids, and a Name Tag for students. `serving.json` gives the Serving Team area a Name Tag too. |
| `roomSoftThreshold`, `roomFirmThreshold` | Each room's limits, by group, at every campus. Only the two nursery rooms have a firm limit, which no supervisor can override. |
| A group's `secondRoom` | A second room for a large group. Grades 4-6 meets in the Outpost Room and the Basecamp Room, and its area uses Rock's Balance strategy. A Kids Ministry position serves each room. |
| `testCheckIn` | Rock's Check-in Test Area, its Test Group, and its two all-day test schedules. The load puts the Test Group in every kids and student room at every campus, on both schedules, so check-in can be tested any day, at any time. Its `volunteerArea` does the same for volunteers: a Volunteer Test Area under Rock's Volunteer Check-in template, open to anyone, with one test group per campus in every room a serving team serves in, printing a Name Tag. No real serving team gets a test schedule. |
| `supervisors`, `supervisorTeam` | Each campus's kids check-in supervisor and PIN. A campus with no Cast kids director uses its Kids Ministry team leader. PINs load only when a login password is given. |
| `relationships` | The Crowd's check-in adults: about 3% of attending families with a child 12 or younger get one adult from another family who checks the children in on about a third of their Sundays. |

To test check-in, open a kiosk at any campus with the Weekly Service Check-in template and its Check-in Test Area, and search for a family. The Test Group is offered all day, every day. To test volunteer check-in, choose the Volunteer Check-in template and its Volunteer Test Area instead, and any person can check in to serve in any serving room. To open the supervisor screen, enter the campus's PIN: 1001 at Phoenix, 1002 at Peoria, 1003 at Gilbert, 1004 at Scottsdale, 1005 at Chandler, 1006 at Goodyear, and 1007 at Tempe.

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
| Phoenix | Core 1, Core 3, Full 7 | Rock's built-in main campus, renamed. North Phoenix, where most of the Cast lives. |
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

## Step workflows

`step-workflows.json` holds the staff follow-up workflows that steps start (decisions 290 to 294). `author` is the staff member who built them, Alisha Marble, and `builtMonthsAgo` says how many months before the install day she built them. A step completed before then has no workflow. `openDays` is how long a task stays open: a task whose step was completed in that many days before the install day still waits at its form.

Each entry in `workflows` is one workflow type, with its own key and Guid. It has a name, a description, a work term, an icon, and a prefix for its workflow Ids, as Rock's workflow editor shows them. `category` is its key in `categories.json`: Equip or Contribute, under Rock Solid Church. `stepType` names its step type in `steps.json`. `trigger` is `complete`, Rock's Step Completed trigger, or `manual`, for a workflow staff start by hand from the step. `person` is the workflow attribute set from the step's person. `assignee` is who gets the task: `campusLeader` for the leader of the step's campus, who is its campus pastor, `staff` for a staff member by Guid, or `currentPerson` for whoever starts it. `form` gives the form's header, button, and response.

Each entry in `fields` shows on the form, as a person, a yes or no, or a memo. A field's `history` says what a completed task holds: `assignee` for the person who did the task, `yesPercent` for a yes or no, `ifBaptizedAfter` for yes when the person was baptized after the step, and `percent` and `pool` for a note. `completedDaysAfter` says how many days after the step its assignee completed an older task.

Each entry in `cast` is a Cast story. It names the workflow, the person whose step starts it, and what Rock shows in `expect`: `open` at the form, or `complete`. A complete one gives the days after the step it was completed, and the `values` it holds, where a person field's value is a person by Guid. Every load checks them (see [Validation](../validation/README.md#cast-step-workflow-expectations)). How the loader uses the file is in [How step workflows load](../loader/README.md#how-step-workflows-load).
