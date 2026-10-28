# Serving

How serving teams, group scheduling, and requests to serve look in the sample data, and why. The numbers live in [configuration/serving.json](../configuration/serving.json) and [configuration/connections.json](../configuration/connections.json), so they can be tuned in one place. The decisions are 68 to 77 in [Decisions](decisions.md).

## The teams

Every campus has the same eight teams. Each one is a Rock Serving Team group, so scheduling is on and calibration finds it by its Serving Area purpose.

| Team | Where it serves | Services | Background check |
|---|---|---|---|
| Greeters | Main Entrance, Kids Entrance | All | No |
| Ushers | Worship Center | All | No |
| Kids Ministry | The six kids check-in rooms | All | Yes |
| Worship Team | Worship Center | All | No |
| Production Team | Production Booth | All | No |
| Student Ministry | The Warehouse and the Garage | The student service | Yes |
| Prayer Team | Prayer Room | All | No |
| First Aid Team | First Aid Station | All | No |

- **Structure.** Each team sits under its campus's parent ("Phoenix Serving Teams"), under Rock's own Serving Teams group. Names are plain, with the campus set on the group. Rock's shipped Greeters, Ushers, and Children's groups become Phoenix's Greeters, Ushers, and Kids Ministry, and the old A/V Team becomes Phoenix's Production Team.
- **Positions are rooms.** A team's rooms are its scheduling positions. The kids and student rooms are the check-in rooms; the other rooms are added to each campus building. Each room has a minimum, desired, and maximum number of people at each service.
- **Staffing fits the campus.** The configured numbers are for a campus that can staff them. A small campus asks for about the people it usually has, so its scheduler is mostly staffed with some open spots. Phoenix in Core 1 is a church of about 180 people running six kids rooms at three services, so many kids rooms still show an open spot. That is realistic, and it gives the scheduler work to show.
- **Requirements.** Kids Ministry and Student Ministry require Rock's Background Check Required, and only volunteers who meet it can be scheduled. Every volunteer on those teams has a passing check from the last few years and the requirement results Rock's job would have saved.

## The volunteers

- **The Cast.** Phoenix's 47 Cast volunteers are hand-picked in `cast/groups/serving-*.json`, one file per team, so tests can rely on them. Their stories hold: the Bells greet, Ted Decker leads the Production Team, Gabriel Hernandez plays on the Worship Team, the Fosters and Cindy Decker pray, and Michelle Nguyen leads First Aid. Natalie Silva serves on two teams.
- **Generated households.** Adults in households that still attend serve at the profile's share of active adults (22.6%). They come from Engaged and Leading adults first, then Connected and Known adults when a campus needs them, who become Engaged. Every team gets a leader and at least two people. Couples often serve together. Volunteers are out of high school, and nobody joined a team before then (decision 169).
- **Leaders.** Every team has a leader, who is Leading. A large team also has co-leaders at the measured share of leaders (8.5%), the way a big team has a coordinator for each service.
- **Rotation.** Each volunteer has one of Rock's four schedule templates: Every Week (15%), Every Other Week (40%), 1st and 3rd Week (25%), or 2nd and 4th Week (20%). Leaders serve every week. On a fifth Sunday, about half of the 1st and 3rd and the 2nd and 4th volunteers fill in.
- **Preferences.** Most volunteers prefer one service (85%), and most on a team with several rooms prefer one room (70%). These are Rock's scheduling preferences, which Auto Schedule uses.
- **Pending.** People who connected in the last few weeks wait on their team as Pending, as Rock ships it. The team leader makes them Active as part of onboarding.

## Serving history and the weeks ahead

Each request to serve is an Attendance row on the team's occurrence for that room, service, and Sunday, the same row Rock's scheduler writes.

- **History.** Serving history covers the dataset's whole history window, like Sunday attendance. A volunteer served on a Sunday their family came. Otherwise they were often not scheduled at all, since the family planned to be away, or they declined with a reason, never answered, or confirmed and did not come.
- **The weeks ahead.** The next six weeks mix confirmed (60%), pending (30%), and declined (10%), around a few blackout dates.
- **Notifications.** A confirmation and a reminder are marked sent only for volunteers Rock's notification job can email, as the job marks them (decision 165). Before install day, only volunteers who confirmed got a reminder. For a Sunday still ahead, both are marked sent for everyone the job can email, so it never emails sample people. No request is dated after install day.
- **Check-in.** Volunteers check in to serve through Rock's Volunteer Check-in template, whose Serving Team area holds every team. A scheduled volunteer is pre-selected at the kiosk. Every time a volunteer served, they checked in a few minutes before the service.

