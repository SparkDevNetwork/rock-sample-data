# Cast text threads

The Cast's hand-written SMS conversations, in [sms-threads.json](sms-threads.json). Every load writes them, so SMS Conversations in Rock shows the same threads on every dataset, and tests can rely on them (decision 93). Generated people get their threads from the pools in [content/communications/conversations.json](../content/communications/README.md) instead.

## Format

```json
{
  "$comment": "...",
  "threads": [
    {
      "key": "decker-cindy-ted",
      "person": "<Cast person Guid>",
      "name": "Cindy Decker",
      "number": "ted",
      "story": "Why the thread exists, in one sentence.",
      "state": "unread",
      "messages": [
        { "from": "person", "daysAgo": 2, "time": "18:42", "text": "..." },
        { "from": "staff", "daysAgo": 2, "time": "19:05", "text": "..." }
      ]
    }
  ]
}
```

- **number** is the key of a phone number in [configuration/communications.json](../configuration/communications.json): `main`, `students`, `ted`, `alisha`, or `isaac`.
- **from** is `person` (a text the Cast person sent) or `staff` (a text sent from the number). On a personal number the staff member is the number's owner. On the main line it is Alisha Marble, and on the student line it is Isaac Silva.
- **daysAgo** and **time** date each message back from the install day. Messages are in order, oldest first.
- **state** is what SMS Conversations shows for the thread:
  - `unread`: the person's texts after the last staff text are unread. There must be at least one.
  - `read`: every text the person sent is read.
  - `stopped`: the person's last text is `STOP`, and a staff text within the 60 days before it is the one Rock marks as unsubscribed. Texting is then turned off on their phone.
- The person must have a mobile phone in their household file.

## What the threads cover

Together they cover every SMS Conversations state: unread and read, a long back-and-forth of 12 or more texts, a STOP opt-out, a thread on the student line, and threads on each personal number. At least four are from the last 1 to 3 days before install and unread, so even Core 1 opens to a live inbox.
