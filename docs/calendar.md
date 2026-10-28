# Calendar

Rock Solid Church's calendar: the events on Rock's Public and Internal calendars, when they happen, and how they agree with the rest of the data. The events live in [configuration/calendar.json](../configuration/calendar.json). The loader's calendar step writes them (decision 174).

## What a church gets

51 events, in every dataset. A dataset gets each event's occurrences at the campuses it has, so Core 1 shows Phoenix only, and Full 7 shows all seven campuses. The events are grouped below by the stage each one serves (see [Stages](#stages)).

| Stage | Events |
|---|---|
| Belong (20) | Easter Sunday, Good Friday Service, Easter Egg Hunt, Christmas Eve Services, Lessons and Carols (Scottsdale), Fall Festival, Summer Movie Nights, Rock Solid Car Show, Starting Point, Small Group Connect, Vacation Bible School, Parents' Night Out, Student Night, Student Camp, Young Adults Night (Tempe), Men's Breakfast, Women's Night Out, Women's Christmas Brunch, Hope for the Holidays, Young at Heart Lunch |
| Equip (12) | Baptism Sunday, Foundations, Women's Bible Study, Tuesday Morning Men's Study, Money Matters, Ready for Marriage, Marriage Date Night, Baby Dedication, Night of Worship, Men's Retreat, Women's Retreat, Student Fall Retreat |
| Contribute (10) | Serve Sunday, Volunteer Safety Training (on both calendars), Community Serve Day, Men's Serve Day, Community Diaper Drive, Thanksgiving Food Drive, Blood Drive, High School Mission Trip, Built on the Rock Commitment Sunday, Goodyear Setup and Load-In (Internal calendar) |
| Lead (3) | Student Leader Interest Meeting, Leader Huddle (on both calendars), Group Leader Training |
| Staff (6, Internal calendar) | All Staff Meeting, Weekend Service Planning, Elder Meeting, Rock Office Hours, Staff Retreat, Staff Christmas Party |

Each event has a summary and a description, one or more audiences, and occurrences with a campus, a place, a schedule, and a contact. Every event is active and approved by Ted Decker, who holds the Calendar Administration role, because Rock's calendar pages and the church app list only approved events.

## Stages

The calendar follows Ministry by Design, the same framework the step programs use (see [Steps](steps.md)): Belong, then Equip, then Contribute, then Lead, with growth before serving, and the path is "the norm, not a gate." Each event's `stage` in `calendar.json` names the stage it serves (decision 183), so the calendar shows a clear path from a first visit to leading others:

| Stage | What its events do | Where the steps meet it |
|---|---|---|
| Belong | Front-door, family, community, and care events, where people are known and connected before they are asked to do anything | Starting Point is the Starting Point Class step, Small Group Connect leads to the Small Group step, and Student Camp is the Student Path's first step |
| Equip | Classes, studies, retreats, and Sundays where people grow as followers of Jesus | Baptism Sunday is the Baptism step, and Foundations is the Foundations step |
| Contribute | Serving, outreach, and generosity, where people take part in the church's mission | Serve Sunday and Volunteer Safety Training lead to the Serve step, Commitment Sunday to the Give step, and the mission trip is the Student Path's Mission Trip step |
| Lead | Training and huddles for the people who lead groups, teams, and students | The Lead step counts small group and serving team leaders, who meet at the Leader Huddle and Group Leader Training; juniors and seniors learn about the Student Leader step at the interest meeting |
| Staff | Staff events on the Internal calendar, which run the church rather than serve a stage | |

Belong has the most events, because a church's front door is wide. Lead has the fewest, because it serves the few who lead. The stage is not stored in Rock; it lives in `calendar.json` and here.

What changed on 2026-10-28 (decision 183): Graduate Sunday was removed, and the Leader Huddle took its place, because Lead had only one event. Group Leader Training was added before each group launch, and the Tuesday Morning Men's Study, which Ted Decker's text to Kevin Dawson already describes, gives men a weekly Equip rhythm. The seniors lunch is now the Young at Heart Lunch.

## Rock's own sample events

Rock ships four sample events with placeholder text and dates in 2015 and 2018. Each becomes one of the church's events, updated in place under Rock's Guid, the same way the steps reuse Rock's Discipleship Path:

| Rock's event | Becomes |
|---|---|
| Staff Meeting | All Staff Meeting |
| Customs & Classics Car Show | Rock Solid Car Show |
| Rock Solid Finances Class | Money Matters |
| Warrior Youth Event | Student Night |

