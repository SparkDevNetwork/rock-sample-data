# Communications

How email, text, and push history will look in the sample data, and why. The setup, bulk email, texts, and one-to-one emails are built: the texting numbers, the communication lists, the branded template, the message library, two years of mass email, two years of texts and SMS conversations, and two years of one-to-one email. Push and step messages are still a plan. The decisions are 90 to 96, 131 to 133, 137 to 141, 149 to 151, D-commstexts-1 to D-commstexts-3, and D-commsoneone-1 to D-commsoneone-2 in [Decisions](decisions.md).

## The goal

A church that installs the sample data should see Rock's communication screens the way a busy church sees them:

- **Communication List and Communication Detail,** with real sends, open rates, click rates, clicked links, unsubscribes, and spam complaints.
- **A person's Communication History,** with the emails, texts, and pushes that person really got.
- **SMS Conversations,** with live threads, unread replies, and a few long back-and-forths.
- **Communication lists,** with members who match the people in the church.
- **The Communication Unsubscribe report,** with unsubscribes at every level Rock records, including some from the last 6 months, its default window.
- **The Communication Saturation report,** with a realistic spread of messages per person: most adults get a few, leaders and volunteers get many, and some get none.

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
9. **The Unsubscribe report** reads only recipient rows with `UnsubscribeDateTime` and `UnsubscribeLevel` set. It shows the recipient, the send's name, topic, and sender, and the recipient's own `SendDateTime`. Rock picks the level from the email that brought the person to the email preference page:
   - An email sent to a communication list makes the person Inactive in that list's group. The level is CommunicationList.
   - An email from a flow takes the person out of the flow. The level is Flows, which the flow performance screen reads too.
   - Any other email steps the person's email preference down: "No mass emails" (level Bulk), or "Do not email" (level All) when they had already chosen "No mass emails".
   - A texted STOP marks the newest text the person got in the last 60 days as a level All unsubscribe, and Twilio turns off texting on that phone.
10. **The Saturation report** counts each active adult's messages in a date range, by the send's own type (Email, SMS, or Push), and sorts adults into buckets, including those who got none. By default it counts only sends flagged `IsBulkCommunication`. Recipient preference sends have no single type, so the report never shows them.

## Nothing ever sends

A loaded database must never email, text, or push to anyone, even though every address is fictional (decision 95).

- Every communication is Approved with a `SendDateTime`. Rock's send job only picks up communications with no `SendDateTime`.
- No recipient is Pending or Sending.
- Every flow instance is finished and inactive, so the flow job has nothing to do.
- The verify tool gets an invariant for each of these.

## The setup

The loader's Communications part writes the setup, after steps. It needs the small groups, serving, and prayer parts, because three lists copy them. The settings are in [configuration/communications.json](../configuration/communications.json).

1. **System phone numbers** (decision 93). A main church line, a student ministry line, and personal texting numbers for Ted Decker, Alisha Marble, and Isaac Silva, the student pastor (decision 137). Full 7 adds a personal number for each other campus's campus pastor, so every campus has one. Pete Foster has none. Every number is in the reserved 555-0100 to 555-0199 range, and no loaded person has it: the Cast never does, and generated people are never given one. Forwarding is off, so a real text is never passed on.
2. **Communication lists** (decision 138). A weekly newsletter per campus (such as "Phoenix Weekly"), plus Men, Women, Parents of Kids, Students, Volunteers, Prayer Team, and Small Group Leaders.
   - **Public** lists show on Rock's email preference page under its Public category: the newsletters, Men, Women, and Parents of Kids.
   - **Private** lists are staff-managed: Students, Volunteers, Prayer Team, and Small Group Leaders.
   - **Members follow involvement** (decision 133). A newsletter holds its campus's adults with an active email who attended, gave, or were in a small group in the last year, plus a third of the others. Men and Women are opt-in, joined far more often by Leading and Engaged adults than by Known adults. Parents of Kids holds parents of children through grade 6 whose family came in the last year, plus a third of other such parents. Students holds students in grades 7 to 12 who came in the last year, by text when they have a phone that can text.
   - **Staff lists copy the data.** Volunteers is everyone active on a serving team, Small Group Leaders is everyone who leads an active small group, and Prayer Team is the prayer team. Invariants check that the first two match.
   - **Nobody who chose "No mass emails" or "Do not email"** is on any list.
   - **Join dates.** A member joined two to six years ago, or soon after their household's first Sunday when that came later. A staff list member joined when they joined the team or became a leader.
   - Rock ships four lists of its own (Members and Attendees, Parents of Children, Parents of Youth, and Sports Ministry). They stay as Rock made them, with no members.
