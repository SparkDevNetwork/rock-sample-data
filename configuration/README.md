# Configuration

Configuration is the church setup the data depends on: campuses, connection statuses, group types, financial accounts, check-in areas, schedules, and similar. The loader saves these rows through Rock's services, because there are few of them and they need full model behavior.

## Files

| File | What it defines |
|---|---|
| `campuses.json` | Campuses, the datasets each appears in, and which service time slots each offers. |
| `schedules.json` | The shared Sunday service time slots and the campus schedule types (Weekend Service, Student Service). |
| `accounts.json` | Funds. General and Building Funds have one child account per campus. |
| `checkin.json` | Check-in areas, groups, rooms, and which services each group meets at. |
| `connection-statuses.json` | The six connection statuses. |
| `giving-alert-types.json` | The giving alert types: Large Gift, Smaller Than Usual, and Late Gift. Rock ships none. |
| `achievements.json` | The Kids Check-in streak type and its seven achievement types. |

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

General Fund and Building Fund each have a child account per campus (for example "Phoenix General Fund"), with Rock's `UsesCampusChildAccounts` turned on. Gifts post to the giver's campus account. Mission Fund and Event Registration have no child accounts.

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

## Campuses

Campuses are named by city only, with no "Campus" suffix. All are in the Phoenix area.

| Campus | Datasets | Notes |
|---|---|---|
| Phoenix | Core 1, Core 3, Full 7 | Rock's built-in main campus, renamed. North Phoenix, where the Cast lives. |
| Peoria | Core 3, Full 7 | West Valley. |
| Gilbert | Core 3, Full 7 | East Valley. |
| Scottsdale | Full 7 | Older, higher-income households. |
| Chandler | Full 7 | Young families; large kids ministry. |
| Goodyear | Full 7 | Fast-growing far west; more first-time guests. |
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
