# After you install

This page is for the person who just installed a sample dataset. Rock runs background jobs that keep a church's data current. A load writes years of history in one step, so it also writes much of what those jobs would have written by install day. This page says which jobs the load covers, which to run yourself, and which to watch.

It covers Rock 21.0.2 and Rock 20.0.10. Both ship the same 56 jobs, with the same settings and schedules. Find them in Rock under **Admin Tools > Settings > Jobs Administration**. To run a job once, open it and press **Run Now**. Jobs run only while Rock's web application is running.

## The short version

1. **Most jobs need nothing from you.** The load already wrote what sixteen of Rock's jobs would have, such as giving analytics, eRA, peer networks, group sync, step program completions, person signals, the Duplicate Finder, and checked addresses. See [Already done by the load](#already-done-by-the-load).
2. **Run Process BI Analytics now** if you want Rock's analytics tables on day one. It is on, but its first run is that night.
3. **Location Services Verify needs nothing.** The load marks every address as checked, so the job finds no sample address to send to an address service. See [Already done by the load](#already-done-by-the-load).
4. **Keep real email and texting off** unless you mean to send. Reminders that do send reach only `example.org` and the Cast staff's `rocksolidchurchdemo.com`, a domain Spark owns for the sample church with no mailbox attached.

## How a sample database ages

Every date in a load counts back from install day, the anchor (see [Dates and repeatability](how-it-works.md#dates-and-repeatability)). On install day, the data looks like a church that has used Rock for years. After that, nobody attends, gives, or signs up unless you do it in Rock. Rock's jobs keep running, so the data moves forward the way a real church's would, but with no new activity:

1. **Ages and grades move forward.** Rock Cleanup updates them every night.
2. **Givers start to look late.** Giving Automation raises Late Gift alerts for givers whose next gift does not come, and journey stages slide toward Lapsed over the following months.
3. **Connection requests come due.** Requests due later become due this week, then past due. Future follow-ups return to Active on their dates.
4. **The care lists shift.** Group Sync updates New Here and We Miss You every day, so New Here slowly empties and We Miss You grows.
5. **Registrations open and close on their dates,** and their reminders send (see [Be careful](#be-careful)).
6. **Communication flows keep sending.** Runs that were open at install send their remaining messages, and recurring flows start new runs on their schedules (decision 220).
7. **The scheduled Christmas Eve email sends** on its date in December (decision 248).
8. **Metrics add a value** each time their schedule comes around. With no new check-ins, gifts, or prayers, the weekly ones fall toward zero.
9. **Reminders come due.** Staff and leaders' reminders come due on their dates, and pending assessment requests get their reminder emails (see [Be careful](#be-careful)).
10. **Families leave eRA.** With no new Sundays or gifts, Family Analytics moves families out of eRA on its Tuesday runs, each with an eRA End Date and a completed eRA step.
11. **Peer scores keep moving.** On the 1st of each month, Calculate Peer Network grows the score of everyone still in a group together and lowers the score of everyone who left.

For a church that looks freshly active again, install into a new database. Sample data installs only into a fresh database (decision 1).

## Already done by the load

The load writes what these jobs would have written by install day, so running them on install day changes nothing. Leave them on. After install day, they keep the data current (see [How a sample database ages](#how-a-sample-database-ages)).

1. **Rock Cleanup.** The load fills what Rock's save hooks and Rock Cleanup set, such as aliases, ages, family salutations, streak counts, daily interaction counts, and event dates (see [How people load](../loader/README.md#how-people-load)). Verify runs Rock's own Rock Cleanup after every load and fails if it changes anything (decision 66).
2. **Group Sync.** Each synced group already holds exactly the people its data view finds, and its last refresh is the install time (decisions 186 and 187). Verify runs Rock's own group sync on every load and fails if it adds or removes anyone.
3. **Process Workflows.** Each step workflow is open or completed as Rock would leave it, and its form sends no email (decision 294). So is each Person Data Error report, three of them waiting for Alisha Marble (decision 387). Verify runs Rock's own job and fails if any workflow changes beyond the times it was processed.
4. **Calculate Person Duplicates.** The load runs the job's own SQL, Rock's duplicate finder procedure, as its last step, so the Duplicate Finder is full on install day (decision 310). The Cast's duplicates show with the decisions staff already made and a waiting merge request, and every generated email and phone number is its own, so no strangers show up (see [Duplicates](duplicates.md)). Verify runs Rock's own job on every load and fails if it adds, removes, or rescores any pair. It takes about 10 seconds on Full 7.
5. **Calculate Group Requirements.** Kids and student volunteers have the background check results this job would have saved (decision 73), and so does every leader of an active small group, whose Leader role needs a current background check (decision 334). Verify runs Rock's own job and fails if it adds, removes, or changes any requirement result.
6. **Update Personalization Data.** Each personalization segment already holds the people Rock's update would put in it on install day (decision 271). Verify runs Rock's own segment update and fails if it adds or removes anyone.
7. **Calculate Person Signals.** Each person's top signal is the one Rock sets, so the Top Person Signal badge is right from the start (decision 281). Verify runs Rock's own `Person.CalculateSignals` and fails if it would change anyone.
8. **Family Analytics (eRA).** The load turns it on, Tuesdays at 8 PM, with its Set Visit Dates setting on. Each family's eRA is filled as if the job had run every week of the history window: Currently an eRA, eRA Start Date and End Date, eRA steps in Rock's Core Steps program, and eRA history lines (decisions 323 to 325). Times Given, First and Last Checked-In, Times Checked-In, and First and Last Gave come from Rock's own procedures, run as of install day (decision 326). First Visit and Second Visit are written as the job would leave them, and Rock's own visit dates procedure fills everyone who first came inside the history window (decision 393). Verify runs Rock's Family Analytics procedures on install day and fails if they would move a family in or out of eRA or change a value.
9. **Calculate Peer Network.** Each person's peer network from their small groups, serving teams, and follows is filled as the job's last two monthly runs left it, with Rock's trend arrows (decisions 328 to 330). Verify runs Rock's two procedures on install day and fails if they would add, remove, or rescore any row.
10. **Giving Automation.** Giving attributes, bins and percentiles, journey stages with a year of history, and a year of giving alerts are filled as if the job ran every night (decision 44). Rock's settings record the job's last run, so its next run starts there.
11. **Update Step Program Completions.** Everyone whose complete steps cover every step type in a program has a completion for it, so the job finds nothing to add (see [Steps](steps.md#campus-history-and-program-completions)). An invariant checks it.
12. **Process Elevated Security.** Every person's account protection profile is set the way this job sets it (decision 182). An invariant checks it.
13. **Process Group History.** Groups, members, and meeting places have the history rows Rock writes (decision 38).
14. **Send Group Schedule Notifications.** Every serving confirmation and reminder the job could send is marked sent, including those for Sundays ahead, so it has nothing to send (decision 165).
15. **Send Note Notifications.** Every note is marked as notified and approved, so the job never emails staff about old notes (see [Notes](../loader/README.md#notes)).
16. **Location Services Verify.** Rock ships its Smarty Streets address service turned on, with Spark's managed key, and this job sends up to 1,000 unchecked addresses to it every night. The load marks every home, business, and campus address it writes the way Rock marks one after a successful check, so the job finds nothing to send, on the first night or any later one (decisions 315 and 316). Two Cast homes failed their check on purpose, and staff locked them, which the job skips (decision 386). An address someone adds or changes after install goes to the service as usual.

Verify runs Rock's own job for items 1 to 5, and the code the job calls for items 6 to 9. For items 10 to 15, the load follows Rock's code, but verify does not run the job. For item 16, verify runs the job's selection on every load and fails if it would pick any loaded address, but never runs the job, so verify never calls the address service.

## Run after install

These jobs fill something the load leaves empty. Each one says whether it is on by default, and whether it fills history or only today.

1. **Process BI Analytics.** On by default, every night at 5 AM. It fills Rock's analytics tables, which analytics reports and outside reporting tools read.
   - **History: partly.** Gifts and attendance are copied for all of history. People and families get a snapshot as of the day it runs, so their history starts that day.
   - Run it now only if you need these tables before morning.

## Runs on its own

These jobs are on. They fill or refresh data on their schedule, with nothing needed from you.

1. **Calculate Metrics.** Every 15 minutes.
   - The load writes history for the metrics Rock ships: two years of weekly attendance, giving, prayer, salvations, and eRA values, two years of monthly baptisms, and a year of Tithing Overview and Insights runs (decisions 373 to 380). The three homepage metrics have a year (decision 347), and the four Ministry Path metrics have a value for every week, by campus (decisions 234 and 379).
   - Each metric's last run is its newest run before install, so the job's first value is for the next run after install, and it fills in no past.
   - **History: full,** except Rock's four hosting metrics, which measure the web server.
   - Three metrics run the church's own SQL, which the load saves in place of Rock's, so Rock's own runs continue the same line. Baptisms counts the month before each run on the 1st, where Rock's counts the month just begun. Total Weekend Attendance adds up only that weekend, where Rock's adds up the previous Monday's children, students, and volunteers again. Weekly eRA Losses counts the people who left eRA that week, where Rock's finds no one (decisions 376 to 378).
2. **Update Persisted DataViews.** Every minute. The load leaves the two persisted views, Regular Attenders and Kids Checked In Recently, for this job, which persists them in its first minute (decision 226).
3. **Update Persisted Attribute Values.** Every night at 2:15 AM. The load writes each attribute value and leaves Rock's stored display text for this job. Until it runs, Rock works out a value's display text when it shows it.
4. **Connection Request Workflow Triggers.** Every day at 7 AM. It returns each future follow-up request to Active on its follow-up date.
5. **Populate Interaction Session Data.** Every 10 minutes. It fills each session's interaction count and length, but only for sessions from the last day. The sessions the load writes for email and push opens keep these empty. Issue [#12](https://github.com/SparkDevNetwork/rock-sample-data/issues/12) would fill them.
6. **Rock's upkeep.** Database Maintenance, Update Analytics Source Postal Code, Update Persisted Datasets, and Job Pulse do the same work on a sample database as on any other.
7. **Rock's first-night jobs.** A brand-new Rock runs its one-time update jobs on its first evening and its post-install job that night, which then deletes itself (decision 170). They belong to Rock. Verify's trusted base runs them before it loads, so a load into a Rock that has not had its first night is not tested.

## Be careful

These jobs can send email, texts, or push, or reach an outside service. The sample data keeps people on reserved contact points: emails on `example.org`, phones in the 555-0100 to 555-0199 range, and push tokens that start with `sample-data-` (decisions 13 and 95, and [content standards](content-standards.md) rules 2 and 3). Cast staff use `rocksolidchurchdemo.com`, a domain Spark owns for the sample church, with no mailbox attached.

> **Warning:** A message leaves Rock only through the email, SMS, or push transport your Rock uses. On a sample site, leave real transports off, or use a test transport that keeps the mail.

1. **Registration Reminder.** On, every hour. Past reminders are marked sent (decision 214). Each instance whose reminder date is still ahead sends its reminder email to each registrar when that date comes. A few registrars are Cast staff.
2. **Event Payment Reminders.** On, weekdays at 9 AM. Each registration with a balance gets a payment reminder on its template's schedule until the instance ends (decision 208). A few go to Cast staff.
3. **Send Attendance Reminders for Group Type.** On, every day at 4 PM. It emails each small group's leaders on the day the group meets, and again 2 and 4 days later, until someone enters attendance. Nobody enters attendance after install, so leaders get these every week. A few Cast staff lead small groups.
4. **Process Reminders.** On, weekdays at 6 AM. The load sets each owner's reminder count as the job would, and verify runs the job and finds nothing to change (decision 298). Each run also emails each owner their reminders due. Owners are staff and small group leaders, some of them Cast staff.
5. **Send Assessment Reminders.** On, every day at 8 AM. Each pending request's last reminder is where the job would have left it (decision 267), so its first run sends only the reminders a church's would. A request still pending gets a reminder every 7 days, for 60 days after it was made.
6. **Process Communication Flows.** On, every 5 minutes. Open runs send their remaining messages, and recurring flows start new runs on their schedules. Every run reaches only reserved addresses and numbers, because each recurring flow's audience includes a reserved email guard (decision 220, and question 18 in [Data views and reports](reporting.md)).
7. **Send Communications.** On, every 10 minutes. The scheduled Christmas Eve email waits for its send date (decision 248). Its recipients are all on `example.org` or have no email.
8. **Group Sync.** Its welcome and exit emails go out only when someone joins or leaves a synced group after install (decision 187).
9. **Spark Link.** On, every 7 hours. Every Rock sends Spark its organization name, address, public URL, and count of person records. A sample database sends the sample people's count.
10. **Only if you set them up.** Chat Sync reaches Rock's chat service only when chat is configured. Sync Media reaches a media account only when one is added. Download Payments is off, and the sample's scheduled gifts use Rock's Test Gateway. Send Following Event Notification and Send Following Suggestion Notification email staff, and Communication Queue Alert emails its recipients. All four are off by default, so leave them off. Every follow the load writes is a staff member's, so if Send Following Event Notification is turned on, its notices reach only staff on the sample church's domain (decision 330).

Items 1 to 5 reach only `example.org` and the staff domain, so no real person gets them. If your site can send real email, you can still turn them off, or keep its email on a test transport.

## Nothing to do yet

These jobs work on features the sample data does not load yet, or on settings Rock ships turned off. They find nothing.

1. **Data Automation.** The load saves all six rules configured the way a church sets them up, and off (decision 389), so the job runs only its gender auto-fill, which finds no one to fill. Verify runs Rock's own job and fails if it changes anyone. To try a rule, turn it on in Data Integrity Settings.
2. **Steps Automation.** No step type has an auto-complete data view.
3. **Campaign Manager** and **Connection Requests Automation.** The load sets up no connection campaigns or status automation rules.
4. **Send Learning Notifications** and **Update Learning Program Completions.** The load writes no learning programs. Issue [#7](https://github.com/SparkDevNetwork/rock-sample-data/issues/7) would add them.
5. **Send Sign-Up Reminders.** The load writes no sign-up groups. Issue [#5](https://github.com/SparkDevNetwork/rock-sample-data/issues/5) would add them.
6. **Send Group Attendance Reminders.** No group type turns on its attendance reminder settings.
7. **Expiring Credit Card Notices.** The sample's scheduled gifts store no card expiration date.
8. **Charge Future Transactions.** The load writes no future charges.
9. **Universal Search Re-Index** and **Index Content Collections.** Nothing is set to be indexed.
10. **Update Media Element Usage** and **Update Outreach Toolbox Touchpoints.** The load writes no media or outreach toolbox data.
11. **Collect Hosting Metrics.** Off by default.

## Every job

Rock 21.0.2 and Rock 20.0.10 ship the same jobs. "On" is the job's state in a new Rock.

| Job | On | Schedule | Section |
|---|---|---|---|
| Calculate Group Requirements | Yes | Daily, 3 AM | [Already done](#already-done-by-the-load) |
| Calculate Metrics | Yes | Every 15 minutes | [Runs on its own](#runs-on-its-own) |
| Calculate Peer Network | Yes | 1st of the month, 2:05 AM | [Already done](#already-done-by-the-load) |
| Calculate Person Duplicates | Yes | Daily, 3 AM | [Already done](#already-done-by-the-load) |
| Calculate Person Signals | Yes | Daily, 3:15 AM | [Already done](#already-done-by-the-load) |
| Campaign Manager | Yes | Daily, 7 AM | [Nothing to do](#nothing-to-do-yet) |
| Charge Future Transactions | Yes | Every 10 minutes | [Nothing to do](#nothing-to-do-yet) |
| Chat Sync | Yes | Daily, 5 AM | [Be careful](#be-careful) |
| Collect Hosting Metrics | No | Every 5 minutes | [Nothing to do](#nothing-to-do-yet) |
| Communication Queue Alert | No | Every 15 minutes | [Be careful](#be-careful) |
| Connection Request Workflow Triggers | Yes | Daily, 7 AM | [Runs on its own](#runs-on-its-own) |
| Connection Requests Automation | Yes | Daily, 11 PM | [Nothing to do](#nothing-to-do-yet) |
| Data Automation | Yes | Tuesdays, 4 AM | [Nothing to do](#nothing-to-do-yet) |
| Database Maintenance | Yes | Daily, 2:30 AM | [Runs on its own](#runs-on-its-own) |
| Download Payments | No | Daily, 5 AM | [Be careful](#be-careful) |
| Event Payment Reminders | Yes | Weekdays, 9 AM | [Be careful](#be-careful) |
| Expiring Credit Card Notices | Yes | 1st of the month, 7:30 AM | [Nothing to do](#nothing-to-do-yet) |
| Family Analytics | No (the load turns it on) | Tuesdays, 8 PM | [Already done](#already-done-by-the-load) |
| Giving Automation | Yes | Daily, 10 PM | [Already done](#already-done-by-the-load) |
| Group Sync | Yes | Every 20 minutes | [Already done](#already-done-by-the-load) |
| Index Content Collections | Yes | Daily, 4:45 AM | [Nothing to do](#nothing-to-do-yet) |
| Job Pulse | Yes | Every minute | [Runs on its own](#runs-on-its-own) |
| Location Services Verify | Yes | Daily, 3 AM | [Already done](#already-done-by-the-load) |
| Populate Interaction Session Data | Yes | Every 10 minutes | [Runs on its own](#runs-on-its-own) |
| Process BI Analytics | Yes | Daily, 5 AM | [Run after install](#run-after-install) |
| Process Communication Flows | Yes | Every 5 minutes | [Be careful](#be-careful) |
| Process Elevated Security | Yes | Daily, 3:15 AM | [Already done](#already-done-by-the-load) |
| Process Group History | Yes | Daily, 3:30 AM | [Already done](#already-done-by-the-load) |
| Process Reminders | Yes | Weekdays, 6 AM | [Be careful](#be-careful) |
| Process Workflows | Yes | Every 10 minutes | [Already done](#already-done-by-the-load) |
| Registration Reminder | Yes | Every hour | [Be careful](#be-careful) |
| Rock Cleanup | Yes | Daily, 1 AM | [Already done](#already-done-by-the-load) |
| Runs data updates that need to occur after a new installation | Yes | Daily, 2 AM, until it deletes itself | [Runs on its own](#runs-on-its-own) |
| Send Assessment Reminders | Yes | Daily, 8 AM | [Be careful](#be-careful) |
| Send Attendance Reminders for Group Type | Yes | Daily, 4 PM | [Be careful](#be-careful) |
| Send Communications | Yes | Every 10 minutes | [Be careful](#be-careful) |
| Send Following Event Notification | No | Weekdays, 7 AM | [Be careful](#be-careful) |
| Send Following Suggestion Notification | No | Weekdays, 3 PM | [Be careful](#be-careful) |
| Send Group Attendance Reminders | Yes | Every 15 minutes | [Nothing to do](#nothing-to-do-yet) |
| Send Group Schedule Notifications | Yes | Daily, 4 PM | [Already done](#already-done-by-the-load) |
| Send Learning Notifications | Yes | Daily, 7 AM | [Nothing to do](#nothing-to-do-yet) |
| Send Note Notifications | Yes | Every 2 hours | [Already done](#already-done-by-the-load) |
| Send Sign-Up Reminders | Yes | Daily, 8 AM | [Nothing to do](#nothing-to-do-yet) |
| Spark Link | Yes | Every 7 hours | [Be careful](#be-careful) |
| Steps Automation | Yes | Daily, 4 AM | [Nothing to do](#nothing-to-do-yet) |
| Sync Media | Yes | Every 2 hours | [Be careful](#be-careful) |
| Universal Search Re-Index | Yes | Daily, 5 AM | [Nothing to do](#nothing-to-do-yet) |
| Update Analytics Source Postal Code | Yes | Daily, 1:20 AM | [Runs on its own](#runs-on-its-own) |
| Update Learning Program Completions | Yes | Daily, 5 AM | [Nothing to do](#nothing-to-do-yet) |
| Update Media Element Usage | Yes | Daily, 3 AM | [Nothing to do](#nothing-to-do-yet) |
| Update Outreach Toolbox Touchpoints | Yes | Every hour | [Nothing to do](#nothing-to-do-yet) |
| Update Persisted Attribute Values | Yes | Daily, 2:15 AM | [Runs on its own](#runs-on-its-own) |
| Update Persisted Datasets | Yes | Every minute | [Runs on its own](#runs-on-its-own) |
| Update Persisted DataViews | Yes | Every minute | [Runs on its own](#runs-on-its-own) |
| Update Personalization Data | Yes | Every minute | [Already done](#already-done-by-the-load) |
| Update Step Program Completions | Yes | Daily, 2 AM | [Already done](#already-done-by-the-load) |

## Keeping this page current

1. When the load starts writing what a job writes, move the job to [Already done by the load](#already-done-by-the-load) and name the decision. For example, issues #11 and #17 moved Family Analytics and Calculate Peer Network there.
2. When the load starts writing data a job sends from, check whether the job can send, and to whom.
3. When a new Rock release joins `release/rock-releases.json`, compare its jobs with this table. A clean Rock's `ServiceJob` table lists each job's class, whether it is active, and its schedule.
