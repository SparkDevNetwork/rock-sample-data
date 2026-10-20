# Prayer text pools

The text for generated prayer requests, answers, and prayer team comments. The loader builds each request from these pools at install time. Everything here follows the [content standards](../../docs/content-standards.md).

## Files

| File | What it holds |
|---|---|
| `categories/<key>.json` | One file per prayer category with weight above 0 in [configuration/prayer.json](../../configuration/prayer.json). The key matches the category's key. |
| `comments.json` | Prayer team comments and requester replies, used on any request. |
| `shared.json` | Closing lines that can end a request in any category. |
| `check-pools.js` | Checks every pool file. Run `node content/prayer/check-pools.js` after any change. |

## Category files

```json
{
  "$comment": "Text pool for the General prayer category.",
  "requests": [ "..." ],
  "details": [ "..." ],
  "answers": [ "..." ]
}
```

1. **requests**: the main text of a request, written by the person asking. Each one stands alone.
2. **details**: one or two more sentences that can follow any request in the category, for example "It has been a long few months for our family." The loader adds some to make longer requests.
3. **answers**: an update from the requester, saved as the request's answer. Each one must fit any request in the category, so it never names a detail that could contradict the request (no "the surgery went well" in a category where the request may not be about surgery).

A request's text is one request, then zero or more details, then sometimes a closing from `shared.json`. The loader picks how many details so the lengths match the profile (`prayer.detail.textLengthP10`, `P50`, and `P90`).

## Comment file

```json
{
  "$comment": "...",
  "team": [ "..." ],
  "requesterReplies": [ "..." ]
}
```

1. **team**: short encouragements from prayer team members. Each fits any request.
2. **requesterReplies**: short thanks or updates from the person who asked.

## Writing rules

1. **Plain, warm, and real.** Write the way church members really write on a prayer card or an online form: some short, some long, some formal, some casual. Vary the opening words. Do not start most entries with "Please pray."
2. **No personal names.** Use relationships instead: "my mom," "a coworker," "our neighbor," "my son's best friend." This also keeps the text away from the Cast's real stories.
3. **Nothing real.** No real businesses, hospitals, schools, churches, or people. Cities and states are fine ("Phoenix," "back in Ohio"). No real news events, dates, or disasters by name.
4. **No politics.** No parties, candidates, laws, or issues. Prayers for a nation or its leaders ask for wisdom, peace, and unity only. Persecuted church requests name no country.
5. **Gentle with hard things.** Illness, grief, addiction, divorce, and money trouble appear because churches pray about them. Describe them plainly and kindly, with no graphic medical or personal detail. No self-harm.
6. **Scripture is rare and public domain.** Quote only the World English Bible or the King James Version, and name it, for example "(Psalm 46:1, WEB)."
7. **Plain characters.** Straight quotes and apostrophes. No em dashes or en dashes; use a comma or a period. No emoji.
8. **Every entry is different.** No two entries in a file share their first five words.

## Lengths

| Pool | Characters |
|---|---|
| requests | 40 to 450, mostly 120 to 300 |
| details | 50 to 260 |
| answers | 30 to 220 |
| team comments | 15 to 200 |
| requester replies | 20 to 220 |
| closings | 10 to 80 |
