# Photo brief: create sample data profile photos

You create the profile photos for Rock RMS sample data. You are the image model: read the files named here, make each image, and save it. Everything you need is already filled in. You do not choose anyone's photo ethnicity, age, or gender. Read this whole page before you start. All paths are from the root of the `rock-sample-data` repository.

Every current Cast person and the 120-photo Core 3 pool already have photos. Use this brief when new Cast people are added, or when the pool needs to grow. `node cast/validate-cast.js` warns about each Cast person who still needs a photo.

## What to make

| Set | Who | Make a photo? |
|---|---|---|
| Cast | Cast people with no entry in `photos/manifest.json`, no `photo.isOmitted`, and `recordType` not `business` | **Yes** |
| Cast, omitted | Cast people with `photo.isOmitted: true` | **No.** They have no photo on purpose. |
| Businesses | `recordType: business` | **No.** |
| Core 3 pool | Generated companion households (not in any file) | **Only when asked**, to grow the pool the builder draws from |

## Where the inputs are

Every Cast person is in `cast/households/*.json`, under `members`. For each person, use:

| Field | Use |
|---|---|
| `photo.ethnicity` | The photo ethnicity. Look up its prompt wording in `photos/ethnicities.json` (`groups[].prompt`). Adults always have one. |
| `birth.age` | Age in the photo. |
| `gender` | `male`, `female`, or `unknown`. |
| `familyRole` | `adult` or `child`. |
| `photo.isOmitted`, `recordType` | Decide whether to make a photo (see the table above). |

**Children** usually have no `photo.ethnicity` of their own. Use their parents' value: the adults (`familyRole: adult`) in the same household. If both parents have the same value, use it. If they differ, use the blended prompt below. If a child has their own value, use it instead.

## Rules you must not break

