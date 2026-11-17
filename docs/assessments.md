# Assessments

Rock's five assessments in the sample data, and why. Both slices are built. The decisions are 265 to 268 in [Decisions](decisions.md). The numbers live in `configuration/assessments.json`, so they can be tuned in one place.

## What Rock ships

Rock ships five assessments: DISC, Spiritual Gifts, Conflict Profile, Emotional Intelligence (EQ), and Motivators. Each request and each result is an `Assessment` row:

- **A request** is Pending. It names who asked (the requester), when, and a due date.
- **A result** is Complete. It holds the person's answers in `AssessmentResultData`.
- **The scores** are person attributes, such as DISC's Natural D or the Dominant Gifts. Rock's result pages, its Assessments badge on the person profile, and data view filters read them.

Rock's Send Assessment Reminders job emails a person with a pending request every 7 days, for 60 days after the request, at 8 AM.

## The idea

The sample church uses assessments more than most real churches do, so every screen has data: the badge, the result pages, the assessment list, and data views on the results. The shares are designed, not measured.

Every result comes from real answers. The loader makes each person's answers, scores them the way Rock does, and saves the answers and the scores from that one score, so they always agree (decision 265).

## Who takes them

| Who | Assessments | Asked by | When | Finish |
|---|---|---|---|---|
| Staff | All five | Their supervisor | Their first week, or after the church adopted assessments 4 years ago | All, within 3 weeks. Staff who started within the last year still have theirs pending. |
| Starting Point Class | Spiritual Gifts | The campus pastor | The class's first night, due on its third | 85% within two weeks |
| Foundations | DISC and Motivators | The campus pastor | The course's first night, due on its third | 80% within two weeks |
| Small group leaders | DISC and Conflict Profile | Jamal Pierce, the Groups Pastor | 2 to 6 weeks before they start leading, unless they already have | All, within 10 days |
| Serving team leaders | DISC and Conflict Profile | Their campus pastor | 2 to 6 weeks before they start leading, unless they already have | All, within 10 days |
| Active adults | DISC or Spiritual Gifts | No one | 5% of active adults each year of the history window | On the spot |

- **Adults only.** A person takes an assessment only as an adult in their family, 18 or older, and out of high school. No student or child has one.
- **The senior pastor,** Pete Foster, has no supervisor, so Pete takes all five without a request.
- **Nothing before adoption.** The church began using Rock's assessments 4 years before install day. Nothing is requested before then.
- **Classes ask on night 1.** The issue asked for the request in week 3 of each class. But the current Starting Point class is only in its second week on install day, so a week 3 request would leave nobody pending. So the class asks on its first night, due on its third, when it talks about the results (decision 266).
- **No workflows.** Rock's DISC Request and Request Assessment workflow types stay unused. The `Assessment` rows fill every screen.

## Answers and results

The loader ports Rock's five scoring services, which are the same in Rock 20.0.10 and 21.0.2. Their question tables are copied from Rock's source, and every number is Rock's own, so the port computes with the same doubles Rock does: the same rounding, the same lookup from a z-score to a percentage, decimals where Rock uses decimals, and ties broken in Rock's order.

Each person's answers lean toward a target result:

- **A target per person.** Each person has a lean on every dimension (a DISC letter, a gift, a motivator), drawn once per type. A retake comes out close to the first result, as a real retake does.
- **Rock's norms.** With no lean, each answer averages Rock's own mean for its dimension, so a typical person scores near the 50th percentile. One point of lean moves the dimension about one of Rock's standard deviations.
- **Leaders.** Small group leaders lean a little toward Shepherding, Teaching, and Discipling, and team leaders toward Leadership, Administration, and Service.
- **What is saved.** The answers go in `AssessmentResultData`, in the shape and text each Rock block saves, such as DISC's Most and Least for each question. The scores go in the person attributes Rock's service saves, as Rock writes them, such as `79` for a DISC score or a comma-separated list of gift Guids. A person's attributes come from their latest result of each type.

## Requests and reminders

