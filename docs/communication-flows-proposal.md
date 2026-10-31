# Communication Flows: proposal

Status: proposal. Jon answered every question on 2026-10-29 (Section 8). No loader code until the coordinator says to start.

Rock Solid Church gets eleven Communication Flows and one scheduled communication. Each one serves a Ministry by Design stage and moves people toward the next one, and most end when the person takes the step that marks it. Together they use every flow type, every conversion goal, every exit rule, and every message channel Rock has. Each flow has months of history: people who entered, the emails, texts, and pushes they got, opens and clicks, people who reached the goal, and people who left. Some runs are still under way on install day, and Rock keeps sending them. The centerpiece is a **Baptism flow**. Staff or a web form can put a person in at any time, they get steady reminders, and the flow stops for them on its own the day their Baptism step is recorded.

One flow already exists on `main`: "After Starting Point" (decision 163). It keeps its shape, with one message changed (question 8).

The example dates assume an install day of Thursday, October 29, 2026. Every real date is a rule counted from install day.

## Ministry by Design

The step programs and the calendar follow [Ministry by Design](https://electric-eustoma-cf2.notion.site/Ministry-by-Design-325c7b9c0987809188aefa868c2da771) (decisions 84 and 183). The flows follow it too. Its Ministry Systems move a person from being noticed, to belonging, to formation, to responsibility: Notice, Belong, Equip, Contribute, and Lead. Care and Re-engage run alongside every stage. Four of its rules shape the flows:

1. **One primary system.** Every environment serves one system first, so its invitation is clear. So each flow serves one stage, and its messages ask for one next step.
2. **Growth before serving.** Discipleship forms readiness, and "responsibility without formation creates pressure." So no flow asks someone to serve before an Equip step, except when they already chose to.
3. **The goal is the step.** Where a step marks the stage, the flow's goal is that step, so the flow ends for a person when they take it. Where no step fits, the goal is the nearest real sign, and the table says why.
4. **The norm, not a gate.** People take steps out of order. A flow's audience requires an earlier stage only when the framework says readiness matters, and the table says why.

| # | Flow | Primary system | Moves people toward | Goal | Audience needs an earlier stage? |
|---|---|---|---|---|---|
| 8 | Christmas at Rock Solid | Notice | Belong, through the friends people invite | None. Inviting a friend leaves no record | No. Involved adults invite |
| 11 | Built on the Rock Follow-Up (new) | Contribute | Contribute: a campaign pledge | Entered the pledgers view. Rock has no goal type for a pledge, and the Give step tracks steady giving, not a pledge | No. Involved adults who have not pledged |
| 3 | First-Time Guest Welcome | Notice | Belong | Starting Point Class step | No. Any first-time guest |
| 1 | After Starting Point | Belong | Belong: a small group | Joined a small group, the same moment as the Small Group step | No. Anyone who finished the class |
| 6 | Student Camp Registration | Belong (Student Path) | Belong: Student Camp | Joined next summer's camp group. The camp step comes at camp, months after the window | No. Students not registered |
| 5 | Baptism Sunday Invitation | Equip | Equip: Baptism | Completed the Baptism Interest form | Yes, regular attenders. The framework invites people into formation once they belong |
| 2 | Baptism Next Steps | Equip | Equip: Baptism | Baptism step | No. Anyone, at any time |
| 7 | Women's Retreat Early Bird | Equip | Equip: the retreat | Registered. No step marks a retreat | No. Women who attend |
| 9 | After Foundations (new) | Equip | Contribute | Serve step | No. Entering at the end of the course is timing, not a gate |
| 10 | Lead a Group (new) | Contribute | Lead | Lead step | Yes, an Equip step, on a serving team now, and in a small group. The framework grows leaders "through formation and qualification rather than recruitment alone" |
| 4 | We Miss You | Re-engage | Back to Belong | Back in the last 30 days. No step marks coming back | Yes, regulars who stopped. That is what Re-engage serves |

**Every transition has a flow.** Notice to Belong: 3 and 8. Within Belong, class to group: 1, and camp on the Student Path: 6. Belong to Equip: 5, 2, and 7. Equip to Contribute: 9, and within Contribute, a campaign pledge: 11. Contribute to Lead: 10. Re-engage: 4. Before this rethink, Equip to Contribute and Contribute to Lead had no flow, so Flows 9 and 10 are new.

**Left out on purpose.** Pastoral Care gets no flow. The framework makes care personal and owned by a named person, and an automated series would replace that. The Give step itself gets no flow: Rock has no goal type for a giving journey stage. Flow 11 serves generosity through the campaign instead. Within Equip, the email each person gets after their baptism already invites them to Foundations (decision 96).

## 1. How Communication Flows work in Rock

Communication Flows shipped in Rock 18.1. A flow is a series of emails, texts, and pushes, sent days apart, toward one goal. Rock 20.0.10 and Rock 21.0.2 store them the same way.

**The tables.** All six are in `Rock/Model/Communication/` in both checkouts.

| Table | What it holds |
|---|---|
| `CommunicationFlow` | The flow: name, category, trigger type, audience data view, schedule, goal type and settings, goal target percent and window in days, exit rule, public name, and status flags |
| `CommunicationFlowCommunication` | Each message: name, order, days to wait after the previous message, time of day, channel (email 1, SMS 2, push 3), and its flow template |
| `CommunicationFlowInstance` | One run of the flow, with a start date (a date, no time) and two flags: messaging done, and goal tracking done |
| `CommunicationFlowInstanceRecipient` | A person in a run: Active or Inactive, why they left, the recipient row that unsubscribed them, and whether they had met the goal before the run started |
| `CommunicationFlowInstanceCommunication` | Links a run's message to the ordinary `Communication` Rock sent |
| `CommunicationFlowInstanceCommunicationConversion` | Each conversion: the date, the person, and the message and recipient row it is credited to |

The messages themselves are ordinary bulk `Communication` and `CommunicationRecipient` rows, with opens and clicks as interactions. The loader already writes these for every other send.

**The three flow types** (`Rock.Enums/Communication/CommunicationFlowTriggerType.cs`):

1. **Recurring.** On each date of its schedule, Rock starts a new run and fills it from the audience data view.
2. **On-Demand.** No schedule and no data view. A workflow's "Activate Communication Flow" action adds a person to today's run, creating it if needed.
3. **One-Time.** One run, on one date, to a data view.

**The six goals** (`ConversionGoalType.cs`), each with the date Rock records as the conversion:

| Goal | Setting | Conversion date |
|---|---|---|
| Completed Form | A workflow type | The workflow's completed time, when the person started it |
| Registered | One registration instance | The registrant's created time |
| Joined Group of Type | A group type | The group member's date added |
| Joined Specific Group | One group | The group member's date added |
| Entered Data View | A data view | When the job saw the person in the view |
| Took Step | A step program and step type | The step's completed time, with a complete status |

A conversion counts only inside the window: from the run's start date for the goal's number of days. Rock credits it to the newest message the person got on or before that day. Rock counts a person once, and skips people who met the goal before the run started.

**The four exit rules** (`ExitConditionType.cs`): Last Message Sent, Any Email Opened, Any Email Clicked, and Conversion Achieved. A person who leaves gets a reason: unsubscribed, goal met, opened, clicked, last message sent, person made inactive, or unsubscribed from the flow.

**The job.** `Rock/Jobs/ProcessCommunicationFlows.cs` runs every 5 minutes. It reads only **active** flows that still have messaging or goal tracking open. For each, it marks unsubscribes, adds conversions, applies the exit rule, and creates each run's next message on the day it is due. For a recurring flow, it starts a new run on each schedule date, but only on that day. It never backfills a missed date.

**The screens.** Rock's Communication Flow List, Detail, Performance, and Message Metrics blocks (`Rock.Blocks/Communication/`) compute everything live. They show the conversion rate, goal progress (Not Tracked, Pending, Achieved, or Missed), conversions, average time to convert, unsubscribes, and each message's open, click, conversion, and unsubscribe rates. Nothing is cached, so the history only has to be right.

## 2. Coverage

Every flow setting Rock has, and the sample flow that shows it. Flow numbers refer to Section 3.

| Setting | Value | Shown by |
|---|---|---|
| Flow type | Recurring | 3 First-Time Guest Welcome, 4 We Miss You, 5 Baptism Sunday Invitation, 6 Student Camp Registration, 7 Women's Retreat Early Bird, 8 Christmas at Rock Solid, 10 Lead a Group |
| | On-Demand | 1 After Starting Point (exists), 2 Baptism Next Steps, 9 After Foundations |
| | One-Time | 11 Built on the Rock Follow-Up |
| Schedule | Weekly | 3 |
| | Monthly | 4 |
| | Yearly | 6 (March), 8 (December 1) |
| | Specific dates | 5 (before each baptism Sunday), 7 (May and September), 10 (before each group launch) |
| Goal | Completed Form | 5 (the Baptism Interest form) |
| | Registered | 7 (the Women's Retreat) |
| | Joined Group of Type | 1 (small groups) |
| | Joined Specific Group | 6 (the Student Camp group) |
| | Entered Data View | 4 (coming back, which no step marks), 11 (pledged to Built on the Rock) |
| | Took Step | 2 (Baptism), 3 (Starting Point Class), 9 (Serve), 10 (Lead) |
| | No goal | 8 |
| Exit rule | Last Message Sent | 1, 3, 8 |
| | Any Email Opened | 4 |
| | Any Email Clicked | 5 |
| | Conversion Achieved | 2, 6, 7, 9, 10, 11 |
| Channel | Email | Every flow |
| | SMS | 2, 3, 4, 5, 6, 8, 9, 10, 11 |
| | Push | 8 |
| Goal progress | Not Tracked, Pending, Achieved, Missed | 8; recent runs of 2 and 9; most runs of 3; most runs of 4 |
| Flow status | Active, Inactive, Completed | 1, 2, 3, 5 to 10; 4, paused to show the feature (questions 1 and 12); 11 |
| Why a person left | Goal met, opened, clicked, unsubscribed from the flow, person made inactive | 2, 6, 7, 9, 10, 11; 4; 5; a few in every flow; a few in 2 |
| | Last message sent | None. Rock's job prunes recipients only while a run is still sending, and it marks the run done on the pass after its last message, so it never records this reason. Flows 1, 3, and 8 leave everyone Active, as Rock does |
| Unsubscribe at the Flows level | Unsubscribe report and flow screens | A few in every flow |
| Goal met before the run | Skipped in the rate | 3, 4, and 9 |
| Run vs. flow average | Needs two or more runs | 1 to 6, 8, 9, and 10 |
| Scheduled send | A communication with a future send date | The Christmas Eve reminder (not a flow) |
| Category and public name | Each flow | Every flow |

**Left out.** Rock's "Unknown" leave reason, which no code path sets on purpose.

## 3. The flows

All flows send from the church's address, through flow templates built on the Rock Solid Church email template (decision 140), as the existing flow does. Texts go out on the main line, and the camp texts on the student line. Each flow's category is its primary Ministry by Design system: Notice, Belong, Equip, Contribute, Lead, or Re-engage. So Rock's flow list groups the flows by stage, and a church sees its path at a glance.

### Flow 1. After Starting Point (exists)

On-Demand, three emails, goal Joined Group of Type (small groups) in 30 days, exit Last Message Sent. It gets a category and a public name. One message changes (question 8): the third email today invites people to serve three weeks after the class. Ministry by Design puts growth before serving, so it becomes an Equip invitation instead: Foundations, and baptism for anyone not yet baptized. Flow 9 now asks people to serve, after Foundations. People who already asked to serve, like the Gilberts, still serve: the step program's prerequisite is only Starting Point. The most recent class's run is mid-flow at install, so its later emails go out after install (decision 163 changes here, since it kept only finished classes).

### Flow 2. Baptism Next Steps

See Section 4.

### Flow 3. First-Time Guest Welcome

- **Purpose.** Welcome every adult first-time guest and help them come back.
- **Trigger.** Recurring, every Wednesday at 9 a.m., to First Visit Last Week, Reserved Email: adults who accept mass email and came to worship for the first time in the last 7 days. It starts Wednesday because each guest's campus pastor sends a personal welcome on Monday (decision 159).
- **Messages.** Email that Wednesday about coming back (parking, kids, coffee). SMS 3 days later, before Sunday ("Any questions before Sunday? Just reply."). Email 4 days after that, inviting them to the next Starting Point Class.
- **Goal.** Took Step: Starting Point Class, the first Belong step. Window 84 days, long enough for the next four-week class to start and finish. The target is set from what the data finds, likely about 10% (see Section 5).
- **Exit.** Last Message Sent. A guest gets all three messages, because the welcome matters even after they sign up.
- **On install day.** Active. Its audience includes the "Reserved or No Email" guard. The runs from the last two Mondays are mid-flow, so their remaining messages go out after install (Section 6, "After install").
- **Cast.** Jordan and Hannah Ellis, who first came 120 days ago. They are in the run for the Monday after. Hannah clicked the Starting Point link, but neither has taken the class, so their run shows the goal missed. They still come most weeks, which is "the norm, not a gate": belonging does not wait for the class.

### Flow 4. We Miss You

- **Purpose.** Reach regulars who have stopped coming, before they drift away for good.
- **Trigger.** Recurring, the first Tuesday of each month, to the Newly Missed data view: Drifting Adults (the We Miss You rule) who accept mass email and came at least once in the last 120 days. So a person qualifies for about one month, and no one gets it twice.
- **Messages.** Email ("We miss you, and we're praying for you"). SMS 7 days later, only to those still in the flow.
- **Goal.** Entered Data View: Back in the Last 30 Days. Window 60 days. Target 15%.
- **Exit.** Any Email Opened. The point is to be seen once, so people who open the email do not get the text.
- **On install day.** Paused, as the pause example. Its audience still includes the "Reserved or No Email" guard, so turning it on is safe (Section 6, "After install").
- **Cast.** Luis and Teresa Ortiz, who stopped coming about six months ago. They are in a run about three months before install, and each leaves when they open the note. Neither has come back, so their run shows the goal missed.

### Flow 5. Baptism Sunday Invitation

- **Purpose.** Before each baptism Sunday, invite regular attenders who have not been baptized.
- **Trigger.** Recurring, on specific dates: 5 weeks before Easter and 5 weeks before the July baptism Sunday. Audience: the Unbaptized Regular Attenders data view, adults who are Regular Attenders and have no Baptism step.
- **Messages.** Email ("Is it time?") with a button to the Baptism Interest form. SMS 7 days later.
- **Goal.** Completed Form: the Baptism Interest workflow (question 2). Window 35 days. The target is set from what the regular attender rule finds in the data, likely about 5% (see Section 5).
- **Exit.** Any Email Clicked. A person who clicked has the form in front of them, so the text would only nag.
- **On install day.** Active. Its audience includes the "Reserved or No Email" guard. A run is mid-flow only when install falls in the week after a run date (Section 6, "After install").
- **Cast.** None pinned. People who fill out the form inside a run's window convert, and the form puts them in the Baptism flow.

### Flow 6. Student Camp Registration

- **Purpose.** Fill next summer's camp before the early bird ends April 30.
- **Trigger.** Recurring, every year on the first Monday of March, to Students Not Registered for Camp (decision 215), with the "Reserved or No Email" guard. Texts go out on the student line, to the students themselves. A flow sends only to its audience, so no parent gets a copy, and every student phone is in the reserved range.
- **Messages.** Email to each student. SMS on the student line 5 days later. Email 14 days later about the early bird.
- **Goal.** Joined Specific Group: the Student Camp group for the coming summer. Camp has a girls' and a boys' registration instance, and Rock's Registered goal takes only one instance, but both fill this one group. So this goal counts every camper. Window 60 days. Target 40%.
- **Each year's goal.** A church points the goal at the new summer's group each year. The flow's setting names the camp group whose registration is open on install day. Each past run's conversions are the joins to that year's group, so the history stays true.
- **Exit.** Conversion Achieved. A student who registers gets no more reminders.
- **History.** Two runs, last March and the March before (the 2-year window, so the run vs. flow average has two runs).
- **On install day.** Active. The next run is the coming March. The view needs recent student check-ins, and none are written after install, so that run usually finds nobody and sends nothing.
- **Cast.** Ben and Abigail Lee, whom Sarah Lee registers on the same day for next summer's camp, convert in a run when that day falls in its window. The build checks whether it does, and pins them only then.

### Flow 7. Women's Retreat Early Bird

- **Purpose.** Invite women to the Women's Retreat while registration and the early bird are open.
- **Trigger.** Recurring, twice a year: the first Tuesday of May (registration is open) and the first Tuesday of September (the early bird ends October 31). The audience is Women Who Attend, with the guard.
- **Messages.** Email. Email 14 days later ("Bring a friend: use code FRIEND").
- **Goal.** Registered: the next Women's Retreat registration instance, the one open on install day. Window 60 days. Target 10%.
- **Each year's goal.** Rock's Registered goal names one instance, and the sample data has only the next retreat's. So the history keeps only runs on or after the day that instance's registration opened, at most two. A church points the goal at the new instance each spring.
- **Exit.** Conversion Achieved.
- **On install day.** Active. The next run is the coming May or September, and it reaches women who came to worship in the 12 months before it, all on reserved addresses. On a fresh install that is most of the women in the church.
- **Cast.** None pinned. Women who registered in a run's window convert.

### Flow 8. Christmas at Rock Solid

- **Purpose.** Help the church invite friends to Christmas Eve.
- **Trigger.** Recurring, every December 1, to Involved Adults (adults who came to worship, gave, or were in a small group or on a serving team in the last 12 months), with the guard.
- **Messages.** Email with service times on December 1. Push on December 20 ("Who will you invite?"), to people with the app. SMS the morning of December 24 ("See you tonight").
- **Goal.** None. Goal progress shows Not Tracked.
- **Exit.** Last Message Sent.
- **History.** The last two Decembers.
- **On install day.** Active. The next run is the coming December 1, to nearly every involved adult, all on reserved addresses. It is the largest send after install (see "Follow-up," item 2).
- **Cast.** The Deckers and the other involved Cast adults on `example.org` get it. Ted Decker opens it on his phone. Staff are not in the guarded audience, so they are not in its history either.

### Flow 9. After Foundations (new)

- **Purpose.** Help people who just finished Foundations find their place to serve. This is Equip to Contribute: they have been formed, and now they are invited to take part.
- **Trigger.** On-Demand, like After Starting Point. Everyone who finishes a Foundations course and is not serving yet joins the run for the day after its last night. Most people who take Foundations already serve (9 in 10 on Full 7), and a workflow that invites people to serve would leave them out.
- **Messages.** Email that day ("You finished Foundations. What's next?"), with the serving teams at their campus. SMS 5 days later ("Want help finding a team? Just reply."). Email 14 days later, with stories from volunteers and the next Serve Sunday.
- **Goal.** Took Step: Serve, within 90 days. Target 15%, set from the data: on Full 7, 18% of the people who entered took the Serve step in the window.
- **Exit.** Conversion Achieved. A person who joins a team gets no more asks.
- **On install day.** Active. The most recent course's run is mid-flow, so its later messages go out after install.
- **Cast.** None pinned. If a Cast adult finished Foundations in the window and then joined a team, the build pins them.

### Flow 10. Lead a Group (new)

- **Purpose.** Before each small group launch, invite people who are growing in a group and serving to lead one. This is Contribute to Lead.
- **Trigger.** Recurring, 6 weeks before each group launch: the fall launch on the second Sunday of September and the spring launch on the third Sunday of January (`configuration/communications.json`). So it runs in early August and early December. The audience is the reports work's Ready to Lead view (adults with an Equip step who serve on a team now, are in a small group, and have no Lead step), with the guard. The framework grows leaders from people who have been formed and who serve, so this audience needs both. Giving alone does not count.
- **Messages.** Email from the church ("Have you thought about leading?"). SMS 7 days later, with the Group Leader Training date. Email 14 days later: what leaders get, from training to the Leader Huddle.
- **Goal.** Took Step: Lead, within 60 days, so it covers the launch.
- **Exit.** Conversion Achieved.
- **History.** The runs before each launch in the last 2 years, about four.
- **On install day.** Active. Its next run comes 6 weeks before the next launch. Group memberships do not fade after install, so it reaches the same Ready to Lead people, all on reserved addresses.
- **Cast.** None pinned. Conversions are the people the small group data already makes new leaders at each launch. If there are few, the target follows the data.

### Flow 11. Built on the Rock Follow-Up (new, the One-Time flow)

- **Purpose.** After Commitment Sunday for the Built on the Rock campaign (decision 97), invite involved adults who have not pledged yet. The campaign's own emails (the Commitment Sunday invitation, the thank-you, and the updates) stay as they are. This flow is the personal follow-up a church sends to everyone who missed the day.
- **Trigger.** One-Time, the Monday after Commitment Sunday, about 40 weeks before install. Its audience is Involved Adults who have not pledged to Built on the Rock. Rock's pledge filter matches only the person named on a pledge, not their spouse, so the view leaves out every household where someone pledged. It reads pledge data, so it is filed in the reports work's locked Finance category. Rock's flow job checks no person's security, so the lock limits only who can open the view.
- **Messages.** Four, over two weeks. Email that Monday ("Missed Commitment Sunday? There's still time"). SMS 4 days later. Email 6 days after that, with a story about the Student Center plans. Email 4 days after that ("Pledge cards close Sunday").
- **Goal.** Entered Data View: Built on the Rock Pledge Households (in Finance), so a person converts when anyone in their household pledges. Window 42 days. The target is set from the data, likely about 15%.
- **Exit.** Conversion Achieved. A person who pledges gets no more asks.
- **On install day.** Its one run finished months ago, so it shows Completed and can never send again.
- **Cast.** None pinned, unless a Cast pledger's pledge falls in the window. The build checks the Cast's campaign stories (decisions 110 to 112).

### A scheduled communication (not a flow)

Jon pointed out that a message sent once is better shown as a regular communication with a future send date. So the sample data adds one.

- **What.** "Christmas Eve at Rock Solid: see you tomorrow," an email from the church's address, approved by Daniel Peak, scheduled for 10 a.m. on the December 23 after install.
- **How Rock stores it.** Status Approved, no `SendDateTime`, and `FutureSendDateTime` set. Rock's Send Communications job sends it when that time comes, and the Communication List shows it as scheduled.
- **Recipients.** Phoenix's involved adults on reserved addresses, each written as a Pending recipient. It names no communication list, because Rock can refresh a list's recipients when it sends, and the newsletter lists include staff.
- **Where it lives.** A new `scheduled` entry in `configuration/communications.json`: the library email key, the sender and approver, the send rule ("December 23, 10 a.m., the next one after install"), and the audience rule. The message text is a new library email.

**Data views.** The flows use views from the reports and data views proposal (`docs/reporting.md` Sections 4.11 to 4.13 on its branch), built with Rock's own filters on its shared foundation views. They are filed by subject in its tree under the church's Rock Solid Church category (decision 185), not by the flow that uses them:

| View | Key | Category | Used by |
|---|---|---|---|
| First Visit Last Week, Reserved Email | `first-visit-last-week-reserved-email` | Sample Data | Flow 3 audience: First Visit Last Week (Notice) and the guard |
| Newly Missed, Reserved Email | `newly-missed-reserved-email` | Sample Data | Flow 4 audience: Newly Missed (Re-engage) and the guard |
| Back in the Last 30 Days | `back-in-the-last-30-days` | Re-engage | Flow 4 goal |
| Unbaptized Regular Attenders, Reserved Email | `unbaptized-regular-attenders-reserved-email` | Sample Data | Flow 5 audience: Unbaptized Regular Attenders (Equip, built on Regular Attenders) and the guard |
| Students Not Registered for Camp, Reserved Email | `students-not-registered-for-camp-reserved-email` | Sample Data | Flow 6 audience: the existing registration view (decision 215) and the guard |
| Women Who Attend, Reserved Email | `women-who-attend-reserved-email` | Sample Data | Flow 7 audience |
| Involved Adults, Reserved Email | `involved-adults-reserved-email` | Sample Data | Flow 8 audience |
| Ready to Lead, Reserved Email | `ready-to-lead-reserved-email` | Sample Data | Flow 10 audience |
| Involved Adults Not Yet Pledged | `involved-adults-not-yet-pledged` | Finance | Flow 11 audience: Involved Adults not in a pledging household. Its run is finished, so it needs no guard |
| Built on the Rock Pledge Households | `built-on-the-rock-pledge-households` | Finance | Flow 11 goal: everyone in a household where someone pledged |

The reports work files its views by Ministry by Design system too: Notice, Belong, Equip, Contribute, Lead, and Re-engage, plus Foundations and a Path category of stage views. Flow 3's goal is now the Starting Point Class step, so it no longer uses Regular Attenders directly.

**Why step goals, not stage views.** The reports work also has stage views, such as Equip Stage and Contribute Stage, that an Entered Data View goal could use. The flows keep Took Step goals where one step fits, for two reasons. A step goal ends the flow for a person when they take the next step on the path, which is how the framework marks movement. And Rock dates an Entered Data View conversion by when its job last ran the view, while a step goal uses the step's own date (Section 6, risk 4).

The views are in the reports work's slice 2. If flows slice 1 lands first, it adds them to `configuration/reporting.json` with these keys, plus the foundation views they build on, as that proposal describes them. Rock's flow job checks no person's security, so a view's category limits only who can open it, not whether the flow works.

**Message text.** New entries in the message library in `content/communications`, about 28 flow messages in all, in the church's voice, plus a new third email for After Starting Point and the scheduled Christmas Eve email. No images are needed: the emails use the branded template's logo.

## 4. The Baptism flow

"Baptism Next Steps" is the flow you can put anyone in, at any time. It keeps reminding them, gently, until they are baptized, and then it stops for them on its own.

**How Rock does this.** It is an On-Demand flow whose goal is Took Step, with Rock's Discipleship Path and its Baptism step type (`801CC43C-0641-4271-939E-75E428F31D06`, the one the steps data already uses). Its exit rule is Conversion Achieved. Each time the job runs, it finds people whose Baptism step was completed inside their window, records the conversion, and marks them Inactive with the reason "goal met." The job never sends an Inactive person another message. That is the "stops on its own" behavior, and it needs no custom code.

**How a person gets in.** Through Rock's "Activate Communication Flow" workflow action, which is the only way into an On-Demand flow. The proposal adds one workflow type, "Baptism Interest," with a short form and that action (question 2). People reach it two ways:

1. They fill out the "I'm interested in baptism" form on the website, often after the Baptism Sunday Invitation (Flow 5).
2. Staff launch it from the person's profile after a conversation.

**The messages.** Four, over six weeks (Jon, 2026-10-29: "Let's limit it to four"). Each one does a different job, and they alternate email and text, so the reminders are steady but not pushy.

| # | Wait | Day | Channel | Message |
|---|---|---|---|---|
| 1 | 0 days, 10 a.m. | 0 | Email | "We're so glad you're thinking about baptism": what it means, and who to ask |
| 2 | 2 days | 2 | SMS | "Any questions about baptism? Just reply to this text." |
| 3 | 12 days | 14 | Email | What happens on a baptism Sunday and what to bring, with stories from people baptized here |
| 4 | 28 days | 42 | SMS | "Our next baptism Sunday is coming up. Want us to save you a spot? Reply YES." |

Left out from the first draft: a push with a story video, which reaches only people with the app; a second stories email; a questions email; and a "whenever you're ready" email. Message 3 carries the stories, and message 4 asks for the decision. Push stays covered by the Christmas flow.

**The goal.** Took Step: Baptism, within 180 days. Target 35%, set from the data: on Full 7, 39% of the people who filled out the form were baptized in the window. The window runs well past the last message, so a person baptized a few months after the reminders end still counts. Runs from 6 weeks to 6 months ago show Pending on install day. Staff can put a person back in for another round at any time.

**Who is in it.** The flow reads the Baptism steps the steps part already wrote. It never changes them.

- **People baptized here** in the last 18 months: about 70% of them entered the flow first, from 1 to 16 weeks before their baptism. The rest signed up on the day. Each one who entered converts, credited to the newest message before their baptism, and gets no more reminders.
- **People not baptized:** about as many again entered and are not baptized. They got all four messages and left after the last one, or they are still Pending. So the flow converts about half of the people who enter, a little above its 50% target.
- **People mid-flow:** everyone who entered in the 6 weeks before install is still in it, with messages left. Rock's job sends those after install (see "On install day" below).
- **A few** unsubscribe from the flow, and a few are made inactive (for example, they moved away).

**Cast stories.**

1. **Josh Sanders** filled out the Baptism Interest form 66 days before install, the day he texted Ted "I turned in my baptism form." That put him in Baptism Next Steps. It came weeks after the July invitation's window closed, so it is not a Flow 5 conversion. He got the welcome email and the text, was baptized 60 days before install, and left the flow with the reason "goal met." He never got message 3.
2. **One Cast adult who is not baptized,** chosen at build from the unbaptized Cast adults with an `example.org` email, entered 30 days before install. They got messages 1 to 3 and opened the email about baptism Sunday. Message 4, the "save you a spot" text, is still to come: Rock's job sends it 12 days after install.

**On install day.** The flow is Active. People who entered in the last 6 weeks are mid-flow (Jon, 2026-10-29: "I would want people to be in mid-flow on install day"), and Rock's job sends their next messages on their dates. Anyone testing the sample data can also launch "Baptism Interest" for a person and watch Rock run the flow for real. Section 6, "After install," says who these sends can reach.

## 5. How the history is backfilled

**How far back.** Communication history covers 2 years (decision 90). After Starting Point keeps its 2 years. The new flows started 18 months before install day, so each one has a launch date, a few rough early runs, and a steadier recent stretch. The yearly flows use the full 2 years, and the One-Time flow has its single run.

**What the loader writes.** Everything Rock's job writes, and nothing it does not. For each run: the instance, its recipients with status and leave reason, one bulk communication per message from that message's flow template, the recipients and their send outcomes, opens and clicks as interactions with `OpenedDateTime` set, unsubscribes at the Flows level, and conversions. A run that is mid-flow at install has every message that was due before install, and nothing more: Rock's job writes the rest when they come due. The code that already writes After Starting Point is generalized to write all eleven flows, and it uses the same email planner, so a person on "No mass emails" never gets a flow email, as Rock decides.

**Rates.** Opens and clicks follow the measured email shapes the bulk email already uses (decision 149). The flow rates are designed, because no calibration source has flows yet:

| Flow | Target | Designed result |
|---|---|---|
| 2 Baptism Next Steps | 35%, set from the data | 39% on Full 7, recent runs Pending |
| 3 First-Time Guest Welcome | 2%, set from the data | 2.2% on Full 7 |
| 4 We Miss You | 50%, set from the data | 50.4% on Full 7 |
| 5 Baptism Sunday Invitation | 2%, set from the data | 2.2% on Full 7 |
| 6 Student Camp Registration | 40% | About 45% |
| 7 Women's Retreat Early Bird | 10% | About 8%, Missed |
| 8 Christmas at Rock Solid | None | Not Tracked |
| 9 After Foundations | 15%, set from the data | 18% on Full 7, recent runs Pending |
| 10 Lead a Group | Set from the data, about 3% | Whatever each launch's new leaders give |
| 11 Built on the Rock Follow-Up | Set from the data, about 15% | Whatever the pledges made after Commitment Sunday give |

**Targets from the data.** Flow 5 uses the church's one "regular attender" rule, 4 or more worship check-ins in the last 12 weeks, through the views built on Regular Attenders (coordinator decision, 2026-10-29), and adds no threshold of its own. Flows 3, 9, and 10 have step goals, and their steps come from the steps plan. So for Flows 3, 5, 9, 10, and 11 the build measures the share of each flow's people who reach the goal on Full 7, and sets the target just under it, rounded down to a whole 5% for Flows 3, 9, and 11 and a whole 1% for Flows 5 and 10. So most runs show Achieved and a few show Missed, and the targets are recorded in `configuration/` with the measured share beside them.

Flow unsubscribes run about 0.3% per email, a little above bulk email, because flows send more often.

**Every conversion is real.** Each one is a step, group join, registration, finished workflow, or data view match that is already in the data, inside the window. The flows never invent an outcome to hit a rate. If the rates miss by a lot, the audience rules change, not the outcomes.

**Size.** Rough estimates for Full 7, to be measured at build:

| Flow | People | Recipient rows |
|---|---|---|
| 3 Guest Welcome, 78 weekly runs | About 1,000 | About 3,000 |
| 4 We Miss You, 18 monthly runs | About 500 | About 800 |
| 2 Baptism Next Steps | About 600 | About 1,500 |
| 5 Baptism Sunday Invitation, 3 runs | About 3,000 each | About 12,000 |
| 6 Student Camp, 2 runs | About 800 each | About 4,000 |
| 7 Women's Retreat, up to 2 runs | About 2,500 each | About 10,000 |
| 8 Christmas, 2 runs | About 6,000 each | About 28,000 |
| 9 After Foundations, a run per course | About 800 | About 2,000 |
| 10 Lead a Group, about 4 runs | About 300 each | About 3,500 |
| 11 Built on the Rock Follow-Up, 1 run | About 4,000 | About 10,000 |
| **Total** | | **About 75,000** |

That is about 5% on top of the 1.6 million communication recipients Full 7 already has. The flows write inside the Communications step's deferred index rebuild, so they add a few seconds to a load of about 173 seconds. Core 1 and Core 3 get the same flows with far fewer people.

**Dates.** Every date is a rule from install day, and every send the loader writes is before it. Runs that are mid-flow at install send the rest after. Times are local wall-clock times, like the rest of the data. Each run's start date is a date only, as Rock stores it. Yearly flows keep the 2-year window, not 18 months, so each has two runs to compare. A flow whose goal names one year's group or registration instance counts each past run against that year's group, and its setting names the one open on install day.

## 6. Risks, and Rock 20 vs Rock 21

**Rock 20 vs Rock 21.** The tables, columns, enums, job, and workflow action are the same in both checkouts, so one data shape works for both packages. The differences are in screens and in editing: Rock 21 can move a one-time flow's start date when its schedule is edited, Rock 20 escapes names in its goal progress display, and Rock 21 relabels one workflow action field. None of them changes the loaded data.

**Risks.**

1. **Sending after install.** Jon wants flows to send after install (questions 1 and 4). So runs that were mid-flow at install send their remaining messages, and the Active recurring flows start new runs on their dates. See "After install" below for what keeps every one of those sends away from real people.
2. **The job as referee.** Rock re-adds conversions for any run whose goal tracking is still open. If the loader credits a conversion differently from Rock, the job would add a second row. So verify runs Rock's Process Communication Flows job once after each load, as it runs Group Sync today (decision 187). It expects no new conversion. It may create messages that are due on install day, and each must reach only reserved contact points.
3. **Mid-flow timing.** Rock dates each next message from the day the previous one actually went out. So the loader writes every message that was due before install as sent, and leaves the next one for the job. No message is ever overdue at install, so the first job run does not send a backlog.
4. **Entered Data View dates.** Rock dates these conversions by when the job last ran the view. History needs each person's real date, so the loader writes them for runs whose window has closed. For a run whose window is still open at install, the loader credits the way the job would on install day, so the referee adds nothing.
5. **Two unsubscribe paths.** Rock marks a flow unsubscribe one way from the email preference page and another way from the job. The loader follows the preference page path, which is what a person clicking the link does, and the build checks both against Rock's code.
6. **Workflows are new here.** No workflow types exist in the sample data yet. The Baptism Interest type must open and edit in Rock's workflow screens like one a person built, and Rock Cleanup must find nothing to fix.
7. **Cast dates.** Josh Sanders' text thank-you to Ted is dated 62 days before install, before his baptism 60 days before. The flow uses his Baptism step date. The text's date is a small Cast fix for whoever owns the threads.

### After install

**Jon's direction, 2026-10-29.** "I don't think nothing sends after install rule is valid. I think I do want things to send." So decision 95's blanket rule retires. Flows, and runs already under way, may send after install. One safety rule takes its place: whatever sends after install can reach only reserved contact points. Jon added: "We'll work on some of that how that works before we're done." The open details are listed under "Follow-up" below.

**What can send.**

- **Runs that are mid-flow at install** send their remaining messages on their dates. That covers the On-Demand flows (After Starting Point, Baptism Next Steps, and After Foundations) and the Active recurring flows. A run is mid-flow when its first message went out before install and its last one falls after.
- **The six Active recurring flows** start new runs on their schedule dates: First-Time Guest Welcome, Baptism Sunday Invitation, Student Camp Registration, Women's Retreat Early Bird, Christmas at Rock Solid, and Lead a Group.
- **The scheduled Christmas Eve email** goes out on its date.
- **Anyone testing** can launch the Baptism Interest workflow for a person and start a new Baptism run.
- **Nothing else sends on its own.** We Miss You ships paused, to show the pause feature (question 12). The One-Time flow's run is finished.

**Who they can reach.** Every generated person, business, and prayer guest has an `example.org` email or none, a 555-0100 to 555-0199 phone, and a `sample-data-` push token (decision 13). Cast people use `example.org` too, **except the 22 Cast staff households, whose emails are on `rocksolidchurchdemo.com`.** That domain is real: it has live Google mail servers, so an email to a staff address would be delivered.

- **First-Time Guest Welcome** cannot reach staff. Its audience is people whose first worship visit was in the last 7 days, and every staff member first came years ago. No attendance is written after install, so the view finds only the last week's guests, for one Monday, and then nobody.
- **Baptism Sunday Invitation** could reach staff without the guard, since a staff member with no Baptism step is an unbaptized regular attender. Its audience shrinks to nobody 12 weeks after install, because no one keeps 4 check-ins in 12 weeks once attendance stops. Its next date, 5 weeks before Easter or the July baptism Sunday, is usually months away, so it often sends nothing at all.
- **We Miss You** ships paused for a reason. No attendance is written after install, so every regular attender "stops coming" on install day. About 90 to 120 days later, Newly Missed holds nearly every regular, staff included, and one monthly run would email thousands. A church that turns it on gets that run. The guard keeps it on `example.org`, but those emails bounce, which can hurt a real mail service's sending reputation in a test copy.
- **Mid-flow runs** reach only the people already in them. On-Demand flows have no audience view, so the loader puts a person in a run that is still open at install only if their contact points are reserved. A staff member can be in a finished run in the history, but never in an open one.

**Each Active recurring flow's next run.** For an install on October 30, 2026. No attendance, steps, or registrations are written after install, so audiences that need recent activity shrink.

| Flow | Next run | What it would send |
|---|---|---|
| 3 First-Time Guest Welcome | Monday, November 2, 2026 | Its 3 messages to the adults who first came in the week before. The Monday after, its view is empty, so it sends nothing |
| 5 Baptism Sunday Invitation | Sunday, February 21, 2027 (5 weeks before Easter) | Nothing. Its view needs 4 check-ins in 12 weeks, and none are written after install |
| 6 Student Camp Registration | Monday, March 1, 2027 | Nothing, or very little. Its view needs recent student check-ins |
| 7 Women's Retreat Early Bird | Tuesday, May 4, 2027 | 2 emails to women who came to worship in the 12 months before, about 2,500 on Full 7 |
| 8 Christmas at Rock Solid | Tuesday, December 1, 2026 | An email, a push, and a text to involved adults, about 6,000 on Full 7, the largest send after install |
| 10 Lead a Group | Sunday, December 6, 2026 (6 weeks before the January 17 launch) | 3 messages to Ready to Lead, about 300 on Full 7 |

Every one of these reaches only reserved contact points. The large ones are why "Follow-up," item 2, matters.

**The guard.** The reports work owns it (its Section 4.13). "Reserved or No Email" (`reserved-or-no-email`, in a new Rock Solid Church > Sample Data category) holds active people whose email ends with `@example.org` or is blank. It names the safe domain, so any new real domain is left out by default. Each recurring flow's audience is a thin view in the same category that joins its subject view with the guard, such as "First Visit Last Week, Reserved Email." The subject views stay clean, so a report or a later flow that uses them does not inherit a sample-data rule. No staff address can ever be in a recurring flow's audience, whatever the dates do. Phones and push tokens need no filter, because every one is reserved. Goals send nothing, so they need no guard.

We Miss You keeps its guarded view while paused, so a church that turns it on later is still safe. The reports work agreed: every recurring flow keeps its guarded view, paused ones included.

**Proposed decision** (placeholder 220, numbered at merge). It retires decision 95's rule that nothing a load writes can ever send:

> Things may send after install, only to reserved contact points. What a load writes is history: every communication it writes is Approved with a `SendDateTime`, and no recipient it writes is Pending or Sending, so Rock never re-sends the past. The one exception is the scheduled communication, which is written to send in the future. After install, Rock may send: flow runs that were mid-flow at install send their remaining messages, recurring flows named in `configuration/` start new runs on their dates, On-Demand flows add people when a workflow is launched, and the one scheduled communication named in `configuration/communications.json` sends on its date. Everything that can send after install reaches only `example.org` emails or none, 555-0100 to 555-0199 phones, and `sample-data-` push tokens. Each recurring flow's audience includes the "Reserved or No Email" view, and every person in a run that is open at install has only reserved contact points.
>
> Reason: Jon wants the sample church to look and act alive after install, with flows that keep sending (2026-10-29). Sends are safe when they can reach only reserved contact points, because no real person can receive them. The real staff domain, `rocksolidchurchdemo.com`, is the one exception the guard exists for.

Decision 95's other parts stay as history rules, not a "nothing sends" rule. Group sync's welcome and exit emails (decision 187) are unchanged.

**What verify checks.**

1. No recipient is Pending or Sending, and every communication has a `SendDateTime`, except the one scheduled communication named in `configuration/communications.json`. That one is Approved, has a `FutureSendDateTime` after install and no list, and every recipient is Pending and passes check 5.
2. A flow run may be mid-flow at install only in an On-Demand flow or an Active recurring flow. Its next message is due on or after install day, never before.
3. A recurring flow is Active only if `configuration/` names it, and every other recurring flow is inactive. Every One-Time flow has messaging closed.
4. Each recurring flow's audience view includes `reserved-or-no-email`, directly or through Rock's Existing Data View filter, checked in the view's definition, not just in today's results.
5. Every person each Active recurring flow's audience finds today, and every Active recipient of a mid-flow run, has an `example.org` email or none, only 555-0100 to 555-0199 phones, and only `sample-data-` devices.
6. Rock's flow job, run once as referee, adds no conversion. Every message it creates belongs to a mid-flow run or a named Active flow, and every recipient passes check 5.

### Follow-up: details to work out with Jon

Jon said, "We'll work on some of that how that works before we're done." These are the details still open. None blocks slice 1.

1. **The staff domain.** `rocksolidchurchdemo.com` is real. The guard keeps it out of every flow audience and every open run, but a tester who launches the Baptism Interest workflow for a staff member would email a real address. Moving staff to a reserved domain fixes the root cause, but the reports proposal's question 18 explains why it undoes decision 139's real sender domain.
2. **A test copy with a real mail service.** The biggest sends after install are Christmas at Rock Solid each December 1 and the Women's Retreat runs each May and September. A church that installs into a copy of production, with its real email and texting services on, will send to `example.org` and 555 numbers. Those bounce or fail, which can hurt the service's reputation, and texts can cost money. Should the installer warn, or turn off the flow job until someone turns it on?
3. **Install-day sends.** Messages due on install day go out on the job's first run, minutes after install. Is that wanted, or should the first sends wait a day?
4. **How long the church stays alive.** No attendance, steps, or registrations are written after install, so audiences and goals drift. The guest flow empties in a week, the baptism invitation in 12 weeks, and We Miss You would swell if turned on. How long should a sample database look alive?
5. **Other features that could send.** This proposal opens sending only for flows. Should other parts follow, such as scheduled communications, serving reminders, or reminders?
6. **What verify can prove.** Verify can check today's audiences and every view's rule, but not every future date. Is checking the rule enough, or should verify also run the views as of later dates?

## 7. Implementation slices

Each slice lands on its own and passes verify on Rock 21.0.2 and Rock 20.0.10. Slice 1 needs no new data view, so it goes first. The recurring flows wait for the reports work's views.

| Slice | What it adds | Size |
|---|---|---|
| 1. Flow setup and the class flows (built) | The flow categories, `configuration/flows.json`, After Starting Point's category, public name, new third email, and open runs, and After Foundations with its history and open runs. The send rule 220, with invariants for open runs and reserved contact points | Small to medium |
| 2. The Baptism flow (built) | The Baptism Interest workflow type and its finished workflows, Baptism Next Steps with its history, its open runs, and Josh's story, and verify running Rock's flow job as referee | Medium |
| 3a. Recurring flows, first three (built) | The recurring machinery (schedules, a run per date with each run's audience worked out by its view's rule, paused flows, the audience link after the reports views load), the Entered Data View and Completed Form goals, opened and clicked exits, verify's flow-audiences check, and First-Time Guest Welcome, We Miss You, and Baptism Sunday Invitation | Medium |
| 3b-1. Women's Retreat and Lead a Group (built) | Specific-date schedules for some months and for the group launches, the Registered goal, the Women Who Attend and Ready to Lead rules, and registrations loading before communications | Medium |
| 3b-2. Recurring flows, the rest | Student Camp Registration, Women's Retreat Early Bird, Christmas at Rock Solid, and Lead a Group, on the reports work's guarded views | Medium to large |
| 4. One-Time flow, scheduled send, and finish | Built on the Rock Follow-Up, the scheduled Christmas Eve email, pushes, flow unsubscribes, people made inactive, the docs, the site, and the roadmap | Small to medium |

## 8. Questions and answers

Every question is answered. The details Jon wants to work out later are in Section 6, "Follow-up."

**Answered by Jon, 2026-10-29** (his words in quotes):

1. **What can still run after install?** "I'd rather not ship them paused. If one's paused to show that pausing is a feature, that's fine, but not all of them." And on decision 95: "I don't think nothing sends after install rule is valid. I think I do want things to send." So First-Time Guest Welcome and Baptism Sunday Invitation ship Active, We Miss You ships paused (question 12), and decision 220 in Section 6 replaces decision 95's rule.
2. **Add the Baptism Interest workflow?** "Two is yes." One workflow type with a form and the Activate Communication Flow action, plus a finished workflow for each person who entered the Baptism flow. Wiring After Starting Point to a workflow can wait.
3. **Baptism flow shape?** "I think the baptism flow shape is probably a few too many messages. Let's limit it to four." It is now four messages over six weeks (email, text, email, text), with a 180-day window and a 50% target. See Section 4.
4. **Mid-flow people on install day?** "I would want people to be in mid-flow on install day. You're right." Runs that started in the weeks before install are still under way, and Rock's job sends their remaining messages to reserved contact points.
9. **Add After Foundations and Lead a Group?** "I like your number five, two flows." Both are in, so every Ministry by Design transition has a flow.

13. **Flow types.** Jon, 2026-10-30: "I don't think we want to have one time, because what's the purpose of the communication flow if it's only once? That would probably make a better example of a future send date on a communication." He chose Option 1, then build. So Student Camp, Women's Retreat, Christmas, and Lead a Group are recurring, the One-Time flow is the new Built on the Rock Follow-Up, and a scheduled Christmas Eve email shows a future send date.

**Settled by the coordinator, 2026-10-29:**

5. **History length.** 18 months for the new flows, inside the 2-year communication window. After Starting Point stays at 2 years. The yearly flows (6, 7, 8, and 10) use the full 2 years, so each has at least two runs.
6. **Datasets.** All three (Core 1, Core 3, and Full 7), scaled, like every other communication.
7. **Data views.** Reuse the reports and data views session's views, filed in its Ministry by Design category tree. The names, keys, and categories are in Section 3. Flow 5 uses the one shared "regular attender" rule, 4 or more worship check-ins in the last 12 weeks, with its target set from what it finds. Flow 3 used it too, until question 11. Using Rock's Last Gave attribute for "gave" in Involved Adults is a question in the reports proposal, and the Christmas flow works with either answer.
8. **After Starting Point's third email.** Yes. It becomes an Equip invitation (Foundations, and baptism for anyone not baptized), because Ministry by Design puts growth before serving. After Foundations takes over the invitation to serve. This changes a built flow's text, not its shape.
10. **Who owns the "Ready to Lead" view?** The reports session, as `ready-to-lead` in its Lead category. It needs an Equip step, a place on a serving team now, and a small group.
11. **The guest flow's goal.** Yes, the Starting Point Class step, so the goal is the step that marks the stage (rule 3 in the Ministry by Design section). Its target, set from the data, is about 10%. We Miss You keeps a data view goal, since no step marks coming back, so Entered Data View is still covered.
12. **Which recurring flow is the paused one?** We Miss You. About 90 to 120 days after install, nearly every regular attender looks "newly missed," so one monthly run would email thousands. The guard keeps those emails on `example.org`, but in a test copy with a real mail service turned on they would bounce and hurt its sending reputation. Baptism Sunday Invitation's audience shrinks to nobody 12 weeks after install, and its next date is usually months away, so it ships Active with First-Time Guest Welcome.
