# Group photo brief: create small group photos

You create one photo for each of the Cast's small groups in Rock RMS sample data. You are the image model: read the files named here, make each image, and save it. Everything you need is filled in below. Read this whole page before you start. All paths are from the root of the `rock-sample-data` repository.

Each photo shows the group's real members, so it must look like the people in their profile photos. Rock shows the photo on Group Finder and on the group's page, so it should look like a picture a group leader would post to invite new people.

## What to make

One photo per group in [The groups](#the-groups) below: 12 photos. Make the active groups first. The inactive and archived groups are lower priority, because Rock shows their photo on the group's page, but Group Finder does not list them.

Only the Cast's groups get photos. Core 3's companion campuses also have small groups, but the loader plans them on install day, so their members change with the date. They are not part of this brief.

## The look

1. **A real group, not a stock photo.** It looks like a phone photo a friend or a spouse took at a meeting, or a casual picture the group posed for. Some people look at the camera and smile; a few can be mid-laugh or talking.
2. **Bibles are around.** Open Bibles on laps, on the coffee table, or on the table. Not everyone holds one. Coffee mugs, snacks, a notebook, or a phone can be there too.
3. **Every group has its own place.** Each group lists its setting. Use it. No two photos may share a room, a yard, furniture, or a view. About half are outdoors and half are indoors.
4. **Arizona.** The church is in the Phoenix area. Outdoor photos show sun, desert plants, stucco, block walls, palo verde or citrus trees. Indoor rooms look like real homes there.
5. **Everyday clothing.** Casual, modern clothes that fit each person's age and the season. No church clothes, costumes, or traditional dress. No visible logos or text.
6. **Faces are clear.** Everyone in the list is in the photo and recognizable. No one is cut off at the edge or hidden behind someone else.
7. **Adults only.** Even groups with child care show only the adults in the list.

## Matching the members

Each group lists its members in order: leader, host, then members. For each person:

- **With a profile photo:** the table gives the full-size original under `photos/originals/cast/`. Use it as the reference for that person's face, hair, skin tone, build, and age. The person should be easy to recognize next to their profile photo. Their clothes and setting should be new; do not copy the profile photo's background or pose.
- **Without a profile photo:** the table says so and describes who to make up: age, gender, and look. Make a new, fictional face for them that fits the rest of the group.

Married couples in the list usually sit or stand together.

Every profile photo was made by an image model. They are fictional people, so you may use them as references.

## Rules you must not break

1. **No real people.** Never use a real photo as a reference, and no face may look like a real or famous person. Use only the Cast profile photos named here.
2. **Only the listed people.** No extra adults and no children. The number of people in the photo matches the list.
3. **No text.** No words, signs, logos, watermarks, or frames, including on clothing, mugs, and book covers.
4. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`.
5. **Do not change any other file.** Do not edit the Cast files, the group files, or `photos/manifest.json`.

## The prompt

Start from this and fill in the parts in braces. Add each person's reference photo as an image input when your tool allows it.

```
Casual group photo of a church small group of {count} adults, meeting at {setting}.
{one line per person: "{name}, {age}-year-old {man/woman}, matching reference photo {n}" or the made-up description}
Bibles are around: open on laps and on the table; not everyone holds one.
Looks like a real phone photo taken at the meeting, warm and welcoming, natural light for the setting. Everyone's face is clearly visible.
Everyday, modern clothing. No text, logos, or watermarks anywhere.
```

## Image format

Landscape, 3:2, as large as your tool allows (for example 1536 by 1024). JPEG or PNG in sRGB. Leave a little room around the group, because Rock crops group photos to fit cards and banners.

## Saving the photos

There is no delivery command for group photos, so save them by hand. The builder packs every photo listed in `photos/groups/manifest.json` into every dataset, and the loader sets it as that group's photo in Rock (decision 62).

1. Save your full-size image to `photos/originals/groups/{group-key}.png` (or `.jpg`, in the format your tool made).
2. Save a JPEG copy at the full size of your image (for example 1536 by 1024), quality 90, to `photos/groups/{group-key}.jpg`, with image metadata removed. Do not shrink it: Rock shows group photos wide, and a smaller copy looks pixelated.
3. Add one entry per photo to `photos/groups/manifest.json`. Create the file if it does not exist:

```json
{
  "model": "the image model you used",
  "images": [
    {
      "file": "groups/decker-group.jpg",
      "group": "decker-group",
      "people": [ "Ted Decker", "Cindy Decker" ],
      "madeUp": [],
      "seed": null,
      "prompt": "the exact prompt you used"
    }
  ]
}
```

`people` lists everyone in the photo, in the order of the group's table. `madeUp` lists anyone you made up because they had no profile photo.

## Check every image

Look at every photo before you save it. Remake any that fail.

1. The number of people matches the list, all adults.
2. Each person with a profile photo is easy to recognize from it: face, hair, skin tone, and age.
3. The setting matches the group's setting and differs from every other photo.
4. Bibles are visible, and the scene looks like a real small group, not a stock photo or a church directory.
5. No text, watermark, logo, or frame.
6. No distorted faces, extra fingers, odd hands, or odd teeth.
7. No face looks like anyone real or famous.

## When you are done

1. Report how many photos you made, how many you remade and why, and anyone you made up.
2. Commit the new files in `photos/groups/` and `photos/originals/groups/`.

## The groups

`node photos/build-group-brief.js` writes this section from `cast/groups/` and `photos/manifest.json`. It lists each group's current members and the profile photo for each one.

<!-- groups:start (written by node photos/build-group-brief.js; do not edit by hand) -->

12 groups and 129 places in the photos (some people are in two groups): 129 with a profile photo and 0 to make up.

### Decker Group

- **File:** `photos/groups/decker-group.jpg`
- **State:** Active
- **Topic:** Parenting. Parents comparing notes, praying for our kids, and learning from each other.
- **Meets:** In person at the Decker home, Thursdays at 18:00, with child care
- **Setting:** Outdoors. A backyard lawn in the early evening, with folding lawn chairs pulled into a loose circle, a few kids' bikes and a soccer ball in the background, and desert landscaping along the block wall.
- **People in the photo:** 12

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Ted Decker | Leader | 38 | male | `photos/originals/cast/decker-ted.jpg` |
| Cindy Decker | Host | 37 | female | `photos/originals/cast/decker-cindy.jpg` |
| Bill Price | Member | 61 | male | `photos/originals/cast/price-william.jpg` |
| Monica Flores | Member | 38 | female | `photos/originals/cast/flores-monica.jpg` |
| Emily Gray | Member | 45 | female | `photos/originals/cast/gray-emily.jpg` |
| Elena Alvarez | Member | 30 | female | `photos/originals/cast/alvarez-elena.jpg` |
| Cathy Price | Member | 59 | female | `photos/originals/cast/price-catherine.jpg` |
| Marco Alvarez | Member | 31 | male | `photos/originals/cast/alvarez-marco.jpg` |
| Barb Jenkins | Member | 63 | female | `photos/originals/cast/jenkins-barbara.jpg` |
| Gary Jenkins | Member | 66 | male | `photos/originals/cast/jenkins-gary.jpg` |
| Rachel Brooks | Member | 41 | female | `photos/originals/cast/brooks-rachel.jpg` |
| Nathan Brooks | Member | 42 | male | `photos/originals/cast/brooks-nathan.jpg` |

### Wright Group

- **File:** `photos/groups/gilbert-group.jpg`
- **State:** Active
- **Topic:** Prayer. We spend most of our time praying for each other, our church, and our city.
- **Meets:** In person at the Gilbert home, Thursdays at 19:00
- **Setting:** Indoors. A cozy living room with warm lamp light, a mid-century modern couch and armchairs, a rug, and a coffee table with Bibles, a candle, and mugs.
- **People in the photo:** 11

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Jerry Wright | Leader | 74 | male | `photos/originals/cast/wright-gerald.jpg` |
| Brian Gilbert | Host | 48 | male | `photos/originals/cast/gilbert-brian.jpg` |
| Wendy Gilbert | Host | 46 | female | `photos/originals/cast/gilbert-wendy.jpg` |
| Dick Ross | Member | 72 | male | `photos/originals/cast/ross-richard.jpg` |
| Sam Hanks | Member | 28 | male | `photos/originals/cast/hanks-sam.jpg` |
| Paul Smith | Member | 20 | male | `photos/originals/cast/smith-paul-paul.jpg` |
| Joan Ross | Member | 70 | female | `photos/originals/cast/ross-joan.jpg` |
| Mark Fischer | Member | 46 | male | `photos/originals/cast/fischer-mark.jpg` |
| Julie Fischer | Member | 45 | female | `photos/originals/cast/fischer-julie.jpg` |
| Dot Wright | Member | 73 | female | `photos/originals/cast/wright-dorothy.jpg` |
| Tom Miller | Member | 44 | male | `photos/originals/cast/miller-tom-tom.jpg` |

### Greggs Group

- **File:** `photos/groups/greggs-group.jpg`
- **State:** Active
- **Topic:** Grief and Loss. A gentle group for anyone carrying a loss. Come as you are.
- **Meets:** In person at the Greggs home, Wednesdays at 18:00, with child care
- **Setting:** Indoors. A bright sunroom full of houseplants, soft afternoon light through tall windows, wicker chairs, and a side table with tissues, Bibles, and tea.
- **People in the photo:** 11

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Bob Greggs | Leader | 36 | male | `photos/originals/cast/greggs-bob.jpg` |
| Lorraine Greggs | Host | 36 | female | `photos/originals/cast/greggs-lorraine.jpg` |
| Mariah Jackson | Member | 37 | female | `photos/originals/cast/jackson-mariah.jpg` |
| Teresa Ortiz | Member | 47 | female | `photos/originals/cast/ortiz-teresa.jpg` |
| Amanda Sullivan | Member | 43 | female | `photos/originals/cast/sullivan-amanda.jpg` |
| Ryan Patel | Member | 36 | male | `photos/originals/cast/patel-ryan.jpg` |
| Luis Ortiz | Member | 48 | male | `photos/originals/cast/ortiz-luis.jpg` |
| Matt Hughes | Member | 35 | male | `photos/originals/cast/hughes-matthew.jpg` |
| Megan Patel | Member | 35 | female | `photos/originals/cast/patel-megan.jpg` |
| Jess Hughes | Member | 34 | female | `photos/originals/cast/hughes-jessica.jpg` |
| Chris Sullivan | Member | 45 | male | `photos/originals/cast/sullivan-chris.jpg` |

### Lee Group

- **File:** `photos/groups/lee-group.jpg`
- **State:** Active
- **Topic:** New to Faith. New to church or just exploring? Bring your questions; there are no wrong ones.
- **Meets:** Online, Sundays at 19:00, with child care
- **Setting:** Outdoors. The group meets online, so this is their in-person kickoff picnic: a shaded park ramada with picnic tables, a cooler, paper plates, and green grass and palo verde trees behind them.
- **People in the photo:** 10

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| James Lee | Leader | 40 | male | `photos/originals/cast/lee-james.jpg` |
| Tony Coleman | Member | 37 | male | `photos/originals/cast/coleman-anthony.jpg` |
| Tiffany Coleman | Member | 36 | female | `photos/originals/cast/coleman-tiffany.jpg` |
| Sarah Lee | Member | 38 | female | `photos/originals/cast/lee-sarah.jpg` |
| Kayla Ward | Member | 33 | female | `photos/originals/cast/ward-kayla.jpg` |
| Will Jones | Member | 56 | male | `photos/originals/cast/jones-will-will.jpg` |
| Kevin Dawson | Member | 39 | male | `photos/originals/cast/dawson-kevin.jpg` |
| Ashley Kelly | Member | 33 | female | `photos/originals/cast/kelly-ashley.jpg` |
| Gabe Hernandez | Member | 28 | male | `photos/originals/cast/hernandez-gabriel.jpg` |
| Derek Ward | Member | 34 | male | `photos/originals/cast/ward-derek.jpg` |

### Marble Group

- **File:** `photos/groups/marble-group.jpg`
- **State:** Active
- **Topic:** Sermon Discussion. A relaxed evening of talking through the weekend sermon, with good questions and good snacks.
- **Meets:** In person at the Coffee home, Mondays at 19:00
- **Setting:** Indoors. A large farmhouse-style living room with a big sectional couch, a stone fireplace, wood beams, and Bibles and coffee cups on an ottoman.
- **People in the photo:** 12

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Bill Marble | Leader | 55 | male | `photos/originals/cast/marble-bill.jpg` |
| Phil Coffee | Host | 40 | male | `photos/originals/cast/coffee-phil.jpg` |
| Sarah Simmons | Member | 34 | female | `photos/originals/cast/simmons-sarah.jpg` |
| Alisha Marble | Member | 48 | female | `photos/originals/cast/marble-alisha.jpg` |
| Allie Song | Member | 36 | unknown | `photos/originals/cast/song-allie.jpg` |
| Daniel Peak | Member | 36 | male | `photos/originals/cast/peak-daniel.jpg` |
| Bob Hayes | Member | 70 | male | `photos/originals/cast/hayes-robert.jpg` |
| Susan Kim | Member | 49 | female | `photos/originals/cast/kim-susan.jpg` |
| Linda Hayes | Member | 68 | female | `photos/originals/cast/hayes-linda.jpg` |
| David Kim | Member | 50 | male | `photos/originals/cast/kim-david.jpg` |
| Ty McClintock | Member | 20 | male | `photos/originals/cast/mcclintock-ty.jpg` |
| Jim Simmons | Member | 35 | male | `photos/originals/cast/simmons-jim.jpg` |

### Foster Group

- **File:** `photos/groups/petes-group.jpg`
- **State:** Active
- **Topic:** Book of John. A slow, honest walk through John, one conversation at a time.
- **Meets:** In person at the Foster home, Tuesdays at 19:00
- **Setting:** Indoors. A family room den with a worn leather sectional, tall bookshelves, a TV on the wall turned off, and Bibles and a few snacks on the coffee table.
- **People in the photo:** 12

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Pete Foster | Leader | 50 | male | `photos/originals/cast/foster-pete.jpg` |
| Pamela Foster | Host | 50 | female | `photos/originals/cast/foster-pamela.jpg` |
| Jenny Michaels | Member | 28 | female | `photos/originals/cast/michaels-jenny.jpg` |
| Peggy Carter | Member | 78 | female | `photos/originals/cast/carter-margaret.jpg` |
| Corrie Stone | Member | 31 | female | `photos/originals/cast/stone-corrie.jpg` |
| Jon Smith | Member | 43 | male | `photos/originals/cast/smith-jon-jon.png` |
| Dennis Perry | Member | 67 | male | `photos/originals/cast/perry-dennis.jpg` |
| Jack Harper | Member | 54 | male | `photos/originals/cast/harper-jack.jpg` |
| Becky Peterson | Member | 53 | female | `photos/originals/cast/peterson-becky.jpg` |
| Drew Kim | Member | 20 | male | `photos/originals/cast/kim-andrew.jpg` |
| Brian Peterson | Member | 53 | male | `photos/originals/cast/peterson-brian.jpg` |
| Hal Russell | Member | 81 | male | `photos/originals/cast/russell-harold.jpg` |

### Rivera Group

- **File:** `photos/groups/rivera-group.jpg`
- **State:** Active
- **Topic:** Marriage. A group for married couples who want to grow together, with practical conversations and plenty of laughter.
- **Meets:** In person at the Rivera home, Tuesdays at 19:00, with child care
- **Setting:** Outdoors. A backyard patio next to a pool with citrus trees and a stucco wall, couples sitting together on patio furniture in warm evening light.
- **People in the photo:** 10

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Mike Rivera | Leader | 44 | male | `photos/originals/cast/rivera-michael.jpg` |
| Jen Rivera | Host | 43 | female | `photos/originals/cast/rivera-jennifer.jpg` |
| Denise Washington | Member | 50 | female | `photos/originals/cast/washington-denise.jpg` |
| Josh Sanders | Member | 24 | male | `photos/originals/cast/sanders-josh.jpg` |
| Lauren Shaw | Member | 37 | female | `photos/originals/cast/shaw-lauren.jpg` |
| Melissa Wood | Member | 40 | female | `photos/originals/cast/wood-melissa.jpg` |
| Ben Jones | Member | 36 | male | `photos/originals/cast/jones-ben-ben.jpg` |
| Marcus Washington | Member | 52 | male | `photos/originals/cast/washington-marcus.jpg` |
| Andy Shaw | Member | 38 | male | `photos/originals/cast/shaw-andrew.jpg` |
| Jason Wood | Member | 41 | male | `photos/originals/cast/wood-jason.jpg` |

### Silva Group

- **File:** `photos/groups/silva-group.jpg`
- **State:** Active
- **Topic:** Money and Generosity. We learn how to manage money well and give freely, together.
- **Meets:** In person at the Bennett home, Wednesdays at 19:00
- **Setting:** Indoors. A modern condo living room with floor-to-ceiling windows, a city view in the late afternoon, a low gray couch, and floor cushions.
- **People in the photo:** 10

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Isaac Silva | Leader | 29 | male | `photos/originals/cast/silva-isaac.jpg` |
| Becca Bennett | Host | 42 | female | `photos/originals/cast/bennett-rebecca.jpg` |
| Tom Bennett | Host | 44 | male | `photos/originals/cast/bennett-tom.jpg` |
| Steve Morgan | Member | 58 | male | `photos/originals/cast/morgan-steve.jpg` |
| Larry Bell | Member | 66 | male | `photos/originals/cast/bell-larry.jpg` |
| Karen Morgan | Member | 57 | female | `photos/originals/cast/morgan-karen.jpg` |
| Dale Tucker | Member | 55 | male | `photos/originals/cast/tucker-dale.jpg` |
| Deb Bell | Member | 64 | female | `photos/originals/cast/bell-debra.jpg` |
| Natalie Silva | Member | 28 | female | `photos/originals/cast/silva-natalie.jpg` |
| Frank Dexter | Member | 42 | male | `photos/originals/cast/dexter-frank.jpg` |

### Hughes Group

- **File:** `photos/groups/hughes-group.jpg`
- **State:** Inactive (lower priority: Rock shows it, but Group Finder does not)
- **Topic:** Sermon Discussion. We dig into Sunday's message together and talk about how it plays out in everyday life.
- **Meets:** In person at the Hughes home, Thursdays at 19:00
- **Setting:** Indoors. An open kitchen and dining area, people gathered around a long wooden dining table with open Bibles, sermon notes, and a couple of pizza boxes.
- **People in the photo:** 7

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Matt Hughes | Leader | 35 | male | `photos/originals/cast/hughes-matthew.jpg` |
| Jess Hughes | Host | 34 | female | `photos/originals/cast/hughes-jessica.jpg` |
| Deb Bell | Member | 64 | female | `photos/originals/cast/bell-debra.jpg` |
| Josh Sanders | Member | 24 | male | `photos/originals/cast/sanders-josh.jpg` |
| Patty Diaz | Member | 54 | female | `photos/originals/cast/diaz-patricia.jpg` |
| Ricardo Diaz | Member | 55 | male | `photos/originals/cast/diaz-ricardo.jpg` |
| Larry Bell | Member | 66 | male | `photos/originals/cast/bell-larry.jpg` |

### Murphy Group

- **File:** `photos/groups/murphy-group.jpg`
- **State:** Inactive (lower priority: Rock shows it, but Group Finder does not)
- **Topic:** Prayer. We spend most of our time praying for each other, our church, and our city.
- **Meets:** In person at the Murphy home, Wednesdays at 18:00, with child care
- **Setting:** Outdoors. A backyard patio at dusk around a gas fire pit, with string lights overhead and blankets on a few laps.
- **People in the photo:** 12

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Patrick Murphy | Leader | 44 | male | `photos/originals/cast/murphy-patrick.jpg` |
| Erin Murphy | Host | 43 | female | `photos/originals/cast/murphy-erin.jpg` |
| Teresa Ortiz | Member | 47 | female | `photos/originals/cast/ortiz-teresa.jpg` |
| Jen Rivera | Member | 43 | female | `photos/originals/cast/rivera-jennifer.jpg` |
| Luis Ortiz | Member | 48 | male | `photos/originals/cast/ortiz-luis.jpg` |
| Craig Lowe | Member | 37 | male | `photos/originals/cast/lowe-craig.jpg` |
| Trish Lowe | Member | 35 | female | `photos/originals/cast/lowe-trish.jpg` |
| Hannah Ellis | Member | 26 | female | `photos/originals/cast/ellis-hannah.jpg` |
| Jordan Ellis | Member | 27 | male | `photos/originals/cast/ellis-jordan.jpg` |
| Mike Rivera | Member | 44 | male | `photos/originals/cast/rivera-michael.jpg` |
| Elena Alvarez | Member | 30 | female | `photos/originals/cast/alvarez-elena.jpg` |
| Marco Alvarez | Member | 31 | male | `photos/originals/cast/alvarez-marco.jpg` |

### Jenkins Group

- **File:** `photos/groups/jenkins-group.jpg`
- **State:** Archived (lower priority: Rock shows it, but Group Finder does not)
- **Topic:** New to Faith. A welcoming place to learn the basics of faith alongside others who are new too.
- **Meets:** In person at the Jenkins home, Sundays at 19:00, with child care
- **Setting:** Outdoors. The front porch and steps of a ranch-style house at golden hour, some people sitting on the steps and some standing behind them.
- **People in the photo:** 11

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Gary Jenkins | Leader | 66 | male | `photos/originals/cast/jenkins-gary.jpg` |
| Barb Jenkins | Host | 63 | female | `photos/originals/cast/jenkins-barbara.jpg` |
| Karen Morgan | Member | 57 | female | `photos/originals/cast/morgan-karen.jpg` |
| John Smith | Member | 36 | male | `photos/originals/cast/smith-john-other-john.png` |
| Nancy Turner | Member | 33 | female | `photos/originals/cast/turner-nancy.jpg` |
| Jordan Ellis | Member | 27 | male | `photos/originals/cast/ellis-jordan.jpg` |
| Jose Cruz | Member | 36 | male | `photos/originals/cast/cruz-jose.jpg` |
| Gaby Cruz | Member | 35 | female | `photos/originals/cast/cruz-gabriela.jpg` |
| Destiny Young | Member | 22 | female | `photos/originals/cast/young-destiny.jpg` |
| Steve Morgan | Member | 58 | male | `photos/originals/cast/morgan-steve.jpg` |
| Hannah Ellis | Member | 26 | female | `photos/originals/cast/ellis-hannah.jpg` |

### Russell Group

- **File:** `photos/groups/russell-group.jpg`
- **State:** Archived (lower priority: Rock shows it, but Group Finder does not)
- **Topic:** Book of John. We read through the Gospel of John a chapter at a time. No background needed.
- **Meets:** In person at the Greggs home, Tuesdays at 19:00
- **Setting:** Indoors. A formal dining room with a long table set for a meal, a china cabinet in the background, and Bibles open beside the plates.
- **People in the photo:** 11

| Name | Role | Age | Gender | Profile photo |
|---|---|---|---|---|
| Hal Russell | Leader | 81 | male | `photos/originals/cast/russell-harold.jpg` |
| Lorraine Greggs | Host | 36 | female | `photos/originals/cast/greggs-lorraine.jpg` |
| Bob Greggs | Host | 36 | male | `photos/originals/cast/greggs-bob.jpg` |
| Jim Simmons | Member | 35 | male | `photos/originals/cast/simmons-jim.jpg` |
| Sarah Simmons | Member | 34 | female | `photos/originals/cast/simmons-sarah.jpg` |
| Ricardo Diaz | Member | 55 | male | `photos/originals/cast/diaz-ricardo.jpg` |
| Patty Diaz | Member | 54 | female | `photos/originals/cast/diaz-patricia.jpg` |
| Denise Washington | Member | 50 | female | `photos/originals/cast/washington-denise.jpg` |
| Daniel Peak | Member | 36 | male | `photos/originals/cast/peak-daniel.jpg` |
| Marcus Washington | Member | 52 | male | `photos/originals/cast/washington-marcus.jpg` |
| Dennis Perry | Member | 67 | male | `photos/originals/cast/perry-dennis.jpg` |

<!-- groups:end -->
