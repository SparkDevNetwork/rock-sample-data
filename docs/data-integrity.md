# Data Integrity

How the sample data fills Rock's Tools > Data Integrity pages, and why. The decisions are D-integrity-1 to D-integrity-14 in [Decisions](decisions.md). Every designed problem lives in `configuration/data-integrity.json`. The work is issue 2.

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
| Reports: Individuals with Duplicate Phone Numbers | People who share a phone number with someone else | Families who share a home phone, the Cast's duplicates, and three designed shared numbers (decision D-integrity-12) |
| Workflows | Rock's Data Integrity workflows: Person Data Error, Photo Request, DISC Request, and Request Assessment | Person Data Error and Photo Request (decisions D-integrity-5 and D-integrity-6) |
| Location Editor | Addresses by street or city, or every address with no map point (Not Geocoded) | Failed addresses (decision D-integrity-4) |
| Photo Requests | The Photo Request group, and Verify Photos for uploads that wait | Photo requests (decision D-integrity-6) |
| Data Integrity Settings | Gender auto-fill and the six Data Automation rules | The rules, configured and off (decision D-integrity-7) |
| NCOA | A National Change of Address run's settings, and its Results page of moves that wait for a staff member | One NCOA run (decision D-integrity-13) |
| Connection Status Changes | Connection status changes in a date range, this year by default, and people made in that range | The Cast's status history and everyone's record dates (decisions D-integrity-1, D-integrity-2, and D-integrity-10) |

## Record dates

Rock's Pending report shows the day each record was made, and its Connection Status Changes report lists every person made in its range as new. Every person used to carry the load's own time, so both pages showed the whole Cast as new on install day.

Now every record has the date it was made (decisions D-integrity-1 and D-integrity-10):

1. A record was made on its household's first visit (`attendance.started`). A child born later was added three weeks after birth.
2. A Pending record has the date of the form that made it (see below).
3. A Cast household with no first visit takes the date in `recordDates.households`, or else its first record. A first record in the history window's first eight weeks shows the household was already coming, so its record was made in the years before. So was the record of a household with nothing written for it, as in a load that leaves parts out.
4. A generated household with no first visit fills a month of the history window. Each month, latest first, takes households until its new records reach source-1's rate of 1.17% of active people, in source-1's busier and quieter months. It takes households whose first record falls in that month first, made by that record, such as a first gift. Then it takes households with no record at all, and then households whose first record comes later. Every household left over was made in the 12 years before the window, more often recently.
5. No record is made after its person's first record: a check-in, gift, group membership, request, registration, note, login, workflow, or History line Rock writes as it happens.

Each family takes its first member's date. Rock's admin person, Alisha Marble, keeps the time Rock made it (decision 177).

Rule 4 keeps the new records level. A household that gives twice a year shows its first gift a few months into the window, but it is not new, so it does not have to be made then.

Communication list memberships, prayer requests, and History lines that Rock dates by an earlier event, such as a baptism, do not count as a first record. Other parts date them without a first visit, so some Cast people have list memberships or prayer requests from before their family came.

## Record sources

Rock's Record Source says where a record came from. Every record has one (decision D-integrity-10), from how it was made:

| How the record was made | Record Source |
|---|---|
| A first visit with a child under 18 | Check-in |
| A first visit by adults | External Website, the website's connection card |
| A child born later | Check-in |
| A first gift, group sign-up, event registration, connection request, benevolence request, login, or workflow | Giving, Group Registration, Event Registration, Serving Connection, Benevolence Request, External Website, or Workflow |
| A Pending record | Its form's source |
| Before the history window, before the first record, or by a note | A mix in `recordDates.sources.earlier`: mostly Check-in, External Website, and Family Registration |
| A record with nothing else written for it | A mix in `recordDates.sources.noRecord`: External Website, Prayer, and Mobile App |

## Visit dates

Rock's Family Analytics job fills each person's First Visit and Second Visit when its Set Visit Dates setting is on. The load turns it on and writes the visits as the job would leave them (decision D-integrity-11):

1. A person whose record is older than the history window, and who comes to services, or an adult whose family's children do, gets the visits they made back then. First Visit is the first Sunday on or after their record's date. Second Visit is one to five weeks later. Both are at the family's service time.
2. Then the load runs Rock's own visit dates procedure, as of the job's last run. It fills everyone who first came inside the window from their check-ins.

