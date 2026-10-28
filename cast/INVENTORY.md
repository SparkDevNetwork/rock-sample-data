# Cast Inventory

This is what we kept from the legacy sample data (`Documentation/sampledata_1_14_1.xml`), what we added, and what the Cast still needs. Run `node validate-cast.js` after any Cast change.

## What came over

- **38 households** (36 families and 2 businesses), **55 people**, and **14 groups**.
- Every Guid is unchanged, so anything that refers to these people today still finds them. Guids are now uppercase.
- Stories, notes, relationships, security roles, logins, follows, connection requests, giving, and attendance all came over. The legacy notes live in [notes/](notes/README.md), in the same format as every other Cast note.
- Dates are now relative. The legacy file kept ages current as of mid 2023, so a 2012 date became "11 years before the anchor."
- Emails at `fakeinbox.com` (a real disposable-mail service) now use `example.org`. Staff emails at `rocksolidchurchdemo.com` are unchanged.

| Household | Members (status) | Main test purpose |
|---|---|---|
| [decker](households/decker.json) | Ted (Leading), Cindy (Engaged), Noah (Connected), Alex (Connected) | Primary demo family used in documentation and screenshots. |
| [jones-ben](households/jones-ben.json) | Ben (Known), Brian (Connected) | Divorced parent with joint custody; the child belongs to two families. |
| [simmons](households/simmons.json) | Jim (Engaged), Sarah (Engaged) | Blended family with a child shared with another household. |
| [jackson](households/jackson.json) | Mariah (Engaged), Maddie (Engaged) | Single parent whose child has a different last name. |
| [lowe](households/lowe.json) | Craig (Known), Trish (Known) | Visitor household that gives regularly. |
| [marble](households/marble.json) | Alisha (Leading), Bill (Leading) | Rock administrator with many security roles. |
| [miller-tom](households/miller-tom.json) | Tom (Engaged) | Original half of a duplicate pair for merge testing. |
| [miller-thomas](households/miller-thomas.json) | Thomas (Prospect) | Duplicate half of a merge-testing pair (same email and phone, different address). |
| [foster](households/foster.json) | Pete (Leading), Pamela (Engaged) | Senior pastor with pastoral and staff security roles. |
| [peak](households/peak.json) | Daniel (Engaged) | Staff member with website and communication administrator roles. |
| [hanks](households/hanks.json) | Sam (Engaged) | Engaged non-staff adult who could be recruited to serve. |
| [michaels](households/michaels.json) | Jenny (Engaged) | Volunteer with the Staff-Like Users role. |
| [smith-paul](households/smith-paul.json) | Paul (Connected) | Young adult living alone. |
| [mcclintock](households/mcclintock.json) | Ty (Connected) | Young adult living alone. |
| [dexter](households/dexter.json) | Frank (Engaged) | Failed background check that needs review. |
| [greggs](households/greggs.json) | Bob (Engaged), Lorraine (Engaged), Tim (Engaged), Jordan (Engaged) | Grieving family with sensitive alert notes. |
| [greggs-jenny](households/greggs-jenny.json) | Jenny (Known) | Deceased child record kept in its own family. |
| [webb](households/webb.json) | Marty (Prospect), Deb (Prospect) | Married couple who are still Prospects but already in a small group. |
| [richards](households/richards.json) | Dan (Prospect) | Single Prospect who is already in a small group. |
| [coffee](households/coffee.json) | Phil (Engaged) | Invited-by-a-friend journey from first visit to baptism. |
| [song](households/song.json) | Allie (Engaged) | The inviting friend in an invitation story. |
| [stone](households/stone.json) | Corrie (Connected) | Middle initial and a nickname that differs from the first name. |
| [evans](households/evans.json) | Helen (Prospect) | Event guest who needs follow-up. |
| [hansen](households/hansen.json) | Sean (Prospect) | Open connection request that needs a connector. |
| [turner](households/turner.json) | Nancy (Prospect) | Open connection request that needs a connector. |
| [gilbert](households/gilbert.json) | Brian (Prospect), Wendy (Prospect) | Couple with matching connection requests to serve together. |
| [harper](households/harper.json) | Jack (Connected) | Pending record that needs review. |
| [jones-will](households/jones-will.json) | Will (Connected) | Pending record that needs review. |
| [tucker](households/tucker.json) | Dale (Connected) | Pending record that needs review. |
| [peterson](households/peterson.json) | Brian (Connected), Becky (Connected) | Married couple where one spouse has no email. |
| [sweeney](households/sweeney.json) | James (Connected), Nancy (Connected) | Married couple in the same small group. |
| [tennant](households/tennant.json) | Rob (Connected), Sheryl (Connected) | Married couple with different employers. |
| [smith-jon](households/smith-jon.json) | Jon (Engaged) | Duplicate detection: original record of a three-way duplicate. |
| [smith-john-duplicate-a](households/smith-john-duplicate-a.json) | John (Prospect) | Duplicate detection: same email and address, phone stored as a different type. |
| [smith-john-duplicate-b](households/smith-john-duplicate-b.json) | John (Connected) | Duplicate detection: same email and address, phone stored as a different type. |
| [smith-john-other](households/smith-john-other.json) | John (Prospect) | Duplicate detection: common name that must NOT be merged. |
| [cholla-creek-hardware](households/cholla-creek-hardware.json) | Len (Prospect) | Business record. |
| [saguaro-ridge-glass](households/saguaro-ridge-glass.json) | Bill (Prospect) | Business record linked to its owner. |

