# Scene photo brief: create small group scene photos

You create 12 scene photos for small groups in Rock RMS sample data. You are the image model: read this page, make each image, and save it. Everything you need is here. Read this whole page before you start. All paths are from the root of the `rock-sample-data` repository.

## Why these photos exist

The Cast's 12 small groups already have photos of their real members (see [the group photo brief](group-brief.md)). Core 3 also has 12 small groups at its two companion campuses, Peoria and Gilbert. The loader picks those groups' members on install day, and the members change with the date, so no photo can show who is really in them.

These scene photos fill that gap. Each one shows a small group meeting, with **no clear faces**, so it never contradicts the members' profile photos. The loader gives each companion group one scene photo on install day. Rock shows it on Group Finder's cards and on the group's page.

## What to make

Twelve photos, `scene-01` through `scene-12`, one for each setting in [The scenes](#the-scenes). Each one looks like a moment from a small group meeting that a member snapped on a phone, or a casual photo the group would post to invite new people.

## The look

1. **No clear faces.** This is the most important rule. People are seen from behind, from the side with their face turned away, cropped at the shoulders, out of focus in the background, or small in a wide shot. Hands, laps, open Bibles, mugs, and the table can fill the frame. Nobody looks at the camera.
2. **A real group.** It still reads as several adults together: knees and shoulders around a coffee table, a circle of chairs, hands passing a plate, someone mid-gesture while talking. Usually 4 to 10 people, at least partly in frame.
3. **Bibles are around.** Open Bibles on laps, on the table, or in hands. Not everyone has one. Coffee mugs, snacks, a notebook, a pen, or a phone can be there too.
4. **Every scene has its own place.** Use the setting listed for each scene. No two photos may share a room, a yard, furniture, or a view, and none may copy a Cast group photo in `photos/groups/`. About half are outdoors and half are indoors.
5. **Arizona.** The church is in the Phoenix area. Outdoor scenes show sun, desert plants, stucco, block walls, and palo verde or citrus trees. Indoor rooms look like real homes there.
6. **Everyday clothing.** Casual, modern clothes. Mixed ages from young adults to seniors, men and women, and a natural mix of skin tones across the set. No church clothes, costumes, or traditional dress.
7. **Adults only.** No children, even in the background.
8. **Warm and welcoming.** Natural light for the setting. It should make someone want to join.

## The scenes

| Scene | Indoors or out | Setting and moment |
|---|---|---|
| `scene-01` | Indoors | A living room shot from behind the couch: the backs of three heads on the couch, others across a low coffee table cropped at the chest, open Bibles and mugs on the table. Warm lamp light. |
| `scene-02` | Indoors | Close on a round kitchen table from above: hands on open Bibles, a notebook with handwriting too small to read, coffee cups, a plate of cookies. Morning light. |
| `scene-03` | Outdoors | A backyard at golden hour, shot wide from behind a circle of patio chairs: people small in the frame, a block wall and a palo verde tree beyond them. |
| `scene-04` | Indoors | A long dining table during a shared meal, shot down the length of the table from one end with people turned toward each other in profile, faces soft and out of focus. Serving dishes and a Bible by a plate. |
| `scene-05` | Outdoors | A covered back patio at dusk with string lights, shot from behind two people in the foreground; the rest of the group blurred across a fire table. |
| `scene-06` | Indoors | A small apartment living room, shot low across a rug: laps, folded legs, and open Bibles in the foreground, faces cropped out above. Afternoon window light. |
| `scene-07` | Outdoors | A park picnic table under a shade ramada, shot from behind one person reading aloud from a Bible to the others, who are out of focus. Grass and mesquite trees beyond. |
| `scene-08` | Indoors | A coffee shop corner the group meets in, shot over shoulders toward a window: cups, Bibles, and the backs of heads against the bright window. |
| `scene-09` | Outdoors | A front courtyard with a fountain and potted cactus, the group seated in a loose circle seen from far enough away that no face is clear. Late afternoon. |
| `scene-10` | Indoors | A den with a sectional, shot from the doorway: people seen from behind and the side, one person standing and gesturing, a TV turned off on the wall. |
| `scene-11` | Outdoors | A desert trailhead picnic, people sitting on a low stone wall seen from behind, Bibles open on laps, desert mountains in the distance. Morning light. |
| `scene-12` | Indoors | Hands joined in a circle for prayer around a coffee table, shot from above and close, so only arms, hands, and the table show. |

## Rules you must not break

1. **No identifiable faces.** If a face is clear enough to recognize, remake the photo.
2. **No real people.** Never use a real photo as a reference. Do not use the Cast profile photos or the Cast group photos as references either.
3. **No text.** No words, signs, logos, watermarks, or frames, including on clothing, mugs, book covers, and notebooks.
4. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`.
5. **Do not change any other file.** Do not edit the Cast files, the group files, `photos/manifest.json`, or `photos/groups/`.

## The prompt

Start from this and fill in the scene's setting and moment.

```
Candid phone photo of a church small group meeting: {setting and moment}.
No faces are clearly visible: people are seen from behind, turned away, cropped, or out of focus.
Several adults of mixed ages, everyday modern clothing. Bibles are around: open on laps and on the table; not everyone holds one.
Warm, welcoming, natural light for the setting, in the Phoenix, Arizona area. No children.
No text, logos, or watermarks anywhere.
```

## Image format

Landscape, 3:2, as large as your tool allows (for example 1536 by 1024). JPEG or PNG in sRGB. Leave a little room at the edges, because Rock crops group photos to fit cards and banners.

## Saving the photos

1. Save your full-size image to `photos/originals/scenes/{scene}.png` (or `.jpg`, in the format your tool made).
2. Save a JPEG copy at the full size of your image, quality 90, to `photos/scenes/{scene}.jpg`, with image metadata removed. Do not shrink it.
3. Add one entry per photo to `photos/scenes/manifest.json`. Create the file if it does not exist:

```json
{
  "model": "the image model you used",
  "images": [
    {
      "file": "scenes/scene-01.jpg",
      "scene": "scene-01",
      "indoors": true,
      "seed": null,
      "prompt": "the exact prompt you used"
    }
  ]
}
```

## Check every image

Look at every photo before you save it. Remake any that fail.

1. No face is clear enough to recognize.
2. It still reads as a group of adults meeting, with Bibles visible.
3. The setting matches its scene and differs from every other scene and every Cast group photo.
4. No children.
5. No text, watermark, logo, or frame.
6. No distorted hands, extra fingers, or odd limbs. Hands are often close to the camera here, so check them carefully.

## When you are done

1. Report how many photos you made, how many you remade and why.
2. Commit the new files in `photos/scenes/` and `photos/originals/scenes/`.
