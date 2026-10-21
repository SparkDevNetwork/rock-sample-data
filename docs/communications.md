# Communications

How email, text, and push history will look in the sample data, and why. This is a plan: nothing here is built yet. The decisions are D-comms-1 to D-comms-7 in [Decisions](decisions.md).

## The goal

A church that installs the sample data should see Rock's communication screens the way a busy church sees them:

- **Communication List and Communication Detail,** with real sends, open rates, click rates, clicked links, unsubscribes, and spam complaints.
- **A person's Communication History,** with the emails, texts, and pushes that person really got.
- **SMS Conversations,** with live threads, unread replies, and a few long back-and-forths.
- **Communication lists,** with members who match the people in the church.

Messages use real, readable text: newsletters, event invitations, service times, and serving reminders, not placeholder text. Some emails are very large: on Full 7, the weekly newsletters reach about 10,000 people each (an estimate, set when bulk email is built).

## How Rock stores communications

The loader writes rows straight into Rock's tables, so it has to write what Rock's screens read. These facts come from Rock 21's code.

1. **Rock stores no counts.** Every number on the Communication List and Communication Detail screens is computed live from `CommunicationRecipient` and `Interaction` rows. No Rock job caches communication analytics, so no job needs to be mimicked.
2. **A sent communication** is `Status` Approved (3) with a `SendDateTime`. Rock never stores "Sent"; screens infer it from recipient statuses. `SendDateKey` (yyyyMMdd) is a foreign key to `AnalyticsSourceDate`, so that date row must exist.
3. **A recipient's status** is Pending (0), Delivered (1), Failed (2), Cancelled (3), Opened (4), or Sending (5). Communication Detail counts Delivered plus Opened as delivered. Unsubscribes and spam complaints come only from `UnsubscribeDateTime` and `SpamComplaintDateTime`.
4. **Opens and clicks are interactions** in Rock's Communication interaction channel. Each communication has one interaction component (its `EntityId` is the communication). Each interaction points at a recipient, with the operation "Opened" or "Click", and a click stores its URL in `InteractionData`. An open also sets the recipient to Opened, with `OpenedDateTime` and `OpenedClient` ("Windows Outlook (Desktop)"). Push opens use the same channel. Texts have no opens or clicks.
5. **SMS Conversations** reads two tables. Inbound texts are `CommunicationResponse` rows. Outbound texts are SMS recipients whose communication has the conversation's `SmsFromSystemPhoneNumberId`. The thread shows only outbound rows with status Delivered (not Opened) and reads their text only from `SentMessage`. `CreatedDateTime`, not `SendDateTime`, sets the order and the date filter. A conversation is unread while any inbound text has `IsRead` = 0.
6. **A system phone number** must be active for SMS Conversations to list it. `AssignedToPersonAliasId` marks a staff member's personal number.
7. **Push** reaches a person's personal devices that are active, have notifications on, and have a registration Id. `PushData` names the mobile application site, and a recipient can point at the device through `PersonalDeviceId`.
8. **Communication Flows** (Rock 17 and later) live in their own tables. Rock's Process Communication Flows job sends the next message of every active flow instance.

## Nothing ever sends

A loaded database must never email, text, or push to anyone, even though every address is fictional (decision D-comms-6).

- Every communication is Approved with a `SendDateTime`. Rock's send job only picks up communications with no `SendDateTime`.
- No recipient is Pending or Sending.
- Every flow instance is finished and inactive, so the flow job has nothing to do.
- The verify tool gets an invariant for each of these.

## The setup

