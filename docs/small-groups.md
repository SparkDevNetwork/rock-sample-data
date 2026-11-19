# Small groups

How small groups look in the sample data, and why. Every number here lives in [configuration/small-groups.json](../configuration/small-groups.json), so it can be tuned in one place.

## What a group looks like

- **Size.** About 12 members, never fewer than 6 or more than 18. Capacity is 18.
- **Leader.** One leader, who is at least 18, out of high school, and already Engaged or Leading. A Women's Bible Study group has a woman leader, and every other group a man. Leading a group makes the leader Leading. A campus with too few possible leaders for its groups also takes Connected people, then Known people.
- **Members.** Adults only, 18 and older and out of high school (decision 169). Spouses join together, except in a women's or men's group, which a married person joins alone. Everyone is in one group at a time, with two exceptions: a leader from Ready to Lead who stays in their old group, and a Cast person whose story puts them in two groups, such as Alisha Marble and Jenny Michaels.
- **Host.** In 60% of groups the leader's spouse is the host. In the rest, another member hosts, and the group meets at that member's home.
- **Name.** The leader's last name plus "Group", for example "Decker Group." When two leaders share a last name, the first name is added: "Ben Jones Group."
- **Topic.** One of eleven topics: Sermon Discussion, Book of John, Marriage, Parenting, Men's Bible Study, Women's Bible Study, Young Adults, Prayer, New to Faith, Grief and Loss, and Money and Generosity. Two are planned on purpose (decision D-womens-groups-1). About one in six of a campus's active groups study the Women's Bible Study, and at least one when the campus has four or more groups. About one in ten study the Men's Bible Study. Their leaders and members are all women or all men. Every other group gets a topic its members fit: Marriage needs mostly married couples (80%), Parenting mostly parents (60%), and Young Adults a median age of 35 or under. Each group gets a short, real description for Group Finder.
- **Schedule.** Weekly, Sunday through Thursday, never Friday or Saturday. Most meet at 7:00 PM, and about a quarter at 6:00 PM.
- **Meeting style.** 90% meet in person at the host's home, which puts them on Group Finder's map. 10% meet online, with a meeting link at `meet.example.org`.
- **Child care.** About half provide child care. Families with young children usually join groups that do.
- **Structure.** Every group sits under its campus's parent group ("Phoenix Small Groups"), under one "Small Groups" parent. Rock's empty starter sections, Section A and Section B, are removed.

## A real year

Groups meet in seasons, the way most churches run them. This shapes only the attendance records, not the church setup.

- Groups meet September through mid-December and January through May.
- About a third keep meeting through the summer.
- Thanksgiving week, Christmas week, and New Year's week are marked "did not meet."
- Most people join in September and January, when churches push group sign-ups.
- Not every leader takes attendance. About 70% record every week; the rest record about half the time.
- Leaders attend about 95% of meetings, hosts about 90%, and members 47 to 67%, so about 65% of attendance records are "did attend." That sits between the measured rate and a livelier demo church. Spouses usually come together.

## Change over time

Groups have history, so Rock's history screens show real change.

- Groups started 1 to 8 years ago.
- About 20% of members leave each year. Most become inactive members; about 1 in 10 is archived.
- Every change is recorded in group history.

## When a household is in a group

A household's group memberships fit when it came to church (decision D-group-fixes-1).

- A household joins no group until four weeks after it started coming. A newer household can only ask to join, as a pending request.
- A household that stopped coming leaves its groups two to six weeks after its last Sunday, as an inactive member with a left date, and its Small Group step ends that day. A member of a group that ended earlier stays a member of the ended group, as Rock keeps them.

## New groups and new leaders

New groups start on launch Sundays, and most new leaders come from people who are Ready to Lead, so the Lead a Group flow shows people who took the Lead step (decision D-launch-groups-1).

- About a third of a campus's active groups started on one of its last four launch Sundays: the second Sunday of September and the third Sunday of January, from `configuration/communications.json`.
- Two thirds of those are led by someone who was Ready to Lead six weeks before the launch, the day the Lead a Group flow runs. That day they were in another small group, on a serving team, and had an Equip step, and they led nothing. Women can be Ready to Lead too, and some launch a women's group.
- Half of these leaders stay in their old group. The other half leave it the day before they launch.
- The serving and step plans make the rest true. Each of these leaders joined a serving team by half a year before that day, and never leads one. One who was not baptized by then took Foundations in the two years before it (see [Steps](steps.md)).

## Test cases

Each dataset includes groups made for testing Group Finder and group history.

| Case | Main campus | Each other campus |
|---|---|---|
| Full (at capacity) | 2 | 1 |
| Closed to new members (hidden from Group Finder) | 2 | 1 |
| Pending requests waiting on the leader | 3 | 1 |
| Inactive groups | 2 | 1 |
| Archived groups | 2 | 1 |

## How many people are in groups

- **The Cast** joins at high rates so Core datasets have plenty of full, lively groups: about 95% of Leading, 85% of Engaged, 80% of Connected, and 30% of Known adults, and a few Prospects and Participants. That gives Core 1 10 active groups.
- **Generated households** (Core 3 companions and the Full 7 crowd) join at the rate the calibration profile measured, which is much lower. A campus too small to fill its test groups at that rate adds the households that came closest to joining, until each group can reach the minimum size. Core 3 gets 6 groups at each companion campus, 26 in all with the Cast's 14.
- **Registrations** read the small group plan, not its rows: a share of the members on the day of the Community Serve Day signed up for it, and some active groups send two to four women to the Women's Retreat on one registration (decision 209). See [Event registrations](registrations.md#who-registered-kids-and-adults).

## The Cast's groups

The Cast's groups are fixed files in `cast/groups/`, which the builder plans with `--plan-cast-groups`, so tests can rely on them. Legacy groups keep their Guids. The Cast plans only main-campus households, so a campus pastor's family on another campus joins no Cast group.

Pins in `configuration/small-groups.json` hold what Cast stories, notes, and prayer requests say (decision D-group-fixes-2):

- Ted Decker, Bill Marble, Pete Foster, Bob Greggs, Jerry Wright, James Lee, Mike Rivera, and Isaac Silva keep leading their groups, and the Greggs and Gilbert groups keep meeting in those homes.
- James Lee's New to Faith group meets online. Mariah Jackson, the Webbs, and Dan Richards are in the Greggs's Grief and Loss group, the Fischers are in the group at the Gilberts' home, and the Sweeneys are in the Rivera Group together.
- Alisha Marble leads Women of the Bible, a Women's Bible Study, and stays in Bill's Marble Group. Jenny Michaels is in Women of the Bible and Pete Foster's group. They are the Cast people in two groups by design.
- Karen Morgan launched a women's group from Ready to Lead on the fall launch Sunday a year or more back, and stays in Women of the Bible. Cast plans change nothing within a year of install day, so the Cast's launch is never more recent than that.
- The Ellises took the Starting Point Class this fall and have one pending request to the Marble Group, and no other group. Victoria Ramos has one pending request to the Silva Group. The Ortizes left their group five weeks after their last Sunday.

A Cast expectation checks every pin on every load: the pinned leader leads the group, and each pinned household's adults are in it or have asked to join.

A group photo loads only when everyone in it is a member, so a women's or men's group never shows the leader's spouse (decision D-womens-groups-2).

## Later

Leader requirements are on the [roadmap](roadmap.md).