## New households (2026-10-15)

45 households and 126 people were added to fill the gaps below. Every name was chosen first; photo ethnicity was assigned afterward toward the target mix, so names and photo ethnicity are independent. Children's ages are on the anchor date.

| Household | Members (child age, status) | Main test purpose |
|---|---|---|
| [alvarez](households/alvarez.json) | Marco (Connected), Elena (Connected), Sofia 3 (Connected), Mateo 0 (Connected) | Newborn in the nursery and a preschooler, so two check-in areas for one family. |
| [bell](households/bell.json) | Larry (Engaged), Deb (Engaged) | Greeters (guest services in Phase 1). |
| [bennett](households/bennett.json) | Tom (Connected), Becca (Connected), Nick 13 (Connected) | Middle school student whose attendance drops in spring. |
| [brooks](households/brooks.json) | Nathan (Engaged), Rachel (Engaged), Ava 14 (Engaged), Caleb 11 (Engaged), Lily 8 (Engaged) | Three kids across the student and elementary check-in areas. |
| [carter](households/carter.json) | Peggy (Engaged) | Widow living alone, age 78. |
| [coleman](households/coleman.json) | Tony (Connected), Tiffany (Connected), Eli 9 (Connected), Zoe 4 (Connected) | Two working parents, two check-in rooms. |
| [cruz](households/cruz.json) | Jose (Participant), Gaby (Participant), Adrian 8 (Participant), Camila 6 (Participant) | Participants through an event registration (VBS). |
| [dawson](households/dawson.json) | Kevin (Connected), Tyler 12 (Connected), Emma 9 (Connected) | Single dad with every-other-weekend attendance. |
| [diaz](households/diaz.json) | Ricardo (Known), Patty (Known) | Holiday-only attenders. |
| [ellis](households/ellis.json) | Jordan (Known), Hannah (Known) | New couple: first visit about four months ago. |
| [fischer](households/fischer.json) | Mark (Connected), Julie (Connected), Ethan 17 (Connected), Grace 15 (Connected) | High school students, including a senior about to graduate. |
| [flores](households/flores.json) | Monica (Connected), Sebastian 10 (Connected), Lucia 5 (Connected) | Never-married single parent. |
| [garcia](households/garcia.json) | Daniel (Engaged), Maria (Engaged), Olivia 6 (Engaged), Bella 6 (Engaged), Leo 2 (Engaged) | Twins with the same birthday. |
| [gray](households/gray.json) | Emily (Connected) | Attends many different services (low primary-service share). |
| [hall](households/hall.json) | Bri (Participant) | Participant: one in-person touch (a serve day), no Sunday attendance. |
| [hayes](households/hayes.json) | Bob (Engaged), Linda (Engaged) | Senior couple with a long anniversary. |
| [hernandez](households/hernandez.json) | Gabe (Engaged) | Young single volunteer (worship team in Phase 1). |
| [hughes](households/hughes.json) | Matt (Engaged), Jess (Engaged), Henry 5 (Engaged), Charlotte 3 (Engaged), Jack 1 (Engaged) | Three kids in three check-in rooms, from nursery to kindergarten. |
| [jenkins](households/jenkins.json) | Gary (Engaged), Barb (Engaged), Austin 11 (Engaged) | Grandparents raising a grandchild. |
| [kelly](households/kelly.json) | Ashley (Known), Mason 7 (Known), Chloe 4 (Known) | Benevolence request (Phase 1). |
| [kim](households/kim.json) | David (Connected), Susan (Connected), Drew (Connected) | Adult child living at home (adult role in the family). |
| [lee](households/lee.json) | James (Engaged), Sarah (Engaged), Ben 16 (Engaged), Abby 14 (Engaged), Sam 11 (Engaged), Libby 8 (Engaged), Joey 5 (Engaged), Ruth 2 (Engaged) | Family of eight with kids in every check-in area. |
| [morgan](households/morgan.json) | Steve (Engaged), Karen (Engaged) | Quarterly giver with a year-end gift. |
| [murphy](households/murphy.json) | Patrick (Engaged), Erin (Engaged), Liam 12 (Engaged) | Inactive record with the Moved reason. |
| [nguyen](households/nguyen.json) | Michelle (Engaged) | Single adult who gives weekly online by card. |
| [ortiz](households/ortiz.json) | Luis (Connected), Teresa (Connected), Diego 13 (Connected) | Lapsed family: attendance and giving stopped about six months ago. |
| [patel](households/patel.json) | Ryan (Engaged), Megan (Engaged) | Gives every week but seldom attends in person. |
| [perry](households/perry.json) | Dennis (Known) | Married man attending without his spouse (spouse not in the database). |
| [price](households/price.json) | Bill (Engaged), Cathy (Engaged) | Top 1% giver with large quarterly gifts. |
| [ramirez](households/ramirez.json) | Carlos (Prospect), Ana (Prospect), Lucas 9 (Prospect), Mia 6 (Prospect) | First-time guests: first visit two weeks ago. |
| [ramos](households/ramos.json) | Tori (Known) | Group-first connection (Phase 1). |
| [rivera](households/rivera.json) | Mike (Engaged), Jen (Engaged), Joy 10 (Engaged) | Adopted child whose photo ethnicity is set on her own record. |
| [ross](households/ross.json) | Dick (Connected), Joan (Connected) | Seasonal attenders (winter visitors). |
| [russell](households/russell.json) | Hal (Engaged) | Homebound senior and widower. |
| [sanders](households/sanders.json) | Josh (Connected) | New believer baptized recently (steps in Phase 1). |
| [shaw](households/shaw.json) | Andy (Connected), Lauren (Connected), Owen 7 (Connected), Ella 4 (Connected) | Transfer family from another church. |
| [silva](households/silva.json) | Isaac (Engaged), Natalie (Engaged) | Every gift goes to two funds. |
| [sullivan](households/sullivan.json) | Chris (Engaged), Amanda (Engaged), Jake 15 (Engaged), Madison 12 (Engaged), Ben 3 (Engaged) | Blended family with half siblings. |
| [torres](households/torres.json) | Victor (Participant), Rosa (Participant), Valeria 16 (Connected) | Teen who is more engaged than her parents. |
| [ward](households/ward.json) | Derek (Connected), Kayla (Connected), Aiden 5 (Connected), Nora 1 (Connected) | One-time giver. |
| [washington](households/washington.json) | Marcus (Engaged), Denise (Engaged), Jasmine 18 (Engaged) | An 18-year-old who is still a child in the family. |
| [watson](households/watson.json) | Brandon (Known), Kristen (Known) | Year-end-only giver. |
| [wood](households/wood.json) | Jason (Connected), Melissa (Connected), Ryan 13 (Connected), Kaylee 10 (Connected) | Middle school student in the student check-in area. |
| [wright](households/wright.json) | Jerry (Engaged), Dot (Engaged) | Weekly cash giver. |
| [young](households/young.json) | Destiny (Participant), Jayden 1 (Participant) | Participant through a community event. |

