# Data Integrity

How the sample data fills Rock's Tools > Data Integrity pages, and why. The decisions are D-integrity-1 to D-integrity-9 in [Decisions](decisions.md). Every designed problem lives in `configuration/data-integrity.json`. The work is issue 2.

## The idea

The sample data is clean on purpose, so Rock's Data Integrity pages were mostly empty. A real church always has work waiting there: records nobody reviewed, people who asked to be removed, addresses that will not map, and reports from staff. Each page now has a small, named set of problems, each tied to a Cast story, so a tester can see the page and read why each row is there. Emails stay on `example.org`, phones stay reserved, and addresses come from the address pool.

## What Rock has

Every page is under Tools > Reporting > Data Integrity.

| Page | What it shows | Filled by |
|---|---|---|
| Duplicate Finder, Merge Requests | Possible duplicate people, and merges asked for | The Cast's duplicates ([Duplicates](duplicates.md)) |
| Reports: Pending Individuals | People whose record is Pending, with the day it was made | Pending records (decisions D-integrity-1 and D-integrity-8) |
| Reports: Self Inactivated Individuals | Inactive people marked Self Inactivated, with their reason and notes | Self-inactivations (decision D-integrity-3) |
| Reports: Individuals with Duplicate Emails | People who share an email with someone else | Couples who share an email (decision D-integrity-9), and the Cast's duplicates |
| Reports: Individuals with Duplicate Phone Numbers | People who share a phone number with someone else | Families who share a home phone today. Designed shared numbers come in a later slice |
| Workflows | Rock's Data Integrity workflows: Person Data Error, Photo Request, DISC Request, and Request Assessment | Person Data Error and Photo Request (decisions D-integrity-5 and D-integrity-6) |
| Location Editor | Addresses by street or city, or every address with no map point (Not Geocoded) | Failed addresses (decision D-integrity-4) |
| Photo Requests | The Photo Request group, and Verify Photos for uploads that wait | Photo requests (decision D-integrity-6) |
| Data Integrity Settings | Gender auto-fill and the six Data Automation rules | The rules, configured and off (decision D-integrity-7) |
| NCOA | Results from a National Change of Address run | A later slice |
| Connection Status Changes | Connection status changes in a date range, this year by default, and people made in that range | The Cast's status history and record dates (decisions D-integrity-1 and D-integrity-2) |

## Record dates

Rock's Pending report shows the day each record was made, and its Connection Status Changes report lists every person made in its range as new. Every person used to carry the load's own time, so both pages showed the whole Cast as new on install day.

Now each Cast record has the date it was made (decision D-integrity-1):

1. A record was made on its household's first visit (`attendance.started`). A child born later was added three weeks after birth.
2. A Pending record has the date of the form that made it (see below).
3. A household with no first visit takes the date in `recordDates.households`, or else its first record.
4. No record is made after its person's first record: a check-in, gift, group membership, request, registration, note, login, workflow, or History line Rock writes as it happens.

Each Cast family takes its first member's date. Rock's admin person, Alisha Marble, keeps the time Rock made it (decision 177). Generated people keep the load's time until a later slice gives everyone a date.

Communication list memberships, prayer requests, and History lines that Rock dates by an earlier event, such as a baptism, do not count as a first record. Other parts date them without a first visit, so some Cast people have list memberships or prayer requests from before their family came.

## Pending records

Rock's web forms make a new person Pending, as a Prospect, and set the form's Record Source, so staff review the record before it counts (decision D-integrity-8). Nobody reviewed these:

| Person | Form | Story |
|---|---|---|
| Jack Harper, Dale Tucker | Group Registration | Signed up online for the Jenkins Group three years ago. Jamal later marked them Connected. |
| Will Jones | External Website | Made an account four years ago, then joined the Jenkins Group. |
| John Smith (the other one) | External Website | Made an account two years ago, and asked online to join the Gilbert Group ten days ago. |
| Jon Smith's two duplicates | Group Registration, Event Registration | Jon signed up as John. Jamal's merge request covers both. |
| Jon Smith | none | His record is older than Rock's Record Source. |

Jamal also reported Jack's Pending record as a data error, which waits for Alisha.

## Self-inactivations

Rock's Email Preferences page lets a person say they are no longer involved, pick a reason, and write a note. Rock inactivates them with the reason and note, and marks the record Self Inactivated for review. An adult in one family inactivates the whole family (decision D-integrity-3).

| Person | Reason | When | Note |
|---|---|---|---|
| Erin Murphy, for Patrick and Liam too | Moved | Two years ago, two months after they moved to Ohio | "We moved to Columbus for Patrick's job, and we found a church home here..." |
| Pam Keller, for Doug too | No Longer Attending | 23 days ago | "We've been going to a church near our daughter in Mesa for years now..." |

The Kellers are a Cast household that came from 15 to 10 years ago. Nobody changed their records, so they stayed active until Pam used the link at the bottom of a church email. Each self-inactivated person has five History lines from the change. Every inactive record now carries its Record Status Reason, which only deceased people had.

## Connection status history

Staff move people along by connection status, and Rock writes each change to the person's History, which the Connection Status Changes report reads (decision D-integrity-2). Fourteen Cast people have changes:

