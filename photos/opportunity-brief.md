# Opportunity photo brief: create serving opportunity photos

You create one photo for each of the eight serving opportunities in Rock RMS sample data. You are the image model: read the files named here, make each image, and save it. Everything you need is filled in below. Read this whole page before you start.

Work in the `rock-sample-data-serving` worktree, on the `serving` branch. The serving teams and their volunteers exist only there for now. All paths are from the root of that worktree.

Rock shows each photo at the top of the opportunity's card on the public Serve page, and on the opportunity's detail page. It should look like a photo a church would post to invite people to serve: real volunteers doing the job, warm and welcoming.

## What to make

One photo per opportunity in [The opportunities](#the-opportunities) below: 8 photos. Three replace the photos Rock ships for its Greeter, Usher, and Children's opportunities, so all eight match. Make them in the order listed.

## The look

1. **Real volunteers serving.** Each photo shows the named volunteers doing the job in the setting given, in the middle of a Sunday morning. It looks like a good phone photo a staff member took, not a stock photo or an ad. Some people look at the camera and smile; others are busy with the work.
2. **One church, one building.** Every photo is inside or at the entrance of the same modern church building in the Phoenix area: a clean, bright building with glass doors, a lobby, a dark worship center with stage lighting, and colorful kids rooms. Keep the style consistent across the eight, but each photo has its own room and view.
3. **Arizona.** The entrance photo shows sun, desert plants, stucco, and a parking lot or walkway. Indoor rooms look like a real church in the area.
4. **Everyday clothing.** Casual, modern clothes that fit each person's age. No church robes, uniforms, or costumes. A simple lanyard name badge is fine, but it must have no readable text.
5. **Named volunteers are clear.** Every volunteer in the table is in the photo and recognizable. No one is cut off or hidden.
6. **Other people stay anonymous.** Guests, kids, and students appear only where the scene calls for them, seen from behind, from the side, or out of focus, with no clear faces. Never add another clear adult face.

## Matching the volunteers

Each opportunity lists its volunteers. For each one, the table gives the full-size original under `photos/originals/cast/`. Use it as the reference for that person's face, hair, skin tone, build, and age. The person should be easy to recognize next to their profile photo. Their clothes and setting should be new; do not copy the profile photo's background or pose.

Every profile photo was made by an image model. They are fictional people, so you may use them as references.

## Rules you must not break

1. **No real people.** Never use a real photo as a reference, and no face may look like a real or famous person. Use only the Cast profile photos named here.
2. **Only the listed people show their faces.** Guests, kids, and students are anonymous, as described above. No child's face is visible in any photo.
3. **No text.** No words, signs, logos, watermarks, or frames, including on clothing, screens, badges, banners, and the stage.
4. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`.
5. **Do not change any other file.** Do not edit the Cast files, the configuration files, or `photos/manifest.json`.

## The prompt

Start from this and fill in the parts in braces. Add each volunteer's reference photo as an image input when your tool allows it.

```
Photo of church volunteers serving on a Sunday morning: {scene}.
{one line per volunteer: "{name}, {age}-year-old {man/woman}, {what they are doing}, matching reference photo {n}"}
{anonymous people, if any: who they are, and "seen from behind, no clear faces"}
Looks like a real phone photo taken by church staff, warm and welcoming, natural light for the setting. The named volunteers' faces are clearly visible.
Everyday, modern clothing. No text, logos, or watermarks anywhere.
```

## Image format

Landscape, 3:2, as large as your tool allows (for example 1536 by 1024). JPEG or PNG in sRGB. Keep the volunteers near the middle with some room around them, because Rock crops the photo to fit its cards.

## Saving the photos

1. Save your full-size image to `photos/originals/opportunities/{team}.png` (or `.jpg`, in the format your tool made).
2. Save a JPEG copy at the full size of your image (for example 1536 by 1024), quality 90, to `photos/opportunities/{team}.jpg`, with image metadata removed. Do not shrink it: the Serve page shows it wide.
3. Add one entry per photo to `photos/opportunities/manifest.json`. Create the file if it does not exist:

```json
{
  "model": "the image model you used",
  "images": [
    {
      "file": "opportunities/greeters.jpg",
      "opportunity": "greeters",
      "people": [ "Larry Bell", "Deb Bell" ],
      "seed": null,
      "prompt": "the exact prompt you used"
    }
  ]
}
```

`opportunity` is the team key in the table heading. `people` lists the named volunteers in the photo, in the order of the opportunity's table.

The loader does not set opportunity photos from these files yet. That comes after the photos are made.

## Check every image

Look at every photo before you save it. Remake any that fail.

1. Every named volunteer is there and easy to recognize from their profile photo: face, hair, skin tone, and age.
2. No other adult face is clear, and no child's face is visible.
3. The scene shows the job the opportunity describes, in its own setting.
4. All eight look like one church building, with no two photos sharing a view.
5. No text, watermark, logo, or frame, including on badges, screens, and banners.
6. No distorted faces, extra fingers, odd hands, or odd teeth, and no odd instruments or equipment.
7. No face looks like anyone real or famous.

## When you are done

1. Report how many photos you made, and how many you remade and why.
2. Commit the new files in `photos/opportunities/` and `photos/originals/opportunities/` on the `serving` branch.

## The opportunities

The volunteers are Phoenix's Cast volunteers on each team (see `cast/groups/serving-{team}.json`).

### Greeter (`greeters`)

- **File:** `photos/opportunities/greeters.jpg`
- **Replaces:** Rock's `greeter.jpg`
- **Scene:** Outdoors at the main entrance in bright morning sun. The volunteers hold open the glass doors and welcome an arriving young family, seen from behind. Desert landscaping and a stucco wall frame the entrance.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| Larry Bell | 66 | male | Holding the door, shaking a guest's hand | `photos/originals/cast/bell-larry.jpg` |
| Deb Bell | 64 | female | Smiling and waving the family in | `photos/originals/cast/bell-debra.jpg` |

### Usher (`ushers`)

- **File:** `photos/opportunities/ushers.jpg`
- **Replaces:** Rock's `usher.jpg`
- **Scene:** Inside the worship center before the service, warm house lights and soft stage light in the background. The volunteers stand in an aisle between rows of chairs, helping a couple find seats; the couple is seen from behind.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| Bill Marble | 55 | male | Gesturing toward open seats | `photos/originals/cast/marble-bill.jpg` |
| Marcus Washington | 52 | male | Smiling, standing at the end of a row | `photos/originals/cast/washington-marcus.jpg` |

### Kids Volunteer (`kids-ministry`)

- **File:** `photos/opportunities/kids-ministry.jpg`
- **Replaces:** Rock's `childrens.jpg`
- **Scene:** A bright elementary classroom with colorful walls and a low table. The volunteers kneel beside the table helping a few kids with a craft; the kids are seen from behind or from the side, with no clear faces.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| Rachel Brooks | 41 | female | Kneeling, helping with the craft and smiling | `photos/originals/cast/brooks-rachel.jpg` |
| Maria Garcia | 34 | female | Handing out supplies, laughing | `photos/originals/cast/garcia-maria.jpg` |

### Musician or Vocalist (`worship-team`)

- **File:** `photos/opportunities/worship-team.jpg`
- **Scene:** On the worship center stage during a morning rehearsal, colored stage lights and an empty, dim room beyond. The team plays and sings together.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| James Lee | 40 | male | Playing keyboard | `photos/originals/cast/lee-james.jpg` |
| Gabe Hernandez | 28 | male | Playing acoustic guitar | `photos/originals/cast/hernandez-gabriel.jpg` |
| Lorraine Greggs | 36 | female | Singing at a microphone | `photos/originals/cast/greggs-lorraine.jpg` |

### Production Volunteer (`production-team`)

- **File:** `photos/opportunities/production-team.jpg`
- **Scene:** The production booth at the back of the worship center, with a sound board, monitors glowing with blurred images, and the stage lit in the distance.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| Ted Decker | 38 | male | Running a camera on a tripod | `photos/originals/cast/decker-ted.jpg` |
| Jim Simmons | 35 | male | At the sound board, wearing headphones around his neck | `photos/originals/cast/simmons-jim.jpg` |

### Student Leader (`student-ministry`)

- **File:** `photos/opportunities/student-ministry.jpg`
- **Scene:** The student room, a casual space with couches, string lights, and a rug. The leaders sit with a small circle of teenagers talking; the teens are seen from behind or from the side, with no clear faces.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| Isaac Silva | 29 | male | Leaning forward, listening | `photos/originals/cast/silva-isaac.jpg` |
| Jenny Michaels | 28 | female | Talking and smiling, holding an open Bible | `photos/originals/cast/michaels-jenny.jpg` |

### Prayer Partner (`prayer-team`)

- **File:** `photos/opportunities/prayer-team.jpg`
- **Scene:** The quiet prayer room after a service, with soft lamp light and a few comfortable chairs. The volunteers pray with a person who is seen from behind.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| Pete Foster | 50 | male | Praying, a hand on the person's shoulder | `photos/originals/cast/foster-pete.jpg` |
| Cindy Decker | 37 | female | Praying beside them, eyes closed | `photos/originals/cast/decker-cindy.jpg` |

### First Aid Responder (`first-aid-team`)

- **File:** `photos/opportunities/first-aid-team.jpg`
- **Scene:** The first aid station off the lobby, a small room with a cot, a cabinet, and an open first aid kit on the counter. The volunteers are ready to help.

| Name | Age | Gender | Doing | Profile photo |
|---|---|---|---|---|
| Michelle Nguyen | 33 | female | Checking the first aid kit, a radio clipped to her belt | `photos/originals/cast/nguyen-michelle.jpg` |
| Sarah Simmons | 34 | female | Restocking supplies, smiling at the camera | `photos/originals/cast/simmons-sarah.jpg` |