With the new households the Cast reached 83 households and 181 people. Adult photo ethnicity is 40% White, 24% Hispanic or Latino, 12% Black, 6% East Asian, 4% Southeast Asian, 4% South Asian, 3% Native American, 3% Middle Eastern or North African, 2% Multiracial, and 2% Pacific Islander. 7 of 43 couples (16%) are mixed.

## New businesses (2026-10-19)

Four businesses were added so the main campus has six (decision 63). Each is tied to an existing Cast story, and its contacts are Cast people (decision 64). Cholla Creek Hardware also gained a contact, Larry Bell.

| Household | Contacts | Main test purpose |
|---|---|---|
| [copper-canyon-video](households/copper-canyon-video.json) | Dale Tucker | Business whose contact's record is still pending review. |
| [mesquite-tree-bookkeeping](households/mesquite-tree-bookkeeping.json) | Steve Morgan | Business that gives once a year, at year end, by mailed check. |
| [desert-bloom-landscaping](households/desert-bloom-landscaping.json) | Daniel and Maria Garcia | Business with two contacts, a married couple who own it together. |
| [javelina-wash-coffee](households/javelina-wash-coffee.json) | Isaac Silva | Business gift split between two funds. |

The Cast is now 87 households and 185 people, with 6 businesses.

## Campus pastors (2026-10-28)

