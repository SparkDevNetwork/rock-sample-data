# Adaptive messages

Status: slice 1 of [issue 6](https://github.com/SparkDevNetwork/rock-sample-data/issues/6) is built: the personalization segments, five adaptive messages with their adaptations and text, and the segment rows Rock's Update Personalization Data job would leave on install day. Slice 2, a year of view history, waits for Interaction Data (issue 12). Decisions D-adaptive-1 to D-adaptive-8 record what is built.

**The idea in one line:** each person sees a next step that fits them, in the same spot on a page, chosen by Rock from the data views the church already keeps.

## How Rock shows a message

An adaptive message is a Rock message with several versions, called adaptations. Each adaptation names one or more personalization segments, and a segment is the people in one data view. Rock's `{% adaptivemessage %}` Lava command shows each person the first active adaptation, by its order, that names a segment they are in. An adaptation with no segments fits everyone, so it goes last, as the fallback.

Rock keeps each segment's people in its `PersonAliasPersonalization` table, by each person's primary alias. Its Update Personalization Data job refreshes a segment on the segment's schedule. Rock's segment editor offers a daily refresh by default, so the church's segments refresh daily.

Rock ships no block that shows adaptive messages. A page shows them only through the Lava command, on a block that enables it.

## Where the messages show

The External Website homepage has a "Your Next Steps" HTML Content block in its Section A zone, below the feature banner (decision D-adaptive-8). Its Lava, `content/adaptive-messages/homepage.lava`, runs the command on the church's category, one adaptation per message and up to four in all, and shows each as a card with its image, name, summary, and call to action. The block enables the AdaptiveMessage command, and its cache stays off, because each person sees their own cards. A visitor who is not signed in sees each message's fallback: Plan a Visit and Ways to Give. Rock records a view of an adaptation with saturation each time the page shows it.

Sign in as `tdecker` to see Ted Decker's cards: Plan a Visit, the Built on the Rock thanks, and the thanks for his scheduled gift. The app's Home page waits for the Sample Mobile App (issue 10).

## The five messages

The messages sit in a Rock Solid Church category under Rock's All Church category, in this order. Each segment points at a data view in `configuration/reporting.json` ([Data views and reports](reporting.md)), so the messages, the reports, and the flows agree on who is who.

| Message | Adaptations, in order | When |
|---|---|---|
| Your Next Step | Ready to Lead: lead a group. Ready to Serve: find a team. Ready to Grow: take Foundations. Ready to Belong: join a small group. Guests Coming Back: Starting Point Class. Everyone else: plan a visit. | Always |
| Built on the Rock | Built on the Rock Pledge Households: thank you. Involved Adults Not Yet Pledged: make a commitment. | While the campaign runs, from the fund's start date through its last day |
| Baptism Sunday | Unbaptized Regular Attenders: be baptized. | From 35 days before the next baptism Sunday until that Sunday begins |
| Student Camp | Students Attending the Student Service: register for camp. | While the next camp's registration is open, through the day it closes |
| Giving | The seven layers below | Always |

Rock shows a message or an adaptation from its start date until its end date begins, because it stores dates without a time. So an adaptation that should show on its last day ends the next day.

### The Giving layers

The Giving message gives each person the next step that fits how they give. The layers read data the loader already writes: Rock's giving journey stage, which Rock computes once per giving unit, and scheduled gifts. So a couple who give together see the same layer.

| Order | Layer | Segment | Its data view's rule | Cast person |
|---|---|---|---|---|
| 1 | Thank you for your scheduled gift | Scheduled Givers | Active people whose giving unit has an active scheduled gift, through Rock's Combine Giving option | Ted and Cindy Decker |
| 2 | Thank you for your first gift | New Givers | Journey stage New: a first gift in the last 150 days, and five gifts or fewer | Jordan Ellis |
| 3 | Make it automatic | Consistent Givers Without a Schedule | Journey stage Consistent, and not in Scheduled Givers | Margaret Carter |
| 4 | Give online any time | Occasional Givers | Journey stage Occasional | Steve Morgan |
| 5 | What God did this year | Lapsed Givers or Former Givers | Journey stage Lapsed or Former | Emily Gray, Luis Ortiz |
| 6 | Generosity starts here | Involved Adults Who Never Gave | Involved Adults with journey stage Non-Giver | Brian and Wendy Gilbert |
| 7 | Ways to give | None | Everyone else, including people not signed in | Bri Hall |

The stage thresholds are Rock's defaults in `GivingAutomationSettings`, the same in Rock 20 and Rock 21: New is a first gift in the last 150 days with one to five gifts, Consistent is a gift in the last 32 days and a mean gap of 32 days or less, Occasional is a gift in the last 150 days and a mean gap of 94 days or less, Lapsed is no gift in 150 days with a mean gap under 100 days, and Former is any other giver.

Bri Hall's issue story put her in layer 6. By Rock's own rule she is not an Involved Adult yet: she has not come on a Sunday, and her request to serve is still pending. So she sees layer 7, and her Cast row says so.

Jordan Ellis gives with cash in the offering. When the profile picked his channel, it made his gift a scheduled one, and Rock showed him layer 1 instead of layer 2.

## Who can see segments

A segment built on a Finance data view tells who gives and how, so it is locked as tightly as Finance, under the consumer rule in [Data views and reports](reporting.md#18-what-this-means-for-the-design) section 1.8.

1. **The segment.** Rock secures a segment by its own rules only, never by a category. So each segment built on a Finance view allows View to RSR - Finance Administration, RSR - Finance Worker, and RSR - Rock Administration, then denies All Users. Rock's segment list and detail blocks check View, so they hide these segments from everyone else.
2. **The Results page.** Rock's Personalization Segment Results page lists any segment's people and checks no segment's security. Rock's CMS pages already limit it to RSR - Rock Administration and RSR - Communication Administration. So the page gets the same rules as the Finance segments. Ted Decker and Daniel Peak, who are Communication Administrators without a finance role, can no longer open it for any segment.
3. **The communication wizard.** Rock's email wizard lists only the segments in Rock's Communications segment category, and checks View. The church's segments are not in it.

Verify asks Rock's own security check about Ted Decker, Alisha Marble, and Pete Foster.

## Text and images

Each adaptation's text is in `content/adaptive-messages/messages.json`, checked by `check-messages.js` ([its README](../content/adaptive-messages/README.md)). Each field fills one of Rock's own adaptation attributes: Call To Action Text and Link, Summary, Details, and Summary Image. Rock 20 and Rock 21 ship no Mobile App Summary XAML attribute, so there is none to fill. Links point at real External Website pages, by route or as Rock's `/page/Id` link.

Ten adaptations reuse an approved calendar event image. Seven have none yet and wait for an image brief: Plan a Visit, and the Giving layers Scheduled Gift, First Gift, Make It Automatic, Give Online, What God Did This Year, and Ways to Give.

## How it loads

The Adaptive messages step runs after the Reports step, when every data view is written (see [How it works](how-it-works.md)).

1. **Segments.** Each segment whose data view this load wrote, with its security, and the primary alias of everyone the loader's data view evaluator finds in its view. The evaluator follows Rock's own filters: Existing and Not in Existing Data View, steps taken, attribute values, scheduled gifts with Combine Giving, pledges, and the Family Members transformation. Each segment is stamped as refreshed now, so the job's next run is tomorrow, and carries the empty additional filters Rock's editor saves, which Rock's segment update needs.
2. **The Results page lock.**
3. **Messages.** Each message with at least one adaptation this load can write, in the church's category, then its adaptations in order with their segments, dates, saturation, and text. An adaptation whose segment was skipped is skipped too, because without its segment it would show to everyone. A load without registrations has no camp dates, so it leaves out Student Camp.
4. **The homepage block,** when any message was written.

## What is checked

- **`check-messages.js` and `check-reporting.js`** check the configuration and the text without a database, including that only a message's last adaptation has no segments, and that every Finance segment and the Results page carry the Finance lock.
- **Cast expectations.** Every load checks that each Cast row in the configuration sees their adaptation, by Rock's own rule.
- **Invariants.** Every segment row is a primary alias, and only a message's last adaptation has no segments.
- **`adaptive-messages` (verify).** Rock's own Lava engine renders the command for each Cast row and must show their adaptation with its call to action. It also renders the homepage block for each of them, which must show that call to action.
- **`segments` (verify).** Rock's own segment update runs on every segment and must add and remove nobody.
- **`access` (verify).** Rock's own security check answers the segment and Results page questions.

## What comes next

- **View history (slice 2).** A year of "Viewed" interactions in Rock's Adaptive Messages channel, after Interaction Data (issue 12) brings website sessions.
- **The app.** The Rock Solid Church App's Home page waits for the Sample Mobile App (issue 10).
- **Leaders see the fallback.** Ted Decker and Pete Foster already lead, so no next-step segment fits them and they see Plan a Visit. A Lead Stage layer, such as "invite a friend", would give leaders their own next step.
