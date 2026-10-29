# Staff photo brief: create staff photos

Names, titles, campuses, and ages follow `configuration/staff.json` and the Cast files. A Rock sample data session installs the delivered images (see [Installing staff photos](README.md#installing-staff-photos)).

You create one staff photo for each of Rock Solid Church's 23 staff members in Rock RMS sample data. You are the image model: read the files named here, make each image, and save it. Everything you need is filled in below. Read this whole page before you start.

You work outside the repository and change nothing in it. Read the files named here from the `staff` branch of `rock-sample-data` (`refs/heads/staff`), or from `main` once `staff` lands there. Paths are from the root of the repository. Save your images in a folder of your own.

Each image is the person's **Staff Photo**, a person attribute in Rock's Staff category. It is not their profile photo. A church website staff page shows these photos side by side, so the 20 photos must look like one photo session: the same spot outside the Phoenix campus, the same light, the same framing. That is the opposite of the profile photo rules, where every photo has its own setting on purpose.

## Make the profile photos first

Every staff photo starts from the person's Cast profile photo, so the same face carries over. Six staff have one today in `photos/cast/`. The 14 new staff, marked "(new)" below, get theirs first from the profile photo hand-off. For them, use the image of that name from your profile photo folder, since it is not in the repository yet. If a person's profile photo does not exist yet, stop and say so in your handback.

## What to make

One photo per person in [The staff](#the-staff) below: 20 images. Make them in the order listed. For each one, attach two images:

1. **The person's profile photo,** named in their section, for the face.
2. **The church entrance reference,** `photos/references/church/main-entrance-close.jpg`, for the place. Attach `front-exterior-wide.jpg` too if your tool takes three images.

## Image size

**Deliver a square, exactly 1536 by 1536 pixels, framed so a 3:1 crop through the middle still works.**

**Delivered 2026-10-29.** For the first 20 photos and the 6 campus versions, Jon dropped the 3:1 crop: they were reviewed for 3:2 and 16:9 landscape crops instead, with centered crops except Walt Hargrove's Phoenix photo, which the site crops at `50% 20%`. The 3 photos added later, Joel Park, Josiah Faleolo, and Miles Okafor, were first framed for the 3:1 crop. They were reframed the same day to match the first 20, so all 23 have the same face size and headroom. The 3:1 crop no longer applies to any staff photo; every one is checked at 3:2 and 16:9. The crop positions are in `photos/staff/manifest.json`.

1. **Why square.** Rock shows a Staff Photo on the person's profile, and themes show people in square cards or circles. A square fits both with no crop.
2. **Why 3:1 too.** A website may show staff in a wide banner or a wide card. A 3:1 crop through the middle of the square is 1536 by 512 pixels. That is wide enough to stay sharp across a page column, so the delivered square must already hold the face and shoulders inside that middle band.
3. **Why 1536.** It is the largest square most tools make, and it keeps the 3:1 crop at full width. Rock stores the file as delivered. Its image handler can shrink it for a small card, but it cannot add detail.
4. **Make it square.** If your tool can make 1024 by 1024 only, make that and upscale it cleanly to 1536 by 1536. Never stretch or squash.

## One look for all 23

Every photo uses exactly this setup. Only the person and their clothing change.

| Part | Rule |
|---|---|
| Place | Just outside the main entrance of the Phoenix campus, on the pale concrete approach beside a low warm-white stucco planter with gravel, agaves, and a palo verde tree. The warm-white stucco walls, charcoal canopy, and oak soffit are behind the person, softly out of focus. The same spot and the same camera direction for every person. |
| Light | Late morning in the mild season, in the open shade of the canopy edge. Soft, even light on the face, with brighter sunlit stucco and plants behind. A gentle rim of sunlight on the hair and shoulders. No hard shadows across the face, no squinting. The same light in every photo. |
| Camera | Eye level, about 2 to 3 meters away. The look of a portrait lens at a wide aperture: the person is sharp, and the building and plants are a soft, recognizable blur. |
| Framing | Somewhat zoomed out, about waist up. The person is centered left to right. The top of the head is about 34% of the height from the top, the eyes about 42%, and the shoulders above 66%. So the middle band, from 33% to 67% of the height, holds the face and shoulders. The top third is soft background. |
| Pose | Standing, shoulders turned slightly toward camera left, about 20 degrees, every person the same way. Face and eyes toward the camera. Upright, relaxed posture. Arms relaxed at the sides or loosely in front, with hands low and out of the middle band. |
| Expression | A natural, friendly smile, warm and approachable. Not a forced grin, not serious. |
| Clothing | Business casual that fits the person's role, listed in their section, for a mild Phoenix morning. Solid colors or quiet textures. Charcoal, deep teal, navy, soft blues, and muted earth tones suit the brand. Avoid ivory or white as the main color, so the person stands out from the stucco. |
| Finish | Clean, natural retouching. Real skin texture, no plastic smoothing, no heavy makeup look. The person looks their age. |

## Rules you must not break

1. **Same face as the profile photo.** Keep the face shape, eyes, nose, mouth, skin tone, hair color, facial hair, glasses, and age from the attached profile photo. Hair may be neater and clothing changes. Never replace or reinterpret the face.
2. **Same building as the reference.** Keep the Phoenix campus's stucco, canopy, soffit, planters, and desert plants. Do not invent a different building.
3. **No text or logos.** The church logo and every sign are text, so frame them out or keep them fully blurred. No words, letters, numbers, logos, brand marks, name tags, lanyards, badges, or watermarks, on clothing or anywhere else.
4. **No other people.** The person is alone in the frame. No cars or parked cars behind them.
5. **No real people.** Never use a photo of a real person as a reference, only the Cast profile photo named in the section. No face may look like a real or famous person.
6. **No religious or costume dress.** No clerical collars, robes, stoles, or traditional dress. Small plain jewelry is fine, with no symbols.
7. **Respectful images.** Follow `promptRules` in `photos/ethnicities.json`: everyday modern clothing, natural variety within each group, friendly natural expression, and no resemblance to any real or famous person.
8. **Photo ethnicity goes nowhere else.** Do not put it in file names, captions, or image metadata.
9. **Do not change the repository.** No Cast files, profile photos, reference pack, manifests, branches, or commits.

## The prompt

Attach the person's profile photo and the entrance reference, then use this prompt. Fill in the parts in braces.

```
Use case: identity-preserve. References: the attached profile photo of {name}, and the
attached Rock Solid Church Phoenix campus entrance.
Outdoor staff photo of this same {age}-year-old {man or woman}, {group prompt}.
Preserve the exact facial identity from the profile photo: face shape, eyes, nose, mouth,
skin tone, hair color, facial hair, and glasses. {look}
Wearing {clothing}.
Standing outside the church's main entrance on the pale concrete approach, beside a low
warm-white stucco planter with gravel, agaves, and a palo verde tree. The stucco walls,
charcoal canopy, and oak soffit are behind them, softly out of focus. Preserve the
building's materials from the reference.
Late morning, open shade at the canopy edge, soft even light on the face, gentle rim of
sunlight on hair and shoulders. Eye level, portrait lens look, wide aperture.
Square image, somewhat zoomed out, about waist up, person centered. Top of the head about a
third of the way down, face and shoulders in the middle third, soft background above.
Shoulders turned slightly to camera left, face and eyes to the camera. Natural, friendly smile.
Alone in the frame. No text, signs, logos, name tags, lanyards, cars, or watermarks.
No resemblance to any real or famous person. Facial likeness is the overriding constraint.
```

`{group prompt}` is the person's photo ethnicity wording from `groups[].prompt` in `photos/ethnicities.json`. `{look}` and `{clothing}` come from the person's section.

## Saving the images

1. **One folder.** Save all the final images in one folder of your own, outside the repository, apart from the profile photos.
2. **Exact names.** Name each image as its **File** says, such as `foster-pete.jpg`. Save a JPEG, exactly 1536 by 1536 pixels, quality 90, sRGB, with image metadata removed. Keep it under 600 KB.
3. **A manifest.** In the same folder, write `manifest.json` with one entry per image, in the order of this page:

```json
{
  "model": "the image model you used",
  "images": [
    {
      "file": "foster-pete.jpg",
      "person": "foster-pete",
      "name": "Pete Foster",
      "width": 1536,
      "height": 1536,
      "sha256": "the file's SHA-256, lowercase hex",
      "reference": "photos/cast/foster-pete.jpg",
      "place": "photos/references/church/main-entrance-close.jpg",
      "seed": null,
      "prompt": "the exact prompt you used"
    }
  ]
}
```

`person` is the file name without `.jpg`: the household key and the member key. `reference` is the profile photo you attached, and `place` is the church reference. `seed` is the seed if your tool gives one, otherwise `null`. If you remade an image, list only the final one.

## How the images reach Rock

This is for context; you do not do it. A Rock sample data session installs the images into the repository. After that, no code changes are needed. The builder packs every image listed in `photos/staff/manifest.json` into every dataset that has that person. The loader's `Photos` part writes each one as a Rock `BinaryFile` of the **Unsecured** binary file type (`C1142570-8CD6-4A20-83B1-ACB47C1CD377`), in database storage, not temporary, and sets the person's `StaffPhoto` attribute to that file's Guid. Unsecured means a website visitor who is not signed in can see it, which a public staff page needs. The person's profile photo (`Person.PhotoId`) does not change. A page that wants the 3:1 version crops it with CSS, so only the square is stored.

A staff member with no installed photo loads with an empty Staff Photo.

## Check every image

Look at every image before you save it. Remake any that fail.

1. It is exactly 1536 by 1536.
2. **The 3:1 crop works.** Crop rows 512 to 1024 (the middle third). That 1536 by 512 band shows the whole face, chin to the top of the hair, and both shoulders, with nothing important cut.
3. The face matches the reference profile photo: same person, same age, same glasses and facial hair.
4. The place is the Phoenix entrance from the reference, from the same spot and direction as the other photos, softly out of focus.
5. The light is late-morning open shade, soft on the face, with the same warmth as the others.
6. The person is centered, about waist up, shoulders turned to camera left, with a natural, friendly expression.
7. Clothing matches the section, has no text or logos, and is not ivory or white.
8. No signs, logo, letters, other people, or cars anywhere, even blurred enough to read.
9. No distorted features, odd ears, uneven eyes, glasses glare that hides the eyes, or odd hands.
10. Set the new image beside three earlier ones. It looks like the same photo session.

## When you are done

Write a short handback, `handback.md`, in the same folder. Say:

1. The folder's full path.
2. How many images you made, and confirm every image you were asked for is there under its exact name.
3. How many you remade and why.
4. Anything you could not do, such as a seed your tool did not give.

A Rock sample data session installs these into the repository.

## The staff

Each section lists the person's title, age, photo ethnicity, reference profile photo, look, and clothing. "(new)" marks a profile photo that the profile photo hand-off makes first. "Look" names features to keep from the reference. For new Cast people, the look is also a suggestion for their profile photo, so make both match.

### 1. Pete Foster

- **Title:** Senior Pastor. **Age:** 50. **Photo ethnicity:** black.
- **Reference:** `photos/cast/foster-pete.jpg`. **File:** `foster-pete.jpg`.
- **Look:** Very short cropped hair, black rectangular glasses, clean-shaven, broad smile.
- **Clothing:** Charcoal blazer over an open-collar light blue shirt.

### 2. Ted Decker

- **Title:** Outreach Pastor. **Age:** 38. **Photo ethnicity:** white.
- **Reference:** `photos/cast/decker-ted.jpg`. **File:** `decker-ted.jpg`.
- **Look:** Short dark brown side-swept hair, trimmed brown beard, black rectangular glasses, cheerful.
- **Clothing:** Navy quarter-zip sweater over a collared shirt.

### 3. Alisha Marble

- **Title:** Executive Assistant. **Age:** 48. **Photo ethnicity:** white.
- **Reference:** `photos/cast/marble-alisha.jpg`. **File:** `marble-alisha.jpg`.
- **Look:** Long layered warm light-brown hair, dark eyes, arched eyebrows, broad warm smile.
- **Clothing:** Deep teal blouse with a soft charcoal cardigan.

### 4. Daniel Peak

- **Title:** Director of Communications. **Age:** 36. **Photo ethnicity:** white.
- **Reference:** `photos/cast/peak-daniel.jpg`. **File:** `peak-daniel.jpg`.
- **Look:** Short dark brown hair, neatly trimmed beard, black rectangular glasses.
- **Clothing:** Muted blue button-down shirt, open collar, under a charcoal knit jacket.

### 5. Isaac Silva

- **Title:** Student Pastor. **Age:** 29. **Photo ethnicity:** white.
- **Reference:** `photos/cast/silva-isaac.jpg`. **File:** `silva-isaac.jpg`.
- **Look:** Keep his hair and face from the reference. The most casual of the staff, but still neat.
- **Clothing:** Dark denim overshirt over a charcoal crewneck tee.

### 6. Greg Holloway

- **Title:** Executive Pastor. **Age:** 55. **Photo ethnicity:** white.
- **Reference:** `photos/cast/holloway-greg.jpg` (new). **File:** `holloway-greg.jpg`.
- **Look:** Short salt-and-pepper hair, clean-shaven, calm and steady.
- **Clothing:** Navy blazer over an open-collar light gray shirt.

### 7. Naomi Castillo

- **Title:** Worship Director. **Age:** 34. **Photo ethnicity:** hispanic-latino.
- **Reference:** `photos/cast/castillo-naomi.jpg` (new). **File:** `castillo-naomi.jpg`.
- **Look:** Long dark wavy hair worn down, bright and expressive smile.
- **Clothing:** Deep teal blouse with simple small earrings.

### 8. Rachel Brooks

- **Title:** Kids Director. **Age:** 41. **Photo ethnicity:** white.
- **Reference:** `photos/cast/brooks-rachel.jpg`. **File:** `brooks-rachel.jpg`.
- **Look:** Hazel eyes and small dimples, as in the reference. Warm and energetic.
- **Clothing:** Soft coral cardigan over a charcoal top.

### 9. Jamal Pierce

- **Title:** Groups Pastor. **Age:** 42. **Photo ethnicity:** black.
- **Reference:** `photos/cast/pierce-jamal.jpg` (new). **File:** `pierce-jamal.jpg`.
- **Look:** Short hair with a neat fade, short trimmed beard, easy smile.
- **Clothing:** Charcoal crewneck sweater over a light blue collared shirt.

### 10. Dee Navarro

- **Title:** Care Director. **Age:** 61. **Photo ethnicity:** hispanic-latino.
- **Reference:** `photos/cast/navarro-dee.jpg` (new). **File:** `navarro-dee.jpg`.
- **Look:** Chin-length silver hair, gentle and kind expression.
- **Clothing:** Heather blue cardigan over a soft gray top, with a simple plain necklace.

### 11. Priya Raman

- **Title:** Finance Director. **Age:** 46. **Photo ethnicity:** south-asian.
- **Reference:** `photos/cast/raman-priya.jpg` (new). **File:** `raman-priya.jpg`.
- **Look:** Shoulder-length black hair, poised and warm.
- **Clothing:** Tailored charcoal blazer over a deep teal top.

### 12. Ray Delgado

- **Title:** Facilities Director. **Age:** 58. **Photo ethnicity:** hispanic-latino.
- **Reference:** `photos/cast/delgado-ray.jpg` (new). **File:** `delgado-ray.jpg`.
- **Look:** Short graying hair, gray mustache, friendly and practical.
- **Clothing:** Solid navy quarter-zip pullover with no logo.

### 13. Kelsey Morrow

- **Title:** Production Director. **Age:** 31. **Photo ethnicity:** east-asian.
- **Reference:** `photos/cast/morrow-kelsey.jpg` (new). **File:** `morrow-kelsey.jpg`.
- **Look:** Straight black hair in a low ponytail, relaxed confident smile.
- **Clothing:** Black crewneck top under a charcoal utility jacket.

### 14. Aaron Mitchell

- **Title:** Phoenix Campus Pastor. **Age:** 45. **Photo ethnicity:** middle-eastern-north-african.
- **Reference:** `photos/cast/mitchell-aaron.jpg` (new). **File:** `mitchell-aaron.jpg`.
- **Look:** Short dark hair, short neat beard, warm brown eyes, welcoming smile.
- **Clothing:** Light blue button-down shirt, open collar, under a navy sweater vest or knit.

### 15. Andre Haywood

- **Title:** Peoria Campus Pastor. **Age:** 48. **Photo ethnicity:** black.
- **Reference:** `photos/cast/haywood-andre.jpg` (new). **File:** `haywood-andre.jpg`.
- **Look:** Close-cropped hair, clean-shaven, broad confident smile.
- **Clothing:** Charcoal suit jacket over an open-collar white-and-blue striped shirt.

### 16. Dana Whitlock

- **Title:** Gilbert Campus Director. **Age:** 40. **Photo ethnicity:** white.
- **Reference:** `photos/cast/whitlock-dana.jpg` (new). **File:** `whitlock-dana.jpg`.
- **Look:** Shoulder-length light brown hair, friendly open expression.
- **Clothing:** Deep teal button-front shirt.

### 17. Walt Hargrove

- **Title:** Scottsdale Campus Pastor. **Age:** 63. **Photo ethnicity:** white.
- **Reference:** `photos/cast/hargrove-walt.jpg` (new). **File:** `hargrove-walt.jpg`.
- **Look:** Neat white hair, clean-shaven, wire-rimmed glasses, gentle smile.
- **Clothing:** Brown tweed sport coat over an open-collar light blue shirt.

### 18. Tina Vu

- **Title:** Chandler Kids Director. **Age:** 37. **Photo ethnicity:** southeast-asian.
- **Reference:** `photos/cast/vu-tina.jpg` (new). **File:** `vu-tina.jpg`.
- **Look:** Dark hair in a shoulder-length bob, bright cheerful smile.
- **Clothing:** Deep teal cardigan over a charcoal top.

### 19. Rafael Ibarra

- **Title:** Goodyear Campus Pastor. **Age:** 35. **Photo ethnicity:** hispanic-latino.
- **Reference:** `photos/cast/ibarra-rafael.jpg` (new). **File:** `ibarra-rafael.jpg`.
- **Look:** Short dark hair, short beard, energetic smile.
- **Clothing:** Medium-wash denim button-down shirt, sleeves out of frame.

### 20. Leah Tsosie

- **Title:** Tempe Worship Leader. **Age:** 32. **Photo ethnicity:** native-american.
- **Reference:** `photos/cast/tsosie-leah.jpg` (new). **File:** `tsosie-leah.jpg`.
- **Look:** Long straight dark hair worn down, warm relaxed smile.
- **Clothing:** Rust-brown corduroy jacket over a charcoal tee.

### 21. Joel Park

Photos 21 to 23 were asked for separately, on 2026-10-29, when the church's pastors became all men (decision 203). They use the same look as the other 20.

- **Title:** Gilbert Campus Pastor. **Age:** 46. **Photo ethnicity:** east-asian.
- **Reference:** `photos/cast/park-joel.jpg` (new). **File:** `park-joel.jpg`.
- **Look:** Short black hair neatly parted, clean-shaven, no glasses, calm warm smile.
- **Clothing:** Charcoal quarter-zip sweater over a light blue collared shirt.

### 22. Josiah Faleolo

- **Title:** Chandler Campus Pastor. **Age:** 34. **Photo ethnicity:** pacific-islander.
- **Reference:** `photos/cast/faleolo-josiah.jpg` (new). **File:** `faleolo-josiah.jpg`.
- **Look:** Short dark curly hair, short beard, broad bright smile.
- **Clothing:** Deep teal button-front shirt, sleeves rolled out of frame.

### 23. Miles Okafor

- **Title:** Tempe Campus Pastor. **Age:** 31. **Photo ethnicity:** black.
- **Reference:** `photos/cast/okafor-miles.jpg` (new). **File:** `okafor-miles.jpg`.
- **Look:** Close-cropped hair, neat short beard, bright easy smile.
- **Clothing:** Olive overshirt over a charcoal crewneck tee.