3. **Senders** (decision 139). Mass emails come from hello@rocksolidchurchdemo.com as "Rock Solid Church", sent in Rock by Daniel Peak, the Director of Communications. Pete Foster sends a few pastoral letters under his own name. Isaac Silva sends student texts from the student line. Daniel approves sends that need approval, and his own need none. The slices that write sends read these.
4. **Templates** (decision 140). Rock's shipped templates, plus the "Rock Solid Church" template: the approved logo on a light gray page, a clean white body, and a footer with the church's name, address, and Rock's unsubscribe option. It is built for Outlook, with a table layout, inline styles, and Outlook conditional comments, and its colors are Lava fields. The HTML is [content/communications/email-template.html](../content/communications/email-template.html), and the logo is a Rock file, as if it had been uploaded in Rock's template editor.
5. **Topics.** Mass sends carry one of the 8 communication topics Rock ships, such as Events & Registrations or Serving Opportunities, because the Unsubscribe report shows them. They are unchanged.
6. **The church's app.** Rock ships no mobile application site, so the sample church adds its own: the Rock Solid Church App, with the store name Rock Solid Church (decision 94). Push history points at it. The stub in [configuration/church-app.json](../configuration/church-app.json) holds its name, Guid, shell, bundle Ids, and planned tabs (Home, Watch, Events, Give, and More). Slice 6 loads the site and its layout. Pages and app content come in later work.
7. **Personal devices.** A designed share of active adults have the app on an iPhone or an Android phone, with notifications on for most of them. Slice 6 loads them.

## The message library

Messages come from a hand-written library in [content/communications/](../content/communications/README.md), checked by `check-library.js` like the prayer and note pools (decision 141).

- **49 emails:** sermon series launches, Easter and Christmas service times, Thanksgiving, VBS, Student Camp and mission trips, year-end giving letters and statements, Built on the Rock's Commitment Sunday and updates, baptism Sunday, the Starting Point Class and Foundations, serve team recruiting, group launches, ministry list emails, a weather closure, and the one-to-one and flow messages.
- **A weekly newsletter** built from rotating pieces: subjects, openers, a line about this Sunday's series, pieces tied to coming events, evergreen pieces, and closings.
- **32 texts and pushes:** weather closures, "we're live now", event reminders, RSVP requests, and student ministry reminders, plus pools of short replies for the texts that ask for one.
- **Occasions, not dates.** Each message names its occasion, such as Easter or a campus's Starting Point Class, and how many days before it the message goes out. The loader dates every occasion from the install day.
- **Sermon series.** Six series fill three slots in the church year (after New Year, after Easter, and in the fall), two per slot, so the two years of history show six. The Built on the Rock series runs the four Sundays that end on Commitment Sunday.
- **Tokens** such as `{eventDate}`, `{campus}`, `{serviceTimes}`, and `{series}` fill in at load time, so a message always matches when it was sent. Rock fills in `{{ Person.NickName }}` for each recipient.
- **Real links.** Links point at paths on the sample church's site (`rocksolidchurchdemo.com`), so the All Links chart groups clicks by page.
- **Content standards apply:** fictional names, `example.org` and `rocksolidchurchdemo.com` emails, reserved phone numbers, and warm text for any audience.

## Bulk email

