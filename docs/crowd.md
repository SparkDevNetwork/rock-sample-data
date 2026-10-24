# The Crowd and personas

The Cast is hand-designed. Everyone else is generated: the **Crowd** gives Full 7 its volume (about 25,000 people across seven campuses), and **campus companions** give Core 3's two extra campuses their own people, since the Cast stays on the main campus.

## Rules for generated households

1. Generation is seeded, so the same inputs always produce the same people.
2. Household shapes, ages, journeys, and personas follow the calibration profile.
3. Names come from public-domain frequency data and never depend on photo ethnicity.
4. Crowd households have no photos. About half of Core 3's companions get photos from the pool (see [Datasets](../datasets/README.md)).
5. Core 3's companions use a more involved connection status mix than the Crowd, set in the dataset manifest, so more of them attend, give, and join groups (decision 142). Core 1 and Core 3 also scale up checks as a payment method, so check batches and check images have enough to show (decision 143).
6. Every generated person has a birth date. Measured age mixes count only people with a birth date, so validation grades the age mix as shares of all people (decision D-crowd-teens-1).
7. A child on their own, in a one-person family, is a student aged 12 to 17 (decision D-crowd-teens-2).

## How big the Crowd is

Full 7's manifest (`datasets/full-7.json`) sets how many households each campus gets (decision 136). Household shapes do not change by campus, so the counts alone set each campus's size. Each count stays under that campus's share of the [address pool](../addresses/README.md), with room for its businesses.

| Campus | Generated households | People, with the Cast and businesses |
|---|---|---|
| Phoenix | 3,450 | 5,319 |
| Chandler | 2,350 | 3,493 |
| Gilbert | 2,300 | 3,407 |
| Peoria | 2,250 | 3,364 |
| Scottsdale | 2,200 | 3,293 |
| Goodyear | 2,150 | 3,199 |
| Tempe | 2,100 | 3,173 |
| **All seven** | **16,800** | **25,248** |

Phoenix, the original and largest campus, also holds the Cast. People are counted by primary campus, at the default seed.

## Personas

Personas are behavior templates. Each household member gets one or more. The numbers inside each persona come from the calibration profile.

| Kind | Personas |
|---|---|
| Giving | One time, occasional, weekly, bi-weekly or twice monthly, monthly, quarterly, irregular, year-end, lapsing, new |
| Attendance | Core, regular, occasional, holiday-only, lapsing |
| Engagement | Group member, group leader, serving, serving lead |

Each giving persona defines its rhythm, amount pattern (steady or variable, round-number tendency), payment method mix, and scheduled versus one-time mix.

The generators live in the loader, so the Crowd is made fresh at install time: `loader/generation/CrowdGenerator.cs` makes the households, and the loaders in `loader/loading/` turn personas into attendance and gifts.