Rock's procedure gives an adult the first check-in of their family's children, so an adult can get a Second Visit from a child's check-in the same morning. The load keeps that as Rock writes it. The procedure also writes an empty First Visit for an adult whose family has no child's check-in, and Rock Cleanup deletes every empty attribute value each night, so the load leaves none.

## Pending records

Rock's web forms make a new person Pending, as a Prospect, and set the form's Record Source, so staff review the record before it counts (decision D-integrity-8). Nobody reviewed these:

| Person | Form | Story |
|---|---|---|
| Jack Harper, Dale Tucker | Group Registration | Signed up online for the Jenkins Group three years ago. Jamal later marked them Connected. |
| Will Jones | External Website | Made an account four years ago, then joined the Jenkins Group. |
| John Smith (the other one) | External Website | Made an account two years ago, and asked online to join the Gilbert Group ten days ago. |
| Jon Smith's two duplicates | Group Registration, Event Registration | Jon signed up as John. Jamal's merge request covers both. |
| Jon Smith | External Website | Filled out a connection card seven years ago. Alisha marked him Engaged once he joined a serving team. |

Jamal also reported Jack's Pending record as a data error, which waits for Alisha.

## Self-inactivations

Rock's Email Preferences page lets a person say they are no longer involved, pick a reason, and write a note. Rock inactivates them with the reason and note, and marks the record Self Inactivated for review. An adult in one family inactivates the whole family (decision D-integrity-3).

| Person | Reason | When | Note |
|---|---|---|---|
| Erin Murphy, for Patrick and Liam too | Moved | Two years ago, two months after they moved to Ohio | "We moved to Columbus for Patrick's job, and we found a church home here..." |
| Pam Keller, for Doug too | No Longer Attending | 23 days ago | "We've been going to a church near our daughter in Mesa for years now..." |

The Kellers are a Cast household that came from 15 to 10 years ago. Nobody changed their records, so they stayed active until Pam used the link at the bottom of a church email. Each self-inactivated person has five History lines from the change. Every inactive record now carries its Record Status Reason, which only deceased people had.

## Connection status history

Staff move people along by connection status, and Rock writes each change to the person's History, which the Connection Status Changes report reads (decision D-integrity-2). Fifteen Cast people have changes:

| People | Changes | Who |
|---|---|---|
| The Shaw family | Known to Connected, 150 days ago | Alisha Marble |
| Josh Sanders | Known to Connected | Alisha Marble |
| Jack Harper, Will Jones, Dale Tucker | Prospect to Connected | Jamal Pierce |
| Jon Smith | Prospect to Engaged, five years ago | Alisha Marble |
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

## Shared phone numbers

Rock's Individuals with Duplicate Phone Numbers report lists every record with a number another record has. Families who share a home phone and the Cast's duplicate records were already there. Three more numbers show common ways a number ends up on two records (decision D-integrity-12):

| Number on | Story |
|---|---|
| Doug and Pam Keller, and the Colemans | The Kellers dropped their home phone after they stopped coming, and the phone company later gave the number to the Colemans. The Kellers' inactive records still carry it. |
| Bill and Cathy Price | They share one cell phone, so the office typed Bill's number on Cathy's record too. |
| Joy Rivera | Michael gave his own cell number when he registered Joy at check-in, so her record carries it as her mobile. |

Rock's duplicate finder matches a number only between people of the same gender, so none of these makes a pair in the Duplicate Finder.

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

General keeps Rock's gender auto-fill confidence of 99.9. Rock's Data Automation job runs every Tuesday at 4 AM. With every rule off, it only fills genders it is sure of, and it finds none to fill. NCOA's settings are saved with the NCOA run, below.

## NCOA