Their old occurrences, schedules, calendars, and audiences are replaced. Rock's two calendars (Public and Internal) and its Audience Type values are used as Rock ships them. Rock has no Young Adults or Seniors audience, so those events use Adults.

## Dates

Every date is a rule counted from the install day, never a fixed date (decision 175). Occurrences reach from the start of the history window to 13 months after the install day (`monthsAhead`), so next year's camp, VBS, and Women's Retreat are always on the calendar for their registrations (decision 190).

Dates that other features use come from the same rules they use, so the calendar, the steps, and the messages always name the same day:

| Event | Where its dates come from |
|---|---|
| Easter Sunday, Good Friday, Easter Egg Hunt | Easter, the same calculation attendance uses |
| Baptism Sunday | Easter and the summer baptism Sunday in `configuration/steps.json` |
| Starting Point | Every Wednesday. Each class meets four Wednesdays and a new one starts every four weeks (`steps.json`), so a class meets every week |
| Foundations | Each six-week course from the Foundations class schedule in `steps.json` |
| Student Camp, High School Mission Trip | The camp and trip seasons in `steps.json` |
| Vacation Bible School, Small Group Connect, Serve Sunday | VBS, the group launches, and Serve Sunday in `configuration/communications.json`. Small Group Connect is the Sunday before each launch |
| Built on the Rock Commitment Sunday | `configuration/campaign.json` |

A few dates are pinned to Cast stories with `daysAgo`, so the story holds on any install day:

| Event | Story | Rule |
|---|---|---|
| Rock Solid Car Show | Helen Evans texts the main line two days before install day about the big event she just came to. Jenny Michaels coordinates the car show. | The Saturday on or before 2 days ago, each year |
| Men's Breakfast | Ted texts Kevin Dawson 47 days before install day that he missed the breakfast on a weekend he had his kids. | Every fourth Saturday from the Saturday on or before 47 days ago. Every fourth Saturday always lands on the same every-other weekend, as Kevin says |
| Community Diaper Drive | Destiny Young came to the drive 45 days before install day, and asked 20 days before about the next one. | Every 13 weeks from the Saturday on or before 45 days ago |
| Community Serve Day | Bri Hall helped at the serve day 35 days before install day. | The Saturday on or before 35 days ago, each year |
| Student Fall Retreat | Isaac's email to Kevin Dawson 30 days before install day says the retreat is next month. | The Friday on or after 10 days from now, each year |

Every other event follows a plain rule: a weekday each week (Student Night), a weekday of each month (the Young at Heart Lunch and Parents' Night Out), a weekday of a month each year (Fall Festival, the last Saturday of October), a date (Christmas Eve), or a weekly season (Women's Bible Study, from September to December and January to May).

### Kinds of rules

Each event's `when` has a `kind`:

| Kind | Fields | Example |
|---|---|---|
| `weekly` | `weekday` | Student Night, every Wednesday |
| `monthly` | `weekday`, `week` (1 to 4, or -1 for the last) | Parents' Night Out, the first Friday |
| `yearly` | `month`, `weekday`, `week`, or a list of those in `dates`; `weeks` to meet that many weeks in a row | Money Matters, five Mondays from the second Monday of January and the third Monday of September |
| `date` | `month`, `day` | Christmas Eve |
| `easter` | `offsetDays` | Good Friday is -2 |
| `occasion` | `occasion`, `offsetDays` | `baptism-sunday`, `serve-sunday`, `group-launch`, `vbs`, or `commitment-sunday` |
| `class` | `stepType` | Foundations' course schedule |
| `step-season` | `stepType`, and `season` for one season | Student Camp's summer season |
| `seasons` | `weekday`, `seasons` (each `from` and `until`) | Women's Bible Study |
| `pinned` | `daysAgo`, `weekday`, `direction`, and `repeat` (`yearly`, or `weeks` with `everyWeeks`) | The car show |

An event's `times` is a list of start times, or `services` for the campus's Sunday service times. `timesByCampus` overrides the list for a campus (Christmas Eve). `minutes` is how long each start lasts. A `span` event runs as one block from its first day's start to its last day's `endTime`, as camps and retreats do; `days` says how many days. Without `span`, `days` makes one start on each day in a row, as VBS does.

## Occurrences and schedules

One occurrence is one campus at one start time, the way a person adds them on Rock's occurrence screen. Easter at Phoenix has three occurrences, one for each service. A church-wide event, such as Student Camp or a staff meeting, has one occurrence with no campus, which Rock shows as "All Campuses."

