# Cast staff emails

Personal emails from Cast staff to Cast people, tied to their stories, in [staff-emails.json](staff-emails.json). Every load writes them, so a Cast person's Communication History shows the same personal notes on every dataset. Generated people get one-to-one messages from the data instead: serving confirmations and reminders, connection follow-ups, prayer, and first-visit welcomes (see [Communications](../docs/communications.md#one-to-one-messages)).

## Format

```json
{
  "$comment": "...",
  "emails": [
    {
      "key": "ted-ellis-welcome",
      "from": "ted",
      "to": "<Cast person Guid>",
      "name": "Hannah Ellis",
      "story": "Why the email exists, in one sentence.",
      "daysAgo": 40,
      "time": "10:15",
      "subject": "So glad you're here",
      "body": [ "Hi Hannah,", "...", "Ted" ]
    }
  ]
}
```

- **from** is a staff key in [configuration/notes.json](../configuration/notes.json): `pete`, `ted`, `alisha`, `daniel`, or `isaac`. The email comes from that staff member by name and from their email address.
- **to** is a Cast person with an email that is active and allows email.
- **daysAgo** and **time** date the email back from the install day, inside the last two years.
- **body** is the email's paragraphs, plain text. The first is the greeting and the last is the signature.