Bulk email is where the very large sends come from (decision 149). On Full 7 it is about 1,500 sends and 1.3 million recipient rows over two years. Core 1 has about 330 sends and 20,000 recipients.

- **Who sends what.** Each campus sends its weekly newsletter to its list every Thursday at 6 a.m. The church sends seasonal emails (Easter, Christmas, year-end giving, the campaign, baptism Sunday, group launches) once per campus to that campus's involved adults with an email, the segment a church's active adults data view gives. Ministries send to their own lists. Student Camp and mission trip emails go to students and their parents.
- **The calendar.** Each library email names an occasion, and `loader/generation/CommunicationCalendar.cs` dates it from the install day with the same rules the rest of the data uses: baptism Sundays, camp, and the mission trips from the steps, Commitment Sunday from the campaign, and the class schedules.
- **Bulk flag and medium.** Mass sends are flagged `IsBulkCommunication` and have a concrete type (Email, SMS, or Push), so the Saturation report counts them by default (decision 132). A few leader sends to small lists stay unflagged, so the report's bulk-only toggle shows a difference, and a small share of sends use recipient preference. The measured data flags only about 10% of mass sends; the sample church shows the better practice.
- **Sender, reviewer, topic.** Daniel Peak sends and approves the church's emails. Pete Foster's and Isaac Silva's are approved by Daniel. Every send has its topic, the branded template, and the from line in configuration/communications.json.
- **Recipients.** Each recipient row has its own `SendDateTime`, which the Unsubscribe report reads.
- **Delivery.** Recipients follow Rock's send path. Rock refuses an inactive email, "Do not email", and "No mass emails" on a bulk send, with its own status notes and no send time. Everyone else is sent through Mailgun and confirmed delivered, and Rock's "Sent" line goes on their History. Email recipients keep no `SentMessage`, because Rock's email transport never stores one.
- **Bounces.** About 1.8% of active emails bounce once during the window. That send fails with the bounce, Rock makes the email inactive with its bounce note and the History lines, and later sends fail as not active.
- **Opens and clicks.** Each person reads at their own rate, higher for Leading and Engaged people, so opens land near the measured rate and mostly within a day. Clicks come only from people who opened, at the measured rate, mostly on the email's first link. Each open and click is an interaction with its own session and an email client's device type, as Rock's Mailgun webhook writes them, so Communication Detail charts opens by client.
- **Unsubscribes.** See Unsubscribes. There are no spam complaints on recipients, because Rock 21's email transports never record one there.

## Unsubscribes

Unsubscribes are designed, not measured (decision 131). The measured data records none on recipient rows, which is common where a church uses the older email preference page. Its people still opt out, so the people data's measured share of "No mass emails" and "Do not email" is the anchor: two years of unsubscribes add up to about those shares.

- **Rock's rules.** Each unsubscribe follows the rules above. A list email makes the person Inactive in that list, a flow email takes them out of the flow, and any other email steps their email preference down. The recipient row gets `UnsubscribeDateTime` and the matching level, and the person's History records the change, as Rock writes it.
- **When.** Most come the same day as the email or the next, and some come in the last 6 months, so the report's default window has data.
- **Which sends.** Large newsletters draw more unsubscribes than small ministry sends.
- **Texted STOP.** A few people text STOP. Their newest text gets a level All unsubscribe, and texting is turned off on their phone, so later texts leave them out.
- **Who can reach which level** (decision 150). A list email unsubscribe leaves the person Inactive in that list. A church-wide email can set "No mass emails" only for someone on no active list, so nobody with that preference is ever left on a list. Rock steps a preference down one level at a time, and a bulk email never reaches someone on "No mass emails", so email unsubscribes almost never reach "Do not email". Generated people therefore start the window with the measured share of "Do not email", and of inactive emails.
- **The Cast.** Kevin Dawson leaves the Men list from its newest email, because its breakfasts fall on the weekends he has his kids. Brandon Watson is on no list and chose "No mass emails" from a church-wide email about three months ago. Every load checks both.
- **Calibration.** Part 7 also counts a whole year of unsubscribes, with their levels and timing, where Rock 19's unsubscribe index exists. In the measured data, recipient rows hold texted STOPs (about 0.34 per 1,000 mass-text recipients, mostly the same day) but no email unsubscribes, so the STOP rate and the timing are measured, and the email levels and rates are designed.