- **Retakes.** Rock lets a person retake an assessment after 365 days (MinimumDaysToRetake, the same for all five). A request is skipped when the person finished that type in the last 365 days, or while another request of the type still waits inside its 60-day reminder window. A request still waiting after that window stays pending when a new one comes, as Rock's own request action leaves it.
- **Reminders.** Each request's `LastReminderDate` is where Rock's reminder job would have left it. The loader replays the job at 8 AM every day before install day, with Rock's rules, and stamps the latest pending row of every type the person has, as the job does. A request finished after a reminder keeps it. So the job's first run after install sends exactly the reminders a church's would (decision 267). Its emails reach only `example.org` and the Spark-owned staff domain.

## The Cast's stories

| Person | What their assessments show | Status |
|---|---|---|
| Alisha Marble | All five, requested by Pete Foster | Built, checked every load |
| Ted Decker | All five. His DISC result is a legacy one, from before the church used Rock's assessments. | Built, checked every load |
| Daniel Peak, Sam Hanks, Francis Dexter | The DISC results in their household files, exactly | Built, checked every load |
| Ty McClintock and Paul Smith | The DISC results in their files, from the test their university group took a year ago: CD and SC, so a demo can compare friends | Built, checked every load |
| Brian and Wendy Gilbert | Spiritual Gifts requested by their current Starting Point class, still pending | Built, checked every load |
| Bri Hall | Spiritual Gifts from last month's Starting Point class. Hospitality, Encouragement, and Service, gifts that fit a greeter, are among her dominant gifts. | Built, checked every load |
| Cindy Decker | An old Spiritual Gifts result and a pending retake that Jamal Pierce asked for | Built, checked every load |

A story lives in the person's `assessments` in their household file, by type (decision 268):

- **leaning** names the gifts or letters their answers lean toward, which must come out on top: among the five dominant gifts, or the two DISC letters.
- **requests** says how the person answers every request the rules make of that type: `complete` or `pending`.
- **takes** sets the person's own history of that type, which replaces the rules for it. A request with no completion stays pending, and a completion with no request was taken on the person's own.

**Legacy DISC results.** Six Cast files held DISC scores. No set of answers to Rock's DISC gives them: some are values Rock's tables cannot produce, such as Daniel Peak's Natural S of 100, and none add up to 30 answers. So they are legacy results, taken outside Rock's own DISC, with exactly those scores and no answers. Ted's, Francis's, Sam's, and Daniel's keep their old dates, from before the church used Rock's assessments. Ty's and Paul's files dated theirs when both were 11, so theirs come from the DISC test their university student group took a year ago. Rock's DISC result page reads only the person attributes, so it shows them as they are.

## How many

What each dataset loads at the default seed:

| | Core 1 | Core 3 | Full 7 |
|---|---|---|---|
| Assessments | 171 | 295 | 5,600 |
| Pending | 19 | 27 | 704 |
| Answer sets, which Rock's own scoring checks | 146 | 262 | 4,890 |
| People with at least one | 67 | 121 | 3,056 |
| Current leaders, every one with DISC and Conflict Profile | 10 | 32 | 430 |

Measured on 2026-11-16, the install day. Full 7's results by rule: 1,619 from Starting Point, 2,158 from Foundations, 840 from leaders, 865 taken on people's own, 110 from staff, and 8 from Cast stories. 2,120 rows carry a reminder. The six most common DISC profiles are SC, SI, IS, CD, ID, and SD, each about 130 to 160 people.

## How it is checked

- **Cast expectations,** on every CLI load and in the verify tool: each story above, and each legacy DISC result exactly as its file has it.
- **Invariants** in `validation/invariants.sql`: a pending request has a requester and no results, and a result has its answers (except a legacy DISC); a person has a type's results exactly when they completed it, and DISC and Spiritual Gifts keep the latest completion as their save date; retakes are at least 365 days apart; only adults out of high school take one; nothing is dated after install day; reminders follow the job; every current leader has DISC and Conflict Profile; and no assessment workflow runs.
- **Rock's own scoring,** in the verify tool: Rock's five services score every saved answer set, each person's latest result must give exactly their attribute values, and Rock's own serializer must write the same answer data.
- **The Rock lock** watches the five scoring services and the reminder job (`node validation/check-rock-lock.js`).

## Later

- **Assessment pages in the church's navigation,** such as a link from the Starting Point class page.
- **Results in reports,** such as a team roster with each volunteer's top gifts.
