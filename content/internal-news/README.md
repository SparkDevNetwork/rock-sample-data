# Staff updates

The text of the weekly staff updates on Rock's staff homepage. Every Monday for the last year, Daniel Peak, the Director of Communications, posts an update: one feature and one to three articles from other staff. Who posts, when, and the homepage's other settings live in [configuration/staff-homepage.json](../../configuration/staff-homepage.json). The design is in [Staff homepage](../../docs/staff-homepage.md).

| File | What it holds |
|---|---|
| `updates.json` | The features and articles, dated and evergreen. |
| `check-news.js` | Checks the text and the settings. Run `node content/internal-news/check-news.js` after any change. |

## The pieces

| List | What each piece has | When it runs |
|---|---|---|
| `features` | `key`, `occasion`, `weeksBefore`, `title`, `text` (one to three paragraphs), and `image` | On the Monday `weeksBefore` weeks before its occasion's week |
| `evergreenFeatures` | The same, with no `occasion` or `weeksBefore` | On a Monday with no dated feature, each used once before any repeats |
| `articles` | `key`, `occasion`, `weeksBefore`, `author`, `title`, and `text` (one paragraph) | First in that Monday's update |
| `evergreenArticles` | The same, with no `occasion` or `weeksBefore` | To fill an update to its one to three articles, one per author |

1. **Occasions.** An occasion is one the message library already dates, such as `easter`, `vbs`, `commitment-sunday`, `group-launch`, or `giving-statement`, or `event:` and a calendar event's key, such as `event:staff-retreat`. They fall on the same dates as the rest of the data.
2. **Weeks before.** 0 is the Monday of the occasion's week, and -1 is the Monday after it. For a Sunday occasion, 0 means "this Sunday" and 1 means "a week from Sunday", so write the text to match.
3. **Images.** A feature's `image` is an approved event image's key in [photos/events/manifest.json](../../photos/events/manifest.json). Most designs print their event's title, so a general feature uses a text-free photo, such as `all-staff`, `service-planning`, `rock-office-hours`, `elder-meeting`, or `staff-retreat`. An article's image is its author's staff photo.
4. **Authors.** An article's `author` is a staff member's `household/member` key in [photos/staff/manifest.json](../../photos/staff/manifest.json).

## Writing rules

1. Follow the [content standards](../../docs/content-standards.md): warm, plain, and fictional.
2. Use plain characters only: straight quotes, no em dashes, and never "--". No Lava, HTML, or web addresses: the loader writes the HTML, and every feature links to the Rock Sample Data site.
3. Facts must match the church's setup, such as an event's day and time in [configuration/calendar.json](../../configuration/calendar.json).
4. A title is 5 to 60 characters, and each paragraph is 20 to 250.
5. Name only Cast staff. Never add a new person here.