Seven households were added so every campus has a named campus pastor who is always the same (decision D-staff-1). Each lives near their own campus. The six outside Phoenix load only in datasets with their campus (decision D-staff-2): Core 1 has the Mitchells, Core 3 adds the Haywoods and Whitlocks, and Full 7 has all seven.

| Household | Campus | Members (child age) | Main test purpose |
|---|---|---|---|
| [mitchell](households/mitchell.json) | Phoenix | Aaron (campus pastor), Beth, Jonah 12, Clara 9 | Phoenix's campus pastor: notes, welcomes, and Phoenix campus events. |
| [haywood](households/haywood.json) | Peoria | Andre (campus pastor), Keisha, Isaiah 16 | Cast family on a campus other than the main one. |
| [whitlock](households/whitlock.json) | Gilbert | Dana (campus pastor), Scott, Harper 10, Wes 7 | Campus pastor whose spouse is not on staff. |
| [hargrove](households/hargrove.json) | Scottsdale | Walt (campus pastor), June | Pastor who came over in the Scottsdale merger. |
| [vu](households/vu.json) | Chandler | Tina (campus pastor), Minh, Ellie 5, Theo 2 | Staff family with a toddler and a preschooler. |
| [ibarra](households/ibarra.json) | Goodyear | Rafael (campus pastor), Elisa, Nico 1 | Staff family with a baby in the nursery. |
| [tsosie](households/tsosie.json) | Tempe | Leah (campus pastor) | Single adult on staff, living alone. |

The Cast is now 94 households and 206 people. Their profile photos are still to be made.

## Issues to fix during Cast review

Each household file lists its own `reviewNotes`. The main themes:

1. **Deceased child status.** Jenny Greggs had an invalid legacy status ("inactive"). She is set to Known for now.

Resolved:

- Every Engaged person is in a small group or serving, or their family gives. Children carry their family's status, so a child is Engaged when an adult at home qualifies (decision 51). `node validate-cast.js` warns about anyone who falls out of step.
- Legacy addresses were real Phoenix homes. Every Cast home now uses a house number that does not exist, and households that shared an address (Helen Evans and the Simmonses, Nancy Turner and Dan Richards) are now neighbors on the same block.
- Every Cast adult has `photo.ethnicity`, and every Cast person has a delivered photo except those left without one on purpose.
- Every phone number now uses the reserved 555-0100 to 555-0199 range, keeping its area code. Numbers that were shared (spouses, duplicate records, the church office) are still shared.
- Every organization is fictional. "Ace Hardware" is now "Cholla Creek Hardware," and "Copper State Glass and Screen" is now "Saguaro Ridge Glass and Screen." Employers are now Sonoran Valley University, Sonoran Systems, Mesa Ridge Electronics, Palo Verde Technical College, Ocotillo Market, Valley Water District, and Valley Philharmonic. The ASU student group is now the Sonoran Valley University Student Group.

## Gaps

Where the Cast stands after the new households.

| Gap | Before | Now | Still needed |
|---|---|---|---|
| Children | 7 children, ages 6 to 10 | 55 children from newborn to 18, in every check-in area | |
| Teens and students | None alive | 13 ages 12 to 17, including students who come midweek | Per-person attendance, so a teen can attend more than their parents |
| Seniors | No one over 58 | 13 adults 63 and older, including two widowed | |
| Participant status | Nobody | 9 Participants (serve day, diaper drive, VBS, holiday visits) | |
| Giving variety | Weekly, monthly, one-time | Adds bi-weekly, quarterly, irregular, occasional, year-end, new, lapsing, lapsed, top 1%, cash, two-fund | Pledges (Phase 1) |
| Serving | One serving team (A/V) with 2 people | Stories name greeters, worship, first aid, and kids volunteers | Serving teams and schedules (Phase 1) |
| Feature coverage | Groups, connections, one registration | Stories set up benevolence, baptism steps, VBS registration, and care follow-up | Prayer, communications, steps, LMS, benevolence, reminders (Phase 1) |
| Addresses | Real homes | Real streets with house numbers that do not exist (see ../addresses/) | |

## Not migrated yet

These legacy sections are church configuration, not Cast. They move into `../configuration/` later.

- Campuses (an inactive "South" campus) and locations (Worship Center, A/V Booth).
- Registration templates and the summer camp registration instance, with fees, discounts, forms, and placement groups.
- Financial gateways.
