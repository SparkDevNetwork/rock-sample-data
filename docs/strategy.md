# Strategy

This page explains the problem, the goal, and the big ideas behind Rock Sample Data. Read it first.

## The problem

Rock's legacy sample data has four problems:

1. **It is slow.** The legacy loader saves one record at a time through the full Rock model.
2. **It is thin.** Many features (connections, prayer, communications, serving) have little or no data.
3. **It is not realistic.** Real churches have weekly, monthly, and erratic givers, Easter peaks, and holiday dips, and a family's giving, attendance, and groups hang together. Random data hides bugs and makes demos look fake.
4. **It goes stale.** Fixed dates fall out of "last 12 months" reports.

## The goal

Sample data that:

- **installs fast**: seconds for tests, under a minute for the largest dataset;
- **looks like a real church**: measured patterns, not random numbers;
- **covers every feature**: people, giving, attendance, check-in, groups, serving, registrations, prayer, communications, connections, and more;
- **stays fresh**: every date counts back from install day.

## Idea 1: three datasets, one Cast

| Dataset | Who is in it | Campuses | For |
|---|---|---|---|
| **Core 1** | The Cast | Phoenix | Tests and most development |
| **Core 3** | The Cast plus 75 generated "companion" households at each of Peoria and Gilbert | 3 | Campus features |
| **Full 7** | The Cast plus the Crowd (about 25,000 generated people) | 7 | Demos, performance, and reports |

The **Cast** is a set of hand-designed households: today 83 households and 181 people. Most of the Cast lives on the main campus and is identical in every dataset, so a test or a help article can name Ted Decker by Guid and trust that he is there. Each other campus's campus pastor is in the Cast too, in the datasets that have that campus. Every Cast household exists for a reason, written in its `testPurposes`. [Meet the Cast](../cast/families/README.md).

The **Crowd** is generated. It gives volume and statistical realism. Crowd people are never named in tests.

## Idea 2: knowledge lives in data, not code

Everything we know (target numbers, Cast stories, church setup, behavior rules) lives in versioned data files. The builder reads those files. When we learn something, we change a file and rebuild. We never start over.

## Idea 3: store the people, generate the timeline

We store **who people are**: households, relationships, ages, statuses, and behavior patterns. The builder generates **what they did** (gifts, attendance, check-ins) at install time, on the real calendar:

- Sundays fall on Sundays, Easter on Easter, and Christmas week is quiet.
- Ages and grades never drift. A Cast child who is 7 is 7 on every install day.
- Every date is relative to an **anchor date**, which defaults to install day. Tests pin the anchor, and the same seed and anchor always give identical data.

## Idea 4: realism is measured, not guessed

Read-only calibration scripts measure Rock databases: giving cadence and amounts, payment mix, seasonality, holiday attendance, check-in frequency, household shapes, distances from home to campus, and more. A normalizer turns the output into anonymous ratios. A **profile** blends those sources into the target numbers the builder uses, with every exclusion and override written down.

The same scripts then measure our generated data, and a validator grades it against the profile. Realism becomes a number. Today 76% of measured values fall within tolerance; the [roadmap](roadmap.md) lists the gaps.

Calibration sources are never named or described anywhere. See [content standards](content-standards.md).

## Idea 5: a family's records agree

A household's story drives all its records. A Leading member leads a group. An Engaged member gives, serves, or belongs to a group. A lapsing family stops attending and giving around the same time. The Cast validator checks these rules.

Connection statuses follow a journey: **Prospect, Participant, Known, Connected, Engaged, Leading**. Three of these rename Rock's built-in statuses (Visitor, Attendee, Member), so every Rock feature that looks them up by Guid still works.

## Idea 6: real places, fictional homes

The sample church is in the Phoenix area. Families live on **real streets with house numbers that do not exist**, placed at measured distances from their campus. Maps, distances, and campus filters behave like a real church, and no real home ever shows a fictional person. See [Addresses](../addresses/README.md).

## Idea 7: faces that feel real

Every Cast person has an AI-generated photo that looks like a casual social media profile picture. Legacy Cast families have new photos based on their old ones, so the faces Rock users know stay recognizable. A family's photos are generated to look related, using a photo ethnicity that exists only for that purpose and never reaches Rock. See [Photos](../photos/README.md).

## Idea 8: speed by bulk loading

The builder computes every row in memory, assigns Ids up front, and bulk-copies each table in one transaction. It fills in the fields Rock's save hooks would normally set. Large tables rebuild their indexes once at the end. Full 7 (25,000 people, about 380,000 attendance rows, and 110,000 gifts) loads in 20 to 40 seconds.

## Out of scope

- Installing into a database that already has real data.
- Translating the sample church into other countries or languages (for now).
- Real people, churches, or businesses, ever.

## Where this goes next

Today the builder writes straight into a Rock database. Later it will produce a dataset package, and a loader inside Rock will install that package. See the [roadmap](roadmap.md).
