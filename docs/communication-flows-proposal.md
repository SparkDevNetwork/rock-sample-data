# Communication Flows: proposal

Status: proposal only. No loader code until Jon approves it through the coordinator.

Rock Solid Church gets ten Communication Flows. Each one serves a Ministry by Design stage and moves people toward the next one, and most end when the person takes the step that marks it. Together they use every flow type, every conversion goal, every exit rule, and every message channel Rock has. Each flow has months of history: people who entered, the emails, texts, and pushes they got, opens and clicks, people who reached the goal, and people who left. The centerpiece is a **Baptism flow**. Staff or a web form can put a person in at any time, they get steady reminders, and the flow stops for them on its own the day their Baptism step is recorded.

One flow already exists on `main`: "After Starting Point" (decision 163). It keeps its shape, with one message changed (open question 8).

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
| 3 | First-Time Guest Welcome | Notice | Belong | Starting Point Class step | No. Any first-time guest |
| 1 | After Starting Point | Belong | Belong: a small group | Joined a small group, the same moment as the Small Group step | No. Anyone who finished the class |
| 6 | Student Camp Registration | Belong (Student Path) | Belong: Student Camp | Joined next summer's camp group. The camp step comes at camp, months after the window | No. Students not registered |
| 5 | Baptism Sunday Invitation | Equip | Equip: Baptism | Completed the Baptism Interest form | Yes, regular attenders. The framework invites people into formation once they belong |
| 2 | Baptism Next Steps | Equip | Equip: Baptism | Baptism step | No. Anyone, at any time |
| 7 | Women's Retreat Early Bird | Equip | Equip: the retreat | Registered. No step marks a retreat | No. Women who attend |
| 9 | After Foundations (new) | Equip | Contribute | Serve step | No. Entering at the end of the course is timing, not a gate |
| 10 | Lead a Group (new) | Contribute | Lead | Lead step | Yes, an Equip step, on a serving team now, and in a small group. The framework grows leaders "through formation and qualification rather than recruitment alone" |
| 4 | We Miss You | Re-engage | Back to Belong | Back in the last 30 days. No step marks coming back | Yes, regulars who stopped. That is what Re-engage serves |

**Every transition has a flow.** Notice to Belong: 3 and 8. Within Belong, class to group: 1, and camp on the Student Path: 6. Belong to Equip: 5, 2, and 7. Equip to Contribute: 9. Contribute to Lead: 10. Re-engage: 4. Before this rethink, Equip to Contribute and Contribute to Lead had no flow, so Flows 9 and 10 are new.

