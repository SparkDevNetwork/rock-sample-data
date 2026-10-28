# Event photo brief: create calendar event photos

You create one photo for each event on Rock Solid Church's calendar in Rock RMS sample data. You are the image model: read the files named here, make each image, and save it. Everything you need is filled in below. Read this whole page before you start.

Work in your own branch from `main`. All paths are from the root of the repository.

Each photo is the event's picture in Rock (`EventItem.PhotoId`). Rock shows it at the top of the event's page on the church website and in the church app. It should look like a photo a church would post to invite people to the event: real, warm, and inviting.

## What to make

One photo per event in [The events](#the-events) below: 51 photos. Make them in the order listed, except the 7 events on the Internal calendar: make those last. They are lower priority, because Rock's public pages never show them.

The events, their dates, and their wording live in [configuration/calendar.json](../configuration/calendar.json). Each event's heading below gives its `key` from that file. Do not change that file.

## Image size

Rock shows an event photo in three places, and none of them resizes the file:

| Where | How Rock shows it | Source in Rock |
|---|---|---|
| Event details page on the church website | The whole image, as wide as the page column (up to about 750 pixels), in its own aspect ratio | `Themes/*/Assets/Lava/CalendarItem.lava` |
| Event page in the church app | Cropped to fill a 2:1 box, full screen width, with rounded corners | The Calendar Event Item Occurrence View mobile template |
| Event page in Rock's admin screens | Shrunk to fit a 300 by 300 thumbnail | `EventItemDetail` view panel |

So every photo is **2:1 landscape**. Then the website and the app show exactly the same picture.

1. **Make the image** in landscape, as large as your tool allows. Most tools make 3:2 (for example 1536 by 1024). If yours can make 2:1 directly, do that.
2. **Compose for a 2:1 crop.** Keep the subject and anything important in the middle band of a 3:2 image, so nothing important is lost when the top and bottom are trimmed. Keep the main subject away from the left and right edges, because the app rounds the corners.
3. **Deliver 2:1.** The delivered JPEG is exactly **1536 by 768 pixels**. If you made 3:2, crop the top and bottom to 2:1. You may move the crop up or down to frame the scene well, but never stretch or squash it.

## The look

1. **Real moments, not ads.** Each photo looks like a good photo a church staff member or volunteer took at the event: natural light, real places, slightly imperfect framing. It is not a stock photo, a poster, or a graphic.
2. **People are anonymous.** Most events show people, because people make an event inviting. But no one in these photos is a named person. Show people from behind, from the side, at a distance, in motion, or softly out of focus. An adult's face may show when it is small in the frame or not the subject. **No child's face is ever clear.** Some photos need no people at all; the event says so.
3. **One church.** Events at "every campus" or "church-wide" happen at the Phoenix campus, the church's original building. Attach the [church reference pack](references/church/README.md) image the event names, and keep its rooms, finishes, and furniture. Events at one campus use that campus's look from the [campus photos](campuses/README.md).
4. **Arizona.** The church is in the Phoenix area. Outdoors means bright sun, blue sky, stucco, desert plants, palo verde and mesquite trees, and gravel. Winter is mild and green-brown, never snowy. Camps and retreats are up north near Prescott and Sedona, among pines, oaks, and red rock, where it is cooler.
5. **The church's colors, lightly.** Rock Solid Church's brand is warm ivory, charcoal, deep teal, and a little coral. Let these show up in tablecloths, balloons, lanyards, cups, and decor, never as signs or graphics.
6. **Everyday clothing** that fits the season and the event. No costumes except where the event says so, and no robes or uniforms.
7. **Each photo is different.** Vary the setting, the time of day, the camera distance, and the angle. No two events share a view of the same room.

## Rules you must not break

1. **No real people.** Never use a photo of a real person as a reference. No face may look like a real or famous person.
2. **No named people.** Do not attach Cast profile photos. No one in these photos is meant to be recognized.
3. **No clear child faces.** Kids appear from behind, from the side, in motion, or far away.
4. **No text.** No words, letters, numbers, signs, logos, brand names, watermarks, or frames. That includes clothing, cups, banners, screens, books, car badges, and license plates. If a reference image shows a sign or a wall graphic, frame it out or blur it. The church logo is text too, so leave it out.
5. **No real places.** Juniper Hollow Camp and Sycamore Bend Retreat Center are made up. Do not show a famous landmark, a real business, or a real school.
6. **Safe and gentle.** Nothing scary, gory, or unsafe. No needles or blood at the blood drive. No alcohol. No political symbols or flags. Grief events show quiet comfort, never distress.
7. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`. Crowds show natural variety in age, skin tone, and build.
8. **Do not change any other file.** Only add files in `photos/events/` and `photos/originals/events/`, and the manifest described below.

## The prompt

Start from this and fill in the parts in braces. Attach the reference image the event names when your tool allows it.

```
Photo of a church event: {scene}.
Setting: {setting}. {reference instruction, if a reference is attached}
People: {people}. No faces are the focus; children's faces are never visible.
Looks like a real photo taken by church staff at the event, warm and inviting, natural light for the setting.
Landscape, with the subject in the middle band so it can be cropped to 2:1.
Everyday, modern clothing. No text, letters, logos, signs, or watermarks anywhere.
```

For a reference image, use this instruction from the reference pack: "Use the attached Rock Solid Church architectural reference. Preserve the room layout, materials, furniture, equipment placement and scale. Change people, activity, camera position and lighting only as requested."

## Saving the photos

1. Save your full-size image to `photos/originals/events/{key}.png` (or `.jpg`, in the format your tool made), where `{key}` is the event's key.
2. Save the delivered 2:1 JPEG, 1536 by 768 pixels, quality 85, sRGB, with image metadata removed, to `photos/events/{key}.jpg`. Keep it under 500 KB.
3. Add one entry per photo to `photos/events/manifest.json`. The file exists with an empty `images` list. Keep the entries in the order of this page.

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

`event` is the event's key. `file` is the path under `photos/`.

## How the photos reach Rock

No code changes when photos arrive. The builder packs every photo listed in `photos/events/manifest.json` into every dataset, and the loader sets it as that event's photo in Rock. An event with no entry loads with no photo. So you can deliver photos a few at a time: each one shows up in the next package build.

The builder refuses a manifest entry whose `event` is not a key in `configuration/calendar.json`, or whose file is missing.

## Check every image

Look at every photo before you save it. Remake any that fail.

1. It is exactly 2:1, 1536 by 768, and nothing important is cut off.
2. It shows the event the section describes, in its setting, at its time of day and season.
3. No child's face is clear, and no adult's face is the subject.
4. No text, letters, numbers, logos, or signs anywhere, including on clothes, cars, screens, and banners.
5. No distorted faces, extra fingers, odd hands, or melted objects.
6. Rooms at the Phoenix campus match the reference pack.
7. No two photos share a view.

## When you are done

1. Report how many photos you made, and how many you remade and why.
2. Commit the new files in `photos/events/` and `photos/originals/events/`, and the manifest, on your branch.

## The events

Each event lists:

- **File:** the delivered photo.
- **When:** when it happens, so the light and season are right.
- **Where:** the campus and the room or place.
- **Reference:** the image to attach, if any.
- **Scene:** what the photo shows.
- **People:** who is in it, and how.

The events are grouped by the Ministry by Design stage each one serves, the same stages the church's steps use: Belong, Equip, Contribute, and Lead. Staff events come last.

### Belong

Front-door, family, community, and care events: where people are known and connected before they are asked to do anything.

#### Easter Sunday (`easter`)

- **File:** `photos/events/easter.jpg`
- **When:** Easter morning, in March or April.
- **Where:** The worship center at every campus. Use Phoenix.
- **Reference:** `photos/references/church/worship-center-from-rear.png`
- **Scene:** A full worship center during a joyful Easter service. Warm house lights, bright stage light, a few white lilies at the front of the stage. Hands raised here and there.
- **People:** A large congregation seen from behind and from the side, standing and singing. Families with kids, kids seen from behind.

#### Good Friday Service (`good-friday`)

- **File:** `photos/events/good-friday.jpg`
- **When:** Friday evening before Easter.
- **Where:** The worship center. Use Phoenix.
- **Reference:** `photos/references/church/worship-center-from-stage.png`
- **Scene:** A quiet, dim worship center. A simple wooden cross on the stage, lit by one soft spotlight. Communion cups and bread on a small table.
- **People:** A few people seated in quiet reflection, seen from behind or far away, some heads bowed.

#### Easter Egg Hunt (`egg-hunt`)

- **File:** `photos/events/egg-hunt.jpg`
- **When:** Saturday morning before Easter, spring sun.
- **Where:** The courtyard and lawn outside the kids wing, at Phoenix, Peoria, Gilbert, and Chandler. Use Phoenix.
- **Reference:** `photos/references/church/front-exterior-wide.png`
- **Scene:** Bright plastic eggs scattered across grass and gravel beside desert plants. A bounce house and a snow cone cart in the background.
- **People:** Small kids running with baskets, seen from behind and in motion. Parents watching at a distance.

#### Christmas Eve Services (`christmas-eve`)

- **File:** `photos/events/christmas-eve.jpg`
- **When:** Christmas Eve, late afternoon and evening.
- **Where:** The worship center at every campus. Use Phoenix.
- **Reference:** `photos/references/church/worship-center-from-rear.png`
- **Scene:** The candlelight moment: the house lights are down, and the room glows with hundreds of small handheld candles. Soft warm stage light, a few evergreen trees with white lights on the stage.
- **People:** The congregation seen from behind, holding candles. A parent holds a small child, both seen from behind.

#### Lessons and Carols (`lessons-and-carols`)

- **File:** `photos/events/lessons-and-carols.jpg`
- **When:** A Sunday evening in December.
- **Where:** The Scottsdale campus sanctuary: the traditional church building it kept, with its steeple and stained glass.
- **Reference:** `photos/campuses/scottsdale.jpg` (the exterior, for the building's style)
- **Scene:** Inside a traditional sanctuary with wooden pews and tall stained glass windows glowing in the evening. Candles in the window sills, evergreen garland on the pew ends. A reader at a wooden lectern.
- **People:** The congregation in the pews seen from behind. The reader is small and far away.

#### Small Group Connect (`group-connect`)

- **File:** `photos/events/group-connect.jpg`
- **When:** A Sunday morning in early September or January.
- **Where:** The lobby at Phoenix.
- **Reference:** `photos/references/church/lobby-toward-worship-center.png`
- **Scene:** Small clusters of adults chatting around high-top tables with coffee, each table with a small bowl of mints and blank cards. Warm, friendly conversation.
- **People:** Adults of many ages in small circles, seen from the side and from behind, mid-laugh. Faces small.

#### Starting Point (`starting-point`)

- **File:** `photos/events/starting-point.jpg`
- **When:** A Wednesday evening, dinner time.
- **Where:** A classroom at Phoenix with round tables.
- **Reference:** `photos/references/church/lobby-toward-worship-center.png` (for the finishes: warm white, pale oak, teal)
- **Scene:** A friendly class over dinner: round tables with pasta and salad on real plates, name tags with no writing, a host standing at the front of the room beside a blank screen.
- **People:** Adults of many ages at the tables, talking and laughing, seen from the side and from behind. The host is small and seen from the side.

#### Hope for the Holidays (`hope-for-the-holidays`)

- **File:** `photos/events/hope-for-the-holidays.jpg`
- **When:** A Thursday evening in early December.
- **Where:** The prayer room at Phoenix.
- **Reference:** `photos/references/church/prayer-room.png`
- **Scene:** A quiet, warm room in lamplight. A small table with a box of tissues, a few battery candles, and a sprig of evergreen. Chairs in a gentle circle. Calm and hopeful.
- **People:** None, or one or two adults seated in the circle seen from behind. No visible distress.

#### Student Night (`student-night`)

- **File:** `photos/events/student-night.jpg`
- **When:** A Wednesday evening.
- **Where:** The student room at Phoenix.
- **Reference:** `photos/references/church/student-room.png`
- **Scene:** A lively student night: the game area in use, a ping pong ball mid-air, string lights glowing, snacks on the round table. The wall identity is framed out.
- **People:** Teenagers playing and hanging out, seen from behind, from the side, and in motion. No clear faces.

#### Student Camp (`student-camp`)

- **File:** `photos/events/student-camp.jpg`
- **When:** Late June, Monday to Friday.
- **Where:** Juniper Hollow Camp, a made-up camp in the pine forest near Prescott.
- **Scene:** Summer camp: a small lake with a wooden dock, tall ponderosa pines, a zip line over the water, and canoes. Late afternoon golden light.
- **People:** Teenagers jumping off the dock and paddling canoes, seen from behind and far away. A leader on the dock.

#### Vacation Bible School (`vbs`)

- **File:** `photos/events/vbs.jpg`
- **When:** A morning in mid-June.
- **Where:** The kids wing at Phoenix, Peoria, Gilbert, Scottsdale, and Chandler. Use Phoenix.
- **Reference:** `photos/references/church/kids-classroom.png`
- **Scene:** High-energy VBS worship in a large kids room: bright teal and coral decorations, balloons, and a big blank backdrop of a painted landscape. Matching plain teal VBS shirts.
- **People:** Kids jumping and dancing with arms up, all seen from behind. A teen crew leader dancing with them, seen from behind.

#### Parents' Night Out (`parents-night-out`)

- **File:** `photos/events/parents-night-out.jpg`
- **When:** A Friday evening.
- **Where:** The kids wing at Phoenix, Gilbert, and Chandler. Use Phoenix.
- **Reference:** `photos/references/church/kids-classroom.png`
- **Scene:** Kids in pajamas on blankets and pillows watching a movie on a big screen (the screen shows soft colored light, no picture), with a few pizza boxes and popcorn bowls.
- **People:** Kids seen from behind, cuddled up. A volunteer sits nearby, seen from the side.

#### Fall Festival (`fall-festival`)

- **File:** `photos/events/fall-festival.jpg`
- **When:** The last Saturday of October, late afternoon into dusk.
- **Where:** The parking lot and courtyard at Phoenix, Peoria, Gilbert, and Chandler. Use Phoenix.
- **Reference:** `photos/references/church/front-exterior-wide.png`
- **Scene:** A neighborhood carnival in the parking lot: string lights coming on, pumpkins and hay bales, carnival game booths, a small petting zoo pen with goats, and food trucks with blank sides.
- **People:** Families and kids in friendly costumes walking between booths, seen from behind and in motion.

#### Summer Movie Nights (`movie-nights`)

- **File:** `photos/events/movie-nights.jpg`
- **When:** A Friday evening in July.
- **Where:** The worship center at Phoenix and Chandler. Use Phoenix.
- **Reference:** `photos/references/church/worship-center-from-rear.png`
- **Scene:** Families spread out on blankets and bean bags on the floor in front of the stage, with popcorn. The big screen glows with soft colored light, no picture.
- **People:** Families seen from behind, silhouetted by the screen.

#### Men's Breakfast (`mens-breakfast`)

- **File:** `photos/events/mens-breakfast.jpg`
- **When:** A Saturday at 7:30 in the morning.
- **Where:** The lobby at Phoenix, with round tables.
- **Reference:** `photos/references/church/lobby-toward-entrance.png`
- **Scene:** Breakfast burritos in foil, a fruit tray, and big coffee carafes on round tables. Morning sun through the glass doors.
- **People:** Men of all ages around the tables, talking and laughing, seen from the side and from behind. A teenage son beside his dad, seen from behind.

#### Women's Night Out (`womens-night-out`)

- **File:** `photos/events/womens-night-out.jpg`
- **When:** A Thursday evening.
- **Where:** The lobby at Phoenix.
- **Reference:** `photos/references/church/lobby-toward-worship-center.png`
- **Scene:** A dessert bar on a long table with cupcakes and a coffee bar. Small canvases and paint cups on tables for a painting activity.
- **People:** Women laughing as they paint, seen from behind and from the side.

#### Women's Christmas Brunch (`womens-christmas-brunch`)

- **File:** `photos/events/womens-christmas-brunch.jpg`
- **When:** The first Saturday of December, late morning.
- **Where:** The lobby at Phoenix.
- **Scene:** Round tables, each decorated differently by its host: evergreen, gold, ivory, and teal, with candles and pretty brunch plates.
- **People:** Women seated and chatting, seen from the side and from behind, faces small.

#### Young at Heart Lunch (`young-at-heart-lunch`)

- **File:** `photos/events/young-at-heart-lunch.jpg`
- **When:** A Thursday at lunch.
- **Where:** The lobby at Phoenix and Scottsdale. Use Phoenix.
- **Scene:** Round tables with plated lunch and iced tea. A small upright piano in the corner.
- **People:** Adults in their 60s, 70s, and 80s at the tables, laughing, seen from the side and from behind. Faces small.

#### Rock Solid Car Show (`car-show`)

- **File:** `photos/events/car-show.jpg`
- **When:** A Saturday morning in the fall.
- **Where:** The parking lot at Phoenix.
- **Reference:** `photos/references/church/front-exterior-wide.png`
- **Scene:** Rows of shiny classic cars, trucks, and hot rods with hoods up in bright sun, food trucks with blank sides, and pop-up shade canopies. The church building in the background.
- **People:** People walking the rows and looking at engines, seen from behind and from the side. **No badges, emblems, or license plates are readable.**

#### Young Adults Night (`young-adults`)

- **File:** `photos/events/young-adults.jpg`
- **When:** A Thursday night.
- **Where:** The Tempe campus: an urban warehouse conversion with brick walls, factory windows, and exposed beams.
- **Reference:** `photos/campuses/tempe.jpg` (the exterior, for the building's materials)
- **Scene:** After the service: people gathered around a taco bar under string lights in a brick room, with a small stage and acoustic guitar in the background.
- **People:** Adults aged 18 to 30 talking in small groups, seen from behind and from the side. Faces small.

### Equip

Classes, studies, retreats, and the Sundays where people grow as followers of Jesus.

#### Baptism Sunday (`baptism-sunday`)

- **File:** `photos/events/baptism-sunday.jpg`
- **When:** A Sunday morning on Easter or in July.
- **Where:** The worship center stage at Phoenix, with a portable baptism tank.
- **Reference:** `photos/references/church/stage-from-side.png`
- **Scene:** A joyful baptism: water splashing as a person comes up out of the water in a dark gray shirt, a pastor beside them. The congregation cheers in the background.
- **People:** The person being baptized is caught in motion, face hidden by water and hair, or seen from the side. The pastor is seen from behind. A crowd cheering, out of focus.

#### Baby Dedication (`baby-dedication`)

- **File:** `photos/events/baby-dedication.jpg`
- **When:** A Sunday morning in May or November.
- **Where:** The front of the worship center at Phoenix.
- **Reference:** `photos/references/church/worship-center-from-stage.png`
- **Scene:** A row of young parents standing at the front of the stage, holding babies and toddlers, while a pastor prays over them. Soft stage light.
- **People:** The families seen from behind or the side, with babies' faces turned away or tucked into a parent's shoulder.

#### Night of Worship (`night-of-worship`)

- **File:** `photos/events/night-of-worship.jpg`
- **When:** A Sunday evening.
- **Where:** The worship center at Phoenix, with people from every campus.
- **Reference:** `photos/references/church/worship-center-from-rear.png`
- **Scene:** A packed room in deep amber and violet stage light, the worship band small on the stage, light haze in the beams.
- **People:** The crowd seen from behind, many with hands raised, silhouetted against the stage light.

#### Foundations (`foundations`)

- **File:** `photos/events/foundations.jpg`
- **When:** A Tuesday evening.
- **Where:** A classroom at Phoenix.
- **Scene:** A small circle of chairs. Open Bibles, notebooks, pens, and coffee cups on laps and a low table. A warm lamp and evening light through a window.
- **People:** Five or six adults in conversation, seen from behind and from the side. No faces are the focus.

#### Money Matters (`money-matters`)

- **File:** `photos/events/money-matters.jpg`
- **When:** A Monday evening in January or September.
- **Where:** A classroom at Phoenix.
- **Scene:** A table set for a practical class: blank budget worksheets with empty columns, a calculator, pencils, coffee mugs, and a small bowl of candy. No numbers or writing on the pages.
- **People:** A couple's hands working on the worksheet, and a volunteer coach leaning in, all seen from the shoulders down or from behind.

#### Marriage Date Night (`date-night`)

- **File:** `photos/events/date-night.jpg`
- **When:** A Friday evening in February or October.
- **Where:** The lobby at Phoenix, turned into a candlelit dinner.
- **Reference:** `photos/references/church/lobby-toward-entrance.png`
- **Scene:** Small round tables for two with ivory tablecloths, candles, and string lights overhead. Plated dessert and coffee.
- **People:** Couples of different ages at their tables, seen from the side and from behind, leaning in and laughing together.

#### Ready for Marriage (`ready-for-marriage`)

- **File:** `photos/events/ready-for-marriage.jpg`
- **When:** A Saturday in March, June, or September, daytime.
- **Where:** A bright classroom at Phoenix.
- **Scene:** Two chairs pulled close at a table with a shared notebook and two coffee cups. A simple engagement ring on one hand.
- **People:** A young couple seen from behind or from the shoulders down, holding hands. Other couples blurred in the background.

#### Student Fall Retreat (`student-retreat`)

- **File:** `photos/events/student-retreat.jpg`
- **When:** A weekend in the fall, evening.
- **Where:** Juniper Hollow Camp, near Prescott.
- **Scene:** A campfire at dusk in a pine forest, with log benches around it, sparks rising, and cabins with warm lit windows behind.
- **People:** Teenagers in hoodies around the fire, silhouetted and seen from behind. One plays an acoustic guitar.

#### Tuesday Morning Men's Study (`mens-tuesday-study`)

- **File:** `photos/events/mens-tuesday-study.jpg`
- **When:** A Tuesday at 6:30 in the morning, first light.
- **Where:** Javelina Wash Coffee, a small made-up neighborhood coffee shop near the Phoenix campus.
- **Scene:** A corner table by the window in a cozy local coffee shop: mugs, open Bibles, a notebook, and a plate of pastries. Warm light inside, a pale blue dawn sky and desert plants outside. No writing on cups, menus, or windows.
- **People:** Four men of different ages around the table, seen from behind and from the side, one mid-laugh.

#### Men's Retreat (`mens-retreat`)

- **File:** `photos/events/mens-retreat.jpg`
- **When:** A weekend in late February, cool weather.
- **Where:** Juniper Hollow Camp, near Prescott.
- **Scene:** A morning on the camp lake: two men fishing from the dock in jackets, coffee mugs beside them, mist on the water and pines behind.
- **People:** Two or three men seen from behind.

#### Women's Bible Study (`womens-bible-study`)

- **File:** `photos/events/womens-bible-study.jpg`
- **When:** A Tuesday morning during the school year.
- **Where:** A classroom at Phoenix with round tables.
- **Scene:** Round tables with open Bibles, journals, colored pens, and coffee mugs. Morning light through the windows.
- **People:** Women of many ages at the tables, seen from the side and from behind, one pointing to a page.

#### Women's Retreat (`womens-retreat`)

- **File:** `photos/events/womens-retreat.jpg`
- **When:** A weekend in late April, mild weather.
- **Where:** Sycamore Bend Retreat Center, a made-up retreat center near Sedona, with red rock cliffs in the distance.
- **Scene:** A shaded patio with Adirondack chairs, blankets, and coffee, looking out at sycamore trees and red rock. Not a famous rock formation.
- **People:** Three or four women in the chairs, seen from behind, looking at the view.

### Contribute

Serving, outreach, and generosity: where people take part in the church's mission.

#### Serve Sunday (`serve-sunday`)

- **File:** `photos/events/serve-sunday.jpg`
- **When:** A Sunday morning in late August.
- **Where:** The lobby at Phoenix.
- **Reference:** `photos/references/church/lobby-toward-entrance.png`
- **Scene:** A busy lobby lined with tables for each serving team, each with its own props: a coffee pot, a stack of kids crafts, a set of headphones, a basket of lanyards. Teal tablecloths.
- **People:** Volunteers behind the tables talking with people walking by, seen from the side and from behind. Faces small.

#### Built on the Rock Commitment Sunday (`commitment-sunday`)

- **File:** `photos/events/commitment-sunday.jpg`
- **When:** A Sunday morning.
- **Where:** The worship center at Phoenix.
- **Reference:** `photos/references/church/worship-center-from-rear.png`
- **Scene:** Families walking forward to place blank white commitment cards in large wooden boxes at the front of the stage. Warm, hopeful light.
- **People:** A line of adults and families seen from behind, carrying cards. Kids seen from behind holding a parent's hand.

#### High School Mission Trip (`mission-trip`)

- **File:** `photos/events/mission-trip.jpg`
- **When:** Spring break or early July.
- **Where:** With a partner church in the Southwest, outdoors.
- **Scene:** Students and adult leaders painting a cinder block wall of a small community building in a bright color, under a clear sky. Paint trays, rollers, and water jugs.
- **People:** Teenagers and leaders working together, seen from behind and from the side. No faces are the focus. No flags, no text on the wall.

#### Men's Serve Day (`mens-serve-day`)

- **File:** `photos/events/mens-serve-day.jpg`
- **When:** A Saturday morning in March or November.
- **Where:** A neighbor's front yard in a Phoenix-area neighborhood.
- **Scene:** A crew doing yard work for a neighbor: trimming a palo verde tree, raking gravel, fixing a gate on a block wall. Wheelbarrows and work gloves.
- **People:** Men in work clothes seen from behind and the side, busy. An older woman watching from her porch, far away.

#### Community Serve Day (`community-serve-day`)

- **File:** `photos/events/community-serve-day.jpg`
- **When:** A Saturday morning in the fall.
- **Where:** A partner school's multipurpose room near a campus.
- **Scene:** Long tables piled with school supplies being sorted into backpacks: notebooks, crayons, and folders in bright colors. Cardboard boxes, no labels.
- **People:** Volunteers of all ages, including college students and families, sorting and packing, seen from behind and from the side.

#### Community Diaper Drive (`diaper-drive`)

- **File:** `photos/events/diaper-drive.jpg`
- **When:** A Saturday morning.
- **Where:** The kids entrance at Phoenix.
- **Reference:** `photos/references/church/main-entrance-close.png` (for the building)
- **Scene:** A welcoming table under a shade canopy with neat stacks of diapers and wipes in plain packaging, sorted by size, and a coffee station.
- **People:** A volunteer handing a pack to a young mom with a stroller, both seen from the side or behind. The baby is not visible.

#### Thanksgiving Food Drive (`food-drive`)

- **File:** `photos/events/food-drive.jpg`
- **When:** Sundays in November.
- **Where:** The lobby at Phoenix, by the welcome counter.
- **Reference:** `photos/references/church/lobby-toward-entrance.png`
- **Scene:** Overflowing cardboard boxes of canned vegetables, boxed stuffing, and cranberry sauce in plain packaging, with a few small pumpkins. No labels or text.
- **People:** A child's hands placing a can in a box, and a family in the background seen from behind.

#### Blood Drive (`blood-drive`)

- **File:** `photos/events/blood-drive.jpg`
- **When:** A Sunday morning.
- **Where:** A side area of the lobby at Phoenix and Chandler. Use Phoenix.
- **Scene:** The snack table after donating: juice boxes, cookies, and water bottles on a teal tablecloth. Donation recliners are blurred in the background. **No needles, tubes, or blood.**
- **People:** A donor with a plain bandage wrap on their arm taking a cookie, seen from the shoulders down.

#### Volunteer Safety Training (`safety-training`)

- **File:** `photos/events/safety-training.jpg`
- **When:** A Saturday morning.
- **Where:** The kids wing at Phoenix.
- **Reference:** `photos/references/church/kids-classroom.png`
- **Scene:** Adults seated in small kids chairs at low tables for a training, with blank handouts, coffee, and a check-in kiosk on a stand at the side of the room.
- **People:** Adult volunteers seen from behind and from the side, listening to a trainer far away.

#### Goodyear Setup and Load-In (`goodyear-load-in`)

On the Internal calendar, so it is lower priority: make it with the staff events.

- **File:** `photos/events/goodyear-load-in.jpg`
- **When:** A Sunday at 6:30 in the morning, early light.
- **Where:** Outside the Goodyear campus: a high school auditorium entrance.
- **Reference:** `photos/campuses/goodyear.jpg`
- **Scene:** A plain enclosed trailer with its ramp down beside the auditorium doors, black road cases and rolled banners on carts. Long early-morning shadows.
- **People:** Volunteers rolling cases up the walk, seen from behind.

### Lead

Training and huddles for the people who lead small groups, serving teams, and students.

#### Student Leader Interest Meeting (`student-leader-meeting`)

- **File:** `photos/events/student-leader-meeting.jpg`
- **When:** A Wednesday night in April.
- **Where:** The student room at Phoenix.
- **Reference:** `photos/references/church/student-room.png`
- **Scene:** A small circle of older teenagers on the couches with a leader, talking seriously and warmly. Notebooks and a plate of cookies.
- **People:** Five or six older teenagers and one adult leader, seen from behind and from the side. No clear faces.

#### Leader Huddle (`leader-huddle`)

- **File:** `photos/events/leader-huddle.jpg`
- **When:** A Thursday evening in February, May, August, or November.
- **Where:** The lobby at Phoenix, set with round tables, for leaders from every campus.
- **Reference:** `photos/references/church/lobby-toward-worship-center.png`
- **Scene:** Round tables after dinner, notebooks open, coffee cups, and blank table cards. A speaker stands far away at the front. The room feels warm and energized.
- **People:** Small group leaders, hosts, and serving team leaders of many ages at the tables, seen from behind and from the side, leaning in.

#### Group Leader Training (`group-leader-training`)

- **File:** `photos/events/group-leader-training.jpg`
- **When:** A Thursday evening in early September or early January.
- **Where:** A classroom at Phoenix.
- **Scene:** A circle of chairs with study guides that have plain covers, coffee, and a whiteboard covered in blank sticky notes. A trainer leans in to talk with the group.
- **People:** Six adults, new and returning group leaders and hosts, seen from behind and from the side.

### Staff

Staff events on the Internal calendar. Rock's public pages never show them, so make these last.

#### All Staff Meeting (`all-staff`)

- **File:** `photos/events/all-staff.jpg`
- **When:** A Tuesday morning.
- **Where:** A conference room at the Phoenix campus offices.
- **Scene:** A long table with laptops, coffee mugs, and notebooks, a wall screen with a blank video call grid glowing softly.
- **People:** Staff around the table seen from behind and from the side, mid-conversation.

#### Weekend Service Planning (`service-planning`)

- **File:** `photos/events/service-planning.jpg`
- **When:** A Wednesday morning.
- **Where:** The conference room at Phoenix.
- **Scene:** A table spread with blank planning sheets, a laptop, sticky notes with no writing, and an acoustic guitar leaning against a chair.
- **People:** None, or hands at the table only.

#### Elder Meeting (`elder-meeting`)

- **File:** `photos/events/elder-meeting.jpg`
- **When:** A Monday evening.
- **Where:** The conference room at Phoenix.
- **Scene:** A calm conference room in warm evening light: closed folders, water glasses, and a few open Bibles on the table.
- **People:** None.

#### Rock Office Hours (`rock-office-hours`)

- **File:** `photos/events/rock-office-hours.jpg`
- **When:** A Thursday afternoon.
- **Where:** The conference room at Phoenix.
- **Scene:** Two laptops side by side on a table, their screens showing soft, unreadable blurred dashboards, with coffee and a notepad.
- **People:** Two people's hands and shoulders, one pointing at a screen.

#### Staff Retreat (`staff-retreat`)

- **File:** `photos/events/staff-retreat.jpg`
- **When:** A weekday in January, cool and clear.
- **Where:** Sycamore Bend Retreat Center, near Sedona.
- **Scene:** A meeting room with big windows onto red rock and winter-bare sycamores, chairs in a circle, and a blank flip chart.
- **People:** A few staff in the circle seen from behind, looking out at the view.

#### Staff Christmas Party (`staff-christmas-party`)

- **File:** `photos/events/staff-christmas-party.jpg`
- **When:** A Friday evening in December.
- **Where:** The lobby at Phoenix.
- **Scene:** A table of wrapped gifts in plain paper for a gift swap, string lights, and a dessert table.
- **People:** Adults laughing in the background, out of focus.