1. **System phone numbers.** A main church line, a student ministry line, and one personal number assigned to a Cast pastor, all in the reserved 555-0100 to 555-0199 range and not used by any Cast person.
2. **Communication lists.** A weekly newsletter list per campus, plus Men, Women, Parents of Kids, Students, Volunteers, Prayer Team, and Small Group Leaders. Members come from people already loaded, so the Volunteers list matches the serving teams and the Small Group Leaders list matches the groups. People who chose "No mass emails" stay off the lists.
3. **Templates.** Rock's shipped templates, plus one sample church template with the approved logo.
4. **The church's app.** Rock ships no mobile application site, so the sample church adds its own: the Rock Solid Church App, with the store name Rock Solid Church (decision D-comms-5). Push history points at it. The stub in [configuration/church-app.json](../configuration/church-app.json) holds its name, Guid, shell, bundle Ids, and planned tabs (Home, Watch, Events, Give, and More). Slice 6 loads the site and its layout. Pages and app content come in later work.
5. **Personal devices.** A designed share of active adults have the app on an iPhone or an Android phone, with notifications on for most of them.

## The message library

Messages come from a hand-written library in `content/communications/`, checked by a pool checker like the prayer and note pools.

- **About 50 emails:** weekly newsletters built from rotating pieces, sermon series launches, Easter and Christmas service times, VBS, summer camp, year-end giving statements, baptism Sunday, the membership class, serve team recruiting, and group launches.
- **About 30 texts and pushes:** weather closures, "we're live now", event reminders, RSVP requests, and student ministry reminders.
- **No fixed dates.** Tokens such as the date, the day of the week, and the sermon series fill in at load time, so a message always matches when it was sent.
- **Real links.** Links point at paths on the sample church's site (`rocksolidchurchdemo.com`), so the All Links chart groups clicks by page.
- **Content standards apply:** fictional names, `example.org` and `rocksolidchurchdemo.com` emails, reserved phone numbers, and warm text for any audience.

## Bulk email

Bulk email is where the very large sends come from.

- **Who sends what.** Each campus sends its weekly newsletter to its list. The church sends seasonal emails (Easter, Christmas, year-end giving) to everyone, and ministries send to their own lists.
- **Delivery.** Most recipients are Delivered or Opened. People whose email is already inactive in the people data are the ones whose sends fail, so the two always agree.
- **Opens and clicks.** Opens follow the measured open rate and come mostly within a day of the send. Clicks come only from people who opened, at the measured click-through rate.
- **Unsubscribes and spam.** A few recipients unsubscribe, and the person's email preference changes to match. Spam complaints are rare.

## Texts and SMS conversations

- **Bulk texts** go out from the main line and the student line. Some ask for a reply ("Reply YES if you're coming"), and those replies link back to the text through `RelatedCommunicationId`.
- **The Cast's threads.** About 15 hand-written threads tied to Cast stories. Together they cover every SMS Conversations state: unread and read, a long back-and-forth, a STOP opt-out, a thread on the student line, and threads on the pastor's personal number. A few are always from the last 1 to 3 days before install, so even Core 1 opens to a live inbox (decision D-comms-4).
- **Generated threads.** Short threads built from text pools, at the measured reply rate and reply delay.

## Push

- Pushes go to people with the app: "we're live now" on Sunday mornings, event reminders, and the occasional announcement.
- Opens are interactions on the same channel as email opens, mostly within an hour of the push.

## One-to-one messages

Most messages a real church sends go to one person. These come from data that is already loaded:

- **Serving.** Schedule confirmations and reminders for every scheduled serve, by each volunteer's preferred medium.
- **Connections.** Follow-up emails and texts from connectors.
- **Prayer.** "Your prayer was answered" emails.
- **Staff.** Personal emails from Cast staff to Cast people, tied to their stories.

## Messages tied to steps

The [steps plan](steps.md) has a seven-step Discipleship Path and a four-step Student Path (decisions 83 to 89). Once steps are built, their milestones get their own messages (decision D-comms-7):

- **Starting Point Class.** Each campus's monthly class gets an invitation email. Everyone who completes it enters one finished Communication Flow, "After Starting Point": a thank-you the next day, a small group invitation a week later, and a serving invitation three weeks later. Each person's flow instance is complete, so Rock's flow screens have data.
- **Baptism.** Baptism Sunday gets a mass invitation. Each person baptized gets a follow-up email that invites them to Foundations.
- **Foundations.** Weekly reminder emails and texts to the people in each six-week course.
- **Student Path.** Student Camp and Mission Trip get registration emails and reminder texts to students and their parents on the student line.
- **Serve and Lead.** These already get serving reminders (see One-to-one messages).

