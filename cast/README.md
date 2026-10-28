# The Cast

The Cast is the set of hand-designed households: today **87 households and 185 people**. They appear in every dataset, always on the main campus, with the same Guids. Tests and documentation may rely on them.

**[Meet the Cast](families/README.md)** for a page about every family, with photos, backstories, and what each family tests.

## Rules

1. Keep Rock Solid Church's legacy people (for example, the Decker family) and their Guids.
2. Every household has a **test purpose**: the features or edge cases it exists to exercise.
3. Guids never change. Changing a Cast member's data can break tests, so review it like a code change.
4. Dates are relative to the anchor date, never fixed dates.

## Files

| File | Purpose |
|---|---|
| `households/*.json` | One file per household. Format: `household.schema.json`. These are the source of truth. |
| `notes/*.json` | Hand-written notes on Cast people, one file per household. Format: `note.schema.json`. See [Cast notes](notes/README.md). |
| `staff-emails.json` | The Cast staff's hand-written personal emails to Cast people, tied to their stories. See [staff-emails.md](staff-emails.md). |
| `sms-threads.json` | The Cast's hand-written text conversations, covering every SMS Conversations state, with several always from the last few days. See [sms-threads.md](sms-threads.md). |
| `prayer-requests.json` | The Cast's own prayer requests, tied to their stories, covering every prayer screen state, with at least 8 always current. Format: `prayer-requests.schema.json`. |
| `groups/*.json` | One file per Cast group. Group membership lives here only. Format: `group.schema.json`. Small groups are plans written by the builder; see [Small groups](#small-groups). |
| `families/*.md` | Generated family pages. Never edit them; rerun `build-pages.js`. |
| `INVENTORY.md` | What came over from the legacy sample data, what was added, open issues, and remaining gaps. |
| `validate-cast.js` | Checks required fields, Guids, cross-references, statuses, and photo rules. Run `node validate-cast.js`. |
| `build-pages.js` | Builds the family pages. Run `node build-pages.js` after any Cast change. |

## Dates

A date is either `yearsAgo` plus a month and day (counted back from the anchor year) or `daysAgo` (counted back from the anchor date, for recent events). A birthday is an age plus a month and day. This keeps holidays on the right day and ages constant.

## Status rules

A person's connection status must match their data. The validator warns when it does not:

- **Leading** people lead at least one current Cast group.
- **Engaged** people are in a current small group, on a serving team, or in a household that gives.

A current small group membership is an active member of an active group. Former members and pending requests do not count.

## Small groups

The Cast's small groups follow the pattern in [Small groups](../docs/small-groups.md). Do not edit these files by hand. Change a household or `configuration/small-groups.json`, then run the planner from `builder/`:

```
dotnet run -- --plan-cast-groups
```

It rewrites every small group file and marks each new leader Leading in the household files. A second run changes nothing. Then run `node validate-cast.js` and `node build-pages.js`.

- Legacy groups keep their keys and Guids. Pins in the configuration keep Ted Decker, Bill Marble, Pete Foster, and Bob Greggs as leaders, and keep the Greggs and Gilbert groups in those homes.
- Groups in `cast.notInPattern` (Alisha Marble's group and the university group) keep their old shape and are not loaded.
- Each member has a `role` (Leader, Host, or Member) and a `state`: Active, Inactive or Archived after leaving (with a `left` date), or Pending for a request to join. General groups still mark leaders with `isLeader`.
- A household with `duplicateOf` is a duplicate record of another household. Duplicate records never join groups.
- People with an inactive record (the Murphys, who moved) appear only as former members of inactive or archived groups.

## Serving teams and requests to serve

Each of the main campus's eight serving teams has a roster in `groups/serving-{team}.json`: the team key from `configuration/serving.json`, the team's Guid, and its members, each a Leader or a Member. These rosters are hand-picked, so edit them directly; `validate-cast.js` checks each team key, name, and single leader. The loader adds each volunteer's rotation, preferences, and serving history. Generated households fill the teams at every campus.

A person's `connectionRequests` in their household file tell their story of asking to serve: the team, the state and status, when they asked, whether the team leader is the connector, and an optional background check result. The loader adds the activity history. See [Serving](../docs/serving.md).

A staff person's `connects` rule names the active request statuses they connect at their campus instead of the team leader, and an optional `minimum`. Alisha Marble connects the main campus's No Contact and In Progress requests, at least 8 in every dataset (decision D-admin-2). `validate-cast.js` checks that the Cast's own requests reach the minimum, so a Cast change cannot leave her board thin.

## Rock's admin person

Exactly one Cast person sets `replacesAdmin`: Alisha Marble. The loader writes her over the admin person a new Rock creates (Person Id 1) instead of adding her, so the `admin` login signs in as her. She keeps Rock's Id, Guid, login, roles, and empty ForeignKey, so her Rock Guid is not the Guid in her household file, and she lists no logins of her own (decision D-admin-1). The Cast expectations look her up through Person 1. `validate-cast.js` checks there is exactly one, an active adult in Rock Administrators.

## Bios and photos

- A person's `bio` is a short backstory for documentation and test writers. Featured families have one for each person. Bios are never written to Rock.
- The `photo` block holds `ethnicity` (adults; children inherit), `isOmitted` (no photo on purpose), and `seed`. See [Photos](../photos/README.md).

To add a household, follow [Maintaining](../docs/maintaining.md).
