# Cast notes

Hand-written notes on Cast people, one file per household, named for the household's key. Every dataset loads them on the main campus, the same way every time, so tests can find them by Guid. Generated households get generated notes instead (see [content/notes](../../content/notes/README.md)).

Each note ties to the household's story in [cast/households](../households/). Together the files cover every state Rock's notes show: each of the six note types, alerts, private notes, pinned notes, replies, mentions, and the restricted Counseling type (see decision 78).

## Format

```json
{
  "$schema": "../note.schema.json",
  "schemaVersion": 1,
  "household": "foster",
  "notes": [
    {
      "key": "pamela-knee-surgery",
      "guid": "UPPERCASE-GUID",
      "about": "pamela",
      "type": "pastoral",
      "author": "ted",
      "written": { "daysAgo": 40 },
      "time": "10:30",
      "text": "Visited Pamela at home after her knee surgery. {@pete} is bringing dinner Thursday.",
      "isAlert": false,
      "isPrivate": false,
      "isPinned": false,
      "testPurpose": "A pastoral note with a mention and a reply.",
      "replies": [
        {
          "guid": "UPPERCASE-GUID",
          "author": "pete",
          "written": { "daysAgo": 39 },
          "time": "08:15",
          "text": "Thanks, Ted. Dinner is set."
        }
      ]
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `household` | The household's key. Must match the file name. |
| `key` | Unique within the file, in kebab case. |
| `guid` | The note's Guid, uppercase. Tests rely on it. |
| `about` | The member key of the person the note is about, in this household. |
| `type` | A note type key from [configuration/notes.json](../../configuration/notes.json). |
| `author` | A staff key from the configuration's `staff` list. |
| `written` | When the note was written, as `daysAgo` from install day. |
| `time` | The time of day, `HH:mm`, 24-hour. |
| `text` | The note. `{@ted}` (any staff key) becomes a mention of that staff member. |
| `isAlert`, `isPrivate`, `isPinned` | Optional. Default false. A Safety Alert note is always an alert. |
| `testPurpose` | Optional. What the note tests or shows. |
| `replies` | Optional. Replies from staff, each written after the note it answers. |

## Rules

1. A private note is seen only by its author, so its author must make sense as the only reader.
2. A staff member never writes a note about themselves or their own household.
3. A reply comes from a different staff member than the note's author, and is written on or after the note's day.
4. The writing rules in [content/notes](../../content/notes/README.md#writing-rules) apply, except that Cast notes may use the Cast's own names.

`node cast/validate-cast.js` checks every note file against these rules.
