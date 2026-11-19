# After you install

This page is for the person who just installed a sample dataset. Rock runs background jobs that keep a church's data current. A load writes years of history in one step, so it also writes much of what those jobs would have written by install day. This page says which jobs the load covers, which to run yourself, and which to watch.

It covers Rock 21.0.2 and Rock 20.0.10. Both ship the same 56 jobs, with the same settings and schedules. Find them in Rock under **Admin Tools > Settings > Jobs Administration**. To run a job once, open it and press **Run Now**. Jobs run only while Rock's web application is running.

## The short version

1. **Most jobs need nothing from you.** The load already wrote what twelve of Rock's jobs would have, such as giving analytics, group sync, step program completions, and person signals. See [Already done by the load](#already-done-by-the-load).
2. **Run Family Analytics once if you want eRA.** It ships turned off, and the load does not fill eRA yet. Turn off its Set Visit Dates setting first. See [Run after install](#run-after-install).
3. **Run Calculate Person Duplicates, Calculate Peer Network, and Process BI Analytics now** if you want the Duplicate Finder, peer networks, or Rock's analytics tables on day one. They are on, but their first run is that night or later.
4. **Turn off Location Services Verify** on a sample site. Otherwise it sends the sample addresses to an address service every night. See [Be careful](#be-careful).
5. **Keep real email and texting off** unless you mean to send. Reminders that do send reach only `example.org` and the Cast staff's `rocksolidchurchdemo.com`, a domain Spark owns for the sample church with no mailbox attached.

## How a sample database ages

Every date in a load counts back from install day, the anchor (see [Dates and repeatability](how-it-works.md#dates-and-repeatability)). On install day, the data looks like a church that has used Rock for years. After that, nobody attends, gives, or signs up unless you do it in Rock. Rock's jobs keep running, so the data moves forward the way a real church's would, but with no new activity:

1. **Ages and grades move forward.** Rock Cleanup updates them every night.
2. **Givers start to look late.** Giving Automation raises Late Gift alerts for givers whose next gift does not come, and journey stages slide toward Lapsed over the following months.
3. **Connection requests come due.** Requests due later become due this week, then past due. Future follow-ups return to Active on their dates.
4. **The care lists shift.** Group Sync updates New Here and We Miss You every day, so New Here slowly empties and We Miss You grows.
5. **Registrations open and close on their dates,** and their reminders send (see [Be careful](#be-careful)).
6. **Communication flows keep sending.** Runs that were open at install send their remaining messages, and recurring flows start new runs on their schedules (decision 220).
7. **The scheduled Christmas Eve email sends** on its date in December (decision 248).
8. **Metrics add a value** each time their schedule comes around.
9. **Reminders come due.** Staff and leaders' reminders come due on their dates, and pending assessment requests get their reminder emails (see [Be careful](#be-careful)).

For a church that looks freshly active again, install into a new database. Sample data installs only into a fresh database (decision 1).

## Already done by the load

The load writes what these jobs would have written by install day, so running them on install day changes nothing. Leave them on. After install day, they keep the data current (see [How a sample database ages](#how-a-sample-database-ages)).

1. **Rock Cleanup.** The load fills what Rock's save hooks and Rock Cleanup set, such as aliases, ages, family salutations, streak counts, daily interaction counts, and event dates (see [How people load](../loader/README.md#how-people-load)). Verify runs Rock's own Rock Cleanup after every load and fails if it changes anything (decision 66).
2. **Group Sync.** Each synced group already holds exactly the people its data view finds, and its last refresh is the install time (decisions 186 and 187). Verify runs Rock's own group sync on every load and fails if it adds or removes anyone.
3. **Process Workflows.** Each step workflow is open or completed as Rock would leave it, and its form sends no email (decision D-stepflows-5). Verify runs Rock's own job and fails if any workflow changes beyond the times it was processed.
4. **Update Personalization Data.** Each personalization segment already holds the people Rock's update would put in it on install day (decision 271). Verify runs Rock's own segment update and fails if it adds or removes anyone.
5. **Calculate Person Signals.** Each person's top signal is the one Rock sets, so the Top Person Signal badge is right from the start (decision D-signals-3). Verify runs Rock's own `Person.CalculateSignals` and fails if it would change anyone.
6. **Giving Automation.** Giving attributes, bins and percentiles, journey stages with a year of history, and a year of giving alerts are filled as if the job ran every night (decision 44). Rock's settings record the job's last run, so its next run starts there.
7. **Update Step Program Completions.** Everyone whose complete steps cover every step type in a program has a completion for it, so the job finds nothing to add (see [Steps](steps.md#campus-history-and-program-completions)). An invariant checks it.
8. **Process Elevated Security.** Every person's account protection profile is set the way this job sets it (decision 182). An invariant checks it.
9. **Calculate Group Requirements.** Kids and student volunteers have the background check results this job would have saved (decision 73).
10. **Process Group History.** Groups, members, and meeting places have the history rows Rock writes (decision 38).
11. **Send Group Schedule Notifications.** Every serving confirmation and reminder the job could send is marked sent, including those for Sundays ahead, so it has nothing to send (decision 165).
12. **Send Note Notifications.** Every note is marked as notified and approved, so the job never emails staff about old notes (see [Notes](../loader/README.md#notes)).

Verify runs Rock's own job for items 1 to 3, and the code the job calls for items 4 and 5. For items 6 to 12, the load follows Rock's code, but verify does not run the job.

## Run after install

These jobs fill something the load leaves empty. Each one says whether it is on by default, and whether it fills history or only today.

1. **Family Analytics (eRA).** Off by default. Once on, it runs Tuesdays at 8 PM.
   - **What it fills.** eRA, Rock's "estimated regular attender" status, for each family that qualifies today: Currently an eRA, eRA Start Date, and an eRA step in Rock's Core Steps program. It also fills Times Given (6 wks), Times Given (52 wks), First Checked-In, Last Checked-In, and Times Checked-In (16 wks). With Set Visit Dates on, it fills First Visit and Second Visit too.
   - **History: eRA starts the day it runs.** Every family that qualifies enters eRA that day. Its eRA Start Date and its eRA step both start then, even for a family that has come for years. No family gets a past eRA step or an eRA End Date. The check-in and giving counts read the loaded history.
   - **Turn off Set Visit Dates before you run it.** Rock takes First Visit from a family's earliest check-in. The load's check-in history starts at the start of its history window, so a family that has come for years gets a first visit there.
   - **It rewrites First Gave and Last Gave** for adult givers, by its own rule. The load fills them the way Giving Automation does. Data views that read Last Gave, such as Involved Adults, can change.
   - **Its Write eRA History setting is on,** so each person who enters eRA gets an eRA line on their History tab, dated the day it runs.
   - Run it once by hand. Turn it on only if you want eRA to follow attendance and gifts you add later. Issue [#11](https://github.com/SparkDevNetwork/rock-sample-data/issues/11) would load eRA with its history, so the job's first run would change nothing.
2. **Calculate Person Duplicates.** On by default, every night at 3 AM. It runs Rock's duplicate finder procedure and fills the Duplicate Finder with scored pairs of records that may be one person.
   - **History: not needed.** It scores the records as they are when it runs.
   - The Cast has duplicate records on purpose (`duplicateOf` in the [Cast README](../cast/README.md)), so the finder has real cases to show. Every generated phone number is its own (decision 259), so shared phones add no pairs. On Full 7, the Crowd's common names can still add low-scoring pairs. How long the job takes on Full 7 is not measured yet.
   - Run it now, or wait for 3 AM. Issue [#1](https://github.com/SparkDevNetwork/rock-sample-data/issues/1) would score the Cast's pairs during the load and add merges and merge requests.
3. **Calculate Peer Network.** On by default, at 2:05 AM on the 1st of each month. It fills each person's peer network from the groups they share with others.
   - **History: yes.** It reads each membership's start and end dates, so years of shared groups count.
   - Without a run, profiles show no peer network until the 1st of next month. Issue [#17](https://github.com/SparkDevNetwork/rock-sample-data/issues/17) would load it.
4. **Process BI Analytics.** On by default, every night at 5 AM. It fills Rock's analytics tables, which analytics reports and outside reporting tools read.
   - **History: partly.** Gifts and attendance are copied for all of history. People and families get a snapshot as of the day it runs, so their history starts that day.
   - Run it now only if you need these tables before morning.

## Runs on its own

These jobs are on. They fill or refresh data on their schedule, with nothing needed from you.

1. **Calculate Metrics.** Every 15 minutes.
   - The load writes the four Ministry Path metrics with a value for every week (decision 234).
   - Rock's own metrics have no values from the load. They include weekend attendance, giving, prayer, the tithing overview, and the counts behind Rock's Insights page. Each gets a value at each of its scheduled times from install week on.
   - **History: none before install week.** A metric that has never run starts at the beginning of the current week. No Rock job fills past weeks, and no issue covers it yet.
2. **Update Persisted DataViews.** Every minute. The load leaves the two persisted views, Regular Attenders and Kids Checked In Recently, for this job, which persists them in its first minute (decision 226).
3. **Update Persisted Attribute Values.** Every night at 2:15 AM. The load writes each attribute value and leaves Rock's stored display text for this job. Until it runs, Rock works out a value's display text when it shows it.
4. **Connection Request Workflow Triggers.** Every day at 7 AM. It returns each future follow-up request to Active on its follow-up date.
5. **Populate Interaction Session Data.** Every 10 minutes. It fills each session's interaction count and length, but only for sessions from the last day. The sessions the load writes for email and push opens keep these empty. Issue [#12](https://github.com/SparkDevNetwork/rock-sample-data/issues/12) would fill them.
6. **Rock's upkeep.** Database Maintenance, Update Analytics Source Postal Code, Update Persisted Datasets, and Job Pulse do the same work on a sample database as on any other.
7. **Rock's first-night jobs.** A brand-new Rock runs its one-time update jobs on its first evening and its post-install job that night, which then deletes itself (decision 170). They belong to Rock. Verify's trusted base runs them before it loads, so a load into a Rock that has not had its first night is not tested.

## Be careful

These jobs can send email, texts, or push, or reach an outside service. The sample data keeps people on reserved contact points: emails on `example.org`, phones in the 555-0100 to 555-0199 range, and push tokens that start with `sample-data-` (decisions 13 and 95, and [content standards](content-standards.md) rules 2 and 3). Cast staff use `rocksolidchurchdemo.com`, a domain Spark owns for the sample church, with no mailbox attached.

> **Warning:** A message leaves Rock only through the email, SMS, or push transport your Rock uses. On a sample site, leave real transports off, or use a test transport that keeps the mail.

1. **Location Services Verify.** On, every night at 3 AM.
   - Rock ships its SmartyStreets address service turned on, with Spark's managed key. The load marks no address as checked, so the job sends up to 1,000 sample addresses a night (its Max Records Per Run) to SmartyStreets until it has tried them all.
   - The addresses are real streets with house numbers that do not exist (content standard 4), so no real home is sent. If SmartyStreets matches one, Rock rewrites the address and moves its map point.
   - **Turn this job off on a sample site.** Issue [#2](https://github.com/SparkDevNetwork/rock-sample-data/issues/2) would mark each address as already checked, so the job finds no work.
2. **Registration Reminder.** On, every hour. Past reminders are marked sent (decision 214). Each instance whose reminder date is still ahead sends its reminder email to each registrar when that date comes. A few registrars are Cast staff.
3. **Event Payment Reminders.** On, weekdays at 9 AM. Each registration with a balance gets a payment reminder on its template's schedule until the instance ends (decision 208). A few go to Cast staff.
4. **Send Attendance Reminders for Group Type.** On, every day at 4 PM. It emails each small group's leaders on the day the group meets, and again 2 and 4 days later, until someone enters attendance. Nobody enters attendance after install, so leaders get these every week. A few Cast staff lead small groups.
5. **Process Reminders.** On, weekdays at 6 AM. The load sets each owner's reminder count as the job would, and verify runs the job and finds nothing to change (decision D-reminders-4). Each run also emails each owner their reminders due. Owners are staff and small group leaders, some of them Cast staff.
6. **Send Assessment Reminders.** On, every day at 8 AM. Each pending request's last reminder is where the job would have left it (decision 267), so its first run sends only the reminders a church's would. A request still pending gets a reminder every 7 days, for 60 days after it was made.
7. **Process Communication Flows.** On, every 5 minutes. Open runs send their remaining messages, and recurring flows start new runs on their schedules. Every run reaches only reserved addresses and numbers, because each recurring flow's audience includes a reserved email guard (decision 220, and question 18 in [Data views and reports](reporting.md)).
8. **Send Communications.** On, every 10 minutes. The scheduled Christmas Eve email waits for its send date (decision 248). Its recipients are all on `example.org` or have no email.
9. **Group Sync.** Its welcome and exit emails go out only when someone joins or leaves a synced group after install (decision 187).
10. **Spark Link.** On, every 7 hours. Every Rock sends Spark its organization name, address, public URL, and count of person records. A sample database sends the sample people's count.
11. **Only if you set them up.** Chat Sync reaches Rock's chat service only when chat is configured. Sync Media reaches a media account only when one is added. Download Payments is off, and the sample's scheduled gifts use Rock's Test Gateway. Send Following Event Notification and Send Following Suggestion Notification email staff, and Communication Queue Alert emails its recipients. All four are off by default, so leave them off.

Items 2 to 6 reach only `example.org` and the staff domain, so no real person gets them. If your site can send real email, you can still turn them off, or keep its email on a test transport.

## Nothing to do yet

These jobs work on features the sample data does not load yet, or on settings Rock ships turned off. They find nothing.

1. **Data Automation.** Rock ships every rule turned off, and the load sets none. Issue [#2](https://github.com/SparkDevNetwork/rock-sample-data/issues/2) would set them.
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
| Calculate Peer Network | Yes | 1st of the month, 2:05 AM | [Run after install](#run-after-install) |
| Calculate Person Duplicates | Yes | Daily, 3 AM | [Run after install](#run-after-install) |
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
| Family Analytics | No | Tuesdays, 8 PM | [Run after install](#run-after-install) |
| Giving Automation | Yes | Daily, 10 PM | [Already done](#already-done-by-the-load) |
| Group Sync | Yes | Every 20 minutes | [Already done](#already-done-by-the-load) |
| Index Content Collections | Yes | Daily, 4:45 AM | [Nothing to do](#nothing-to-do-yet) |
| Job Pulse | Yes | Every minute | [Runs on its own](#runs-on-its-own) |
| Location Services Verify | Yes | Daily, 3 AM | [Be careful](#be-careful) |
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

1. When the load starts writing what a job writes, move the job to [Already done by the load](#already-done-by-the-load) and name the decision. For example, issue #11 moves Family Analytics there.
2. When the load starts writing data a job sends from, check whether the job can send, and to whom.
3. When a new Rock release joins `release/rock-releases.json`, compare its jobs with this table. A clean Rock's `ServiceJob` table lists each job's class, whether it is active, and its schedule.