Each occurrence has its own unnamed schedule, as Rock's screen saves it:

- A weekly or monthly event has one rule with no end, starting at the history window's start.
- A yearly event gets an occurrence each year: one date, a list of dates (`RDATE`), or a weekly rule with a count (Women's Bible Study).
- A span event is one block with no repeat.

The loader writes what Rock's save hooks and its nightly Rock Cleanup job would: each schedule's effective start and end dates, its `ScheduleDate` rows from two years before now to two years after, and each occurrence's next start. So Rock Cleanup finds nothing to fix (see [Verification](verification.md#rock-cleanup-referee)).

## Places and contacts

Rooms have the same names at every campus. A campus can rename a room in `locationNames`: Goodyear meets in a high school auditorium on Sundays and in a small leased ministry center during the week, Scottsdale's worship center is its sanctuary, and Tempe's lobby is a cafe. Camps and retreats meet at Juniper Hollow Camp near Prescott and Sycamore Bend Retreat Center near Sedona, both made up.

Each event names a contact:

| Contact | Person | Events |
|---|---|---|
| `pete` | Pete Foster, senior pastor | Big Sundays, marriage and grief events, staff meetings, the Young at Heart Lunch |
| `ted` | Ted Decker, outreach pastor | Outreach, men's events, Serve Sunday, the blood drive, the Leader Huddle |
| `isaac` | Isaac Silva, student pastor | Student events, with the student line and inbox |
| `alisha` | Alisha Marble, Rock administrator | Women's events, Money Matters, safety training, Rock Office Hours |
| `jenny` | Jenny Michaels, who coordinates the car show and camp | The car show and Student Camp |
| `rachel` | Rachel Brooks, kids volunteer | Kids events, with the kids inbox |
| `campus-pastor` | Each campus's campus pastor, the one notes and texting use. Ted stands in on the main campus. | Campus events such as Easter, Starting Point, and the Fall Festival |

Phones are the church office line (623) 555-0108, or the student line (623) 555-0160. Emails are at `rocksolidchurchdemo.com`, with a campus inbox such as `peoria@rocksolidchurchdemo.com` for campus pastors.

## Photos

Each event's image is `EventItem.PhotoId`. Rock shows it whole on the event's web page and cropped to 2:1 in the church app, so every image is 2:1, 1536 by 768 pixels (decision 176). The [event image brief](../photos/event-brief.md) asks the image agent for all 51. 28 events get a design, a marketing graphic whose only text is the event's title, and 23 get a photo of the event with no text (decision 184).

The loader sets a photo for each event listed in `photos/events/manifest.json`, stored the way Rock stores an uploaded one. The builder packs every listed photo into every dataset and refuses an entry for an event that does not exist or a file that is missing. Adding photos needs no code change: deliver the files, list them, and rebuild the packages. Until then, events load with no photo.

## Checks

- **Invariants** (`validation/invariants.sql`): every occurrence belongs to an active, approved event on a calendar with an audience; has an active schedule with iCalendar text and an effective start; has a contact person with a church email and a reserved phone; and every event photo is a stored file.
- **Cast expectations**: each event's `expect` entries are Cast stories the load checks against Rock's dates, such as a car show start 2 to 8 days before today at Phoenix.
- **Rock Cleanup referee**: the schedules' dates and next starts must be what Rock would set.

## The calendar part

The calendar is its own loader part, `Calendar` ("Calendar events"). It needs no other part: it reads only configuration and people. A package built before the calendar existed has no `configuration/calendar.json`, and the step skips.

The calendar step hands its plan to the registrations step, which links each registration to an occurrence the calendar wrote. Both take the occurrence's key, and so its Guid, from one place.

## Registrations

Student Camp, the mission trip, the fall retreat, VBS, Parents' Night Out, Serve Day, Money Matters, Date Night, and the Women's Retreat are linked to registration instances, so their event pages show a Register button. See [Event registrations](registrations.md).

## Later

1. **Content channel items.** Rock links the Public calendar to the External Website Ads and Service Bulletin channels. Promoting a few events there would fill the home page rotator.
2. **Social images.** Rock's Public calendar has Facebook (1200 by 630) and Twitter (440 by 220) image attributes. They could be cropped from the event photos.
3. **A midweek student check-in.** Student Night is on the calendar because Cast stories describe it, but students check in only on Sunday (see the roadmap).