## Message load per person

Rock's Saturation report shows how many messages each adult got. A plan that sends every newsletter to a whole campus list would put most adults in the same bucket, and no real church looks like that (decision 133).

- **List membership follows involvement.** Leading and Engaged adults are on more lists (Volunteers, Small Group Leaders, Prayer Team, Parents of Kids) than Known adults, and some adults are on none. Parents get kids and student messages, and volunteers get serving reminders, so the busiest people sit in the high buckets.
- **Some get nothing.** Adults with no email, an inactive email, "No mass emails", or no list get no mass email, and adults who cannot text get no texts.
- **Graded.** Part 7 measures the share of active adults in each bucket over a month, by medium, for sends flagged bulk and for all mass sends. Validation grades the sample church against the all-mass-sends shape, since the sample church flags every mass send as bulk. In the measured data, most active adults get no mass email in a month and a core group gets about one a week, with a thin tail above that; texts reach far fewer people.

## Texts and SMS conversations

Texts follow Rock's Twilio path (decision D-commstexts-1). On Full 7 there are about 300 mass texts to 72,000 recipients, about 1,800 threads, and 5,800 texts from people over two years.

- **Bulk texts** go out from the main line and the student line. Church-wide texts reach each campus's involved adults who chose text as their communication preference (about 11% of generated adults, as measured), and list texts reach the list's members. A member with no phone that can text fails with Rock's note. Each sent text keeps the words the person got in `SentMessage`, as SMS Conversations needs, and has Rock's "SMS message" History line.
- **Replies.** Some texts ask for a reply ("Reply YES if you're coming"), and those get many more. Staff answer about half the replies from SMS Conversations, which marks them read. Replies are not linked to the text they answer through `RelatedCommunicationId`: Rock 21 looks for that text by comparing a communication Id with a recipient Id, so it almost never finds one, which is also why the measured data shows none.
- **The Cast's threads** (decision D-commstexts-2). Sixteen hand-written threads in [cast/sms-threads.json](../cast/sms-threads.md), tied to Cast stories. Together they cover every SMS Conversations state: unread and read, a 16-text back-and-forth, Brandon Watson's STOP, two threads on the student line, and threads on Ted Decker's, Alisha Marble's, and Isaac Silva's personal numbers. Six are from the last 1 to 3 days before install and unread, so even Core 1 opens to a live inbox (decision 93). Every load checks each thread.
- **Generated threads.** About 70 conversations a year per 1,000 active adults, from the pools in `content/communications/conversations.json`. Most are on a staff member's personal number: each campus pastor's for their campus on Full 7, and Ted's or Alisha's otherwise. The rest are on the main and student lines. Lengths and reply delays follow the measured shape, people text during waking hours, a few regular texters write staff again and again, and some short conversations never get an answer. A text with no staff answer after it is often left unread.
- **STOP.** A texted STOP comes at the measured rate, mostly the same day as a mass text. It is recorded the way Rock records it: a level All unsubscribe on the person's newest text in the last 60 days, and texting turned off and opted out on their mobile, with the History line Rock writes. Later texts to them fail.

## Push

- Pushes go to people with the app: "we're live now" on Sunday mornings, event reminders, and the occasional announcement.
- Opens are interactions on the same channel as email opens, mostly within an hour of the push.

## One-to-one messages

Most messages a real church sends go to one person (decision D-commsoneone-1). These come from data that is already loaded, over the same 2 years as bulk email. Each is its own communication with one recipient. On Full 7 there are about 120,000, almost all of them serving emails. Core 1 has about 5,400.

