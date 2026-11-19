# Staff homepage

Status: built for [issue 9](https://github.com/SparkDevNetwork/rock-sample-data/issues/9). Decisions D-homepage-1 to D-homepage-5 record it.

**The idea in one line:** when staff sign in, Rock's homepage looks like a church that uses it every week.

## What Rock shows

Rock's staff homepage is the Internal Homepage of the Rock RMS site. Its Internal Communication View block shows one item from the Internal Communications - Homepage content channel: the newest approved item whose start date has passed. The item has a feature, with an image, a title, text, and a link, and an Internal Article List of one to three articles, each with an image, a title, and text. Below them the block shows its metric tiles. Staff can page back to older items.

A new Rock shows only its 2018 item, "Let The Adventure Begin", and tiles with values from 2020. The Quick Links block beside it has Rock's own links.

## The weekly updates

Every Monday for the last 52 weeks, Daniel Peak, the Director of Communications, posts a staff update at 8 AM. The channel stores a date with no time, so each update starts at midnight Monday, as Rock's editor saves it. The newest update is this week's, or last week's when the install is on a Monday before 8 AM.

Each update is one feature and one to three articles, from [content/internal-news/updates.json](../content/internal-news/README.md):

1. **A feature** from Daniel. Dated features follow the church calendar: Easter planning six weeks out, Easter volunteers, Holy Week, the Easter recap, Baptism Sunday, VBS volunteers and VBS week, Student Camp, the mission trip, Serve Sunday, each group launch, the four weeks to Commitment Sunday and the thanks after it, the fall festival, Thanksgiving, Christmas Eve planning, the staff Christmas party, Christmas week, the staff retreat, and year-end giving statements. They fall on the same dates the rest of the data uses. Other Mondays take an evergreen feature, such as Sunday Wins, Rock office hours, or a weekend debrief, each used once before any repeats.
2. **Articles** from other staff. Ray Delgado writes about facilities, Priya Raman about giving statements and the budget, and Rachel Brooks about kids check-in, with others from groups, students, worship, production, care, and the front office. Dated articles come first, then evergreen ones fill the update, one per author. Each article's image is its author's staff photo.

Every feature links to the Rock Sample Data site, https://sampledata.rockrms.com. Rock's own 2018 item becomes the oldest update in place, and its old images are removed, as Rock removes an image no value names any longer.

Feature images are approved calendar event images. Photos without text go with general features, and an event's own design goes with its own feature, such as the Easter design for Easter planning. Each image is stored once and shared by every update that shows it, which Rock allows: it deletes an image file only when no attribute value names it any longer.

The updates are the Content part, which issue 10's sermon content will share. They do not need the Calendar part: the loader plans the calendar's dates on its own.

## The metric tiles

Rock's block shows three metrics Rock ships, and the church adds the four Ministry Path metrics after them.

| Metric | Rock's rule | Schedule | History |
|---|---|---|---|
| Active Records | Active person records | Daily at 1 AM | A value each day for a year |
| Active Families | Families of active people, by campus | Mondays at 4 AM | A value each Monday for a year, one per campus |
| Active Connection Requests | Requests in the Active state | Daily at 1 AM | A value each day for a year |

Each value is the one Rock's Calculate Metrics job would have saved at that run. Rock keeps no history of who was a record when, so a run counts today's active people whose family had come by that day. A family comes with its first check-in, gift, request, or group membership, and a family with none has always been there. A request counts from the day it was made until the day it was connected or set inactive. Each metric's last run is set, so Rock's job next runs on schedule and does not fill in the past again. Rock's own 2020 values stay.

The Ministry Path metrics are the reporting metrics ([Data views and reports](reporting.md)), so the homepage shows how many people reached each stage this week.

## Quick Links

The church adds four links after Rock's own: Church Calendar, My Connection Requests, Prayer Requests, and Group Scheduler. Rock's own links differ by version: Rock 20.0.10 shows five, with Classic Check-in, and Rock 21.0.2 shows four.

## How it loads

1. **Content** (a part), after Adaptive messages. The loader plans the 52 Mondays, picks each one's feature and articles, stores the images once each, then writes the items, their global keys and slugs by Rock's own rules, their attribute values, and an article matrix per item.
2. **Staff homepage** (always), after Content. The metric history, the path metrics on the block, and the Quick Links. They read whatever rows the load wrote, as the path metrics do, so they run in every load.

The settings are in [configuration/staff-homepage.json](../configuration/staff-homepage.json).

## What is checked

- **`check-news.js`** checks the text and the settings without a database: keys, occasions, approved images, staff authors with photos, lengths, and plain characters.
- **The package self-check** refuses a feature image or an article author the package does not have.
- **`staff-updates-are-complete` (invariant).** Every update has its feature, a stored image, one to three articles with a title and content, a global key, and one slug.
- **`staff-homepage` (verify).** The load wrote an update on each of 52 Mondays in a row. The item Rock's block picks is this week's update, and Rock's own Lava engine renders it with the block's Body Template, showing its feature title and image. Each metric's newest value is its last run, a daily metric's newest value equals Rock's own SQL when that run was today, and no campus's Active Families value is above Rock's count today. The block lists the path metrics, and the Quick Links hold the church's links. Rock 21's article list builds its image links from the web request, which verify does not have, so there the invariant checks the articles.

## What comes next

- **Website content.** Issue 10 adds the church's sermon series to the Content part.