1. **Never make a photo for anyone who already has one or has `photo.isOmitted`.** Existing Cast photos, including the 45 legacy portraits, are approved and never remade.
2. **No real people.** Never use a real photo as a reference. No face may look like a real or famous person.
3. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`: everyday modern clothing, natural variety within each group, and no costume or traditional dress.
4. **Do not change the Cast files**, except to add `photo.seed` if your tool gives you a seed.
5. **Photo ethnicity goes nowhere else.** Do not add it to file names, captions, or image metadata.

## The prompt

Use this for every photo. Fill in the parts in braces.

```
Casual social media profile photo of a {age}-year-old {subject}, {group prompt}.
Everyday, modern clothing that fits the person's age. Natural, friendly expression.
{setting}. Looks like a real photo a friend took on a phone, not a studio portrait.
Head and shoulders, face clearly visible. No text, logos, or watermarks. Hands out of frame.
```

`{setting}`: a different everyday place for each photo. These should feel like Facebook profile pictures, not studio headshots or a church directory. Mix outdoor and indoor about half and half, and do not repeat a setting within a family. Some ideas:

| Outdoors | Indoors |
|---|---|
| On a hiking trail with desert mountains behind them | In a kitchen at home, warm light |
| At a park on a sunny afternoon | In a coffee shop by a window |
| In a backyard at dusk with string lights | On a living room couch |
| At a lake or beach on vacation | At a restaurant table |
| On a city sidewalk downtown | In a home office or at a desk |
| At a ballgame or outdoor event | At a friend's party |
| In front of a mural or brick wall | In a car's passenger seat (adults only) |
| On a porch or front step | In a library or bookstore |

Let lighting follow the setting: bright sun, golden hour, shade, or warm indoor light. Backgrounds can be in focus or softly blurred, as a phone would take them. Slight off-center framing and a natural, unposed feel are good.

`{subject}`: "boy" or "girl" through age 12, "teenage boy" or "teenage girl" from 13 to 17, "man" or "woman" from 18 up, and "person" when `gender` is `unknown`.

`{group prompt}`: the `prompt` text for the person's photo ethnicity in `photos/ethnicities.json`. For a child whose parents differ, use: `whose parents are {parent A's group prompt} and {parent B's group prompt}, and who resembles both parents`.

**Siblings and parents:** family members should clearly look related, but each photo has its own setting, as if each person picked their own profile picture.

## Growing the Core 3 pool

Only when asked. Pool photos have no family ties, so use single-group prompts only. Keep the pool's current balance:

1. Split new photos across the ten groups by `crowdWeight` in `photos/ethnicities.json`.
2. Split each group about evenly between men and women, or boys and girls.
3. Spread ages across these bands: 0-2, 3-5, 6-11, 12-17, 18-29, 30-44, 45-64, 65+. Weight toward adults 30 to 64. Pick one exact age within the band for each prompt.
4. Number new files after the last one in `photos/pool/`.

## Image format

Make square images, as large as your tool allows. JPEG or PNG in sRGB. Frame the head and shoulders with the face clearly visible and roughly centered, and leave room above the head (Rock shows photos as circles). The delivery command keeps your image untouched as the original, and saves a cropped 400-pixel square copy for Rock.

## Delivering the photos

Save your images anywhere outside the repository. Then write a batch file with one item per image: the image's path, and its manifest entry.

```json
[
  {
    "source": "C:/temp/photos/greggs-tim.png",
    "entry": {
      "file": "cast/greggs-tim.jpg",
      "set": "cast",
      "household": "greggs",
      "member": "tim",
      "ethnicity": "white",
      "gender": "male",
      "age": 10,
      "seed": null,
      "prompt": "the exact prompt you used"
    }
  },
  {
    "source": "C:/temp/photos/pool-121.png",
    "entry": {
      "file": "pool/core3-0121.jpg",
      "set": "core-3-pool",
      "ethnicity": "south-asian",
      "gender": "female",
      "age": 38,
      "ageBand": "30-44",
      "seed": null,
      "prompt": "the exact prompt you used"
    }
  }
]
```

A Cast `file` is `cast/{household-key}-{member-key}.jpg`. The household key is the file name without `.json`, and the member key is the person's `key`. For a child, `ethnicity` is the value the photo used (the parents' value, or `multiracial` for a blend). If your tool gives a seed, record it in the entry and in the person's `photo.seed`; otherwise leave it `null`.

Run the builder's delivery command from the `builder` folder. A relative batch path is read from the folder you run it in, and relative `source` paths are read from the batch file's folder.

```
cd builder
dotnet run -- --deliver-photos C:/temp/photos/batch.json
```

For each item it copies your image unchanged to `photos/originals/` (same name, your image's own format), writes the 400-pixel JPEG to `photos/cast/` or `photos/pool/` with all image metadata removed, and adds the entry to the end of `photos/manifest.json`. It writes nothing if any item is already in the manifest, is not under `cast/` or `pool/`, or has a missing source image.

## Check every image

Look at every photo before you save it. Remake any that fail.

1. The age looks right, within a few years.
2. The gender matches.
3. Children look like they belong with their parents and siblings.
4. No text, watermark, logo, or frame.
5. No distorted faces, extra fingers, or odd teeth.
6. The face does not look like anyone real or famous.
7. Clothing is everyday and modern. Nothing that reads as a costume or stereotype.
8. The set feels like real profile pictures: varied settings, about half outdoors and half indoors, and nothing that looks like a studio or a church directory. Image quality is consistent across the set.

## When you are done

1. Run `node cast/validate-cast.js`. It must show 0 errors, and no Cast person you made a photo for should still be listed as missing one.
2. Run `node cast/build-pages.js` so the family pages show the new photos.
3. Report how many photos you made in each set, how many you remade and why, and anyone you skipped and why.
4. Commit the new files in `photos/cast/`, `photos/pool/`, and `photos/originals/`, plus `photos/manifest.json` and any `photo.seed` values in the Cast files.