**Left out on purpose.** Pastoral Care gets no flow. The framework makes care personal and owned by a named person, and an automated series would replace that. The Give step (Contribute) gets no flow of its own: the Built on the Rock campaign already sends its emails (decisions 97 and 141), and Rock has no goal type for a giving journey stage. Within Equip, the email each person gets after their baptism already invites them to Foundations (decision 96).

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
| Flow type | Recurring | 3 First-Time Guest Welcome, 4 We Miss You, 5 Baptism Sunday Invitation |
| | On-Demand | 1 After Starting Point (exists), 2 Baptism Next Steps, 9 After Foundations |
| | One-Time | 6 Student Camp Registration, 7 Women's Retreat Early Bird, 8 Christmas at Rock Solid, 10 Lead a Group |
| Schedule | Weekly | 3 |
| | Monthly | 4 |
| | Specific dates | 5 (the weeks before each baptism Sunday) |
| Goal | Completed Form | 5 (the Baptism Interest form) |
| | Registered | 7 (the Women's Retreat) |
| | Joined Group of Type | 1 (small groups) |
| | Joined Specific Group | 6 (next summer's Student Camp group) |
| | Entered Data View | 4 (coming back, which no step marks) |
| | Took Step | 2 (Baptism), 3 (Starting Point Class), 9 (Serve), 10 (Lead) |
| | No goal | 8 |
| Exit rule | Last Message Sent | 1, 3, 8 |
| | Any Email Opened | 4 |
| | Any Email Clicked | 5 |
| | Conversion Achieved | 2, 6, 7, 9, 10 |
| Channel | Email | Every flow |
| | SMS | 2, 3, 4, 5, 6, 8, 9, 10 |
| | Push | 2, 8 |
| Goal progress | Not Tracked, Pending, Achieved, Missed | 8; recent runs of 2 and 9; most runs of 3; most runs of 4 |
| Flow status | Active, Inactive, Completed | 1, 2, and 9; 3, 4, and 5 (see open question 1); 6, 7, 8, and 10 |
| Why a person left | Goal met, opened, clicked, last message sent, unsubscribed from the flow, person made inactive | 2, 6, 7, 9, 10; 4; 5; 1, 3, 8; a few in every flow; a few in 2 |
| Unsubscribe at the Flows level | Unsubscribe report and flow screens | A few in every flow |
| Goal met before the run | Skipped in the rate | 3, 4, and 9 |
| Run vs. flow average | Needs two or more runs | 1 to 5, and 9 |
| Category and public name | Each flow | Every flow |

**Left out.** Rock's "Unknown" leave reason, which no code path sets on purpose.

## 3. The flows

All flows send from the church's address, through flow templates built on the Rock Solid Church email template (decision 140), as the existing flow does. Texts go out on the main line, and the camp texts on the student line. Each flow's category is its primary Ministry by Design system: Notice, Belong, Equip, Contribute, Lead, or Re-engage. So Rock's flow list groups the flows by stage, and a church sees its path at a glance.

### Flow 1. After Starting Point (exists)

On-Demand, three emails, goal Joined Group of Type (small groups) in 30 days, exit Last Message Sent. It gets a category and a public name. One message changes (open question 8): the third email today invites people to serve three weeks after the class. Ministry by Design puts growth before serving, so it becomes an Equip invitation instead: Foundations, and baptism for anyone not yet baptized. Flow 9 now asks people to serve, after Foundations. People who already asked to serve, like the Gilberts, still serve: the step program's prerequisite is only Starting Point.

### Flow 2. Baptism Next Steps

See Section 4.

### Flow 3. First-Time Guest Welcome

- **Purpose.** Welcome every adult first-time guest and help them come back.
- **Trigger.** Recurring, every Monday at 9 a.m., to the First Visit Last Week data view: adults who accept mass email and came to worship for the first time in the last 7 days.
- **Messages.** Email the Monday after the first visit, from the campus pastor's name. SMS 3 days later ("Glad you came! Any questions? Just reply."). Email 7 days later, inviting them to the next Starting Point Class.
- **Goal.** Took Step: Starting Point Class, the first Belong step. Window 84 days, long enough for the next four-week class to start and finish. The target is set from what the data finds, likely about 10% (see Section 5).
- **Exit.** Last Message Sent. A guest gets all three messages, because the welcome matters even after they sign up.
- **Cast.** Jordan and Hannah Ellis, who first came 120 days ago. They are in the run for the Monday after. Hannah clicked the Starting Point link, but neither has taken the class, so their run shows the goal missed. They still come most weeks, which is "the norm, not a gate": belonging does not wait for the class.

### Flow 4. We Miss You

- **Purpose.** Reach regulars who have stopped coming, before they drift away for good.
- **Trigger.** Recurring, the first Tuesday of each month, to the Newly Missed data view: Drifting Adults (the We Miss You rule) who accept mass email and came at least once in the last 120 days. So a person qualifies for about one month, and no one gets it twice.
- **Messages.** Email ("We miss you, and we're praying for you"). SMS 7 days later, only to those still in the flow.
- **Goal.** Entered Data View: Back in the Last 30 Days. Window 60 days. Target 15%.
- **Exit.** Any Email Opened. The point is to be seen once, so people who open the email do not get the text.
- **Cast.** Luis and Teresa Ortiz, who stopped coming about six months ago. Teresa opened the email and left the flow. Luis did not open it, so he got the text. Neither has come back, so their run shows the goal missed.

### Flow 5. Baptism Sunday Invitation

- **Purpose.** Before each baptism Sunday, invite regular attenders who have not been baptized.
- **Trigger.** Recurring, on specific dates: 5 weeks before Easter and 5 weeks before the July baptism Sunday. Audience: the Unbaptized Regular Attenders data view, adults who are Regular Attenders and have no Baptism step.
- **Messages.** Email ("Is it time?") with a button to the Baptism Interest form. SMS 7 days later.
- **Goal.** Completed Form: the Baptism Interest workflow (open question 2). Window 35 days. The target is set from what the regular attender rule finds in the data, likely about 5% (see Section 5).
- **Exit.** Any Email Clicked. A person who clicked has the form in front of them, so the text would only nag.
- **Cast.** None pinned. People who fill out the form inside a run's window convert, and the form puts them in the Baptism flow.

### Flow 6. Student Camp Registration

- **Purpose.** Fill next summer's camp.
- **Trigger.** One-Time, about two weeks after next summer's camp registration opens, to the existing "Students Not Registered for Camp" view (decision 215).
- **Messages.** Email to each student. SMS on the student line 5 days later. Email 14 days later about the early bird.
- **Goal.** Joined Specific Group: next summer's Student Camp group. Camp has a girls' and a boys' registration instance, and Rock's Registered goal takes only one instance, but both fill this one group. So this goal counts every camper. Window 60 days. Target 40%.
- **Exit.** Conversion Achieved. A student who registers gets no more reminders.
- **Cast.** Ben and Abigail Lee, whom Sarah Lee registers on the same day for next summer's camp, convert in this run when that day falls in its window. The build picks the run's date rule so it does.

### Flow 7. Women's Retreat Early Bird

- **Purpose.** Invite women to the Women's Retreat before the early bird ends October 31.
- **Trigger.** One-Time, in early September, to the Women Who Attend data view: adult women 25 to 75 who accept mass email and came to worship in the last 12 months.
- **Messages.** Email. Email 14 days later ("Bring a friend: use code FRIEND").
- **Goal.** Registered: the next Women's Retreat registration instance. Window 60 days. Target 10%.
- **Exit.** Conversion Achieved.
- **Cast.** None pinned. Women who registered in the window convert.

### Flow 8. Christmas at Rock Solid

- **Purpose.** Help the church invite friends to Christmas Eve.
- **Trigger.** One-Time, last December 1, to the Involved Adults data view: adults who came to worship, gave (Rock's Last Gave attribute), or were in a small group or on a serving team in the last 12 months.
- **Messages.** Email with service times on December 1. Push on December 20 ("Who will you invite?"), to people with the app. SMS the morning of December 24 ("See you tonight").
- **Goal.** None. Goal progress shows Not Tracked.
- **Exit.** Last Message Sent.
- **Cast.** The Deckers and the other involved Cast adults get it. Ted Decker opens it on his phone.

### Flow 9. After Foundations (new)

- **Purpose.** Help people who just finished Foundations find their place to serve. This is Equip to Contribute: they have been formed, and now they are invited to take part.
- **Trigger.** On-Demand, like After Starting Point. Everyone who finishes a Foundations course joins the run for the day after its last night.
- **Messages.** Email that day ("You finished Foundations. What's next?"), with the serving teams at their campus. SMS 5 days later ("Want help finding a team? Just reply."). Email 14 days later, with stories from volunteers and the next Serve Sunday.
- **Goal.** Took Step: Serve, within 90 days. People already serving met the goal before the run, so Rock leaves them out of the rate. They still get a thank-you, since the messages also celebrate the course.
- **Exit.** Conversion Achieved. A person who joins a team gets no more asks.
- **Cast.** None pinned. If a Cast adult finished Foundations in the window and then joined a team, the build pins them.

### Flow 10. Lead a Group (new)

- **Purpose.** Before a small group launch, invite people who are growing in a group or serving to lead one. This is Contribute to Lead.
- **Trigger.** One-Time, 6 weeks before the most recent group launch before install day (the launches in `configuration/communications.json`), to the reports work's Ready to Lead view: adults with an Equip step who serve on a team now, are in a small group, and have no Lead step. The framework grows leaders from people who have been formed and who serve, so this audience needs both. Giving alone does not count, because a giver who is not on a team is not a leader candidate.
- **Messages.** Email from the campus pastor ("Have you thought about leading?"). SMS 7 days later, with the Group Leader Training date. Email 14 days later: what leaders get, from training to the Leader Huddle.
- **Goal.** Took Step: Lead, within 60 days, so it covers the launch.
- **Exit.** Conversion Achieved.
- **Cast.** None pinned. Conversions are the people the small group data already makes new leaders at that launch. If there are few, the target follows the data.

**Data views.** The flows use views from the reports and data views proposal (`docs/reporting.md` Section 3.8 on its branch), built with Rock's own filters on its shared foundation views. They are filed by subject in its tree under the church's Rock Solid Church category (decision 185), not by the flow that uses them:

| View | Key | Category | Used by |
|---|---|---|---|
| First Visit Last Week | `first-visit-last-week` | Notice | Flow 3 audience |
| Newly Missed | `newly-missed` | Re-engage | Flow 4 audience |
| Back in the Last 30 Days | `back-in-the-last-30-days` | Re-engage | Flow 4 goal |
| Unbaptized Regular Attenders | `unbaptized-regular-attenders` | Equip | Flow 5 audience (built on Regular Attenders) |
| Women Who Attend | `women-who-attend` | Registrations | Flow 7 audience |
| Involved Adults | `involved-adults` | Foundations | Flow 8 audience |
| Ready to Lead | `ready-to-lead` | Lead | Flow 10 audience |

The reports work files its views by Ministry by Design system too: Notice, Belong, Equip, Contribute, Lead, and Re-engage, plus Foundations and a Path category of stage views. Flow 3's goal is now the Starting Point Class step, so it no longer uses Regular Attenders directly.

**Why step goals, not stage views.** The reports work also has stage views, such as Equip Stage and Contribute Stage, that an Entered Data View goal could use. The flows keep Took Step goals where one step fits, for two reasons. A step goal ends the flow for a person when they take the next step on the path, which is how the framework marks movement. And Rock dates an Entered Data View conversion by when its job last ran the view, while a step goal uses the step's own date (Section 6, risk 3).

The views are in the reports work's slice 2. If flows slice 1 lands first, it adds them to `configuration/reporting.json` with these keys, plus the foundation views they build on, as that proposal describes them. Rock's flow job checks no person's security, so a view's category limits only who can open it, not whether the flow works.

**Message text.** New entries in the message library in `content/communications`, about 28 messages in all, in the church's voice, plus a new third email for After Starting Point. No images are needed: the emails use the branded template's logo.

## 4. The Baptism flow

"Baptism Next Steps" is the flow you can put anyone in, at any time. It keeps reminding them, gently, until they are baptized, and then it stops for them on its own.

**How Rock does this.** It is an On-Demand flow whose goal is Took Step, with Rock's Discipleship Path and its Baptism step type (`801CC43C-0641-4271-939E-75E428F31D06`, the one the steps data already uses). Its exit rule is Conversion Achieved. Each time the job runs, it finds people whose Baptism step was completed inside their window, records the conversion, and marks them Inactive with the reason "goal met." The job never sends an Inactive person another message. That is the "stops on its own" behavior, and it needs no custom code.

**How a person gets in.** Through Rock's "Activate Communication Flow" workflow action, which is the only way into an On-Demand flow. The proposal adds one workflow type, "Baptism Interest," with a short form and that action (open question 2). People reach it two ways:

1. They fill out the "I'm interested in baptism" form on the website, often after the Baptism Sunday Invitation (Flow 5).
2. Staff launch it from the person's profile after a conversation.

**The messages.** Eight, over 24 weeks, so the reminders are steady but not pushy.

| # | Wait | Channel | Message |
|---|---|---|---|
| 1 | Day 0, 10 a.m. | Email | "We're so glad you're thinking about baptism": what it means, and who to ask |
| 2 | 2 days | SMS | "Any questions about baptism? Just reply to this text." |
| 3 | 12 days | Email | What happens on a baptism Sunday, and what to bring |
| 4 | 14 days | Push | A baptism story video, for people with the app |
| 5 | 21 days | Email | Stories from people who were baptized here |
| 6 | 28 days | SMS | "Our next baptism Sunday is coming up. Want us to save you a spot?" |
| 7 | 35 days | Email | Questions people ask about baptism |
| 8 | 56 days | Email | "Whenever you're ready, we're here," with the campus pastor's contact |

**The goal.** Took Step: Baptism, within 365 days, target 50%. The window runs past the last message, so a person baptized a few months after the reminders end still counts. That also gives runs from 6 to 12 months ago the Pending state on install day.

**Who is in it.** The flow reads the Baptism steps the steps part already wrote. It never changes them.

- **People baptized here** in the last 18 months: about 70% of them entered the flow first, from 1 to 16 weeks before their baptism. The rest signed up on the day. Each one who entered converts, credited to the newest message before their baptism, and gets no more reminders.
- **People not baptized:** about as many again entered and are not baptized. They got all eight messages and left after the last one, or they are still Pending. So the flow converts about half of the people who enter, a little above its 50% target.
- **A few** unsubscribe from the flow, and a few are made inactive (for example, they moved away).

**Cast stories.**

1. **Josh Sanders** filled out the Baptism Interest form 66 days before install, the day he texted Ted "I turned in my baptism form." That put him in Baptism Next Steps. It came weeks after the July invitation's window closed, so it is not a Flow 5 conversion. He got the welcome email and the text, was baptized 60 days before install, and left the flow with the reason "goal met." He never got message 3.
2. **One Cast adult who is not baptized,** chosen at build from the adults the steps plan leaves unbaptized, entered about 8 months before install. They got all eight messages, opened a few, and are still Pending.

**On install day.** The flow is Active, so anyone testing the sample data can launch "Baptism Interest" for a person and watch Rock run the flow for real. No run in the history still has a message to send, because nothing a load writes may ever send (decision 95). See open question 1.

## 5. How the history is backfilled

**How far back.** Communication history covers 2 years (decision 90). After Starting Point keeps its 2 years. The new flows started 18 months before install day, so each one has a launch date, a few rough early runs, and a steadier recent stretch. The one-time flows each have their single run.

**What the loader writes.** Everything Rock's job writes, and nothing it does not. For each run: the instance, its recipients with status and leave reason, one bulk communication per message from that message's flow template, the recipients and their send outcomes, opens and clicks as interactions with `OpenedDateTime` set, unsubscribes at the Flows level, and conversions. The code that already writes After Starting Point is generalized to write all ten flows, and it uses the same email planner, so a person on "No mass emails" never gets a flow email, as Rock decides.

**Rates.** Opens and clicks follow the measured email shapes the bulk email already uses (decision 149). The flow rates are designed, because no calibration source has flows yet:

| Flow | Target | Designed result |
|---|---|---|
| 2 Baptism Next Steps | 50% | About 55%, recent runs Pending |
| 3 First-Time Guest Welcome | Set from the data, about 10% | A little above the target, most runs Achieved |
| 4 We Miss You | 15% | About 10%, most runs Missed |
| 5 Baptism Sunday Invitation | Set from the data, about 5% | A little above the target |
| 6 Student Camp Registration | 40% | About 45% |
| 7 Women's Retreat Early Bird | 10% | About 8%, Missed |
| 8 Christmas at Rock Solid | None | Not Tracked |
| 9 After Foundations | Set from the data, about 20% | A little above the target, recent runs Pending |
| 10 Lead a Group | Set from the data, about 3% | Whatever the launch's new leaders give |

**Targets from the data.** Flow 5 uses the church's one "regular attender" rule, 4 or more worship check-ins in the last 12 weeks, through the views built on Regular Attenders (coordinator decision, 2026-10-29), and adds no threshold of its own. Flows 3, 9, and 10 have step goals, and their steps come from the steps plan. So for Flows 3, 5, 9, and 10 the build measures the share of each flow's people who reach the goal on Full 7, and sets the target just under it, rounded down to a whole 5% for Flows 3 and 9 and a whole 1% for Flows 5 and 10. So most runs show Achieved and a few show Missed, and the targets are recorded in `configuration/` with the measured share beside them.

Flow unsubscribes run about 0.3% per email, a little above bulk email, because flows send more often.

**Every conversion is real.** Each one is a step, group join, registration, finished workflow, or data view match that is already in the data, inside the window. The flows never invent an outcome to hit a rate. If the rates miss by a lot, the audience rules change, not the outcomes.

**Size.** Rough estimates for Full 7, to be measured at build:

| Flow | People | Recipient rows |
|---|---|---|
| 3 Guest Welcome, 78 weekly runs | About 1,000 | About 3,000 |
| 4 We Miss You, 18 monthly runs | About 500 | About 800 |
| 2 Baptism Next Steps | About 600 | About 2,500 |
| 5 Baptism Sunday Invitation, 3 runs | About 3,000 each | About 12,000 |
| 6 and 7, one run each | About 800 and 2,500 | About 7,000 |
| 8 Christmas | About 6,000 | About 14,000 |
| 9 After Foundations, a run per course | About 800 | About 2,000 |
| 10 Lead a Group, one run | About 2,500 | About 6,000 |
| **Total** | | **About 48,000** |

That is about 3% on top of the 1.6 million communication recipients Full 7 already has. The flows write inside the Communications step's deferred index rebuild, so they add a few seconds to a load of about 173 seconds. Core 1 and Core 3 get the same flows with far fewer people.

**Dates.** Every date is a rule from install day, and every send is before it. Times are local wall-clock times, like the rest of the data. Each run's start date is a date only, as Rock stores it. One-time flows tied to an event pick the newest occurrence whose run and goal window fit before install day. For example, the camp flow uses next summer's camp once its registration has been open for 75 days, and otherwise last summer's.

## 6. Risks, and Rock 20 vs Rock 21

**Rock 20 vs Rock 21.** The tables, columns, enums, job, and workflow action are the same in both checkouts, so one data shape works for both packages. The differences are in screens and in editing: Rock 21 can move a one-time flow's start date when its schedule is edited, Rock 20 escapes names in its goal progress display, and Rock 21 relabels one workflow action field. None of them changes the loaded data.

**Risks.**

1. **Sending after install.** Rock's job starts a recurring flow's next run on its next schedule date, and it sends to whoever the data view finds. An active recurring flow would email real addresses on the first Monday after install. Open question 1 covers this. On-Demand flows are safe while Active, because only a workflow can add someone, and one-time flows are safe once their run is done.
2. **The job as referee.** Rock re-adds conversions for any run whose goal tracking is still open. If the loader credits a conversion differently from Rock, the job would add a second row. So verify runs Rock's Process Communication Flows job once after each load and expects it to send nothing and add nothing, as it runs Group Sync today (decision 187).
3. **Entered Data View dates.** Rock dates these conversions by when the job last ran the view. History needs each person's real date, so the loader writes them, and the flows with this goal close their tracking (or are paused) before install, so the job never re-counts them.
4. **Two unsubscribe paths.** Rock marks a flow unsubscribe one way from the email preference page and another way from the job. The loader follows the preference page path, which is what a person clicking the link does, and the build checks both against Rock's code.
5. **Workflows are new here.** No workflow types exist in the sample data yet. The Baptism Interest type must open and edit in Rock's workflow screens like one a person built, and Rock Cleanup must find nothing to fix.
6. **Cast dates.** Josh Sanders' text thank-you to Ted is dated 62 days before install, before his baptism 60 days before. The flow uses his Baptism step date. The text's date is a small Cast fix for whoever owns the threads.

## 7. Implementation slices

Each slice lands on its own and passes verify on Rock 21.0.2 and Rock 20.0.10.

| Slice | What it adds | Size |
|---|---|---|
| 1. Setup | Flow categories, the data views (reused or filed in the reports and data views tree), flow templates for email, SMS, and push, the nine new flows with their messages and no runs, and After Starting Point's category, public name, and new third email. Invariants: a goal always has a window, settings match the goal type, SMS templates have a phone number, and no flow can start a run after install | Small |
| 2. On-Demand flows | The Baptism Interest workflow type and its finished workflows, Baptism Next Steps with its history and Josh's story, After Foundations with its history, and verify running Rock's flow job as referee | Medium |
| 3. Recurring flows | First-Time Guest Welcome, We Miss You, and Baptism Sunday Invitation, with the Ellis and Ortiz stories | Medium |
| 4. One-time flows and finish | Student Camp, Women's Retreat, Christmas, and Lead a Group, with pushes, unsubscribes, people made inactive, the docs, the site, and the roadmap | Small to medium |

## 8. Open questions

1. **What can still run after install?** Rock sends the next run of an active recurring flow on its next date. Options: (a) ship the three recurring flows **paused**, with their schedules intact, so they show Inactive and one click turns them on; (b) end their schedules the day before install, so they show Completed; (c) leave them live and let them send, which breaks decision 95. **Recommendation: (a).** The history and settings look exactly like a running flow, nothing can send, and a church can see what turning one on does. The two On-Demand flows stay Active, since only a workflow can add people.
2. **Add the Baptism Interest workflow?** It is the only way into an On-Demand flow, and the only home for the Completed Form goal. It is the first workflow type in the sample data. **Recommendation: yes,** one workflow type with a form and the Activate Communication Flow action, plus a finished workflow for each person who entered the Baptism flow. Wiring After Starting Point to a workflow too can wait.
3. **Baptism flow shape.** Eight messages over 24 weeks, a 365-day window, and a 50% target. **Recommendation: approve as written.** A shorter flow (5 messages over 10 weeks) is the alternative if Jon wants it lighter.
4. **Mid-flow people on install day.** Jon may want to see people part way through the Baptism flow. That needs a run with messages left to send, which the job would send. **Recommendation: no.** Keep decision 95. Show "in progress" through Pending runs and Josh's finished story, and let a tester add someone live.

**From the Ministry by Design rethink (2026-10-29):**

8. **Change After Starting Point's third email?** Today it invites people to serve three weeks after the class. Ministry by Design puts growth before serving. **Recommendation: yes.** It becomes an Equip invitation (Foundations, and baptism for anyone not baptized), and After Foundations takes over the invitation to serve. This changes a built flow's text, not its shape.
9. **Add After Foundations and Lead a Group?** Without them, no flow moves people from Equip to Contribute, or from Contribute to Lead. **Recommendation: yes.** Both are types that cannot send after install (On-Demand and One-Time), and both end on a step: Serve and Lead.
10. **Who owns the "ready to lead" view?** Answered: the reports session owns it as Ready to Lead (`ready-to-lead`, in its Lead category). It needs an Equip step, a place on a serving team now, and a small group, which is narrower than the first draft's "in a group or on a team," and closer to the framework.
11. **The guest flow's goal.** It moves from the Regular Attenders view (settled earlier) to the Starting Point Class step, so the goal is the step that marks the stage (rule 3 in the Ministry by Design section). Its target, set from the data, drops from about 25% to about 10%. **Recommendation: yes.** We Miss You keeps a data view goal, since no step marks coming back, so Entered Data View is still covered.

**Settled by the coordinator, 2026-10-29:**

5. **History length.** 18 months for the new flows, inside the 2-year communication window. After Starting Point stays at 2 years.
6. **Datasets.** All three (Core 1, Core 3, and Full 7), scaled, like every other communication.
7. **Data views.** Reuse any view the reports and data views session plans, and file the flow audiences and goals in its layered category tree where they fit, not in a separate "Communication Flows" category. Done: the names, keys, and categories are in Section 3. Flow 5 uses the one shared "regular attender" rule, 4 or more worship check-ins in the last 12 weeks, with its target set from what it finds (coordinator decision, 2026-10-29). Flow 3 used it too, until question 11. Using Rock's Last Gave attribute for "gave" in Involved Adults is still an open question in the reports proposal. The Christmas flow works with either answer.
