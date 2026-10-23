# The Crowd and personas

The Cast is hand-designed. Everyone else is generated: the **Crowd** gives Full 7 its volume (about 25,000 people across seven campuses), and **campus companions** give Core 3's two extra campuses their own people, since the Cast stays on the main campus.

## Rules for generated households

1. Generation is seeded, so the same inputs always produce the same people.
2. Household shapes, ages, journeys, and personas follow the calibration profile.
3. Names come from public-domain frequency data and never depend on photo ethnicity.
4. Crowd households have no photos. About half of Core 3's companions get photos from the pool (see [Datasets](../datasets/README.md)).
5. Core 3's companions use a more involved connection status mix than the Crowd, set in the dataset manifest, so more of them attend, give, and join groups (decision D-core3-1). Core 1 and Core 3 also scale up checks as a payment method, so check batches and check images have enough to show (decision D-core3-2).

## Personas

Personas are behavior templates. Each household member gets one or more. The numbers inside each persona come from the calibration profile.

| Kind | Personas |
|---|---|
| Giving | One time, occasional, weekly, bi-weekly or twice monthly, monthly, quarterly, irregular, year-end, lapsing, new |
| Attendance | Core, regular, occasional, holiday-only, lapsing |
| Engagement | Group member, group leader, serving, serving lead |

Each giving persona defines its rhythm, amount pattern (steady or variable, round-number tendency), payment method mix, and scheduled versus one-time mix.

The generators live in the loader, so the Crowd is made fresh at install time: `loader/generation/CrowdGenerator.cs` makes the households, and the loaders in `loader/loading/` turn personas into attendance and gifts.
