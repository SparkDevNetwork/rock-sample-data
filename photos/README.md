# Photos

An image model makes the people photos from the [photo brief](brief.md). This folder holds the photo rules, the delivered photos, and the manifest the builder reads.

[Campus exteriors](campuses/README.md) show all seven Rock Solid Church locations ready for Sunday morning without people. The set includes a high school auditorium and a traditional church campus. It is available in the public site's campus cards and downloads; campus images are not yet installed into Rock by the loader.

Reusable campaign artwork belongs in [graphics/initiatives/](../graphics/README.md), with one folder per initiative. The Starting Point campaign master lives there; its installed lobby appearance remains in [the church reference pack](references/church/README.md).

## Kinds of images

| Kind | Rock field | Source |
|---|---|---|
| People | `Person.PhotoId` | AI-generated portraits, matched to each person's photo ethnicity, gender, and age |
| Staff photos | The Staff Photo person attribute, a file of Rock's Unsecured type | AI-generated outdoor photos of each of the 23 staff at the Phoenix campus entrance, made from their profile photos, from the [staff photo brief](staff-brief.md) (decision D-staff-4). All 20 share one look, so they line up on a staff page. |
| Cast small groups | `Group.PhotoId` | AI-generated group photos of each group's real members, made from their profile photos, from the [group photo brief](group-brief.md) (decision 56) |
| Serving opportunities | `ConnectionOpportunity.PhotoId` | AI-generated photos of Phoenix's Cast volunteers doing each job, made from their profile photos, from the [opportunity photo brief](opportunity-brief.md). They replace the three photos Rock ships. |
| Calendar events | `EventItem.PhotoId` | AI-generated 2:1 images of each event on the church calendar from the [event image brief](event-brief.md): titled designs for most events, and photos with anonymous people for the rest (decisions 176 and 184) |
| Scenes | Other groups' `Group.PhotoId`, connection opportunities, campuses, accounts, LMS courses | A small themed library (small group, serving team, youth night, building), with faces small or in the background |
| Scanned checks | `FinancialTransactionImage` | Drawn at install from fictional check templates, for the months each [dataset](../datasets/README.md) sets |

## Photo style

People photos look like casual social media profile pictures, not studio portraits or a church directory. Every new people photo, now and in the future, follows these rules:

1. **Varied settings.** Each photo has its own everyday setting, about half outdoors (a trail, a park, a backyard, a ballgame) and half indoors (a kitchen, a coffee shop, a couch, a restaurant). The [photo brief](brief.md) has a list of ideas.
2. **Real-life feel.** It looks like a phone photo a friend took. Lighting follows the setting. Framing can be slightly off-center and unposed.
3. **Head and shoulders.** The face is clearly visible, with room above the head, because Rock shows photos as circles.
4. **Families look related, but not matched.** Each family member has a different setting, as if each person chose their own profile picture.
5. **Everyday clothing.** Modern clothes that fit the person's age. No costume, traditional, or religious dress unless a Cast file asks for it.
6. **No studio look.** No plain backdrops, no matching lighting across a family, and nothing that reads as a headshot session.

The 45 legacy Cast people have approved replacement portraits, made from their original photos so long-time Rock users still recognize them (decision 32).

## Photo ethnicity

Each Cast adult has a `photo` block with a photo ethnicity. It exists only to generate matching family photos.

1. It is never written to Rock (not to race, ethnicity, or any attribute), and it never affects names.
2. Children blend their parents unless their own value is set (for example, adoption).
3. It stays after photos exist, so a photo can be regenerated from the same inputs.
4. About 15% of couples are mixed: about 20% under 35, 14% from 35 to 54, and 8% from 55 up.
5. The overall mix is broader than the Phoenix area.

The field is `photo.ethnicity` (decision 29 in [docs/decisions.md](../docs/decisions.md)). It uses ten broad regional groups, not country-level ones: White, Black, Hispanic or Latino, East Asian, Southeast Asian, South Asian, Middle Eastern or North African, Native American, Pacific Islander, and Multiracial. [ethnicities.json](ethnicities.json) holds the prompt wording, Crowd weights, and mixed-couple rates, so they are tuned in one place.

## Photo manifest

[manifest.json](manifest.json) names the image model at the top (`model`) and lists every delivered photo in `images`. Each entry has:

| Field | Description |
|---|---|
| `file` | Path under `photos/`, for example `cast/greggs-tim.jpg` or `pool/core3-0001.jpg` |
| `set` | `cast` or `core-3-pool` |
| `household`, `member` | Cast photos only: the household key and member key |
| `ethnicity` | One of the groups above |
| `gender` | `male` or `female` |
| `age` | The age the photo shows |
| `ageBand` | Pool photos only: 0-2, 3-5, 6-11, 12-17, 18-29, 30-44, 45-64, or 65+ |
| `seed` | The generation seed when the tool gave one, else `null` |
| `prompt` | The exact prompt used |

