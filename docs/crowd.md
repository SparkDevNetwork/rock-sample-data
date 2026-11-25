# The Crowd and personas

The Cast is hand-designed. Everyone else is generated: the **Crowd** gives Full 7 its volume (about 25,000 people across seven campuses), and **campus companions** give Core 3's two extra campuses their own people, since the Cast stays on the main campus.

## Rules for generated households

1. Generation is seeded, so the same inputs always produce the same people.
2. Household shapes, ages, journeys, and personas follow the calibration profile.
3. Names come from the public-domain name tables in [names/](../names/README.md): a first name for the person's gender and birth decade, and a surname by how common it is. They never depend on photo ethnicity. No generated person shares a full name with a Cast person, and some go by a nickname, such as Bob for Robert. An email is the name a person goes by and their surname, numbered when an earlier person has that address (decisions 357 to 359).
4. Crowd households have no photos. About half of Core 3's companions get photos from the pool (see [Datasets](../datasets/README.md)).
5. Core 3's companions use a more involved connection status mix than the Crowd, set in the dataset manifest, so more of them attend, give, and join groups (decision 142). Core 1 and Core 3 also scale up checks as a payment method, so check batches and check images have enough to show (decision 143).
6. Generated adults have the gaps a real church's records have (decision 396). 74.5% have an email and 67.8% a mobile phone, drawn with each person, so nothing emails or texts someone the church cannot reach. 74.6% have a birth date in Rock, 59.1% a marital status, and 74.4% a home address. These three are facts the person has that the church never recorded: planning still reads them, such as an age for a class or a spouse to give with, but Rock gets none. A person with no birth date in Rock does not register for an event with age limits, and a home Rock does not have is never a small group's meeting place. Generated children and the Cast stay complete. Measured age mixes count only people with a birth date, so validation grades the age mix against source-1's as measured.
7. A child on their own, in a one-person family, is a student aged 12 to 17 (decision 157).
8. Parents are 25 to 54. Adults without children take what is left of each adult band after the parents, so about 39% of them are 18 to 24 (decision 166).
9. A couple is one man and one woman. A single adult's chance of being a woman is set for each age band, so each band's share of women matches the profile (decision 167).
10. When the person installing gives a password, about 30% of active generated adults have a Rock login, and most people have none. Each adult's chance comes from their strongest reason in `configuration/logins.json`, such as the church app, leading, or giving online, and every campus pastor has one (decision 179). See [How logins load](../loader/README.md#how-logins-load).
11. Generated people have the person attribute values a church records, each drawn from its own seeded stream, so no other row changes. About 10% of working-age adults have an employer, and a business contact works at their own business. Kids in half the households have a school, from invented schools near their campus. The kids of two single-parent households per campus have a pickup note, some young kids have the ability level of their nursery room, and households that first came inside the history window have their visit information. See [How person attributes load](../loader/README.md#how-person-attributes-load).
12. Every generated person's mobile and every generated business's work phone is a number no one else has, never a Cast number or one of the church's texting numbers. Numbers come from a seeded draw of the reserved 555-0100 to 555-0199 range: the Phoenix codes 602, 480, and 623 first, then the other US geographic codes in `configuration/phones.json`. The Cast and the church already hold most of the 300 Phoenix numbers, so only the first hundred or so generated numbers are in Phoenix. Core 3's companions are mostly in Phoenix. Most of Full 7's Crowd has codes from outside Phoenix, as many people in a growing metro area keep the mobile number from where they used to live (decision 259).
13. Each campus pastor follows up with six generated adults on their campus who are Connected, Engaged, or Leading, one per household at most, as a Care Team Follow-up signal. One has no end, one ends in the next few weeks, and four ended in the last year, so most profiles show no flag. Each campus draws from its own stream (decision 282). See [How signals load](../loader/README.md#how-signals-load).
14. Some generated people carry the church's tags, drawn by each tag's rules in `configuration/tags.json`, each person from their own seeded stream. A draw never picks a Cast person, so no Cast story gains a tag it does not tell. Homebound holds 20% of the generated adults 80 and older who live alone, at every campus (decision 304). See [Tags](../configuration/README.md#tags).
15. In about 3% of attending families with a child 12 or younger, the children have one adult from another family who may check them in, a Known Relationship with Rock's Can check in role. That adult checks them in on about a third of the family's Sundays, and is in that service (decision 372).
16. About 1% of generated people have a second record, the kind a connection card or a registration makes when it does not find someone: Pending, under the name they go by, with no birth date and the email or mobile they gave. Half stay open in Rock's Duplicate Finder, 15% were set aside, and 35% were merged. A duplicate record takes no part in church life (decisions 397 to 399). See [Duplicates](duplicates.md#the-crowds-duplicates).

## How big the Crowd is

Full 7's manifest (`datasets/full-7.json`) sets how many households each campus gets (decision 136). Household shapes do not change by campus, so the counts alone set each campus's size. Each count stays under that campus's share of the [address pool](../addresses/README.md), with room for its businesses.

| Campus | Generated households | People, with the Cast and businesses |
|---|---|---|
| Phoenix | 3,450 | 5,370 |
| Chandler | 2,350 | 3,518 |
| Gilbert | 2,300 | 3,436 |
| Peoria | 2,250 | 3,392 |
| Scottsdale | 2,200 | 3,312 |
| Goodyear | 2,150 | 3,224 |
| Tempe | 2,100 | 3,193 |
| **All seven** | **16,800** | **25,445** |

Phoenix, the original and largest campus, also holds most of the Cast. Each other campus holds its campus pastor's family (decision 199). People are counted by primary campus, at the default seed. They include 149 Pending duplicate records, each in a family of its own (decision 397).

## Personas

Personas are behavior templates. Each household member gets one or more. The numbers inside each persona come from the calibration profile.

| Kind | Personas |
|---|---|
| Giving | One time, occasional, weekly, bi-weekly or twice monthly, monthly, quarterly, irregular, year-end, lapsing, new |
| Attendance | Core, regular, occasional, holiday-only, lapsing |
| Engagement | Group member, group leader, serving, serving lead |

Each giving persona defines its rhythm, amount pattern (steady or variable, round-number tendency), payment method mix, and scheduled versus one-time mix.

The generators live in the loader, so the Crowd is made fresh at install time: `loader/generation/CrowdGenerator.cs` makes the households, and the loaders in `loader/loading/` turn personas into attendance and gifts.