A church runs NCOA (the post office's National Change of Address list) from Rock's NCOA page. It sends its addresses to TrueNCOA and imports the results file, and Rock processes the moves. Alisha imported one run 41 days before install day (decision D-integrity-13). The page kept her settings: the Active People data view, a minimum move distance of 25 miles, the reason Moved, and previous addresses marked for invalid addresses and moves older than 18 months.

Rock sent every active person with a home address, and kept one NCOA History row for each. Most did not move. The moves are:

| Move | How many | What Rock did |
|---|---|---|
| A family moved less than 25 miles | The Woods, and 1% of the generated families Rock sent | Added the new home as Home and the mailing address, kept the old one as Previous, and wrote History for the family and each member |
| A family moved 25 miles or more | The Barlows, and 0.2% of the generated families | The same, and inactivated each member with the reason Moved and Rock's note: "Received a Change of Address (NCOA) notice that was for more than 25 miles away." |
| An adult moved out of a family of two or more | Andrew Kim, and 0.3% of the generated families: an adult child, or else the youngest adult | Nothing. The move waits on Rock's NCOA Results page for a staff member |

Each new home comes from the address pool: one near the family's campus, or for a move away, one from the pool's far list. The far list holds homes at least 30 miles from every campus, in Wickenburg, Tonopah, Gila Bend, Wittmann, and Aguila. A generated family moves away only if it has had no check-in or gift for a year and has no group, list, request, or flow that Rock would inactivate with it, so nothing else changes.

Rock's move away check also compares the new home with each campus, but it compares meters with miles, so every move past the minimum counts as away. Every far home is far from every campus too, so both readings agree. TrueNCOA gives a move date as a year and month, which Rock does not read, so the rows have no move date, as Rock leaves them.

| Story | Move |
|---|---|
| The Woods | Moved across town. Their stories do not depend on their address. |
| Greg and Diane Barlow | Retired to Wickenburg and stopped coming. A new Cast household with no photos on purpose. |
| Andrew Kim | Moved into an apartment near his college. Rock still has him at home, and the move waits on the NCOA Results page. |

## How it is checked

- **Cast expectations** check every story after each load: each record's date against its first record, each date in the file, each Pending record's form and source, each self-inactivation's reason, notes, and History, each status change, each workflow's state and assignee, each photo reply, each shared email and phone number, each failed address, the NCOA settings and each Cast move, and each rule off.
- **Invariants** keep the rules Rock's code would: every record has a Record Source and is made before its first record, visit dates are system values in Rock's format, a workflow started from a profile has its person and initiator and waits on one assigned activity, a photo reply has a photo, a self-inactivated record has its note, and each Data Automation rule is off. The address rules allow a failed address only when it is locked, and the phone rule allows a number on two families only when one record is inactive.
- **Validation** grades the new records a month against source-1's 1.17% of active people, and the five shares of adults with each detail.
- **Invariants for NCOA** check that every processed move has its new Home and its old home as Previous, that a move Rock left for a staff member changed nothing, and that a family that moved away is inactivated with the run's reason and note and has nothing Rock would have inactivated with it.
- **Verify's family-analytics check** runs Rock's visit dates procedure with the other Family Analytics procedures, and it must change no First Visit or Second Visit.
- **Verify's data-automation check** reads each rule back with Rock's own settings classes, expects exactly what the load saved, and runs Rock's Data Automation job, which must change no person and no family.
- **Verify's process-workflows check** runs Rock's Process Workflows job, which leaves the waiting reports at Alisha's form.
- **`node cast/validate-cast.js`** checks every person, name, and date in `configuration/data-integrity.json`, that the Pending records match the Cast's Pending people, that a self-inactivation's household is inactive with its reason, that a shared number is on the records its households give it, with only one of those households active, and that each NCOA move is a Cast household, inactive with the run's reason only when it moved away.

## Record completeness

A real church's records have gaps, and Rock's reports and data views show them. Generated adults now have source-1's gaps (decision D-integrity-14):

| Detail | Adults who have it | How |
|---|---|---|
| Email | 74.5% | Drawn with each person, so nothing emails someone the church cannot reach |
| Mobile phone | 67.8% | Drawn with each person, so nothing texts someone the church cannot reach |
| Birth date | 74.6% | The person has an age, but Rock has no birth date |
| Marital status | 59.1% | Singles already went without one at the measured share. Couples and the singles who have one lose it at the rate that keeps the mix of known statuses, and a couple shares theirs |
| Home address | 74.4% | The family has a home, but Rock has no home address |

Planning still reads the facts Rock lacks, so a person keeps their age for a class, a spouse to give with, and a home a check is printed with. Rock checks an event's age limits against the birth date it has, so a person with none does not register for an event with age limits, and Marriage Date Night registers only couples Rock has as married. A home Rock does not have is never a small group's meeting place, and NCOA never sends it. Generated children and the Cast stay complete. Validation grades all five shares.

A Crowd duplicate record carries the email or mobile its person gave. Emails and mobiles are drawn before duplicates are made, so a duplicate only ever shares a contact its person has.
