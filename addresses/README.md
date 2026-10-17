# Addresses

Sample households live on **real Phoenix-area streets with house numbers that do not exist**. Maps and distances stay accurate, and no real home ever shows a fictional person.

## What is here

| File | What it holds |
|---|---|
| `pool.json` | The address pool: 18,000 addresses (3,600 for Phoenix and 2,400 for each other campus), one per line. Committed. |
| `build-pool.js` | Builds the pool from the source data, and with `--cast` gives Cast households their addresses. |
| `raw/` | Downloaded source data and cached lookups. Git ignores it. |

## Source data

Both sources are public. They stay in `raw/` and are never committed or shipped.

1. **Census TIGER/Line address ranges** for Maricopa County (`tl_2025_04013_addrfeat.zip`, about 22 MB, public domain). Download it from `https://www2.census.gov/geo/tiger/TIGER2025/ADDRFEAT/` and unzip it into `raw/tiger/`. It gives every street segment, its house number range on each side, its ZIP code, and its shape.
2. **Maricopa County Recorder address points**, a public ArcGIS service. `build-pool.js` queries it one street at a time, a few requests at once, and caches each answer in `raw/county/`. A rerun never queries a street twice.

## How an address is made

1. **Distance.** For each campus, draw a distance from the profile's distance-to-campus pattern (`distance` in the resolved `sample-church` profile). The main campus draws farther, by the profile's multiplier.
2. **Place.** Pick a random direction and find the nearest local street (Census road type `S1400`). The profile's share of families who live nearer a different campus is respected.
3. **Residential only.** Keep the block only if the county shows at least 3 real homes (`RES`) on it.
4. **Unused number.** Choose a house number with the block's odd or even parity, inside its range, that is not already used and that no real address has on any street with that name in that ZIP code, whatever its direction or type. ("2512 E Riviera Dr" is never made up when "2512 W Riviera Dr" is real.)
5. **Coordinates.** Interpolate along the street shape, the way the Census geocoder does, and step about 12 meters to the correct side.
6. **City.** Use the county's mailing city for that street.

To limit lookups to the county service (about 1,900 street lookups for the whole pool and the Cast), picks cluster: a pick reuses a block on a street already chosen within half a mile, and each campus can open only so many new streets. The distance pattern still holds, because every pick starts from a point drawn from the distance curve.

## Cast addresses

`node build-pool.js --cast` updates the Cast households:

1. **Legacy households** came with real homes. Each moves to the nearest unused number on the same block, which keeps its neighborhood and distance. The three duplicate Jon Smith records keep sharing one address, because duplicate detection tests depend on it. Other accidental shares (Evans and Simmons, Turner and Richards) are split.
2. **New households** without an address get one near the main campus, from the same streets the pool uses.

It only moves an address that matches a real county address point, so running it again changes nothing.

## Tradeoff

Strict USPS verification (for example, Smarty) reports these addresses as undeliverable. Installs do not run verification, and coordinates ship with the data.

## Rebuilding

```
node build-pool.js
node build-pool.js --cast
node build-pool.js --count
```

`--count` reports how many street lookups a build would need, without querying. Every random choice is seeded, so a rebuild from the same source data gives the same pool.
