# Small groups

How small groups look in the sample data, and why. Every number here lives in [configuration/small-groups.json](../configuration/small-groups.json), so it can be tuned in one place.

## What a group looks like

- **Size.** About 12 members, never fewer than 6 or more than 18. Capacity is 18.
- **Leader.** One leader, a man who is at least 18, out of high school, and already Engaged or Leading. Leading a group makes him Leading. A campus with too few such men for its groups also takes Connected men, then Known men.
- **Members.** Adults only, 18 and older and out of high school (decision D-hs-leaders-1). Spouses join together.
- **Host.** In 60% of groups the leader's spouse is the host. In the rest, another member hosts, and the group meets at that member's home.
- **Name.** The leader's last name plus "Group", for example "Decker Group." When two leaders share a last name, the first name is added: "Ben Jones Group."
- **Topic.** One of ten topics: Sermon Discussion, Book of John, Marriage, Parenting, Men's Bible Study, Young Adults, Prayer, New to Faith, Grief and Loss, and Money and Generosity. A group only gets a topic its members fit: Marriage needs mostly married couples (80%), Parenting mostly parents (60%), Young Adults a median age of 35 or under, and Men's Bible Study only men. Each group gets a short, real description for Group Finder.
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

- **The Cast** joins at high rates so Core datasets have plenty of full, lively groups: about 95% of Leading, 85% of Engaged, 80% of Connected, and 30% of Known adults, and a few Prospects and Participants. That gives Core 1 about 8 active groups.
- **Generated households** (Core 3 companions and the Full 7 crowd) join at the rate the calibration profile measured, which is much lower. A campus too small to fill its test groups at that rate adds the households that came closest to joining, until each group can reach the minimum size. Core 3 gets 6 groups at each companion campus, 24 in all.

## The Cast's groups

The Cast's groups are fixed files in `cast/groups/`, so tests can rely on them. Legacy groups keep their Guids. Ted Decker, Bill Marble, and Pete Foster keep leading their groups, and the Greggs and Gilbert groups keep meeting in those homes.

## Later

Women's groups, leader requirements, and group photos are on the [roadmap](roadmap.md). Alisha Marble's group becomes a women's group then.
