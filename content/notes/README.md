# Note text pools

The text for generated notes on people. The loader builds each generated note from these pools at install time. Cast notes are hand-written in [cast/notes](../../cast/notes/README.md) instead. Everything here follows the [content standards](../../docs/content-standards.md).

## Files

| File | What it holds |
|---|---|
| `<type>.json` | One file per note type in [configuration/notes.json](../../configuration/notes.json). The name matches the type's key. |
| `shared.json` | Staff replies and mention lines, used with any type. |
| `check-pools.js` | Checks every pool file. Run `node content/notes/check-pools.js` after any change. |

## Type files

A type file has one section for each audience the type has a weight for. An audience the type gives no weight to has no section.

```json
{
  "$comment": "Text pool for the Follow-up note type.",
  "guest": { "openers": [ "..." ], "details": [ "..." ], "alerts": [ "..." ] },
  "attender": { "openers": [ "..." ], "details": [ "..." ] },
  "child": { "openers": [ "..." ], "details": [ "..." ] }
}
```

The audiences match the weights in the configuration:

1. **guest**: adults who are Prospect, Participant, or Known. Notes are about first visits, follow-up calls, and first steps.
2. **attender**: adults who are Connected, Engaged, or Leading. Notes are about care, serving, groups, life events, and conversations.
3. **child**: anyone under 18. Notes are written by staff about the child, for example check-in, kids ministry, or students.

Each section has:

1. **openers**: the main sentence or two of a note, written by a staff member. Each one stands alone.
2. **details**: one or two more sentences that can follow any opener in the same section. The loader adds zero to four of them.
3. **alerts** (optional): short heads-up notes the loader marks as alerts, for example "Prefers to be called by a middle name." Only the Personal Note file has them. The Safety Alert type marks every note as an alert, so it needs no alerts list.

A note's text is one opener, then zero to four details, then sometimes a mention line from `shared.json`. The loader picks how many details so the lengths match the profile (`personNotes.detail.textLengthP10`, `P50`, and `P90`).

## Tokens

The loader fills in these tokens. Use them instead of names.

| Token | Becomes |
|---|---|
| `{name}` | The noted person's nickname, or first name. |
| `{he}`, `{him}`, `{his}`, `{He}`, `{His}` | The noted person's pronouns, from their gender. |
| `{campus}` | The person's campus name. |
| `{staff}` | Another staff member, as a Rock mention when the loader has Rock's keys, or as plain text. Use it only in `shared.json`'s mention lines. |

## Shared file

```json
{
  "$comment": "...",
  "replies": [ "..." ],
  "mentionLines": [ "..." ]
}
```

1. **replies**: a short reply from another staff member to any note, for example "Thanks. I'll check in with the family this week."
2. **mentionLines**: a closing sentence that brings in another staff member with `{staff}`, for example "Looping in {staff} for the next step."

## Writing rules

1. **Staff voice.** Write the way a pastor, a director, or an office assistant writes a quick note after a call or a visit: plain, specific, and useful to the next staff member who reads it. Some short, some longer. Vary the opening words.
2. **No personal names** except tokens. Use relationships instead: "his wife," "her mom," "their oldest."
3. **Nothing real.** No real businesses, hospitals, schools, churches, or people. Arizona cities are fine. No real news events.
4. **No politics.**
5. **Gentle with hard things.** Illness, grief, job loss, marriage strain, and recovery appear because care notes are about them. Describe them plainly and kindly, with no graphic medical or personal detail. No self-harm and no abuse. Counseling notes stay brief and professional: the topic in general terms, the plan, and the next meeting.
6. **Safety alerts stay calm and practical.** State who may pick up a child, a serious allergy, or a serving restriction, and where to find details. Never say why a court or the church made a restriction.
7. **Plain characters.** Straight quotes and apostrophes. No em dashes or en dashes; use a comma or a period. No emoji.
8. **Every entry is different.** No two entries in a list share their first five words.

## Lengths

| Pool | Characters, before tokens are filled |
|---|---|
| openers | 30 to 220, mostly 60 to 150 |
| details | 30 to 200 |
| alerts | 20 to 120 |
| replies | 20 to 160 |
| mentionLines | 20 to 100 |