| People | Changes | Who |
|---|---|---|
| The Shaw family | Known to Connected, 150 days ago | Alisha Marble |
| Josh Sanders | Known to Connected | Alisha Marble |
| Jack Harper, Will Jones, Dale Tucker | Prospect to Connected | Jamal Pierce |
| Ted Decker | Known, Connected, Engaged, then Leading, ten years ago and after | Alisha Marble |
| Cindy Decker | Known, Connected, then Engaged | Alisha Marble |
| Derek and Kayla Ward | Known to Connected | Alisha Marble |
| Doug and Pam Keller | Known to Connected, 14 years ago | Alisha Marble |

## Person Data Error reports

Staff report a problem from a person's profile with Rock's Person Data Error workflow. Rock gives every report to its admin person, who is Alisha Marble, and waits for her resolution (decision D-integrity-5).

| Person | Reported by | State | What |
|---|---|---|---|
| Jack Harper | Jamal Pierce | Waiting | His record still says Pending. |
| Thomas Miller | Aaron Mitchell | Waiting | Probably a second record for Tom Miller. The Duplicate Finder lists the pair too. |
| Hannah Ellis | Ted Decker | Waiting | Their new home does not show on the map. |
| Linda Hayes | Pete Foster | Resolved | Two copies of every email: she and Bob share one address. No change. |
| Corrie Stone | Jamal Pierce | Resolved | Her nick name was Cora. Alisha set it to Corrie, which her History shows. |

## Photo requests

Staff send Rock's Photo Request workflow from a person's profile. It emails a link to upload a photo or opt out. An upload puts the person in Rock's Photo Request group as Pending until staff verify it on Verify Photos (decision D-integrity-6). Alisha sent five, for the church photo directory:

| Person | Reply |
|---|---|
| Bob Hayes, Deb Bell | Uploaded, waiting on Verify Photos |
| Larry Bell | Uploaded and verified |
| Dick Ross | Opted out |
| Peggy Carter | No reply yet |

An upload is the person's own Cast photo, dated the day they sent it, so no new image is needed.

## Couples who share an email

Bob and Linda Hayes share one email address, and so do Dick and Joan Ross (decision D-integrity-9). Rock's Individuals with Duplicate Emails report lists them. Rock's duplicate finder scores a couple about 36, below the Duplicate Finder's 60, because their first names and genders differ, so the Duplicate Finder does not list them. Each spouse still gets their own copy of each church email.

## Failed addresses

Rock's address service could not match two new Cast homes, the Ellises' and the Ramirezes', because each street is too new for its data (decision D-integrity-4). Rock saved No Match as both results, with no success dates and no map point. Alisha checked Location Locked on each, which stops Rock's Location Services Verify job from sending the address again after its 200-day retry period. Sample addresses never go to an address service (decision 316). Rock's Location Editor lists both under Not Geocoded, waiting for someone to place the pin.

## Data Integrity Settings

Rock's six Data Automation rules are configured the way a church sets them up, and all are off (decision D-integrity-7, Jon's choice). Each is saved as Rock's settings page saves it: its settings class as JSON in its system setting, with this database's Ids.

| Rule | Configured |
|---|---|
| Reactivate People | A gift, worship, registration, login, or prayer request in 90 days, or small group or serving attendance |
| Inactivate People | None of those in 500 days, counting kids check-in, for records older than 180 days |
| Update Family Campus | By attendance, three times in a row, unless staff changed the campus in 90 days |
| Move Adult Children | Graduates at 18, keeping the home address and phone |
| Update Connection Status | Leading from the Already Leading data view |
| Update Family Status | Nothing, since Rock's two family statuses have no family views |

General keeps Rock's gender auto-fill confidence of 99.9. Rock's Data Automation job runs every Tuesday at 4 AM. With every rule off, it only fills genders it is sure of, and it finds none to fill. NCOA's settings are saved with an NCOA run, which comes in a later slice.

## How it is checked

- **Cast expectations** check every story after each load: each record's date against its first record, each Pending record's form and source, each self-inactivation's reason, notes, and History, each status change, each workflow's state and assignee, each photo reply, each shared email, each failed address, and each rule off.
- **Invariants** keep the rules Rock's code would: a workflow started from a profile has its person and initiator and waits on one assigned activity, a photo reply has a photo, a self-inactivated record has its note, and each Data Automation rule is off. The address rules allow a failed address only when it is locked.
- **Verify's data-automation check** reads each rule back with Rock's own settings classes, expects exactly what the load saved, and runs Rock's Data Automation job, which must change no person and no family.
- **Verify's process-workflows check** runs Rock's Process Workflows job, which leaves the waiting reports at Alisha's form.
- **`node cast/validate-cast.js`** checks every person, name, and date in `configuration/data-integrity.json`, that the Pending records match the Cast's Pending people, and that a self-inactivation's household is inactive with its reason.

## What comes next

Issue 2's later slices, after Real Crowd Names (issue 42): a created date and Record Source for everyone, one NCOA run, first and second visit dates from Rock's Set Visit Dates, and a few designed shared phone numbers. Last, record completeness: Crowd adults leave some details blank at the shares measured at source-1, so about 74.5% have an email, 67.8% a mobile phone, 74.6% a birth date, 59.1% a marital status, and 74.4% a home address. The Cast stays complete.
