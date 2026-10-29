# Group photo brief: the leaders and their spouses

You create one new photo for each of the Cast's 12 small groups in Rock RMS sample data. You are the image model: read this whole page, make each image, and save it. All paths are from the root of the `rock-sample-data` repository.

Each photo shows **only the group's leader and the leader's spouse**. If the leader is single or widowed, the photo shows just the leader. For example, the Decker Group photo shows Ted and Cindy Decker, and nobody else.

Rock shows the photo on Group Finder cards and on the group's page. It should feel like an invitation: a warm, friendly couple at home who would love to have you join them.

**Delivered 2026-10-28.** All 12 photos are in `photos/groups/`, with their originals in `photos/originals/groups/` and their prompts in `photos/groups/manifest.json` (decision 196). The changes Jon asked for during review are written into this page: no bike or ball at the Deckers', no lemonade at the Lees', closed books at the Hughes', and a closed Bible at Hal's. Use this page to remake a photo, or for a new group's leaders.

The photos before these showed every member of each group. They are kept as download-only assets in [groups-full-members/](groups-full-members/README.md), and no package carries them.

## What to make

12 photos, one per group in [The groups](#the-groups). Make the 8 active groups first. The inactive and archived groups are lower priority, because Group Finder doesn't list them.

## The look

1. **A welcoming portrait, not a stock photo.** The couple sits or stands close together, looks at the camera, and smiles. It looks like a nice photo a friend took for the group's listing, not a studio shot or a church directory.
2. **A hint of small group.** Add one or two small touches, such as a closed Bible, coffee mugs, a plate of cookies, or a couple of extra chairs. An open spot on the couch or an empty patio chair nearby is a nice way to say "there's room for you."
3. **Each group has its own place.** Use the setting listed for each group. Six are outdoors in a backyard or patio, and six are indoors in a home. No two photos may share a room, a yard, furniture, or a view.
4. **Arizona.** The church is in the Phoenix area. Outdoor photos show sun, desert plants, stucco, block walls, and palo verde or citrus trees. Indoor rooms look like real Phoenix-area homes.
5. **Everyday clothing.** Casual, modern clothes that fit each person's age and the season. No church clothes, costumes, or matching outfits.
6. **Faces are clear.** Both people are easy to recognize, with nobody cut off or hidden.

## Matching the people

Every person below has a Cast profile photo. Attach it as a reference image when your tool allows it, and make the person easy to recognize next to it: face, hair, skin tone, build, and age. Give them new clothes, a new pose, and the group's setting. Don't copy the profile photo's background.

Every profile photo was made by an image model. They are fictional people, so you may use them as references.

## Rules you must not break

1. **Only the listed people.** Two people for a couple, one for a single leader. No extra adults, no children, and no pets in the frame.
2. **No real people.** Use only the Cast profile photos named here. No face may look like a real or famous person.
3. **No text.** No words, signs, logos, watermarks, or frames, including on clothing, mugs, and book covers. Bibles and books stay closed with plain covers, so no print shows.
4. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`.
5. **Do not change any other file.** Only replace the files and manifest described in [Saving the photos](#saving-the-photos).

## The prompt

Start from this and fill in the parts in braces. Add each person's profile photo as an image input.

```
Warm, welcoming photo of {a married couple / one person} who lead a church small group, at {setting}.
{One line per person: "{name}, {age}-year-old {man/woman}, matching reference photo {n}"}
They sit close together and smile at the camera, relaxed and friendly, as if inviting the viewer to join them.
A small touch of small group: {a closed Bible, coffee mugs, or an empty chair nearby}.
Looks like a real photo a friend took, natural light for the setting. Both faces are clear.
Everyday, modern clothing. No other people. No text, logos, or watermarks anywhere.
Landscape, with the people centered and some room around them.
```

## Image format

Landscape, 3:2, as large as your tool allows (for example 1536 by 1024). Keep the people in the middle of the frame with some room around them, because Rock crops group photos to fit cards and banners.

## Saving the photos

1. Save your full-size image to `photos/originals/groups/{group-key}.png` (or `.jpg`), replacing the old file.
2. Save a JPEG copy at full size (for example 1536 by 1024), quality 90, sRGB, with image metadata removed, to `photos/groups/{group-key}.jpg`, replacing the old file.
3. Update that group's entry in `photos/groups/manifest.json`: set `people` to the people in the new photo, `madeUp` to an empty list, and `prompt` to the exact prompt you used. Keep the entries in the order of this page.

```json
{
  "file": "groups/decker-group.jpg",
  "group": "decker-group",
  "people": [ "Ted Decker", "Cindy Decker" ],
  "madeUp": [],
  "seed": null,
  "prompt": "the exact prompt you used"
}
```

`{group-key}` is the key in each group's heading. Some keys don't match the group's name, for example the Wright Group's key is `gilbert-group`. Always use the key.

## Check every image

Look at every photo before you save it. Remake any that fail.

1. It shows exactly the listed people, all adults, and nobody else.
2. Each person is easy to recognize from their profile photo.
3. The setting matches the group's setting and differs from every other photo.
4. It feels warm and inviting, with a small touch of small group.
5. No text, watermark, logo, or frame.
6. No distorted faces, extra fingers, odd hands, or odd teeth.

## When you are done

1. Report how many photos you made, and how many you remade and why.
2. Commit the replaced files in `photos/groups/` and `photos/originals/groups/`, and the manifest, on your branch.

## The groups

### Decker Group (`decker-group`)

- **State:** Active. Parenting group.
- **Setting:** Outdoors. A simple backyard in the early evening, a cushioned wood patio loveseat, a small table with two mugs, and desert landscaping along a block wall. No bike, ball, or folding chairs.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Ted Decker | Leader | 38, man | `photos/originals/cast/decker-ted.jpg` |
| Cindy Decker | Spouse | 37, woman | `photos/originals/cast/decker-cindy.jpg` |

### Wright Group (`gilbert-group`)

- **State:** Active. Prayer group.
- **Setting:** Indoors. A cozy living room in warm lamp light, a mid-century couch, a rug, and a coffee table with Bibles, a candle, and mugs.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Jerry Wright | Leader | 74, man | `photos/originals/cast/wright-gerald.jpg` |
| Dot Wright | Spouse | 73, woman | `photos/originals/cast/wright-dorothy.jpg` |

### Greggs Group (`greggs-group`)

- **State:** Active. Grief and loss group, so the mood is gentle and kind.
- **Setting:** Indoors. A bright sunroom full of houseplants, soft afternoon light through tall windows, two wicker chairs, and a side table with tea and a Bible.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Bob Greggs | Leader | 36, man | `photos/originals/cast/greggs-bob.jpg` |
| Lorraine Greggs | Spouse | 36, woman | `photos/originals/cast/greggs-lorraine.jpg` |

### Lee Group (`lee-group`)

- **State:** Active. New to faith group.
- **Setting:** Outdoors. A backyard with a wooden picnic table under a big citrus tree, an empty tabletop, and a gravel yard with desert plants. No lemonade or glasses.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| James Lee | Leader | 40, man | `photos/originals/cast/lee-james.jpg` |
| Sarah Lee | Spouse | 38, woman | `photos/originals/cast/lee-sarah.jpg` |

### Marble Group (`marble-group`)

- **State:** Active.
- **Setting:** Indoors. A large farmhouse-style living room with a big sectional couch, a stone fireplace, wood beams, and Bibles and coffee cups on an ottoman.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Bill Marble | Leader | 55, man | `photos/originals/cast/marble-bill.jpg` |
| Alisha Marble | Spouse | 48, woman | `photos/originals/cast/marble-alisha.jpg` |

### Foster Group (`petes-group`)

- **State:** Active.
- **Setting:** Indoors. A family room den with a worn leather sectional, tall bookshelves, and Bibles and a plate of cookies on the coffee table.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Pete Foster | Leader | 50, man | `photos/originals/cast/foster-pete.jpg` |
| Pamela Foster | Spouse | 50, woman | `photos/originals/cast/foster-pamela.jpg` |

### Rivera Group (`rivera-group`)

- **State:** Active.
- **Setting:** Outdoors. A backyard patio next to a pool, with citrus trees and a stucco wall, a cushioned patio loveseat, and warm evening light.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Mike Rivera | Leader | 44, man | `photos/originals/cast/rivera-michael.jpg` |
| Jen Rivera | Spouse | 43, woman | `photos/originals/cast/rivera-jennifer.jpg` |

### Silva Group (`silva-group`)

- **State:** Active.
- **Setting:** Outdoors. A modern condo balcony at sunset with a city view behind them, two lounge chairs, a small table with coffee mugs, and potted succulents.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Isaac Silva | Leader | 29, man | `photos/originals/cast/silva-isaac.jpg` |
| Natalie Silva | Spouse | 28, woman | `photos/originals/cast/silva-natalie.jpg` |

### Hughes Group (`hughes-group`)

- **State:** Inactive. Lower priority.
- **Setting:** Indoors. An open kitchen and dining area, the couple at a long wooden dining table with closed, plain books and a small pizza box. No notes.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Matt Hughes | Leader | 35, man | `photos/originals/cast/hughes-matthew.jpg` |
| Jess Hughes | Spouse | 34, woman | `photos/originals/cast/hughes-jessica.jpg` |

### Murphy Group (`murphy-group`)

- **State:** Inactive. Lower priority.
- **Setting:** Outdoors. A backyard patio at dusk around a gas fire pit, with string lights overhead and a blanket across their laps.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Patrick Murphy | Leader | 44, man | `photos/originals/cast/murphy-patrick.jpg` |
| Erin Murphy | Spouse | 43, woman | `photos/originals/cast/murphy-erin.jpg` |

### Jenkins Group (`jenkins-group`)

- **State:** Archived. Lower priority.
- **Setting:** Outdoors. The front porch and steps of a ranch-style house at golden hour, the couple sitting together on the top step.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Gary Jenkins | Leader | 66, man | `photos/originals/cast/jenkins-gary.jpg` |
| Barb Jenkins | Spouse | 63, woman | `photos/originals/cast/jenkins-barbara.jpg` |

### Russell Group (`russell-group`)

- **State:** Archived. Lower priority.
- **Setting:** Indoors. A warm study with a leather armchair by a window, a reading lamp, bookshelves, and a closed Bible with no lettering on a side table. Hal is widowed, so he is alone in the photo, smiling and relaxed.

| Name | Role | Age | Profile photo |
|---|---|---|---|
| Hal Russell | Leader | 81, man | `photos/originals/cast/russell-harold.jpg` |
