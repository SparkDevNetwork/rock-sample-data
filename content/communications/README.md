# Communication messages

The hand-written messages the sample church sends: emails, texts, and pushes. The loader builds every communication from these files at install time, so no date or name is fixed here. Everything follows the [content standards](../../docs/content-standards.md). See [Communications](../../docs/communications.md) for how sends, lists, and recipients are planned.

## Files

| File | What it holds |
|---|---|
| `series.json` | The sermon series, and the slot in the church year each one fills. |
| `newsletter.json` | The pieces each campus's weekly newsletter is built from. |
| `emails.json` | Every other email: seasonal sends, ministry sends, pastoral letters, and one-to-one messages. |
| `texts.json` | Texts and pushes, plus the reply pools for texts that ask for an answer. |
| `conversations.json` | The pools that generated text conversations are built from. |
| `email-template.html` | The sample church's branded email template (see [configuration/communications.json](../../configuration/communications.json)). |
| `email-logo.png` | The approved Rock Solid Church logo at 480 by 120 pixels with a transparent background, for the template. |
| `check-library.js` | Checks every message file. Run `node content/communications/check-library.js` after any change. |

## Occasions

Each message names the occasion it belongs to. The loader dates every occasion from the install day, so a message always matches when it was sent. `daysBefore` counts back from the occasion: 0 is the day itself, and a negative number is after it.

| Occasion | When |
|---|---|
| `newsletter` | Every Thursday, one send per campus. Only `newsletter.json` uses it. |
| `sunday` | Every Sunday. |
| `series-start` | The first Sunday of a sermon series. |
| `easter` | Easter Sunday. |
| `christmas` | Christmas Eve, December 24. |
| `thanksgiving` | Thanksgiving Day, the fourth Thursday of November. |
| `year-end` | December 31, the last day to give for the year. |
| `giving-statement` | January 31, when giving statements go out. |
| `commitment-sunday` | Built on the Rock's Commitment Sunday (configuration/campaign.json). |
| `campaign-update` | Every 13 weeks after Commitment Sunday, while the campaign runs. |
| `baptism-sunday` | Each baptism Sunday: Easter, and the third Sunday of July (configuration/steps.json). |
| `starting-point` | The first night of each campus's Starting Point Class (Wednesdays, every four weeks). |
| `foundations` | The first night of each campus's Foundations course (Tuesdays, every eight weeks). |
| `student-camp` | Student Camp's first day (the fourth Monday of June). |
| `mission-trip` | Each student mission trip's first day (spring break and early July). |
| `vbs` | Vacation Bible School's first day (configuration/communications.json). |
| `group-launch` | The fall and spring small group launch Sundays. |
| `serve-sunday` | Serve Sunday in late August, when teams recruit. |
| `weather` | A rare closure or change, such as a monsoon storm. The loader picks a few dates. |
| `serving-scheduled` | A volunteer is scheduled to serve. One message per person. |
| `serving-reminder` | A few days before a volunteer serves. One message per person. |
| `connection` | A connector follows up on a request to serve. One message per person. |
| `prayer-answered` | A prayer request is marked answered. One message per person. |
| `first-visit` | The week after a household's first Sunday. One message per person. |
| `baptized` | The week after a person is baptized. One message per person. |
| `after-starting-point` | The "After Starting Point" Communication Flow. `daysBefore` is negative: days after the class ends. |
| `after-foundations` | The "After Foundations" Communication Flow. `daysBefore` is negative: days after the course ends. |
| `baptism-interest` | The "Baptism Next Steps" Communication Flow. `daysBefore` is 0 or negative: days after the person fills out the Baptism Interest form. |

## Audiences

| Audience | Who |
|---|---|
| `newsletter` | One campus's weekly newsletter list. The send goes once per campus. |
| `everyone` | Every adult with an email the church may send mass email to. |
| `list:<key>` | One communication list in configuration/communications.json, such as `list:volunteers`. |
| `students-and-parents` | The Students list and the parents of those students. |
| `class` | The people in the class or course the occasion names. |
| `person` | One person. Used by the one-to-one occasions. |

## Senders

`church` sends from hello@rocksolidchurchdemo.com as "Rock Solid Church". `pastor` sends from Pete Foster by name; use it only for a few pastoral letters. `students` sends from the student ministry. The addresses are in configuration/communications.json.

## Tokens

The loader fills in these tokens. Use them instead of dates, names, and times.

| Token | Becomes |
|---|---|
| `{eventDate}` | The occasion's date, such as "Sunday, April 5". |
| `{eventDay}` | The occasion's weekday, such as "Sunday". |
| `{eventYear}` | The occasion's year, such as "2026". |
| `{registerBy}` | Two weeks before the occasion, such as "Monday, June 8". |
| `{sendDate}` | The send's own date, such as "Thursday, April 2". |
| `{campus}` | The campus name, such as "Peoria". |
| `{campusKey}` | The campus key, such as "peoria". Use it only inside a link path. |
| `{serviceTimes}` | The campus's Sunday service times, such as "9:00 and 10:30 a.m.". |
| `{series}` | The sermon series running on the occasion, such as "Unshaken". |
| `{seriesTagline}` | That series' one-line description. |
| `{seriesWeek}` | Which week of the series it is, such as "3". |

Rock fills in one Lava merge field for each recipient: `{{ Person.NickName }}`. No other Lava belongs in a message.

## Links

Every link is a path on the church's site, written as Markdown: `[Plan your visit](/next-steps)`. The loader writes `https://rocksolidchurchdemo.com` in front, so Rock's All Links chart groups clicks by page. The checker allows only these paths:

`/`, `/events`, `/easter`, `/christmas`, `/give`, `/give/statements`, `/watch`, `/watch/live`, `/watch/series`, `/groups`, `/serve`, `/starting-point`, `/baptism`, `/foundations`, `/students`, `/students/camp`, `/students/mission-trip`, `/kids`, `/kids/vbs`, `/prayer`, `/built-on-the-rock`, `/next-steps`, `/men`, `/women`, `/app`, `/campuses/{campusKey}`.

## series.json

```json
{
  "$comment": "...",
  "slots": {
    "new-year": { "$comment": "Starts the first Sunday of January.", "weeks": 7 },
    "after-easter": { "$comment": "Starts the Sunday after Easter.", "weeks": 7 },
    "fall": { "$comment": "Starts the second Sunday of September.", "weeks": 10 }
  },
  "series": [
    { "key": "unshaken", "name": "Unshaken", "tagline": "Finding steady ground when life moves.", "slot": "new-year" }
  ],
  "campaign": { "key": "built-on-the-rock", "name": "Built on the Rock", "tagline": "...", "weeks": 4 }
}
```

Each slot has two series. A year uses the first in even years and the second in odd years. The campaign series runs the four Sundays that end on Commitment Sunday, and it replaces any other series those weeks. Sundays outside a series have no `{series}`, so a message that uses it is only sent in a series.

## newsletter.json

```json
{
  "$comment": "...",
  "subjects": [ "This week at Rock Solid {campus}" ],
  "openers": [ "..." ],
  "series": [ "This Sunday we continue {series} with week {seriesWeek}. ..." ],
  "pieces": [
    { "key": "easter-invite", "occasion": "easter", "from": 21, "until": 1, "heading": "...", "text": "...", "button": "Plan your Easter", "href": "/easter" }
  ],
  "evergreen": [
    { "key": "find-a-group", "heading": "...", "text": "...", "button": "Find a group", "href": "/groups" }
  ],
  "closings": [ "..." ]
}
```

A newsletter is one subject, one opener, the series line when a series is running, the pieces whose occasion falls `from` to `until` days after the send, evergreen pieces to fill it to four, and one closing.

## emails.json

```json
{
  "$comment": "...",
  "emails": [
    {
      "key": "easter-invite",
      "occasion": "easter",
      "daysBefore": 10,
      "audience": "everyone",
      "topic": "events",
      "sender": "church",
      "subject": "You're invited to Easter at Rock Solid",
      "preheader": "Services, kids, and what to expect.",
      "body": [
        { "h": "Easter is {eventDate}" },
        { "p": "Hi {{ Person.NickName }}, ..." },
        { "button": "Plan your Easter", "href": "/easter" }
      ]
    }
  ]
}
```

Body blocks are `h` (a heading), `p` (a paragraph, which may hold Markdown links), `list` (a list of short items), and `button` (with an `href`). `topic` is a key of the topics in configuration/communications.json. Add `"bulk": false` to a small leader send that is not flagged bulk.

## texts.json

```json
{
  "$comment": "...",
  "texts": [
    { "key": "easter-reminder", "medium": "sms", "occasion": "easter", "daysBefore": 1, "hour": 16, "audience": "everyone", "line": "main", "topic": "events", "text": "..." },
    { "key": "sunday-live", "medium": "push", "occasion": "sunday", "daysBefore": 0, "hour": 9, "audience": "everyone", "topic": "general", "title": "We're live", "text": "..." }
  ],
  "replies": {
    "yes": [ "..." ],
    "no": [ "..." ],
    "question": [ "..." ],
    "thanks": [ "..." ],
    "staff": [ "..." ]
  }
}
```

A text that asks for an answer sets `"asksReply": true`, and its replies come from the pools. Staff answer some replies with a line from `staff`. `line` is the phone number it goes out on: `main` or `students`. A push has a `title`.

## conversations.json

```json
{
  "$comment": "...",
  "topics": [
    {
      "key": "serving-swap",
      "numbers": [ "personal", "main" ],
      "audience": "adult",
      "openers": [ "Hi {staff}, can I swap my Sunday on the greeter team?" ],
      "staffReplies": [ "Hi {name}! Of course. I will find someone for {campus} this week." ],
      "personFollowUps": [ "..." ],
      "staffFollowUps": [ "..." ],
      "closings": [ "Thank you so much!" ]
    }
  ]
}
```

A generated thread is one topic: an opener from the person, a staff reply, then follow-ups back and forth to its length, and sometimes a closing. `numbers` says where the topic happens: `personal` (a staff member's personal number), `main` (the main line), or `students` (the student line). `audience` is `adult`, `parent`, or `student`. Tokens are `{name}` (the person's nickname), `{staff}` (the staff member's first name), and `{campus}`.

## Lengths

| Field | Characters |
|---|---|
| Email `subject` | 20 to 70 |
| Email `preheader` | 20 to 110 |
| Text `text` (SMS) | 40 to 300, mostly under 160 |
| Push `title` | 5 to 40 |
| Push `text` | 20 to 120 |
| Reply | 2 to 120 |
| Conversation opener, reply, follow-up, or closing | 2 to 300, mostly under 160 |

## Rules

1. Plain printable characters only: no curly quotes, dashes, or emoji. Write "--" never.
2. Warm, short sentences in the church's voice. No placeholder text.
3. Name only Cast staff: Pete Foster (Senior Pastor), Ted Decker (Outreach Pastor), Isaac Silva (Student Pastor), Alisha Marble, and Daniel Peak.
4. Scripture is a reference, or a quote from a public-domain translation.
5. Each key is unique within its file, in lowercase words joined by hyphens.