- **Serving.** A confirmation the day a volunteer is asked to serve, and a reminder at 4 p.m. two days before each Sunday they did not decline. Both come from the system communications Rock's Send Group Schedule Notifications job uses, so each email links to its system communication. They are email for everyone: Rock sends a texter an email when the system communication has no text message, and Rock ships none. The stored message is the library's short version, without Rock's global email header and footer, so the database stays small.
- **Connections.** An email from the connector each time they log "Contacted Waiting for Response" on a request.
- **Prayer.** A celebration email to the person who asked when their request is answered.
- **First visits.** A welcome the Monday after a generated adult's first Sunday, from their campus pastor on Full 7, or Ted Decker otherwise.
- **Staff** (decision D-commsoneone-2). Fourteen personal emails from Cast staff to Cast people in [cast/staff-emails.json](../cast/staff-emails.md), each tied to a Cast story. Each comes from the staff member by name and their own email address.

One-to-one emails go through the bulk email planner, so they follow the same rules. A person on "No mass emails" still gets them. A person on "Do not email", with no email, or with an address that bounced gets the note Rock writes instead. A personal email is opened a little more often than a mass email.

## Messages tied to steps

The [steps plan](steps.md) has a seven-step Discipleship Path and a four-step Student Path (decisions 83 to 89). Once steps are built, their milestones get their own messages (decision 96):

- **Starting Point Class.** Each campus's monthly class gets an invitation email. Everyone who completes it enters one finished Communication Flow, "After Starting Point": a thank-you the next day, a small group invitation a week later, and a serving invitation three weeks later. Each person's flow instance is complete, so Rock's flow screens have data.
- **Baptism.** Baptism Sunday gets a mass invitation. Each person baptized gets a follow-up email that invites them to Foundations.
- **Foundations.** Weekly reminder emails and texts to the people in each six-week course.
- **Student Path.** Student Camp and Mission Trip get registration emails and reminder texts to students and their parents on the student line.
- **Serve and Lead.** These already get serving reminders (see One-to-one messages).

This needs the step type Guids and each person's step dates from the steps work. The communications load runs after the steps load.

## Volume and the history window

Communication history covers the last 2 years, not the 3-year attendance window (decision 90). On Full 7 that is roughly 1.5 to 2 million recipient rows (an estimate), mostly from weekly newsletters. Three years would push it past 3 million and slow both the load and verify. Core 1 and Core 3 get the same messages with smaller lists.

## Calibration

Calibration part 3 already measures communications, but only one source has data, and that data is almost all one-to-one workflow messages (a median of 1 recipient, 0.1% bulk). It cannot shape bulk sends.

**Calibration part 7** ([the script](../calibration/sql/Report_SampleDataCalibration_7_Communications.sql)) measures mass sends (25 or more recipients) and text conversations (decision 91):

- Mass sends per month, by medium, and the share of sends and recipients they make up.
- Audience size as a share of adults, size bands, and how often sends use a list, segments, a template, or a topic.
- The weekday and hour of sends.
- Delivered, opened, and failed rates, unique openers and clickers, links per email, and how soon first opens and clicks follow the send.
- Unsubscribe and spam rates (Rock 17 and later).
- Email and communication preferences, phones that can text, and the share of adults with the app and with notifications on.
- Text replies to mass texts, stop words, unread texts, thread length, how fast staff answer, and how fast people answer.
- Church phone numbers and communication flows (Rock 18 and later).
- Saturation: the share of active adults in each message-count bucket over the last complete month, by medium, for sends flagged bulk and for all mass sends, the way Rock's Saturation report counts.
- A whole year of unsubscribes, by medium and level, per 1,000 mass-send recipients, and how long after the send they came (Rock 19 and later).