`node cast/validate-cast.js` checks that every entry has a 400-pixel JPEG and an original, that Cast entries match their person, and warns about Cast people who still need a photo.

## How the builder picks photos

1. Cast people have a pinned delivered photo, so tests and docs stay stable. People with `photo.isOmitted` get none.
2. Core 3 companions draw from a shared pool by photo ethnicity, gender, and age band, never reusing a face within a family or campus. Full 7 crowd households get no photos.
3. Photos are pre-sized JPEGs (400 pixels square, about 50 KB) stored with Rock's database storage provider.

## Coverage

| Set | Coverage |
|---|---|
| Cast (in every dataset) | Every adult and child, except a few omitted on purpose (duplicate records and a deceased child). The 45 legacy portraits have approved replacements. Businesses get no portrait. |
| Staff photos | Each staff member in the dataset whose photo is delivered and listed in `staff/manifest.json`. A campus pastor's photo is in the datasets with their campus. None are delivered yet; the [staff photo brief](staff-brief.md) asks for all 23. |
| Core 3 companions | About half, from a pool of 120 photos. |
| Cast small groups (in every dataset) | All 12, set as each group's photo and shown on Group Finder cards. Core 3's companion groups get none. |
| Serving opportunities (in every dataset) | All 8, set as each opportunity's photo and shown on the public Serve page. |
| Calendar events (in every dataset) | Each event listed in `events/manifest.json` gets its photo. None are made yet; the [event image brief](event-brief.md) asks for all 51. |
| Full 7 crowd | None. Full 7 shows the Cast photos only. |

## Generating the photos

The [photo brief](brief.md) is the complete brief for the image model. The builder's `--deliver-photos` command turns its images into delivered photos.

| Path | What it holds | In git? |
|---|---|---|
| `cast/` | Delivered Cast photos, `{household-key}-{member-key}.jpg` | Yes |
| `pool/` | Delivered Core 3 pool photos, `core3-0001.jpg` and up | Yes |
| `manifest.json` | One entry per delivered photo, with the exact prompt | Yes |
| `ethnicities.json` | Prompt wording, Crowd weights, and mixed-couple rates | Yes |
| `brief.md` | The brief for the image model | Yes |
| `group-brief.md` | The brief for the Cast small group photos. `node photos/build-group-brief.js` rewrites its member lists | Yes |
| `groups/` | Delivered group photos, `{group-key}.jpg`, and their `manifest.json` | Yes |
| `opportunity-brief.md` | The brief for the serving opportunity photos | Yes |
| `opportunities/` | Delivered opportunity photos, `{team}.jpg`, and their `manifest.json` | Yes |
| `event-brief.md` | The brief for the calendar event images: sizes, the design and photo looks, rules, and one section per event | Yes |
| `events/` | Delivered event photos, `{event-key}.jpg` at 1536 by 768, and their `manifest.json` | Yes |
| `staff-brief.md` | The brief for the 23 staff photos: one shared outdoor look at the Phoenix entrance, 1536 by 1536, with a 3:1 crop check | Yes |
| `staff/` | Delivered staff photos, `{household}-{member}.jpg` at 1536 by 1536, and their `manifest.json` | Yes |
| `campuses/` | Seven distinct campus exterior JPEGs, their manifest, and reuse guide | Yes |
| `originals/campuses/` | Full-size original campus exterior PNGs | Yes |
| `originals/` | The image model's full-size originals of both, same names, in the format the model made (JPEG or PNG) | Yes |

## Installing staff photos

Jon's image agent makes the staff photos outside the repository, from the [staff photo brief](staff-brief.md), and hands back a folder of JPEGs with a manifest and a handback note. A Rock sample data session installs them:

1. **Check each image** against the brief's check list, and that it is a 1536 by 1536 JPEG named `{household}-{member}.jpg` for a staff member in `configuration/staff.json`.
2. **Copy the files.** Each JPEG goes to `photos/staff/` under the same name. If the agent handed back a larger original, it goes to `photos/originals/staff/` under the same name (in its own format); otherwise copy the JPEG there too.
3. **List each one** in `photos/staff/manifest.json`, with `file` (such as `staff/foster-pete.jpg`), `household`, `member`, and the agent's `reference`, `place`, `seed`, and `prompt`. Set `model` to the model the agent named.
4. **Run** `node cast/validate-cast.js` (0 errors, and the staff photo warning gone once all 20 are in) and `node site/build.js`.

No code changes when they arrive. The builder packs every listed photo into the datasets that have that person, and the loader's `Photos` part writes each one as a file of Rock's Unsecured type, stored in the database and not temporary, and sets the person's Staff Photo to it. A staff member with no listed photo loads with an empty Staff Photo.