## Requests to serve

Requests use Rock's Involvement connection type, which keeps its name. Many churches call connections "involvement" and the teams "serving".

- **Opportunities.** Eight, one per team. Each opportunity's Name is its team's name, and its PublicName is the role a person takes on: Greeter, Usher, Kids Volunteer, Musician or Vocalist, Production Volunteer, Student Leader, Prayer Partner, and First Aid Responder. Each has an icon, a summary, a full description, a photo of Phoenix's Cast volunteers doing the job, and the Serve page's search filters (Preference Area, Days of the Week, and Role). The eight photos replace the three Rock ships.
- **Statuses.** No Contact, In Progress, Background Check, and Shadowing, each a real onboarding step.
- **Due dates.** Involvement uses Rock's Duration Per Status mode, so a request's due date runs from its last status change. No Contact gives 7 days, In Progress 21, and Background Check and Shadowing 28. On install day, Full 7 has about 62% of its active requests due later, 26% due this week, and 12% past due (decision 173). Before, 98% were past due.
- **Connected requests.** Every volunteer who joined in the last two years has the request that put them on their team, ending the day they joined.
- **Open requests.** People who are not serving ask to serve in the profile's mix of active and inactive requests, with one future follow-up and one transferred request per campus. Each request has the activity a connector would leave.
- **Connectors.** Each team leader is the default connector for their team's opportunity at their campus. About 35% of requests have a connector, as measured. At the main campus, Alisha Marble connects the active requests still at No Contact or In Progress, as the staff person who reaches out first, and at least eight in every dataset (decision 178). She is Rock's admin person, so signing in as admin shows her requests on the connection board.
- **Person history.** Each request shows on the person's History tab, the way Rock records it: added, status changed, connected, and set aside or saved for a future follow-up.

## The Cast's stories

| Person | Request | What it shows |
|---|---|---|
| Brian and Wendy Gilbert | Greeters, In Progress | A couple asking to serve together |
| Sean Hansen | Ushers, No Contact | A new request with no connector |
| Nancy Turner | Ushers, No Contact | An idle, overdue request with no connector |
| Helen Evans | Kids Ministry, Background Check | Waiting on a background check |
| Frank Dexter | Kids Ministry, Background Check | A failed background check that needs review |
| Sam Hanks | Production Team, No Contact | 75 days with no follow-up |
| Bri Hall | Greeters, Connected | Just connected, waiting as a Pending greeter |
| Ryan Patel | Production Team, future follow-up | Contact him again after his work travel |
| Lauren Shaw, Derek Ward, Mark Fischer, Sheryl Tennant | Greeters, Ushers, Production Team, and Prayer Team, In Progress | Requests Alisha Marble is working, from 5 to 26 days old |
| Tiffany Coleman, Susan Kim, Melissa Wood | Kids Ministry, Prayer Team, and Greeters, No Contact | New requests assigned to Alisha Marble that she has not reached yet |

With the Gilberts, Alisha Marble connects nine of the Cast's open requests, so signing in as admin shows at least eight on her connection board in every dataset. A Cast expectation checks that floor on every load (decision 178).

## How the numbers compare

On Full 7, these serving and connection values pass validation: the share of active adults who serve, the share of serving attendance attended, the share of leaders, the state mix, the connector rate, and time to connect.

Three measures are skipped on purpose, and [tolerances.json](../validation/tolerances.json) says why:

- **Adult check-ins per child check-in.** The sample church's adults check in only to serve. It has no midweek programs, adult classes, or events where adults check in.
- **Adult check-in frequency.** Measured from the same adult check-ins.
- **Team size.** Eight teams per campus make a large campus's teams large, by design.

Still outside tolerance, by a few points: two holiday multipliers for adults (fall kickoff and Labor Day) and four months of the connection month pattern.

## Later

- A midweek student night (parked in the [roadmap](roadmap.md)).
