# Data views and reports: proposal

Status: proposal only. Nothing here is built. No loader code is written until Jon approves it through the coordinator. Decision placeholders (`D-reporting-N`) are written when it is approved.

**The idea in one line:** give Rock Solid Church a layered set of data views and reports that teaches good Rock practice and follows Ministry by Design. A few foundation data views hold the shared rules. Stage views show who has reached each stage of the path. Next-step views, filed by Ministry System, find who is ready for the next stage. Reports sit on top. Categories carry the security, and finance and safety are locked to their own roles.

Everything here works the same in Rock 20.0.10 and Rock 21.0.2. The models, filters, data select components, and security code match in both (section 1.6).

## Contents

1. [Who can see what: the parent authority finding](#1-who-can-see-what-the-parent-authority-finding)
2. [Ministry by Design](#2-ministry-by-design)
3. [The layers](#3-the-layers)
4. [Data views](#4-data-views)
5. [Reports](#5-reports)
6. [Categories and their security](#6-categories-and-their-security)
7. [Security walkthrough with the Cast](#7-security-walkthrough-with-the-cast)
8. [Implementation slices](#8-implementation-slices)
9. [Open questions](#9-open-questions)

## 1. Who can see what: the parent authority finding

Jon asked for this first. Line numbers are from `C:\Development\Rock-21`. Rock 20 has the same code, sometimes one line earlier.

### 1.1 The answer

Yes. **The category is the parent authority for both data views and reports.**

| Item | Parent authority | Source |
|---|---|---|
| Data view | Its category | `Rock/Model/Reporting/DataView/DataView.Logic.cs:62-73`; the cache copy matches at `Rock/Web/Cache/Entities/DataViewCache.cs:599-610` |
| Report | Its category | `Rock/Model/Reporting/Report/Report.Logic.cs:40-51` |
| Category | Its parent category | `Rock/Model/Core/Category/Category.Logic.cs:59-70`; cache at `Rock/Web/Cache/Entities/CategoryCache.cs:234` |
| Top category | The Category entity type's own rules, then the global default | `Rock/Data/Model.cs:349-362` |
| Global default | Nothing. If no rule matched anywhere, View and Tag are allowed and everything else is denied | `Rock/Security/Authorization.cs:508-511`; `Rock/Data/Model.cs:413-418` |

Rock walks up this chain one step at a time. It stops at the **first rule that matches the person**, allow or deny. It moves up only when nothing on the current item matched (`Rock/Security/Authorization.cs:1288-1406`).

**No category.** A data view or report with no category skips the category steps. It goes straight to the DataView (or Report) entity type's rules, then the global default. So the top of a category tree inherits from the *Category* entity type, but an uncategorized data view inherits from the *DataView* entity type.

### 1.2 What a new Rock install does by default

Read from the trusted base database:

- **Global default:** View is allowed to All Users. Edit and Administrate are allowed to RSR - Rock Administration, then denied to All Users.
- **Category, DataView, and Report entity types:** no View, Edit, or Administrate rules.

So a category with no rules of its own means **anyone who reaches the page can view every data view and report in it, and only Rock Administration can edit them.** Locking anything needs an explicit "allow these roles, then deny All Users".

Rock ships one example of exactly this. The report category Organization > Data Integrity allows View to RSR - Data Integrity Worker and RSR - Rock Administration, then denies All Users.

The Tools > Data Views and Tools > Reports pages have no rules of their own. The internal site lets in RSR - Rock Administration, RSR - Staff Workers, and RSR - Staff Like Workers only.

### 1.3 Running a report without rights to its data view

**A person who can view a report can run it, even if they cannot view its data view.**

- At run time, `Rock/Reporting/ReportingHelper.cs:113` checks only the report's View right. It checks nothing about the data view or the columns.
- The data view check is only in the Report Detail screen (`RockWeb/Blocks/Reporting/ReportDetail.ascx.cs`). At :1323 the report turns read-only, with "This report uses a data view that you do not have access to view". At :1471 the link to the data view is hidden.

So the person sees the results. They cannot edit the report or open its data view. Rock's own docs say the same and call it intentional: "report data bypasses the person's security rights" (Secure Report Data). Their example is a thank-you card list built on giving data, for someone with no finance access.

Report columns are not checked at run time either. A person attribute column shows its values even to someone who could not see that attribute on a person's profile. The attribute and data select security only limits which columns an author can **pick** (`ReportDetail.ascx.cs:1087` and :1126).

### 1.4 A data view that uses another data view

The "Existing Data View" filter (`OtherDataViewFilter`) and "Not in Existing Data View" filter **do not check the inner data view's security at run time** (`Rock/Reporting/DataFilter/OtherDataViewFilter.cs:432-463`). The data view filter's own check (`Rock/Model/Reporting/DataViewFilter/DataViewFilter.Logic.cs:385-420`) looks at the filter *component*, not at the data view it points to.

The check is in the UI only. The data view pickers list only data views the person can view, and hide any category the person cannot view, with everything under it (`Rock/ClientService/Core/Category/CategoryClientService.cs:136, 228, 252`; `Rock.Rest/Controllers/CategoriesController.Partial.cs:239, 276`).

So locking a data view **does not lock the data views and reports built on it.** Each one needs its own lock.

### 1.5 Every place a data view is used

| Use | Checks the person at run time? | Source |
|---|---|---|
| Report | Only the report's View right | `ReportingHelper.cs:113` |
| Data View Detail and Data View Results blocks | The data view's View right | `DataViewDetail.ascx.cs:784`; `Rock.Blocks/Reporting/DataViewResults.cs:182, 535` |
| Existing Data View filter | No | `OtherDataViewFilter.cs:432-463` |
| Group sync | No. A job runs it, with no person | `Rock/Model/Group/GroupSync/GroupSyncService.cs:97-135` |
| Communication lists | Only through group sync. The list's group has its own security | same |
| Communication flows (the audience, and the Entered Data View goal) | No. A job runs it | `Rock/Jobs/ProcessCommunicationFlows.cs:434, 696`, in both builds |
| Personalization segments | No. A job runs it | `Rock/Model/Cms/PersonalizationSegment/PersonalizationSegmentService.cs:164-171` |
| Persisted data view job | No | `Rock/Jobs/UpdatePersistedDataviews.cs:152` |
| Dynamic Data block | Does not use data views. It runs SQL, so its security is the page and block | `RockWeb/Blocks/Reporting/DynamicData.ascx.cs` |

Every one of these screens picks its data view through the category tree, so a person never sees one they cannot view there. That is a UI check only.

Saving a data view or report into a category needs Edit on that category (`DataViewDetail.ascx.cs:307`, `ReportDetail.ascx.cs:650`), unless the block's "Add Administrate Security to Item Creator" setting is on.

### 1.6 The finance building blocks

Rock ships security on the person giving components. These allow View only to RSR - Finance Administration, RSR - Finance Worker, and RSR - Rock Administration, then deny All Users:

- Filters: Giving Amount, First Contribution Date.
- Report columns: Total Giving Amount, First Contribution, Last Contribution.
- Person attributes: Giving Bin and Giving Percentile.

These limit who can **build** with them. Once a data view or report exists, anyone who can view it runs it (Rock's "Security for Finance" doc says so too).

Not locked by default: the FinancialTransaction entity type, and the Person filters that point at a financial transaction, pledge, or scheduled transaction data view. So a staff member can still build a giving data view the long way. **The locked Finance category is what keeps finished finance items private.**

### 1.7 Rock 20 and Rock 21

The DataView, Report, ReportField, and DataViewFilter models have the same properties. Both have the same data filter, data select, and transform components. The security chain and run-time checks match.

One difference, edit time only: the Rock 20.0.10 checkout has a hotfix (`1f32f8991c`) that makes a data view read-only when one of its filters uses an attribute the person cannot view. The Rock 21 checkout does not have it yet. It does not change who can run anything.

### 1.8 What this means for the design

1. Put security on categories. Items inherit it, and new items made later do too.
2. Lock a sensitive area with "allow these roles, then deny All Users". Without the deny, the global default lets everyone view.
3. Lock both trees. A finance data view in a locked data view category does not protect a report built on it. The report must sit in a locked report category too.
4. A data view that builds on a finance data view reveals giving, even if it looks harmless. It must live in Finance too.
5. Put a rule on a single item only when it must be **narrower** than its category. A child item or category can never be found in the tree if its parent is hidden, so making one wider than its parent does not work in practice.

## 2. Ministry by Design

Jon asked that reporting follow Ministry by Design, the framework the step programs (decisions 84 and 85) and the calendar's stages (decision 183) already follow. This section says how. It changes the categories, adds stage and next-step data views, and adds a set of path reports. The layers, the foundations, and every security rule stay as they were.

### 2.1 What the framework asks of reporting

Ministry by Design has seven Ministry Systems. Four are a pathway, one is formation, and two are care that runs alongside the rest:

| Ministry System | Stage | Adult steps | Student steps | Calendar | Category here |
|---|---|---|---|---|---|
| First Contact | Notice | None. A first visit | None | Front-door events | Notice |
| Connection | Belong | Starting Point Class, Small Group | Student Camp | Belong events | Belong |
| Discipleship | Equip | Baptism, Foundations | Follow Jesus | Equip events | Equip |
| Engagement | Contribute | Serve, Give | Mission Trip | Contribute events | Contribute |
| Leadership Development | Lead | Lead | Student Leader | Lead events | Lead |
| Retention | Re-engage | None | None | None | Re-engage |
| Pastoral Care | Care | None | None | Care events | None yet (section 2.3) |

The stage of each step type is already in `configuration/steps.json`. The stage views read it from there, so a new step type joins its stage without a change here.

Five of the framework's ideas shape the data views and reports:

1. **The norm, not a gate.** People may enter out of order, such as a mature believer who serves quickly. So the stage views are **not exclusive buckets**. Each one means "has taken at least one step in this stage, in either program". A person can be in Contribute and Lead without a Belong step. Exclusive buckets would need a rule for each person's "highest" stage. That rule turns the path back into a gate, and it hides the people who entered out of order, who are the ones the framework says to expect.
2. **Growth before serving.** "Responsibility without formation creates pressure, not participation." So the next-step view into Contribute (Ready to Serve) requires an Equip step. The first draft's "Group Members Ready to Serve" went straight from a small group to serving, and it is gone. Ready to Lead needs an Equip step and a team.
3. **Care runs alongside everything.** Re-engage gets its own category: Drifting Adults, Newly Missed, and Back in the Last 30 Days move there.
4. **One shared definition for each idea.** The framework's Operational Standards ask for "clear definitions" so leaders see what is really happening. That is what the Foundations and Path categories are. Reports, syncs, and flows all use the same definitions.
5. **One primary system for each environment, and one owner for each system.** So each data view and each report sits in exactly one system category, and each system's owner has one place to look.

### 2.2 What moves and what stays

| | Before | After |
|---|---|---|
| Layers | Foundations, ministry views, reports | The same, plus a Path layer between foundations and ministry views |
| Foundations | F1 to F10 | Unchanged |
| Categories | Groups and Serving, Kids and Students, Care | Notice, Belong, Equip, Contribute, Lead, Re-engage, plus Path for the stage views |
| Security | Root locked to staff; Leadership, Safety, Finance locked; Top Givers item rule | Unchanged. The new categories inherit, as the old ones did |
| G1 Regular Attending Adults Not in a Group | Groups and Serving | Renamed **N1 Ready to Belong**, in Belong. Same rule |
| G2 Group Members Ready to Serve | Groups and Serving | **Replaced** by N3 Ready to Serve, which needs an Equip step |
| G3, K1, K3 | Groups and Serving, Kids and Students | Belong |
| K2 Kids With an Allergy, and R6 | Kids and Students | **Safety**. An allergy is medical information (question 17) |
| C1, C4 | Care | Notice |
| C2, C7 | Care | Equip |
| C3, C5, C6 | Care | Re-engage |
| Finance, Registrations, Group Sync | | Unchanged |
| New | | Stage views P1 to P4, the movement view P5, next-step views N2 to N5, and reports R18 to R25 |

### 2.3 What is not covered yet

- **Pastoral Care** has no data views. Its data today is prayer requests, which have their own Rock screens and security. Benevolence requests are planned work. A Care category is added when one of them needs a data view.
- **Notice** has no step. Rock's first-visit attendance and the First Visit attribute stand in for it.
- **Counts over time.** Rock's Step Program page already charts each step type by month. Rock's Step Flow block, which draws movement between steps, ships in both builds, but no page uses it on a new install. Stage counts over time would be Rock Metrics fed by the stage views (question 15).

## 3. The layers

Rock's reporting guide says: build data views that "extend and build off of each other", so a rule is defined once, and do not take reuse too far. This design has four layers.

1. **Foundations.** The shared rules: who is active, who is an adult, who is part of the church family, who comes regularly, who is in a group, who serves. Each one is a single idea.
2. **Path.** The Ministry by Design stages, built from steps: who has reached each stage, and who took a step lately.
3. **Ministry data views.** Each answers one staff question, filed in its Ministry System. Most are **next-step** views: people in one stage who are ready for the next.
4. **Reports.** Each shows the columns a staff member needs for that question. Several reports can share one data view.

Every data view in the tree below is used by another data view, a report, a group sync, a registration's eligibility rule, or a communication flow. None stands alone.

```
Person data views
Active People (F1)
├── Adults (F2)
│   ├── Ready to Belong (N1)                            = F2 + F5 − F7
│   ├── Ready to Grow (N2)                              = F2 + P1 − P2
│   ├── Ready to Serve (N3)                             = F2 + P2 − F8
│   ├── Ready to Lead (N4)                              = F2 + P2 + F8 + F7 − P4
│   ├── Guests Coming Back (C1)
│   ├── First Visit Last Week (C4)                      = F2 + F6 + first visit
│   ├── Drifting Adults (C3)
│   │   └── Newly Missed (C5)                           = C3 + F6 + last visit 90 to 120 days ago
│   ├── Unbaptized Regular Attenders (C7)               = F2 + F5 − Baptism step
│   ├── Women Who Attend (RG1)                          = F2 + F6 + women 25 to 75
│   ├── Involved Adults (F10)                           = F2 + (worship, F7, F8, or Last Gave)
│   └── Money Matters Invite List (FN8)  [Finance]      = F2 + F4 − FN2
├── Children (F3)
│   └── Kids Checked In Recently (K1)
│       └── Kids With an Allergy (K2)  [Safety]
├── Church Family (F4)
├── Regular Attenders (F5)  [persisted nightly; also the guest flow's goal]
├── Back in the Last 30 Days (C6)
├── Accepts Mass Email (F6)
├── In a Small Group (F7)
├── Serving (F8)
│   └── Kids and Student Volunteers (S1)  [Safety]
├── Belong Stage (P1)
│   └── Belonging a Year, Not Yet Growing (N5)          = P1 + F7 − recent Belong step − P2
├── Equip Stage (P2)
├── Contribute Stage (P3)
├── Lead Stage (P4)
├── Path Steps in the Last 90 Days (P5)
├── Students Attending the Student Service (K3)
├── Baptized This Year (C2)
├── First-Time Givers (FN3)  [Finance]
├── Lapsed Givers (FN4)  [Finance]
└── Top Givers (FN5)  [Finance, item locked]

Gifts in the Last 12 Months (FN1, financial transactions)
└── Givers in the Last 12 Months (FN2, people)  [Finance, persisted nightly]

Active Small Groups (F9, groups)
└── Small Groups With Room (G3)

Built on the Rock Pledges (FN6, pledges)  [Finance]
Event Registration Payments (FN7, financial transactions)  [Finance]
```

The minus sign means "not in that data view" (Rock's "Not in Existing Data View" filter).

The next-step views follow the path in order, each from one stage to the next:

```
Notice ──► Belong ──► Equip ──► Contribute ──► Lead
  C1, C4     N1         N2, N5,    N3             N4
                        C7
                     Re-engage alongside: C3, C5, C6
```

The church already has eight data views: five for group sync and three for registrations (decisions 186 and 215). They repeat rules the foundations would hold. For example, four of the five syncs repeat "Adult" and "accepts mass email". Slice 6 rebuilds them on the foundations (question 7).

## 4. Data views

All data views use Rock's own filter components and selection strings, as `configuration/group-syncs.json` does. Where a selection holds a database Id (the Existing Data View filter, and the related data view filters), the configuration names the other view by key and the loader fills the Id at load, as the registration views already do.

"Persisted" follows Rock's guidance: persist a data view that is slow and reused, and accept that it can be out of date by up to its interval. Persisting a fast data view only makes it stale and freezes its run statistics. Rock's Update Persisted DataViews job runs every minute.

### 4.1 Foundations (category: Rock Solid Church > Foundations)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| F1 | Active People | Person | Record status is Active. Deceased people left out (the data view default) | None. It is the root | No. One indexed column | Every Person view below |
| F2 | Adults | Person | In F1, and age classification is Adult | F1 | No | N1 to N4, C1, C3, C4, C7, RG1, F10, FN8, and the syncs after slice 6 |
| F3 | Children | Person | In F1, and age classification is Child | F1 | No | K1 |
| F4 | Church Family | Person | In F1, and connection status is Connected, Engaged, or Leading | F1 | No | FN8 |
| F5 | Regular Attenders | Person | In F1, and attended a worship service (the Service Attendance group type, as the syncs use) 4 or more times in the last 12 weeks | F1 | **Yes, nightly**, on a named schedule "Nightly at 2 AM" in Rock's Persisted DataView Schedules category | N1, C7, the First-Time Guest Welcome flow's goal ("Coming Regularly"), and the syncs after slice 6 |
| F6 | Accepts Mass Email | Person | In F1, email preference is Email Allowed, and email is not blank | F1 | No | C4, C5, RG1, and the four syncs that need email after slice 6 |
| F7 | In a Small Group | Person | In F1, and an active member of an active Small Group, any role | F1 | No. Membership changes should show the same day | N1, N4, N5, F10 |
| F8 | Serving | Person | In F1, and an active member of an active Serving Team, any role | F1 | No | N3, N4, S1, F10, and the Volunteers sync after slice 6 |
| F9 | Active Small Groups | Group | Group type is Small Group, and the group is active | None | No | G3 |
| F10 | Involved Adults | Person | In F2, and any of: attended worship in the last 12 months; in F7; in F8; or the Last Gave attribute is in the last 12 months | F2, F7, F8 | No | The Christmas at Rock Solid flow. A broad "who is part of us" list that later views and reports can share |

Why the church has its own Church Family view instead of using Rock's shipped "Member & Attendees": the church renamed Rock's statuses and added Leading (decision 11). Rock's view checks Attendee and Member, which are now Connected and Engaged, so it leaves out every leader. Rock's 14 shipped data views stay as they are, in their own categories.

Why F5 is persisted: it counts attendance rows, and Full 7 has millions of them. Several views build on it. Who counts as a regular attender does not change within a day. Its consumers turn on the filter's "Use Persisted" option.

Why F10 uses Rock's Last Gave attribute and not a Finance data view: "gave in the last year" through Givers in the Last 12 Months (FN2) would make F10 a finance consumer, and it would have to live in Finance (design rule 4). Rock already shows Last Gave to Staff Workers and Staff Like Workers (its attribute security in the trusted base), so using it reveals nothing Rock does not already show staff.

### 4.2 Path (category: Rock Solid Church > Path)

Each stage view uses Rock's Steps Taken filter, once for each step program, with that program's Complete status. The loader picks the step types from their `stage` in `configuration/steps.json`. The filter and its options are the same in Rock 20 and Rock 21.

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| P1 | Belong Stage | Person | In F1, and any of: a Complete Starting Point Class or Small Group step; a Complete Student Camp step | F1 | No. Steps are far fewer rows than attendance. Verify times it, and it is persisted only if it proves slow | N2, N5, R19 |
| P2 | Equip Stage | Person | In F1, and any of: a Complete Baptism or Foundations step; a Complete Follow Jesus step | F1 | No | N2, N3, N4, R20 |
| P3 | Contribute Stage | Person | In F1, and any of: a Complete Serve or Give step; a Complete Mission Trip step | F1 | No | R21 |
| P4 | Lead Stage | Person | In F1, and any of: a Complete Lead step; a Complete Student Leader step | F1 | No | N4, R22 |
| P5 | Path Steps in the Last 90 Days | Person | In F1, and any step in either program completed in the last 90 days | F1 | No | R18 |

A step counts once it is Complete. A rhythm step, such as Small Group or Serve, is Complete from the day it starts (steps.md), so a person stays in a stage after a group or team ends. That is "has reached the stage", which is what movement along the path means. "Is in a group today" is still F7, and "is serving today" is still F8.

Contribute Stage counts the Give step. Rock already shows each Give step on the person's Steps tab to staff (decision 85), so P3 reveals nothing new and stays out of Finance (question 16).

### 4.3 Notice (category: Rock Solid Church > Notice)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| C1 | Guests Coming Back | Person | In F2; first worship attendance in the last 6 months; 3 or more worship attendances in the last 6 months; and a "none" group holding "took the Starting Point Class step" | F2 | No | R9. The Notice to Belong next step |
| C4 | First Visit Last Week | Person | In F2, in F6, and first worship attendance in the last 7 days | F2, F6 | No | The First-Time Guest Welcome flow's audience |

### 4.4 Belong (category: Rock Solid Church > Belong)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| N1 | Ready to Belong | Person | In F2, in F5 (use persisted), not in F7 | F2, F5, F7 | No. F5 already carries the slow part | R3 |
| G3 | Small Groups With Room | Group | In F9, and fewer than 10 active members (Rock's member count filter) | F9 | No | R4 |
| K1 | Kids Checked In Recently | Person | In F3, and checked in to the Nursery/Preschool or Elementary area at least once in the last 4 weeks | F3 | **Yes, every 60 minutes**. Kids staff open it on Sunday, so it should be fresh that afternoon. This is the interval example, beside F5's named schedule | K2, R7 |
| K3 | Students Attending the Student Service | Person | In F1, grade 7 to 12, and checked in to the Jr High or High School area at least once in the last 12 weeks | F1 | No | R8, and the registration views after slice 6 |

### 4.5 Equip (category: Rock Solid Church > Equip)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| N2 | Ready to Grow | Person | In F2, in P1, not in P2 | F2, P1, P2 | No | R23 |
| N5 | Belonging a Year, Not Yet Growing | Person | In P1, in F7, a "none" group holding "a Belong step completed in the last 12 months", and not in P2 | P1, F7, P2 | No | R24. The one "stalled" view |
| C7 | Unbaptized Regular Attenders | Person | In F2, in F5 (use persisted), and a "none" group holding "took the Baptism step" | F2, F5 | No | The Baptism Sunday Invitation flow's audience |
| C2 | Baptized This Year | Person | In F1, and the Baptism Date attribute is in the last 12 months | F1 | No | R2 |

### 4.6 Contribute (category: Rock Solid Church > Contribute)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| N3 | Ready to Serve | Person | In F2, in P2, not in F8 | F2, P2, F8 | No | R5. Growth before serving: an Equip step comes first |

### 4.7 Lead (category: Rock Solid Church > Lead)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| N4 | Ready to Lead | Person | In F2, in P2, in F8, in F7, not in P4 | F2, P2, F8, F7, P4 | No | R25. Formed, serving on a team, and in a group, but not yet leading |

### 4.8 Re-engage (category: Rock Solid Church > Re-engage)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| C3 | Drifting Adults | Person | In F2; 10 or more worship attendances in the last 18 months; and none in the last 90 days. This is the We Miss You rule without the email part | F2 | No | R10, C5, and the We Miss You sync after slice 6 |
| C5 | Newly Missed | Person | In C3, in F6, and attended worship at least once in the last 120 days. Since C3 already means none in the last 90, this leaves people whose 90 days ran out in the last month | C3, F6 | No | The We Miss You flow's audience. It builds on C3, not on the We Miss You sync view, because a sync is a consumer, not a source |
| C6 | Back in the Last 30 Days | Person | In F1, and attended worship at least once in the last 30 days | F1 | No | The We Miss You flow's goal |

### 4.9 Registrations (category: Rock Solid Church > Registrations, exists)

The category already holds three registration data views (decision 215). One flow audience joins them, because it is about who may be invited to register.

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| RG1 | Women Who Attend | Person | In F2, in F6, gender is Female, age 25 to 75, and attended worship at least once in the last 12 months | F2, F6 | No | The Women's Retreat Early Bird flow's audience |

### 4.10 Safety (category: Rock Solid Church > Safety, locked)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| S1 | Kids and Student Volunteers | Person | In F8, and an active member of a Kids Ministry or Student Ministry team, the two teams with the background check requirement | F8 | No | R11 |
| K2 | Kids With an Allergy | Person | In K1 (use persisted), and the Allergy person attribute is not blank | K1 | No | R6 |

R11 also shows how Rock's shipped "Background check about to expire" data view (in Rock's Group Requirements category) fits in. Question 5 asks whether Safety is in scope.

### 4.11 Finance (category: Rock Solid Church > Finance, locked)

| # | Name | Entity | Filters | Builds on | Persisted | Used by |
|---|---|---|---|---|---|---|
| FN1 | Gifts in the Last 12 Months | Financial Transaction | Transaction type is Contribution, and the transaction date is in the last 12 months | None | No | FN2 |
| FN2 | Givers in the Last 12 Months | Person | Has a transaction in FN1 (Rock's Financial Transaction Data View filter) | FN1 | **Yes, nightly** on the same named schedule. It scans a year of gifts | FN8 |
| FN3 | First-Time Givers | Person | In F1, and first contribution date in the last 90 days (a filter Rock locks to finance roles) | F1 | No | R1, R12 |
| FN4 | Lapsed Givers | Person | In F1, and Current Journey Giving Stage is Lapsed Giver (Rock's own giving journey) | F1 | No | R13 |
| FN5 | Top Givers | Person | In F1, and Giving Bin is 1 (Rock's top bin, a finance-locked attribute) | F1 | No | R14. **Item locked**, see section 6.3 |
| FN6 | Built on the Rock Pledges | Financial Pledge | The account is the Built on the Rock fund or one of its campus accounts | None | No | R15 |
| FN7 | Event Registration Payments | Financial Transaction | Transaction type is Event Registration, in the last 12 months | None | No | R16 |
| FN8 | Money Matters Invite List | Person | In F2, in F4, not in FN2 (use persisted) | F2, F4, FN2 | No | R17 |

FN8 shows design rule 4. Its name and filters look harmless, but because it builds on FN2 it tells anyone who can see it who does **not** give. So it lives in Finance.

### 4.12 Communication flow audiences

The communication flows proposal (`docs/communication-flows-proposal.md` on branch `claude/comm-flows-47b4f5`) needs seven person data views for its flow audiences and goals. They are filed by Ministry System in this tree, not in a separate Communication Flows category, and each builds on the foundations.

| Flow need | Data view | Key | Category | Note |
|---|---|---|---|---|
| First Visit Last Week (guest flow audience) | C4 First Visit Last Week | `first-visit-last-week` | Notice | |
| Coming Regularly (guest flow goal) | F5 Regular Attenders | `regular-attenders` | Foundations | Reused, not a new view. Its rule is 4 or more in 12 weeks (question 13) |
| Newly Missed (We Miss You flow audience) | C5 Newly Missed | `newly-missed` | Re-engage | |
| Back in the Last 30 Days (We Miss You flow goal) | C6 Back in the Last 30 Days | `back-in-the-last-30-days` | Re-engage | |
| Unbaptized Regular Attenders (baptism invitation audience) | C7 Unbaptized Regular Attenders | `unbaptized-regular-attenders` | Equip | Uses F5's rule (question 13) |
| Women Who Attend (Women's Retreat flow audience) | RG1 Women Who Attend | `women-who-attend` | Registrations | |
| Involved Adults (Christmas flow audience) | F10 Involved Adults | `involved-adults` | Foundations | "Gave" uses Rock's Last Gave attribute (question 14) |

The stage and next-step views have keys too, so a flow can use one as an audience or an Entered Data View goal: `belong-stage`, `equip-stage`, `contribute-stage`, `lead-stage`, `path-steps-last-90-days`, `ready-to-belong`, `ready-to-grow`, `ready-to-serve`, `ready-to-lead`, and `belonging-a-year-not-growing`. For example, a Foundations invitation could go to Ready to Grow, with Equip Stage as its goal.

A Communication Flows category would hold data views by who uses them, not by what they are. Then a care view and a flow view with the same rule would drift apart. Filing by Ministry System keeps one definition, and Rock's data view screen lists every flow and report that uses each one.

The flows run their audiences and goals through Rock's Process Communication Flows job, which checks no person (section 1.5). So the category a flow's view sits in limits only who can open it, not whether the flow works.

These views go in slice 2. If the flows land first, the flows session adds them to `configuration/reporting.json` with these keys and the foundations they need (F1, F2, F5, F6, F7, F8, C3), and slice 2 builds on them.

## 5. Reports

Each report names its data view and its columns. Column kinds are: **Property** (a field on the record), **Attribute** (a person or group attribute), **Select** (a Rock data select component), and **Lava** (Rock's Lava template column). Person reports turn on Rock's communication recipient field, so staff can email or text the list from the grid.

The exact Lava merge fields and data select settings are checked against Rock in slice 3, on both builds. The step columns use Rock's `Steps` Lava filter, which both builds have. Verify times every report, and a Lava column that proves slow on Full 7 is dropped.

Each next-step report names the calendar event it invites to, so the reports, the steps, and the calendar tell one story.

### 5.1 Leadership (category: Rock Solid Church > Leadership, pastors only)

The path reports give the senior team the whole path in one place. Each stage report's grid shows how many people are in the stage, and the stages overlap on purpose.

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R18 | Path Movement, Last 90 Days | P5 | Name (Property), Campus (Select: Campus), Connection Status (Property), Steps Taken (Lava: each step completed in the last 90 days, with its stage and date) | Who moved along the path this quarter, and into which stage |
| R19 | Belong Stage | P1 | Name (Property), Campus (Select), Age (Property), Connection Status (Property), Belong Steps (Lava: step names and years), Small Group (Select: In Group of Group Type) | How many have belonged, and who |
| R20 | Equip Stage | P2 | Name, Campus, Age, Connection Status, Equip Steps (Lava), Small Group (Select) | How many have taken an Equip step, and who |
| R21 | Contribute Stage | P3 | Name, Campus, Age, Connection Status, Contribute Steps (Lava), Team (Select: In Group of Group Type, serving) | How many contribute, and who |
| R22 | Lead Stage | P4 | Name, Campus, Age, Connection Status, Lead Steps (Lava), Group or Team Led (Select: In Group of Group Type) | How many lead, and who |
| R1 | First-Time Givers to Thank | FN3 (Finance) | Name (Select: Person Link), Spouse (Select: Spouse Name), Address (Select: Address), Campus (Select: Campus), Connection Status (Property) | Who to write a thank-you note to this week. No amounts, by design. See section 6.4 |

### 5.2 Notice (category: Rock Solid Church > Notice)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R9 | Guests Coming Back | C1 | Name (Property), First Visit (Attribute), Campus (Select), Connection Status (Property), Mobile Phone (Select: Phone Number), Email (Property) | Who to invite to the next Starting Point Class |

### 5.3 Belong (category: Rock Solid Church > Belong)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R3 | Ready to Belong | N1 | Name (Property), Age (Property), Campus (Select), Mobile Phone (Select), Email (Property), Connection Status (Property), Last Worship Attendance (Select: Last Attended Group of Type) | Regulars to invite to Small Group Connect, newest regulars first |
| R4 | Small Groups With Room | G3 (groups) | Group (Select: Group Link), Campus (Select), Leaders (Select: Member List, leader role), Members (Select: Member Count), Meets (Select: Schedule), Location (Select: Location) | Where to send someone who wants a group |
| R7 | Kids Checked In Lately | K1 | Name (Property), Grade (Select: Grade), Campus (Select), Last Check-in (Select: Last Attended Group of Type), Parents (Select: Parents' Names), Parent Email (Select: Parent Email) | Which families came lately, for a welcome or a follow-up |
| R8 | Students Attending | K3 | Name (Property), Grade (Select), Campus (Select), Last Check-in (Select), Mobile Phone (Select), Parents (Select) | The student ministry's active roster, the people Student Camp is for |

### 5.4 Equip (category: Rock Solid Church > Equip)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R23 | Ready to Grow | N2 | Name (Property), Campus (Select), Small Group (Select), Belong Steps (Lava), Mobile Phone (Select), Email (Property) | Who belongs and is ready for Foundations or baptism |
| R24 | Belonging a Year, Not Yet Growing | N5 | Name (Property), Campus (Select), Small Group (Select), In the Group Since (Lava: the Small Group step's start date), Mobile Phone (Select) | Who has stalled, so their group leader can offer a next step |
| R2 | Baptized This Year | C2 | Name (Property), Age (Property), Baptism Date (Attribute), Campus (Select), Connection Status (Property), Small Group (Select), Baptized (Lava: the date as "3 months ago") | Whose story to celebrate, and who still needs a group |

### 5.5 Contribute (category: Rock Solid Church > Contribute)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R5 | Ready to Serve | N3 | Name (Property), Campus (Select), Small Group (Select), Equip Steps (Lava), Mobile Phone (Select), Email (Property) | Who has been formed and is ready for a team, to invite on Serve Sunday |

### 5.6 Lead (category: Rock Solid Church > Lead)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R25 | Ready to Lead | N4 | Name (Property), Campus (Select), Team (Select: In Group of Group Type, serving), Small Group (Select), Equip Steps (Lava), Email (Property) | Who to invite to Group Leader Training |

### 5.7 Re-engage (category: Rock Solid Church > Re-engage)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R10 | Drifting Adults | C3 | Name (Property), Campus (Select), Last Worship Attendance (Select), Small Group (Select), Spouse (Select: Spouse Name), Mobile Phone (Select), Profile (Lava: a link to the person) | Who a pastor should call this week |

### 5.8 Safety (category: Rock Solid Church > Safety, locked)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R11 | Volunteer Background Checks | S1 | Name (Property), Team (Select: In Group of Group Type), Background Checked (Attribute), Background Check Date (Attribute), Background Check Result (Attribute) | Who needs a renewal before they serve again |
| R6 | Kids Allergy List | K2 | Photo (Select: Photo), Name (Property), Grade (Select), Allergy (Attribute), Parents (Select), Parent Phone (Select: Parent Phone Number), Allergy alert (Lava: a red label) | The sheet a classroom leader keeps by the snack table |

R11's three attributes are locked by Rock to RSR - Safety & Security Workers and RSR - Rock Administration. As section 1.3 says, the columns would still show to anyone who can view the report. That is why the report category is locked too.

### 5.9 Finance (category: Rock Solid Church > Finance, locked)

| # | Report | Data view | Columns | What a staff member learns |
|---|---|---|---|---|
| R12 | First-Time Givers | FN3 | Name (Property), First Gift (Select: First Contribution, finance locked), Given in the Last 90 Days (Select: Total Giving Amount, finance locked), Campus (Select), Connection Status (Property), Email (Property) | The same people as R1, with the amounts finance needs |
| R13 | Lapsed Givers | FN4 | Name (Property), Last Gave (Attribute), Giving Stage (Attribute), Frequency (Attribute: Frequency Label), Campus (Select) | Who stopped giving, and how often they used to give |
| R14 | Top Givers | FN5 | Name (Property), Giving Bin (Attribute), Giving Percentile (Attribute), Given in the Last 12 Months (Select: Total Giving Amount), Last Gift (Select: Last Contribution) | The church's largest givers. **Item locked** to Finance Administration |
| R15 | Built on the Rock Pledges | FN6 (pledges) | Person (Property), Account (Property), Total Pledged (Property), Frequency (Property), Start and End (Property), Pledge page (Lava: a link) | The campaign's pledges at a glance, beside Rock's Pledge Analytics |
| R16 | Registration Payments | FN7 (transactions) | Date (Property), Person (Property), Amount (Select: Total Amount), Currency Type (Property), Transaction Code (Property) | Every registration payment this year, for reconciling |
| R17 | Money Matters Invite List | FN8 | Name (Property), Campus (Select), Connection Status (Property), Small Group (Select), Email (Property) | Engaged adults to invite to the Money Matters class |

The 25 reports use all four column kinds, two of Rock's locked finance components, and three record types besides people (groups, pledges, and transactions).

## 6. Categories and their security

### 6.1 Data view categories

The church's tree already exists under Rock Solid Church (decision 185). This adds the Ministry System children, Path, Safety, Finance, and Staff Workspace, and one rule on the parent. The children are ordered along the path.

```
Rock Solid Church                 View: Staff Workers, Staff Like Workers, Rock Administration. Deny All Users
├── Foundations                   inherits
├── Path                          inherits
├── Notice                        inherits
├── Belong                        inherits
├── Equip                         inherits
├── Contribute                    inherits
├── Lead                          inherits
├── Re-engage                     inherits
├── Group Sync                    inherits (exists)
├── Registrations                 inherits (exists)
├── Safety                        View: Safety & Security Workers, Background Check Administration, Rock Administration. Deny All Users
│                                 Edit: Background Check Administration
├── Finance                       View: Finance Administration, Finance Worker, Rock Administration. Deny All Users
│                                 Edit: Finance Administration
└── Staff Workspace               Edit: Staff Workers (question 8)
```

### 6.2 Report categories

Reports get a matching tree. It sits beside Rock's own Organization category, which holds Rock's Data Integrity reports and stays as it is.

```
Rock Solid Church                 View: Staff Workers, Staff Like Workers, Rock Administration. Deny All Users
├── Leadership                    View: Pastoral Workers, Rock Administration. Deny All Users
├── Notice                        inherits
├── Belong                        inherits
├── Equip                         inherits
├── Contribute                    inherits
├── Lead                          inherits
├── Re-engage                     inherits
├── Safety                        same rules as the Safety data view category
├── Finance                       same rules as the Finance data view category
└── Staff Workspace               Edit: Staff Workers (question 8)
```

Rules are kept as few as possible. For example, Finance's Edit rule is a single "allow Finance Administration". Anyone else falls through to the global default, which allows Rock Administration and denies everyone else.

Ministry by Design gives each system one owner, at the center and at each campus. Rock could later give each owner Edit on their system's categories through a security role. The Cast has no such roles today, so the system categories inherit for now.

### 6.3 What sits on a category and what sits on an item

**On categories:** every rule except one. Why:

1. One place to read and change the rule for a whole area.
2. A data view or report added later, by staff or by a later slice, is secured the day it is made.
3. It is what Rock's docs recommend ("modify the security for all Data Views in that category").

**On items:** only an exception that is narrower than its category. There is one: **Top Givers**, both the data view FN5 and the report R14. Each allows View to Finance Administration and Rock Administration, then denies All Users. A Finance Worker sees the rest of Finance but not this. Both items need the rule, because locking the data view would not lock the report (section 1.4).

Never make an item wider than its category. The category tree hides a category the person cannot view, with everything in it, so a wider item could only be reached by a direct link.

### 6.4 One deliberate exception: First-Time Givers to Thank

R1 sits in Leadership, but its data view FN3 sits in Finance. This breaks design rule 3 on purpose. It is Rock's own documented example: a pastor writes thank-you notes to first-time givers but has no access to giving.

The control is the columns. R1 shows names, spouses, addresses, and campus, and no amounts. A pastor can run it, but cannot open its data view, see its filters, or edit it (section 1.3). The finance team's version, R12, shows the amounts, in Finance. Both descriptions say why the other exists.

### 6.5 How this changes the existing data views

The new rule on Rock Solid Church also covers the eight data views already in Group Sync and Registrations. Today anyone who reaches them can view them. After this, only staff, staff-like users, and Rock Administration can. The jobs that run them (group sync, registration eligibility) check no person, so nothing they do changes. Question 2 asks about this.

## 7. Security walkthrough with the Cast

Each row is a Cast person with a login and the roles in their household file. "Ministry" means the categories that inherit from Rock Solid Church: Foundations, Path, Notice, Belong, Equip, Contribute, Lead, Re-engage, Group Sync, and Registrations.

| Person | Roles that matter here | Ministry | Leadership | Safety | Finance | Top Givers | Can edit |
|---|---|---|---|---|---|---|---|
| Alisha Marble, Executive Assistant | Rock Administration, Finance Administration | Yes | Yes | Yes | Yes | Yes | Everything |
| Priya Raman, Finance Director | Finance Administration, Staff Workers | Yes | No | No | Yes | Yes | Finance, Staff Workspace |
| Greg Holloway, Executive Pastor | Finance Worker, Pastoral Workers, Staff Workers | Yes | Yes | No | Yes | **No** | Staff Workspace |
| Cindy Decker, volunteer | Finance Worker, Staff Like Workers | Yes | No | No | Yes | **No** | Nothing |
| Pete Foster, Senior Pastor | Pastoral Workers, Staff Workers | Yes | Yes | No | **No** | No | Staff Workspace |
| Rachel Brooks, Kids Director | Safety & Security Workers, Staff Workers | Yes | No | Yes | No | No | Staff Workspace |
| Jamal Pierce, Groups Pastor | Connection Administration, Pastoral Workers, Staff Workers | Yes | Yes | No | No | No | Staff Workspace |
| Jenny Michaels, volunteer event coordinator | Staff Like Workers | Yes | No | No | No | No | Nothing |
| Jerry Wright, small group leader | None | **No** | No | No | No | No | Nothing |

How Rock reaches each answer, for the interesting rows:

1. **Greg and Top Givers.** R14 has its own rules: allow Finance Administration (Greg is not in it), allow Rock Administration (not in it), deny All Users (matches). Rock stops there and denies. For R13, which has no rules of its own, Rock moves up to the Finance category, where "allow Finance Worker" matches.
2. **Pete and R1.** R1 is in Leadership, where "allow Pastoral Workers" matches, so he runs it and sees the names and addresses. Its data view FN3 is in Finance, where only "deny All Users" matches him. So Report Detail shows the report read-only, with the data view link hidden. He never sees a gift amount.
3. **Pete and the path.** The path reports R18 to R22 are in Leadership too, so the pastors see the whole path. Each system's owner sees their own next-step reports in that system's category.
4. **Priya and Leadership.** Priya is not a pastor, so she cannot see R1 or the path reports. She sees the same givers, with amounts, in R12.
5. **Rachel and the allergy list.** The Kids Allergy List is in Safety now. Rachel and Tina Vu, the two kids directors, are both Safety & Security Workers, so they see it. Jamal and Jenny do not.
6. **Cindy Decker.** Rock's own sample data made her a Finance Worker, and the Cast kept it. So she sees the Finance reports, but not Top Givers. The coordinator settled question 10: her story will say she helps count the offering.
7. **Jenny Michaels.** As a staff-like user she sees the ministry reports, including Registrations, which fits her work on camp and the car show. She sees nothing locked, and she cannot build in Staff Workspace, which is for staff.
8. **Jerry Wright.** He cannot reach the internal site at all. Even with a direct link, the Rock Solid Church rule denies him, because he matches only "deny All Users". That is why the top category has an explicit deny instead of relying on the site.
9. **Edit.** Only Rock Administration edits the curated categories, through the global default. Finance Administration edits Finance. Background Check Administration edits Safety, and no Cast person has that role except Alisha, who is Rock Administration anyway. Every Staff Worker can build in Staff Workspace.

## 8. Implementation slices

Each slice is small, lands on its own, and has its own checks. Every slice that changes loader code needs a new loader version for the packages.

**Verify** gets four new rock-runner tasks, used from slice 1 on. Each runs inside real Rock on both builds, as the group sync check does:

- `authorize`: calls Rock's own `Authorization.Authorized` for each row of an expected-access table in the configuration (person, item, action, expected answer). Section 7 becomes a test.
- `data-views`: runs every church data view through Rock's engine (`DataViewCache.GetEntityIds`). It fails on any error, records each count and run time in the receipt, and checks Cast anchors, such as Luis and Teresa Ortiz in Drifting Adults, and each Cast person's stages matching their steps.
- `persist`: runs Rock's own `PersistResult` on each persisted view, records the time, and checks that each "Use Persisted" consumer returns the same people as when it runs live.
- `reports`: runs every report's query (`Report.GetQueryable`) and checks its row and column counts and run time.

A new static checker, `validation/check-reporting.js`, runs without a database. It checks that every category and data view key exists, that no data view refers to itself, that every data view is used, that each stage view holds exactly the step types `configuration/steps.json` gives that stage, and the consumer rule: anything built on a Finance or Safety data view sits in an equally locked category, unless it is on a short list of exceptions with a reason (only R1).

| # | Slice | What it builds | How it is checked |
|---|---|---|---|
| 1 | Categories and security | The data view and report categories in `configuration/categories.json`, with a `security` list on each; the loader writes their Auth rows. The rule on Rock Solid Church. Staff Workspace | `authorize` against the existing eight data views: staff yes, Jerry Wright no. Contract re-recorded |
| 2 | Foundations, path, and ministry data views | F1 to F10, P1 to P5, N1 to N5, G3, K1, K3, C1 to C7, and RG1, in a new `configuration/reporting.json`. This includes the seven communication flow audiences (section 4.12). The writer learns the Existing Data View, Not in Existing Data View, and Steps Taken filters, the persisted fields, and the named schedule | `data-views`, `persist`, Cast anchors, the static checker |
| 3 | Reports | A report writer for all four column kinds; R2 to R5, R7 to R10, and R18 to R25 | `reports` on both builds, with run times |
| 4 | Finance | FN1 to FN8, R1, R12 to R17, and the Top Givers item rules | `authorize` with the full walkthrough; `data-views`; `reports`; the consumer rule |
| 5 | Safety | S1, K2, R6, and R11 | `authorize` for Rachel Brooks yes, Jamal Pierce no; `reports` |
| 6 | Rebuild the existing views on foundations | The five sync views and three registration views use F2, F5, F6, F8, C3, and K3. The loader's own data view evaluator learns the two Existing Data View filters, so it still fills each synced group exactly | The existing group sync check: Rock's own sync must change nothing. Registration eligibility checks |
| 7 | Usage history | Run counts and last run dates, and one retired data view (question 9, settled yes) | Invariants on the dates |
| 8 | Path metrics (if question 15 says yes) | A Rock Metric for each stage, sourced from P1 to P4, with weekly values back through the history window, read from the steps | Each metric's latest value equals its stage view's count |
| 9 | Docs and site | How it works, Verification, the glossary, Planned work, Roadmap, and a reporting page on the site if Jon wants one | `node site/build.js` |

## 9. Open questions

Each has one recommendation.

1. **Our own foundations, or Rock's shipped Foundational Views?** Recommend our own, under Rock Solid Church > Foundations. Rock's "Member & Attendees" checks the old Member and Attendee statuses, so it misses every Leading person. Rock's 14 views stay untouched.
2. **Lock the top Rock Solid Church categories to staff, staff-like users, and Rock Administration?** Recommend yes. It also covers the eight existing views. No job behaves differently, and it shows why an explicit deny matters (Jerry Wright).
3. **Make Leadership reports pastors only?** Recommend yes. It gives the walkthrough a third kind of lock, and it is how a real church would keep a thank-you list and the path reports with the senior team.
4. **Keep the R1 exception (a report outside Finance on a Finance data view)?** Recommend yes. It is Rock's own documented example, and it teaches that a report runs without rights to its data view.
5. **Include Safety (background checks and the allergy list) now?** Recommend yes, as its own slice 5. Background checks are already in the data, Rock already locks those attributes, and it shows a locked area that is not finance.
6. **Persisted views on install day: let Rock persist them, or have the loader write the results?** Recommend letting Rock do it. The loader leaves the last refresh empty, so the views run live until Rock's job persists them within its first minute. Verify runs Rock's own persist. The loader would otherwise need to evaluate every filter itself.
7. **Rebuild the eight existing data views on the foundations (slice 6)?** Recommend yes, after slices 1 to 3 land. Rock's own Males and Females views show the pattern, and the group sync check proves nothing changes.
8. **Add a "Staff Workspace" category in both trees, where Staff Workers can edit?** Recommend yes. The curated categories stay edited by Rock Administration, and staff still have a place to build their own. It teaches that Edit and View are separate rules.
9. **Give data views and reports a run history?** Recommend yes. Rock shows Runs Since and Last Run on each one, and an install where nothing was ever run does not look real. Add one retired data view not run in over a year, so Rock's "remove unused data views" advice has an example.
10. **Say why Cindy Decker is a Finance Worker?** Recommend adding one sentence to her story: she helps count the Sunday offering. The role came from Rock's old sample data, and the walkthrough now depends on it.
11. **Regular attender rule: 4 or more worship check-ins in the last 12 weeks?** Recommend that, kept in `configuration/reporting.json` so it can be tuned. Verify will report how many people it finds on each dataset.
12. **Also lock Rock's financial "related data view" filters?** Recommend no. That changes Rock's shipped settings for every church. The locked Finance categories already protect the finished items, and the docs can mention it as a tip.
13. **One "regular attender" rule for the flows too?** Recommend both flows use F5 (4 or more in 12 weeks), so the church has one definition, and the flows session sets its targets from what F5 finds.
14. **Involved Adults: Rock's Last Gave attribute instead of a Finance data view?** Recommend yes. Rock already shows Last Gave to all staff, so F10 can stay out of Finance and be shared.
15. **Stage counts over time as Rock Metrics (slice 8)?** Recommend yes, as a later slice after slice 3. A metric per stage, fed by the stage views, is Rock's own tool for a count over time, and dashboards can show it. The loader fills weekly values for the history window from the steps, so the trend is there on install. Until then, Rock's Step Program page charts steps by type.
16. **Contribute Stage counts the Give step. Keep it outside Finance?** Recommend yes. Rock already shows every Give step on the person's Steps tab to staff (decision 85), so P3 adds no new exposure. If Jon wants giving hidden from staff, the fix is security on the Give step type, which would hide it from the Steps tab too, and P3 would then move to Finance.
17. **Move the Kids Allergy List into Safety?** Recommend yes. An allergy is medical information. Both kids directors are already Safety & Security Workers, so the people who need the list keep it.

**Settled by the coordinator, 2026-10-29:** questions 6 to 14, each as recommended. For question 13, the flows session sets its targets from what F5 finds. Questions 1 to 5 are with Jon. Questions 15 to 17 came with the Ministry by Design rethink.
