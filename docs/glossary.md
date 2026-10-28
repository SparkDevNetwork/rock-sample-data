# Glossary

The words this project uses, in alphabetical order.

| Term | Meaning |
|---|---|
| **Achievement attempt** | One try at an achievement in Rock. It is open while it can still grow, and closed when it succeeds or a streak breaks. Check-in shows the open ones as in progress. |
| **Achievement type** | Something a person can earn in Rock, such as Century Club. Each one reads a streak type and has a goal. |
| **Anchor** | The date every relative date counts back from. It defaults to install day: today on the database server. Tests pin it with `--anchor`. |
| **Built on the Rock** | The sample church's two-year building campaign, about 38% through on install day. Its fund has a campus account per campus. See [Pledges](pledges.md). |
| **Business contact** | A person linked to a business record in Rock's Known Relationships. Business Detail lists the contacts, and each contact's Known Relationships shows the business. |
| **Blackout date** | Days a volunteer is away, set in Rock's schedule toolbox. Nobody is scheduled to serve inside them. |
| **Builder** | The .NET tool in `builder/` that turns this repository into one package per dataset. It never touches a database. |
| **Campaign line** | The amount a pledger adds for the building campaign to their usual gift, in the same transaction. |
| **Calibration** | Measuring real Rock databases with read-only scripts, so generated data matches real patterns. |
| **Cast** | The hand-designed households (87 today, 6 of them businesses). The same in every dataset, always on the main campus, and safe to rely on by Guid. |
| **Cast expectation** | What Rock should show for a Cast giver (frequency, journey stage, alerts) or for a Cast person's achievements. Every load checks it, or how a Cast pledge is going on install day, along with the business expectations: each campus has its minimum number of businesses, and every business has a contact and a gift in the last 12 months. |
| **Church app** | The Rock Solid Church App, the sample church's mobile app. It starts as a stub so push notifications have an app to point at. |
| **Communication list** | A Rock group of people who get the same mass emails or texts, such as a campus's weekly newsletter list. See [Communications](communications.md). |
| **Commitment Sunday** | The Sunday the building campaign started and most pledges were made: 40 weeks before install day. |
| **Companion** | A generated household on one of Core 3's two extra campuses. Companions are more involved than the Crowd, so each campus looks like a healthy congregation (decision 142). |
| **Connection request** | A person's request to serve on a team, in Rock's Involvement connection type. It moves through statuses until it is connected, set aside as inactive, or saved for a future follow-up. |
| **Connection status** | A person's place in the journey: Prospect, Participant, Known, Connected, Engaged, or Leading. |
| **Connector** | The person who follows up on a connection request. In the sample church, it is the team's leader at the requester's campus, except that Alisha Marble connects the main campus's new requests (decision 178). |
| **Core 1, Core 3, Full 7** | The three datasets. The number is how many campuses each has. |
| **Crowd** | The generated households that give Full 7 its volume (about 25,000 people). See [The Crowd and personas](crowd.md). |
| **Data view** | A saved Rock filter that finds people, such as "adults who first came to worship in the last 6 months". The church's own data views sit under its Rock Solid Church category. See [Configuration](../configuration/README.md#categories). |
| **Dataset** | One installable set of sample data. |
| **Event item** | One event on a Rock calendar, such as Easter Sunday, with its summary, description, audiences, and photo. Its dates are in its occurrences. See [Calendar](calendar.md). |
| **Event occurrence** | One campus's instance of an event at one start time, with its own schedule, place, and contact. Easter at Phoenix has one per service. |
| **Featured family** | A Cast household with a bio for each person. These are the families most used in testing. |
| **Giving alert** | A note Rock raises about a gift: a large gift, a smaller gift than usual, or a gift that is late. |
| **Giving unit** | The people Rock counts as one giver: a couple who give together, or one person who gives on their own. |
| **Group sync** | A Rock setting that keeps a group's members in step with a data view. Rock's Group Sync job adds and removes people every 20 minutes, and can send welcome and exit emails. See [Group sync](communications.md#group-sync). |
| **Journey stage** | Rock's label for where a giver stands: Non-Giver, New, Consistent, Occasional, Lapsed, or Former. |
| **Legacy** | Data that came from Rock's earlier sample data (version 1.14.1). Legacy Cast families keep their Guids, and their new photos were made from the old ones. |
| **Loader** | The library in `loader/` that installs a package into a fresh Rock database, generating every date for the install day. The loader CLI uses it, and Rock uses it through the `SparkDevNetwork.Rock.SampleData.Loader` NuGet package, which also builds for .NET Framework. |
| **Login** | A Rock Database login: a username and password that signs a person in to Rock. Every sample login uses the one password the person installing chose, and most people have none, as in a real church (decision 179). |
| **Mass send** | An email, text, or push sent to 25 or more people at once. Calibration part 7 measures them. |
| **Opportunity** | One way to serve that people can ask about, such as Greeter. Each opportunity places people on its serving team. |
| **Package** | A zip the builder writes for one dataset and one Rock major release, such as Rock 21: data files only, never SQL, with a checksum for every file in `package.json`. It serves every dot release of that major (decision 52). |
| **Package index** | `index.json`, the hosted list of packages that Rock's installer block reads, at `storage.rockrms.com/sampledata/packages/`. Each entry carries its dataset's card (decision 144). The release workflow merges each release into it, signs it, and uploads it last (decisions 128 and 172). |
| **Pending** | A team member who has connected but is not serving yet. The team leader makes them Active after onboarding. The scheduler only schedules Active members. |
| **Persona** | A behavior pattern, such as a weekly giver or a holiday-only attender. |
| **Pledge** | A promise to give a total to a fund over set dates, with an optional frequency. Rock's Pledge Analytics compares it with what the giving unit gave. |
| **Photo ethnicity** | A value used only to generate photos that look like a family. It is never written to Rock. |
| **Pool** | A set of ready-made items the builder draws from: the address pool, or the Core 3 photo pool. |
| **Position** | A room a serving team serves in, with a minimum, desired, and maximum number of people at each service. |
| **Profile** | The blended target numbers the builder uses, in `calibration/profiles/`. |
| **Recent graduate** | A generated adult aged 18 to 20 who finished high school inside the history window. They check in as a student until they graduate. |
| **Relative date** | A date stored as `yearsAgo` plus a month and day, or as `daysAgo`, instead of a fixed date. |
| **Release workflow** | `.github/workflows/release.yml`. On a version tag it builds, verifies, and publishes every package for every supported Rock, and pushes the loader to nuget.org (decision 127). |
| **Rhythm step** | A step that is a habit, such as being in a small group or giving. It starts and can stop. A milestone step happens once, such as baptism. See [Steps](steps.md). |
| **Rotation** | How often a volunteer serves: one of Rock's four schedule templates (every week, every other week, 1st and 3rd, or 2nd and 4th). |
| **Saturation** | How many messages each adult got in a period. Rock's Communication Saturation report sorts adults into buckets by count. See [Communications](communications.md#message-load-per-person). |
| **Seed** | The text every random choice starts from. The same seed and anchor give the same data. |
| **Serving team** | A group of volunteers with a shared job on Sunday, such as Greeters. Each campus has eight. See [Serving](serving.md). |
| **Signature** | Proof that Spark made the package index. The release workflow signs it with a key in Azure Key Vault, and the loader reads only an index whose signature matches one of the public keys it carries (decision 172). |
| **SMS conversation** | A text thread between one person and one of the church's system phone numbers, as Rock's SMS Conversations screen shows it. |
| **Source** | One anonymous set of measured ratios, labeled `source-1`, `source-2`, and so on. |
| **Step program** | A path of steps in Rock, such as the Discipleship Path or the Student Path. A person who completes every step gets a program completion. See [Steps](steps.md). |
| **Streak** | One person's record on a streak type: the weeks they took part, and their current and longest run of weeks in a row. |
| **Streak type** | What Rock counts week by week for streaks and achievements. The sample church has Kids Check-in, which counts kids check-ins. |
| **Student** | A child in grades 7 to 12 who checks in to the Sunday student service. The Student Path is for students. |
| **System phone number** | One of the church's own phone numbers in Rock, used to send and receive texts. One can be assigned to a staff member as a personal number. |
| **Test purpose** | The reason a Cast household exists: the features or edge cases it exercises. |
| **Unsubscribe level** | Where a person unsubscribed from, as Rock records it: All, Bulk, a communication list, or a flow. See [Communications](communications.md#unsubscribes). |
| **Validation** | Running the calibration scripts on generated data and grading the results against the profile. |
| **Version guard** | The checks that catch Rock changing code the builder ports: the lock check against GitHub and the parity test before a release. |