This needs the step type Guids and each person's step dates from the steps work. The communications load runs after the steps load.

## Volume and the history window

Communication history covers the last 2 years, not the 3-year attendance window (decision D-comms-1). On Full 7 that is roughly 1.5 to 2 million recipient rows (an estimate), mostly from weekly newsletters. Three years would push it past 3 million and slow both the load and verify. Core 1 and Core 3 get the same messages with smaller lists.

## Calibration

Calibration part 3 already measures communications, but only one source has data, and that data is almost all one-to-one workflow messages (a median of 1 recipient, 0.1% bulk). It cannot shape bulk sends.

**Calibration part 7** ([the script](../calibration/sql/Report_SampleDataCalibration_7_Communications.sql)) measures mass sends (25 or more recipients) and text conversations (decision D-comms-2):

- Mass sends per month, by medium, and the share of sends and recipients they make up.
- Audience size as a share of adults, size bands, and how often sends use a list, segments, a template, or a topic.
- The weekday and hour of sends.
- Delivered, opened, and failed rates, unique openers and clickers, links per email, and how soon first opens and clicks follow the send.
- Unsubscribe and spam rates (Rock 17 and later).
- Email and communication preferences, phones that can text, and the share of adults with the app and with notifications on.
- Text replies to mass texts, stop words, unread texts, thread length, how fast staff answer, and how fast people answer.
- Church phone numbers and communication flows (Rock 18 and later).

It returns no names, Ids, list names, message text, phone numbers, or URLs. Clicked links are compared by checksum only. Monthly volumes are raw counts, and the normalizer scales them by part 2's active adults, as for parts 5 and 6. The script was run against a test copy of a loaded sample database with seeded sends, texts, devices, and replies, and every section returned the expected numbers. The normalizer does not read part 7 yet; that comes with the first source.

Volume is designed, as for prayer and notes: the sample church shows what good communication looks like. Shapes and rates come from the measured sources.

## How it is checked

- **Invariants:** no Pending or Sending recipient, no communication without a `SendDateTime`, no active flow instance, every email recipient address on a reserved domain, and every outbound SMS conversation row Delivered with `SentMessage` set.
- **Cast expectations:** each Cast thread shows in SMS Conversations in its intended state, and the recent threads are unread.
- **Validation** grades the generated data against calibration part 7.
- **The verify tool** re-records the contract for the new tables and runs Rock Cleanup as the referee.

## Build order

Each slice lands on its own, passes verify, and goes to the integrator.

1. **Design and calibration.** This page, the decisions, the calibration part 7 script, and the church app stub. Done on the `communications` branch; the normalizer support for part 7 comes when the first source runs it.
2. **Setup.** System phone numbers, communication lists, the sample church template, and the message library.
3. **Bulk email.** Sends, recipients, opens, clicks, unsubscribes, and failures.
4. **Texts and SMS conversations.** Bulk texts, replies, the Cast's threads, and generated threads.
5. **One-to-one messages.** Serving, connections, prayer, and staff messages.
6. **Push.** The church app's site and layout, personal devices, pushes, and push opens.
7. **Steps.** Step messages and the finished Communication Flow, after steps land.

## Open questions

1. **Email message copies.** Each recipient row can hold a copy of the sent message in `SentMessage`, which makes the table large. Slice 3 checks which screens read it for email. If none do, email recipients leave it empty.
2. **`SendDateKey` dates.** Prayer interactions already write date keys, so `AnalyticsSourceDate` probably covers the 2-year window. Slice 3 confirms it.
3. **The app's pages.** The stub plans five tabs. Which pages and data each tab gets is later work.
