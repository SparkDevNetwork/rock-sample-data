# Glossary

The words this project uses, in alphabetical order.

| Term | Meaning |
|---|---|
| **Achievement attempt** | One try at an achievement in Rock. It is open while it can still grow, and closed when it succeeds or a streak breaks. Check-in shows the open ones as in progress. |
| **Achievement type** | Something a person can earn in Rock, such as Century Club. Each one reads a streak type and has a goal. |
| **Anchor** | The date every relative date counts back from. It defaults to install day. Tests pin it with `--anchor`. |
| **Business contact** | A person linked to a business record in Rock's Known Relationships. Business Detail lists the contacts, and each contact's Known Relationships shows the business. |
| **Blackout date** | Days a volunteer is away, set in Rock's schedule toolbox. Nobody is scheduled to serve inside them. |
| **Builder** | The .NET tool in `builder/` that turns this repository into one package per dataset. It never touches a database. |
| **Calibration** | Measuring real Rock databases with read-only scripts, so generated data matches real patterns. |
| **Cast** | The hand-designed households (87 today, 6 of them businesses). The same in every dataset, always on the main campus, and safe to rely on by Guid. |
| **Cast expectation** | What Rock should show for a Cast giver (frequency, journey stage, alerts) or for a Cast person's achievements. Every load checks it, along with the business expectations: each campus has its minimum number of businesses, and every business has a contact and a gift in the last 12 months. |
| **Church app** | The Rock Solid Church App, the sample church's mobile app. It starts as a stub so push notifications have an app to point at. |
| **Communication list** | A Rock group of people who get the same mass emails or texts, such as a campus's weekly newsletter list. See [Communications](communications.md). |
| **Companion** | A generated household on one of Core 3's two extra campuses. |
| **Connection request** | A person's request to serve on a team, in Rock's Involvement connection type. It moves through statuses until it is connected, set aside as inactive, or saved for a future follow-up. |
| **Connection status** | A person's place in the journey: Prospect, Participant, Known, Connected, Engaged, or Leading. |
| **Connector** | The person who follows up on a connection request. In the sample church, it is the team's leader at the requester's campus. |
| **Core 1, Core 3, Full 7** | The three datasets. The number is how many campuses each has. |
| **Crowd** | The generated households that give Full 7 its volume (about 25,000 people). See [The Crowd and personas](crowd.md). |
| **Dataset** | One installable set of sample data. |
| **Featured family** | A Cast household with a bio for each person. These are the families most used in testing. |
| **Giving alert** | A note Rock raises about a gift: a large gift, a smaller gift than usual, or a gift that is late. |
| **Giving unit** | The people Rock counts as one giver: a couple who give together, or one person who gives on their own. |
| **Journey stage** | Rock's label for where a giver stands: Non-Giver, New, Consistent, Occasional, Lapsed, or Former. |
| **Legacy** | Data that came from Rock's earlier sample data (version 1.14.1). Legacy Cast families keep their Guids, and their new photos were made from the old ones. |
| **Loader** | The library in `loader/` that installs a package into a fresh Rock database, generating every date for the install day. The loader CLI uses it, and Rock uses it through the `SparkDevNetwork.Rock.SampleData.Loader` NuGet package, which also builds for .NET Framework. |
| **Mass send** | An email, text, or push sent to 25 or more people at once. Calibration part 7 measures them. |
| **Opportunity** | One way to serve that people can ask about, such as Greeter. Each opportunity places people on its serving team. |
| **Package** | A zip the builder writes for one dataset and one exact Rock: data files only, never SQL, with a checksum for every file in `package.json`. |
| **Package index** | `index.json`, the hosted list of packages that Rock's installer block reads, at `storage.rockrms.com/sampledata/packages/`. The release workflow merges each release into it and uploads it last (decision D-release-2). |
| **Pending** | A team member who has connected but is not serving yet. The team leader makes them Active after onboarding. The scheduler only schedules Active members. |
| **Persona** | A behavior pattern, such as a weekly giver or a holiday-only attender. |
| **Photo ethnicity** | A value used only to generate photos that look like a family. It is never written to Rock. |
| **Pool** | A set of ready-made items the builder draws from: the address pool, or the Core 3 photo pool. |
| **Position** | A room a serving team serves in, with a minimum, desired, and maximum number of people at each service. |
| **Profile** | The blended target numbers the builder uses, in `calibration/profiles/`. |
| **Relative date** | A date stored as `yearsAgo` plus a month and day, or as `daysAgo`, instead of a fixed date. |
| **Release workflow** | `.github/workflows/release.yml`. On a version tag it builds, verifies, and publishes every package for every supported Rock, and pushes the loader to nuget.org (decision D-release-1). |
| **Rhythm step** | A step that is a habit, such as being in a small group or giving. It starts and can stop. A milestone step happens once, such as baptism. See [Steps](steps.md). |
| **Rotation** | How often a volunteer serves: one of Rock's four schedule templates (every week, every other week, 1st and 3rd, or 2nd and 4th). |
| **Seed** | The text every random choice starts from. The same seed and anchor give the same data. |
| **Serving team** | A group of volunteers with a shared job on Sunday, such as Greeters. Each campus has eight. See [Serving](serving.md). |
| **SMS conversation** | A text thread between one person and one of the church's system phone numbers, as Rock's SMS Conversations screen shows it. |
| **Source** | One anonymous set of measured ratios, labeled `source-1`, `source-2`, and so on. |
| **Step program** | A path of steps in Rock, such as the Discipleship Path. A person who completes every step gets a program completion. See [Steps](steps.md). |
| **Streak** | One person's record on a streak type: the weeks they took part, and their current and longest run of weeks in a row. |
| **Streak type** | What Rock counts week by week for streaks and achievements. The sample church has Kids Check-in, which counts kids check-ins. |
| **System phone number** | One of the church's own phone numbers in Rock, used to send and receive texts. One can be assigned to a staff member as a personal number. |
| **Test purpose** | The reason a Cast household exists: the features or edge cases it exercises. |
| **Validation** | Running the calibration scripts on generated data and grading the results against the profile. |
| **Version guard** | The checks that catch Rock changing code the builder ports: the lock check against GitHub and the parity test before a release. |