It returns no names, Ids, list names, message text, phone numbers, or URLs. Clicked links are compared by checksum only. It reads whole recipient rows only for a sample, and it adapts when Rock's post-update jobs have not built the indexes it needs, because the first versions timed out on a real church; see the [calibration README](../calibration/README.md#part-7-communications). Monthly volumes are raw counts, and the normalizer scales them by part 2's active adults, as for parts 5 and 6. The normalizer writes it to the `communicationDetail` section of a source file, and source-1 has it. The profile leaves out the volumes (designed, decision 91) and the values the measured data cannot support: unsubscribes and spam complaints (none recorded), replies to mass texts (replies were not linked to their texts), and one open rate that some opens never update. See the exclusions in `calibration/profiles/sample-church.json`.

Volume is designed, as for prayer and notes: the sample church shows what good communication looks like. Shapes and rates come from the measured sources.

## How it is checked

- **One-to-one invariants (built):** every serving email has the request it came from and one recipient, and no one on "Do not email" got a one-to-one email.
- **Text invariants (built):** every sent text is Delivered with its words in `SentMessage` and its "SMS message" History line, a text Rock could not send failed with its note, every STOP turns off texting on the person's mobile, and every text a person sent comes from their own mobile to an active church number.
- **Bulk email invariants (built):** every communication is Approved with its send time and date key, no recipient is Pending or Sending, recipient statuses agree with their dates and notes, every open and click belongs to an opened recipient after its first open, every unsubscribe matches the person and the list, and every sent email has its "Sent" History line.
- **Setup invariants (built):** no list member chose "No mass emails" or "Do not email", a newsletter holds only its campus's adults with an active email, the Volunteers and Small Group Leaders lists match the teams and groups, and every texting number is reserved, active, and no loaded person's number. The Cast check confirms each staff texting number is assigned to its staff member.
- **Invariants:** no Pending or Sending recipient, no communication without a `SendDateTime`, no active flow instance, every email recipient address on a reserved domain, and every outbound SMS conversation row Delivered with `SentMessage` set. Every unsubscribed recipient matches the person: a list unsubscribe leaves them Inactive in that list, a Bulk or All unsubscribe matches their email preference, and a texted STOP has texting turned off on their phone.
- **Cast expectations (built):** each Cast thread shows in SMS Conversations with its texts and in its intended state, unread, read, or stopped. Each Cast staff email shows in the person's Communication History, delivered, from the staff member, on its day.
- **Validation** grades the generated email data against calibration part 7 (decision 151): the open rate, engagement, open delays, top link share, the shares of inactive emails, "No mass emails", and "Do not email", and the designed share of active adults with no mass email in a month. For texts it grades thread lengths, threads with a staff text, unread threads, threads on personal numbers, the STOP share, and the designed share with no mass text in a month (decision D-commstexts-3).
- **The verify tool** re-records the contract for the new tables and runs Rock Cleanup as the referee.

## Build order

Each slice lands on its own, passes verify, and goes to the integrator.

1. **Design and calibration.** This page, the decisions, the calibration part 7 script, and the church app stub. Done. Source-1 has run part 7, and its shapes are in the profile.
2. **Setup.** System phone numbers, communication lists with membership that follows involvement, topics, the sample church template, and the message library. Done.
3. **Bulk email.** Sends flagged bulk with a sender, reviewer, and topic, recipients with their own send time, opens, clicks, failures, and unsubscribes that follow Rock's rules with matching History. Validation starts grading saturation. Done.
4. **Texts and SMS conversations.** Bulk texts, replies, the Cast's threads, generated threads, and STOP opt-outs. Done.
5. **One-to-one messages.** Serving, connections, prayer, first visits, and staff messages. Done.
6. **Push.** The church app's site and layout, personal devices, pushes, and push opens.
7. **Steps.** Step messages and the finished Communication Flow, after steps land.

## Open questions

1. **The app's pages.** The stub plans five tabs. Which pages and data each tab gets is later work.

Answered: email recipients leave `SentMessage` empty, because Rock's email transport never stores it (decision 149), and `AnalyticsSourceDate` covers 1876 to 2126, so every send's date key exists.

2. **The app's pages.** The stub plans five tabs. Which pages and data each tab gets is later work.
