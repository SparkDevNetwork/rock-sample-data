# Adaptive messages

The text of the church's adaptive messages. An adaptive message has several versions, called adaptations, and Rock shows each person the first one that fits them. Which adaptations a message has, their order, and who each one is for live in [configuration/adaptive-messages.json](../../configuration/adaptive-messages.json). The words live here. The design is in [Adaptive messages](../../docs/adaptive-messages.md).

| File | What it holds |
|---|---|
| `messages.json` | Each adaptation's text, by message key and adaptation key. |
| `homepage.lava` | The Lava of the External Website homepage block that shows each person their cards. The loader fills in the category's Id. |
| `check-messages.js` | Checks the text, the configuration, and `photos/messages/manifest.json` when it exists. Run `node content/adaptive-messages/check-messages.js` after any change. |

## The fields

Each field fills one of the attributes Rock gives every adaptation, so the text shows in Rock's own adaptation editor.

| Field | Rock attribute | Rules |
|---|---|---|
| `callToAction` | Call To Action Text | 3 to 30 characters. A short verb phrase, such as "Find a group". |
| `link` | Call To Action Link | One of the routes Rock ships on its External Website (`/Give`, `/GroupFinder`, `/MyAccount`, `/watch`, `/learn`, `/connect/get-connected`), or `page:<key>` for a page in the configuration's `pages`. The loader writes a page as Rock's `/page/<Id>` link. |
| `summary` | Summary | 40 to 160 characters. One or two sentences a card can show. |
| `details` | Details | 1 to 3 paragraphs of 20 to 200 characters. The loader writes them as Rock's structured content, one paragraph block each. |
| `image` | Summary Image | Optional. The event key of an approved image in [photos/events/manifest.json](../../photos/events/manifest.json). The loader stores a copy for the adaptation. Leave it out for an adaptation that gets its own image from the [message image brief](../../photos/message-brief.md), which Jon's image agent lists in `photos/messages/manifest.json` by `message` and `adaptation`. An adaptation has one or the other, never both. Never generate an image here. |

## Writing rules

1. Follow the [content standards](../../docs/content-standards.md): warm, plain, and fictional.
2. Use plain characters only: straight quotes, no em dashes, and never "--".
3. No Lava, braces, or full web addresses. Rock resolves Lava only in the call to action, and the text does not need it.
4. Facts must match the church's setup, such as a class's day and length in [configuration/calendar.json](../../configuration/calendar.json).
5. In one message, no two summaries start with the same five words.
6. A giving message never guilts anyone. The Lapsed or Former layer thanks people for being part of the church.
