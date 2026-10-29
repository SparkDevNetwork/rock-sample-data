# Event image brief: create calendar event images

You create one image for each event on Rock Solid Church's calendar in Rock RMS sample data. You are the image model: read the files named here, make each image, and save it. Everything you need is filled in below. Read this whole page before you start.

Work in your own branch from `main`. All paths are from the root of the repository.

**Delivered 2026-10-28.** All 51 images are in `photos/events/`, with their originals in `photos/originals/events/` and their prompts and approval in `photos/events/manifest.json` (decision 197). Jon approved every one, and some of his choices differ from this page; [What changed after review](#what-changed-after-review) lists them. Use this page to remake an image or to make one for a new event, and follow the approved images where they differ.

Each image is the event's picture in Rock (`EventItem.PhotoId`). Rock shows it at the top of the event's page on the church website and in the church app. It should look like what a real church posts to invite people to the event.

## What changed after review

Jon reviewed every image and approved choices that differ from the rules below. His approvals win over this page:

1. **Vacation Bible School shows children's faces.** Kids making a paper mural, with the title in charcoal and teal on the left. A clear child's face is fine here because the kids are made up and the scene is a craft table.
2. **Baby Dedication shows Pete Foster,** the senior pastor, praying over the families, from his Cast profile photo.
3. **The High School Mission Trip is in Mexico,** with a partner church, not a made-up place in the Southwest.
4. **The women's events have their own purple family.** Women's Night Out, the Women's Christmas Brunch, the Women's Bible Study, and the Women's Retreat share a smoky plum look, inside the church's brand.
5. **Not every event looks like Arizona.** Some designs are about the event, not the desert.
6. **Some events switched between design and photo** after review, so the Kind lines below are where each one started, not always where it ended.
7. **The Internal calendar's staff events are photos with no text.**
8. **The Women's Retreat is approved for now,** and may be remade later.

## Two kinds of image

Real churches promote most events with a designed graphic, and use photos for events where the real moment is the point. This calendar does the same. Each event below says which kind it gets.

1. **Design** (28 events). A marketing graphic, like a postcard or a flyer from the church's design team. It carries the event's title and nothing else in words. Easter, Christmas Eve, classes, and the big family events are designs.
2. **Photo** (23 events). A real-looking photo taken at the event, with no text. Baptisms, retreats, mission trips, serve days, leader gatherings, and staff events are photos, because the people and the place tell the story.

## What to make

One image per event in [The events](#the-events) below: 51 images. Make them in the order listed, except the 7 events on the Internal calendar: make those last. They are lower priority, because Rock's public pages never show them.

The events, their dates, and their wording live in [configuration/calendar.json](../configuration/calendar.json). Each event's heading below gives its `key` from that file. Do not change that file.

## Image size

Rock shows an event image in three places, and none of them resizes the file:

| Where | How Rock shows it | Source in Rock |
|---|---|---|
| Event details page on the church website | The whole image, as wide as the page column (up to about 750 pixels), in its own aspect ratio | `Themes/*/Assets/Lava/CalendarItem.lava` |
| Event page in the church app | Cropped to fill a 2:1 box, full screen width, with rounded corners | The Calendar Event Item Occurrence View mobile template |
| Event page in Rock's admin screens | Shrunk to fit a 300 by 300 thumbnail | `EventItemDetail` view panel |

So every image is **2:1 landscape**. Then the website and the app show exactly the same picture.

1. **Make the image** in landscape, as large as your tool allows. Most tools make 3:2 (for example 1536 by 1024). If yours can make 2:1 directly, do that.
2. **Compose for a 2:1 crop.** Keep the subject, and a design's title, in the middle band of a 3:2 image, so nothing important is lost when the top and bottom are trimmed. Keep them away from the left and right edges, because the app rounds the corners.
3. **Deliver 2:1.** The delivered JPEG is exactly **1536 by 768 pixels**. If you made 3:2, crop the top and bottom to 2:1. You may move the crop up or down to frame the image well, but never stretch or squash it.

## The look of a design

1. **A postcard, not a scene.** A design is a polished graphic a church design team would make: strong type, a clear idea, and art that sets the mood. It is not a photo of the room where the event happens.
2. **One church's family of designs.** Every design uses Rock Solid Church's brand colors: warm ivory, charcoal, deep teal, and a little coral. A design may add its season's colors, such as evergreen at Christmas or pumpkin orange in the fall. Seen side by side, the designs look like one church made them.
3. **Varied styles.** Each design names its style. The styles are:
   - **Bold type:** large, clean lettering on a solid or soft gradient field, with a few simple shapes.
   - **Flat illustration:** a friendly drawn scene in flat color, with light texture.
   - **Paper cut:** layered cut paper with soft shadows, like a paper craft photographed from above.
   - **Watercolor:** loose painted shapes on ivory paper, with soft edges.
   - **Block print:** a linocut or woodcut look, with bold carved lines and two or three inks.
   - **Retro poster:** a mid-century or screen-printed poster look, with grain and halftone texture.
   - **Photo with type:** a real-looking photo of an object, a texture, or light, toned in brand colors, with the title over it. People rules for photos apply.
4. **The title is the only text.** Show the words in the event's **Text** line, spelled exactly, with the apostrophes shown. Use one or two clean, legible typefaces. Make the title large enough to read when the image is shrunk to 300 pixels wide. Keep it in the middle band, with wide margins on every side.
5. **No other words.** No dates, times, campuses, addresses, web addresses, prices, Scripture references, or taglines. Event dates move with the install day, and Rock shows the details next to the image, so the image never repeats them.
6. **People are rare and simple.** A design mostly shows objects, places, and shapes. When a design includes people, draw them simply, as shapes or silhouettes, with no detailed faces.

If your tool misspells or garbles the title, make the art with clear space for the title, then set the title yourself with an image editor in a clean serif or sans serif typeface.

## The look of a photo

1. **Real moments, not ads.** Each photo looks like a good photo a church staff member or volunteer took at the event: natural light, real places, slightly imperfect framing. It is not a stock photo, a poster, or a graphic.
2. **People are anonymous.** Most photos show people, because people make an event inviting. But no one in these photos is a named person. Show people from behind, from the side, at a distance, in motion, or softly out of focus. An adult's face may show when it is small in the frame or not the subject. **No child's face is ever clear.** Some photos need no people at all; the event says so.
3. **One church.** Events at "every campus" or "church-wide" happen at the Phoenix campus, the church's original building. Attach the [church reference pack](references/church/README.md) image the event names, and keep its rooms, finishes, and furniture. Events at one campus use that campus's look from the [campus photos](campuses/README.md).
4. **Arizona.** The church is in the Phoenix area. Outdoors means bright sun, blue sky, stucco, desert plants, palo verde and mesquite trees, and gravel. Winter is mild and green-brown, never snowy. Camps and retreats are up north near Prescott and Sedona, among pines, oaks, and red rock, where it is cooler.
5. **The church's colors, lightly.** Let ivory, charcoal, deep teal, and a little coral show up in tablecloths, balloons, lanyards, cups, and decor, never as signs or graphics.
6. **Everyday clothing** that fits the season and the event. No costumes, robes, or uniforms.
7. **Each photo is different.** Vary the setting, the time of day, the camera distance, and the angle. No two events share a view of the same room.

## Rules you must not break

1. **No real people.** Never use a photo of a real person as a reference. No face may look like a real or famous person.
2. **No named people.** Do not attach Cast profile photos. No one in these images is meant to be recognized.
3. **No clear child faces.** Kids appear from behind, from the side, in motion, or far away, or as simple drawn shapes in a design.
4. **Only the title as text.** A photo has no text at all. A design has only its **Text** line. Neither has other words, letters, numbers, signs, logos, brand names, watermarks, or frames. That includes clothing, cups, banners, screens, books, car badges, and license plates. If a reference image shows a sign or a wall graphic, frame it out or blur it. The church logo is text too, so leave it out.
5. **No real places, brands, or characters.** Juniper Hollow Camp and Sycamore Bend Retreat Center are made up. Do not show a famous landmark, a real business, a real school, a car maker's design, a real movie, or a character from a real book, show, or published Vacation Bible School program.
6. **Safe and gentle.** Nothing scary, gory, or unsafe. No needles or blood at the blood drive. No alcohol. No political symbols or flags. Grief events show quiet comfort, never distress.
7. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`. Crowds show natural variety in age, skin tone, and build.
8. **Do not change any other file.** Only add files in `photos/events/` and `photos/originals/events/`, and the manifest described below.

## The prompts

Start from the prompt for the event's kind and fill in the parts in braces.

For a **design**:

```
Church event graphic for "{text}", like a postcard or flyer from a church design team.
Style: {style}. Concept: {design}.
Colors: warm ivory, charcoal, deep teal, and a touch of coral{, plus season colors}.
Text: only the words "{text}", spelled exactly, large and centered in the middle band, with wide margins.
No other words, letters, numbers, dates, or logos anywhere.
Landscape, polished, modern, and inviting. Not a photo of the event.
```

For a **photo**, attach the reference image the event names when your tool allows it:

```
Photo of a church event: {scene}.
Setting: {setting}. {reference instruction, if a reference is attached}
People: {people}. No faces are the focus; children's faces are never visible.
Looks like a real photo taken by church staff at the event, warm and inviting, natural light for the setting.
Landscape, with the subject in the middle band so it can be cropped to 2:1.
Everyday, modern clothing. No text, letters, logos, signs, or watermarks anywhere.
```

For a reference image, use this instruction from the reference pack: "Use the attached Rock Solid Church architectural reference. Preserve the room layout, materials, furniture, equipment placement and scale. Change people, activity, camera position and lighting only as requested."

## Saving the images

1. Save your full-size image to `photos/originals/events/{key}.png` (or `.jpg`, in the format your tool made), where `{key}` is the event's key.
2. Save the delivered 2:1 JPEG, 1536 by 768 pixels, quality 85, sRGB, with image metadata removed, to `photos/events/{key}.jpg`. Keep it under 500 KB.
3. Add one entry per image to `photos/events/manifest.json`. The file exists with an empty `images` list. Keep the entries in the order of this page.

```json
{
  "model": "the image model you used",
  "images": [
    {
      "file": "events/easter.jpg",
      "event": "easter",
      "seed": null,
      "prompt": "the exact prompt you used"
    }
  ]
}
```

`event` is the event's key. `file` is the path under `photos/`. If you set a design's title yourself in an image editor, end the prompt with "Title set by hand."

## How the images reach Rock

No code changes when images arrive. The builder packs every image listed in `photos/events/manifest.json` into every dataset, and the loader sets it as that event's photo in Rock. An event with no entry loads with no image. So you can deliver images a few at a time: each one shows up in the next package build.

The builder refuses a manifest entry whose `event` is not a key in `configuration/calendar.json`, or whose file is missing.

## Check every image

Look at every image before you save it. Remake any that fail.

1. It is exactly 2:1, 1536 by 768, and nothing important is cut off.
2. It is the kind the event names: a design or a photo.
3. A design shows its **Text** line, spelled exactly, readable at 300 pixels wide, inside the middle band, and no other text.
4. A photo has no text, letters, numbers, logos, or signs anywhere, including on clothes, cars, screens, and banners.
5. A photo shows the event the section describes, in its setting, at its time of day and season.
6. No child's face is clear, and no adult's face is the subject.
7. No distorted faces, extra fingers, odd hands, or melted objects.
8. Photo rooms at the Phoenix campus match the reference pack.
9. No two photos share a view, and no two designs look alike.

## When you are done

1. Report how many images you made, and how many you remade and why.
2. Commit the new files in `photos/events/` and `photos/originals/events/`, and the manifest, on your branch.

## The events

Each **design** lists:

- **Kind:** Design.
- **File:** the delivered image.
- **When:** when the event happens, for the season's colors.
- **Text:** the only words on the design.
- **Style:** one of the styles in [The look of a design](#the-look-of-a-design).
- **Design:** the idea and the art.

Each **photo** lists:

- **Kind:** Photo.
- **File:** the delivered image.
- **When:** when it happens, so the light and season are right.
- **Where:** the campus and the room or place.
- **Reference:** the image to attach, if any.
- **Scene:** what the photo shows.
- **People:** who is in it, and how.

The events are grouped by the Ministry by Design stage each one serves, the same stages the church's steps use: Belong, Equip, Contribute, and Lead. Staff events come last.

### Belong

Front-door, family, community, and care events: where people are known and connected before they are asked to do anything.

#### Easter Sunday (`easter`)

- **Kind:** Design
- **File:** `photos/events/easter.jpg`
- **When:** Easter morning, in March or April.
- **Text:** "Easter"
- **Style:** Paper cut
- **Design:** Layered paper hills of the desert at sunrise, with an open stone tomb on one hill and rays of coral and ivory light spreading behind it. A few white lilies in the foreground layer. Joyful and bright.

#### Good Friday Service (`good-friday`)

- **Kind:** Design
- **File:** `photos/events/good-friday.jpg`
- **When:** Friday evening before Easter.
- **Text:** "Good Friday"
- **Style:** Block print
- **Design:** A simple rough wooden cross carved in charcoal ink on ivory paper, under a deep teal evening sky. Lots of quiet space. Reverent and still.

#### Easter Egg Hunt (`egg-hunt`)

- **Kind:** Design
- **File:** `photos/events/egg-hunt.jpg`
- **When:** Saturday morning before Easter, spring.
- **Text:** "Easter Egg Hunt"
- **Style:** Flat illustration
- **Design:** Bright patterned eggs tumbling across green grass and gravel, with a small saguaro, a palo verde in bloom, and a basket on its side. Cheerful spring colors with brand teal and coral.

#### Christmas Eve Services (`christmas-eve`)

- **Kind:** Design
- **File:** `photos/events/christmas-eve.jpg`
- **When:** Christmas Eve, evening.
- **Text:** "Christmas Eve"
- **Style:** Photo with type
- **Design:** A close photo of a few hands holding small lit candles, with warm golden bokeh of many more candles behind. The title glows in ivory over the soft dark area. Hands only, no faces.

#### Lessons and Carols (`lessons-and-carols`)

- **Kind:** Design
- **File:** `photos/events/lessons-and-carols.jpg`
- **When:** A Sunday evening in December.
- **Text:** "Lessons and Carols"
- **Style:** Watercolor
- **Design:** The tall arched shape of a stained glass window, painted in glowing jewel tones, with candles on the sill and evergreen garland below. Elegant and traditional, with a classic serif title.

#### Small Group Connect (`group-connect`)

- **Kind:** Design
- **File:** `photos/events/group-connect.jpg`
- **When:** September or January.
- **Text:** "Small Group Connect"
- **Style:** Bold type
- **Design:** Clusters of overlapping circles in teal, coral, and charcoal on ivory, gathering into small groups across the image. It suggests people coming together around tables.

#### Starting Point (`starting-point`)

- **Kind:** Design
- **File:** `photos/events/starting-point.jpg`
- **When:** Any season.
- **Text:** "Starting Point"
- **Style:** Bold type
- **Design:** A single coral dot on the left, with a clean teal line that winds across an ivory field like a path. The title sits along the path. Simple, welcoming, and clear.

#### Hope for the Holidays (`hope-for-the-holidays`)

- **Kind:** Design
- **File:** `photos/events/hope-for-the-holidays.jpg`
- **When:** Early December.
- **Text:** "Hope for the Holidays"
- **Style:** Watercolor
- **Design:** One small candle with a soft warm glow beside a sprig of evergreen, in muted teal and ivory, with lots of quiet open space. Gentle and comforting, not festive.

#### Student Night (`student-night`)

- **Kind:** Design
- **File:** `photos/events/student-night.jpg`
- **When:** The school year.
- **Text:** "Student Night"
- **Style:** Retro poster
- **Design:** A grainy screen-printed poster with bold shapes: a ping pong paddle and ball, a strand of string lights, and a game controller, in coral and teal halftone on charcoal. Loud, fun, and made for teenagers.

#### Student Camp (`student-camp`)

- **Kind:** Photo
- **File:** `photos/events/student-camp.jpg`
- **When:** Late June, Monday to Friday.
- **Where:** Juniper Hollow Camp, a made-up camp in the pine forest near Prescott.
- **Scene:** Summer camp: a small lake with a wooden dock, tall ponderosa pines, a zip line over the water, and canoes. Late afternoon golden light.
- **People:** Teenagers jumping off the dock and paddling canoes, seen from behind and far away. A leader on the dock.

#### Vacation Bible School (`vbs`)

- **Kind:** Design
- **File:** `photos/events/vbs.jpg`
- **When:** Mid-June.
- **Text:** "Vacation Bible School"
- **Style:** Flat illustration
- **Design:** A cheerful desert adventure: a big smiling sun, cartoon saguaros, a winding trail, hot air balloons, and a canyon in bright teal, coral, and sunny yellow. Playful rounded type. No characters from a real program.

#### Parents' Night Out (`parents-night-out`)

- **Kind:** Design
- **File:** `photos/events/parents-night-out.jpg`
- **When:** A Friday evening.
- **Text:** "Parents' Night Out"
- **Style:** Flat illustration
- **Design:** A deep teal night sky with a crescent moon and stars over a small house with warm windows. A pizza slice, a popcorn bucket, and a pair of tiny slippers float in the foreground.

#### Fall Festival (`fall-festival`)

- **Kind:** Design
- **File:** `photos/events/fall-festival.jpg`
- **When:** The last Saturday of October.
- **Text:** "Fall Festival"
- **Style:** Paper cut
- **Design:** Layered paper pumpkins, hay bales, a striped carnival tent, and a little goat, under a strand of string lights at dusk. Pumpkin orange with brand teal and ivory.

#### Summer Movie Nights (`movie-nights`)

- **Kind:** Design
- **File:** `photos/events/movie-nights.jpg`
- **When:** Fridays in July.
- **Text:** "Summer Movie Nights"
- **Style:** Retro poster
- **Design:** A mid-century poster: a big glowing blank screen, a striped popcorn box spilling popcorn, film-reel shapes, and stars over a desert sunset. No real movie or character.

#### Men's Breakfast (`mens-breakfast`)

- **Kind:** Photo
- **File:** `photos/events/mens-breakfast.jpg`
- **When:** A Saturday at 7:30 in the morning.
- **Where:** The lobby at Phoenix, with round tables.
- **Reference:** `photos/references/church/lobby-toward-entrance.png`
- **Scene:** Breakfast burritos in foil, a fruit tray, and big coffee carafes on round tables. Morning sun through the glass doors.
- **People:** Men of all ages around the tables, talking and laughing, seen from the side and from behind. A teenage son beside his dad, seen from behind.

#### Women's Night Out (`womens-night-out`)

- **Kind:** Design
- **File:** `photos/events/womens-night-out.jpg`
- **When:** A Thursday evening.
- **Text:** "Women's Night Out"
- **Style:** Watercolor
- **Design:** Loose watercolor flowers in coral and blush, with a cupcake, a coffee cup, and a few paint swatches scattered among them. Fun, warm, and a little playful.

#### Women's Christmas Brunch (`womens-christmas-brunch`)

- **Kind:** Design
- **File:** `photos/events/womens-christmas-brunch.jpg`
- **When:** The first Saturday of December.
- **Text:** "Women's Christmas Brunch"
- **Style:** Block print
- **Design:** Carved evergreen branches framing a teacup with a curl of steam, printed in deep green, teal, and a touch of gold on ivory. Cozy and festive.

#### Young at Heart Lunch (`young-at-heart-lunch`)

- **Kind:** Photo
- **File:** `photos/events/young-at-heart-lunch.jpg`
- **When:** A Thursday at lunch.
- **Where:** The lobby at Phoenix and Scottsdale. Use Phoenix.
- **Scene:** Round tables with plated lunch and iced tea. A small upright piano in the corner.
- **People:** Adults in their 60s, 70s, and 80s at the tables, laughing, seen from the side and from behind. Faces small.

#### Rock Solid Car Show (`car-show`)

- **Kind:** Design
- **File:** `photos/events/car-show.jpg`
- **When:** A Saturday morning in the fall.
- **Text:** "Rock Solid Car Show"
- **Style:** Retro poster
- **Design:** A chrome-bright classic car shape, not any real make or model, parked in front of a big desert sun and saguaros, with a checkered stripe along the bottom. Bold mid-century lettering.

#### Young Adults Night (`young-adults`)

- **Kind:** Design
- **File:** `photos/events/young-adults.jpg`
- **When:** Thursday nights.
- **Text:** "Young Adults Night"
- **Style:** Photo with type
- **Design:** A photo of warm string lights hanging across an old brick wall at night, softly out of focus, toned in charcoal and amber. The title in clean modern type over the brick. Urban and relaxed.

### Equip

Classes, studies, retreats, and the Sundays where people grow as followers of Jesus.

#### Baptism Sunday (`baptism-sunday`)

- **Kind:** Photo
- **File:** `photos/events/baptism-sunday.jpg`
- **When:** A Sunday morning on Easter or in July.
- **Where:** The worship center stage at Phoenix, with a portable baptism tank.
- **Reference:** `photos/references/church/stage-from-side.png`
- **Scene:** A joyful baptism: water splashing as a person comes up out of the water in a dark gray shirt, a pastor beside them. The congregation cheers in the background.
- **People:** The person being baptized is caught in motion, face hidden by water and hair, or seen from the side. The pastor is seen from behind. A crowd cheering, out of focus.

#### Baby Dedication (`baby-dedication`)

- **Kind:** Photo
- **File:** `photos/events/baby-dedication.jpg`
- **When:** A Sunday morning in May or November.
- **Where:** The front of the worship center at Phoenix.
- **Reference:** `photos/references/church/worship-center-from-stage.png`
- **Scene:** A row of young parents standing at the front of the stage, holding babies and toddlers, while a pastor prays over them. Soft stage light.
- **People:** The families seen from behind or the side, with babies' faces turned away or tucked into a parent's shoulder.

#### Night of Worship (`night-of-worship`)

- **Kind:** Design
- **File:** `photos/events/night-of-worship.jpg`
- **When:** A Sunday evening.
- **Text:** "Night of Worship"
- **Style:** Photo with type
- **Design:** A photo of stage light beams cutting through haze in deep amber and violet, with a row of raised hands in silhouette along the bottom edge. The title sits in the open light above them.

#### Foundations (`foundations`)

- **Kind:** Design
- **File:** `photos/events/foundations.jpg`
- **When:** Any season.
- **Text:** "Foundations"
- **Style:** Bold type
- **Design:** Large stacked stone blocks drawn as clean geometric shapes in charcoal, teal, and ivory, forming a solid base under the title. Steady and strong.

#### Money Matters (`money-matters`)

- **Kind:** Design
- **File:** `photos/events/money-matters.jpg`
- **When:** January or September.
- **Text:** "Money Matters"
- **Style:** Flat illustration
- **Design:** A small green seedling growing out of a short stack of plain coins, beside a simple glass jar. Calm and hopeful. No dollar signs or numbers.

#### Marriage Date Night (`date-night`)

- **Kind:** Design
- **File:** `photos/events/date-night.jpg`
- **When:** February or October.
- **Text:** "Marriage Date Night"
- **Style:** Block print
- **Design:** Two coffee cups side by side under a looping strand of lights, carved in coral and charcoal on ivory, with a small heart where the strands cross. Warm and a little romantic.

#### Ready for Marriage (`ready-for-marriage`)

- **Kind:** Design
- **File:** `photos/events/ready-for-marriage.jpg`
- **When:** March, June, or September.
- **Text:** "Ready for Marriage"
- **Style:** Watercolor
- **Design:** Two interlocking rings painted in loose gold and teal, surrounded by soft desert wildflowers. Fresh, hopeful, and elegant.

#### Student Fall Retreat (`student-retreat`)

- **Kind:** Photo
- **File:** `photos/events/student-retreat.jpg`
- **When:** A weekend in the fall, evening.
- **Where:** Juniper Hollow Camp, near Prescott.
- **Scene:** A campfire at dusk in a pine forest, with log benches around it, sparks rising, and cabins with warm lit windows behind.
- **People:** Teenagers in hoodies around the fire, silhouetted and seen from behind. One plays an acoustic guitar.

#### Tuesday Morning Men's Study (`mens-tuesday-study`)

- **Kind:** Photo
- **File:** `photos/events/mens-tuesday-study.jpg`
- **When:** A Tuesday at 6:30 in the morning, first light.
- **Where:** Javelina Wash Coffee, a small made-up neighborhood coffee shop near the Phoenix campus.
- **Scene:** A corner table by the window in a cozy local coffee shop: mugs, open Bibles, a notebook, and a plate of pastries. Warm light inside, a pale blue dawn sky and desert plants outside. No writing on cups, menus, or windows.
- **People:** Four men of different ages around the table, seen from behind and from the side, one mid-laugh.

#### Men's Retreat (`mens-retreat`)

- **Kind:** Photo
- **File:** `photos/events/mens-retreat.jpg`
- **When:** A weekend in late February, cool weather.
- **Where:** Juniper Hollow Camp, near Prescott.
- **Scene:** A morning on the camp lake: two men fishing from the dock in jackets, coffee mugs beside them, mist on the water and pines behind.
- **People:** Two or three men seen from behind.

#### Women's Bible Study (`womens-bible-study`)

- **Kind:** Design
- **File:** `photos/events/womens-bible-study.jpg`
- **When:** The school year.
- **Text:** "Women's Bible Study"
- **Style:** Paper cut
- **Design:** An open book in layered paper, its pages curling up and turning into leaves and desert blooms in teal, coral, and soft green. Light and lovely.

#### Women's Retreat (`womens-retreat`)

- **Kind:** Photo
- **File:** `photos/events/womens-retreat.jpg`
- **When:** A weekend in late April, mild weather.
- **Where:** Sycamore Bend Retreat Center, a made-up retreat center near Sedona, with red rock cliffs in the distance.
- **Scene:** A shaded patio with Adirondack chairs, blankets, and coffee, looking out at sycamore trees and red rock. Not a famous rock formation.
- **People:** Three or four women in the chairs, seen from behind, looking at the view.

### Contribute

Serving, outreach, and generosity: where people take part in the church's mission.

#### Serve Sunday (`serve-sunday`)

- **Kind:** Design
- **File:** `photos/events/serve-sunday.jpg`
- **When:** Late August.
- **Text:** "Serve Sunday"
- **Style:** Bold type
- **Design:** A tidy grid of simple line icons for the serving teams: a coffee cup, headphones, a crayon, a lanyard, a door, and a helping hand, in teal and coral on ivory. The title sits in the middle of the grid.

#### Built on the Rock Commitment Sunday (`commitment-sunday`)

- **Kind:** Design
- **File:** `photos/events/commitment-sunday.jpg`
- **When:** A Sunday morning.
- **Text:** "Built on the Rock"
- **Style:** Block print
- **Design:** A simple house standing firm on a solid rock while wind and rain sweep past, carved in charcoal and teal on ivory, with a band of coral sky. It echoes the parable of the wise builder. The title uses the campaign's name only.

#### High School Mission Trip (`mission-trip`)

- **Kind:** Photo
- **File:** `photos/events/mission-trip.jpg`
- **When:** Spring break or early July.
- **Where:** With a partner church in the Southwest, outdoors.
- **Scene:** Students and adult leaders painting a cinder block wall of a small community building in a bright color, under a clear sky. Paint trays, rollers, and water jugs.
- **People:** Teenagers and leaders working together, seen from behind and from the side. No faces are the focus. No flags, no text on the wall.

#### Men's Serve Day (`mens-serve-day`)

- **Kind:** Photo
- **File:** `photos/events/mens-serve-day.jpg`
- **When:** A Saturday morning in March or November.
- **Where:** A neighbor's front yard in a Phoenix-area neighborhood.
- **Scene:** A crew doing yard work for a neighbor: trimming a palo verde tree, raking gravel, fixing a gate on a block wall. Wheelbarrows and work gloves.
- **People:** Men in work clothes seen from behind and the side, busy. An older woman watching from her porch, far away.

#### Community Serve Day (`community-serve-day`)

- **Kind:** Photo
- **File:** `photos/events/community-serve-day.jpg`
- **When:** A Saturday morning in the fall.
- **Where:** A partner school's multipurpose room near a campus.
- **Scene:** Long tables piled with school supplies being sorted into backpacks: notebooks, crayons, and folders in bright colors. Cardboard boxes, no labels.
- **People:** Volunteers of all ages, including college students and families, sorting and packing, seen from behind and from the side.

#### Community Diaper Drive (`diaper-drive`)

- **Kind:** Design
- **File:** `photos/events/diaper-drive.jpg`
- **When:** A Saturday morning.
- **Text:** "Community Diaper Drive"
- **Style:** Flat illustration
- **Design:** A soft, friendly stack of folded diapers with a rubber duck on top, a pack of wipes, and a baby bottle, in pale teal, ivory, and coral. Gentle and welcoming.

#### Thanksgiving Food Drive (`food-drive`)

- **Kind:** Design
- **File:** `photos/events/food-drive.jpg`
- **When:** November.
- **Text:** "Thanksgiving Food Drive"
- **Style:** Paper cut
- **Design:** A layered paper cardboard box overflowing with plain cans, a small pumpkin, wheat stalks, and autumn leaves. Harvest orange and gold with brand teal. No labels on the cans.

#### Blood Drive (`blood-drive`)

- **Kind:** Design
- **File:** `photos/events/blood-drive.jpg`
- **When:** A Sunday morning.
- **Text:** "Blood Drive"
- **Style:** Bold type
- **Design:** A single clean coral drop shape with a small ivory heart inside it, on a calm ivory field with a thin teal line. Simple and kind. No needles or medical gear.

#### Volunteer Safety Training (`safety-training`)

- **Kind:** Photo
- **File:** `photos/events/safety-training.jpg`
- **When:** A Saturday morning.
- **Where:** The kids wing at Phoenix.
- **Reference:** `photos/references/church/kids-classroom.png`
- **Scene:** Adults seated in small kids chairs at low tables for a training, with blank handouts, coffee, and a check-in kiosk on a stand at the side of the room.
- **People:** Adult volunteers seen from behind and from the side, listening to a trainer far away.

#### Goodyear Setup and Load-In (`goodyear-load-in`)

On the Internal calendar, so it is lower priority: make it with the staff events.

- **Kind:** Photo
- **File:** `photos/events/goodyear-load-in.jpg`
- **When:** A Sunday at 6:30 in the morning, early light.
- **Where:** Outside the Goodyear campus: a high school auditorium entrance.
- **Reference:** `photos/campuses/goodyear.jpg`
- **Scene:** A plain enclosed trailer with its ramp down beside the auditorium doors, black road cases and rolled banners on carts. Long early-morning shadows.
- **People:** Volunteers rolling cases up the walk, seen from behind.

### Lead

Training and huddles for the people who lead small groups, serving teams, and students.

#### Student Leader Interest Meeting (`student-leader-meeting`)

- **Kind:** Photo
- **File:** `photos/events/student-leader-meeting.jpg`
- **When:** A Wednesday night in April.
- **Where:** The student room at Phoenix.
- **Reference:** `photos/references/church/student-room.png`
- **Scene:** A small circle of older teenagers on the couches with a leader, talking seriously and warmly. Notebooks and a plate of cookies.
- **People:** Five or six older teenagers and one adult leader, seen from behind and from the side. No clear faces.

#### Leader Huddle (`leader-huddle`)

- **Kind:** Photo
- **File:** `photos/events/leader-huddle.jpg`
- **When:** A Thursday evening in February, May, August, or November.
- **Where:** The lobby at Phoenix, set with round tables, for leaders from every campus.
- **Reference:** `photos/references/church/lobby-toward-worship-center.png`
- **Scene:** Round tables after dinner, notebooks open, coffee cups, and blank table cards. A speaker stands far away at the front. The room feels warm and energized.
- **People:** Small group leaders, hosts, and serving team leaders of many ages at the tables, seen from behind and from the side, leaning in.

#### Group Leader Training (`group-leader-training`)

- **Kind:** Photo
- **File:** `photos/events/group-leader-training.jpg`
- **When:** A Thursday evening in early September or early January.
- **Where:** A classroom at Phoenix.
- **Scene:** A circle of chairs with study guides that have plain covers, coffee, and a whiteboard covered in blank sticky notes. A trainer leans in to talk with the group.
- **People:** Six adults, new and returning group leaders and hosts, seen from behind and from the side.

### Staff

Staff events on the Internal calendar. Rock's public pages never show them, so make these last. They are all photos, because a church does not design marketing graphics for its own staff meetings.

#### All Staff Meeting (`all-staff`)

- **Kind:** Photo
- **File:** `photos/events/all-staff.jpg`
- **When:** A Tuesday morning.
- **Where:** A conference room at the Phoenix campus offices.
- **Scene:** A long table with laptops, coffee mugs, and notebooks, a wall screen with a blank video call grid glowing softly.
- **People:** Staff around the table seen from behind and from the side, mid-conversation.

#### Weekend Service Planning (`service-planning`)

- **Kind:** Photo
- **File:** `photos/events/service-planning.jpg`
- **When:** A Wednesday morning.
- **Where:** The conference room at Phoenix.
- **Scene:** A table spread with blank planning sheets, a laptop, sticky notes with no writing, and an acoustic guitar leaning against a chair.
- **People:** None, or hands at the table only.

#### Elder Meeting (`elder-meeting`)

- **Kind:** Photo
- **File:** `photos/events/elder-meeting.jpg`
- **When:** A Monday evening.
- **Where:** The conference room at Phoenix.
- **Scene:** A calm conference room in warm evening light: closed folders, water glasses, and a few open Bibles on the table.
- **People:** None.

#### Rock Office Hours (`rock-office-hours`)

- **Kind:** Photo
- **File:** `photos/events/rock-office-hours.jpg`
- **When:** A Thursday afternoon.
- **Where:** The conference room at Phoenix.
- **Scene:** Two laptops side by side on a table, their screens showing soft, unreadable blurred dashboards, with coffee and a notepad.
- **People:** Two people's hands and shoulders, one pointing at a screen.

#### Staff Retreat (`staff-retreat`)

- **Kind:** Photo
- **File:** `photos/events/staff-retreat.jpg`
- **When:** A weekday in January, cool and clear.
- **Where:** Sycamore Bend Retreat Center, near Sedona.
- **Scene:** A meeting room with big windows onto red rock and winter-bare sycamores, chairs in a circle, and a blank flip chart.
- **People:** A few staff in the circle seen from behind, looking out at the view.

#### Staff Christmas Party (`staff-christmas-party`)

- **Kind:** Photo
- **File:** `photos/events/staff-christmas-party.jpg`
- **When:** A Friday evening in December.
- **Where:** The lobby at Phoenix.
- **Scene:** A table of wrapped gifts in plain paper for a gift swap, string lights, and a dessert table.
- **People:** Adults laughing in the background, out of focus.
