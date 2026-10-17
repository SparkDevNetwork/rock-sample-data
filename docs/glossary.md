# Glossary

The words this project uses, in alphabetical order.

| Term | Meaning |
|---|---|
| **Anchor** | The date every relative date counts back from. It defaults to install day. Tests pin it with `--anchor`. |
| **Builder** | The .NET tool in `builder/` that turns this repository into one package per dataset. It never touches a database. |
| **Calibration** | Measuring real Rock databases with read-only scripts, so generated data matches real patterns. |
| **Cast** | The hand-designed households (83 today). The same in every dataset, always on the main campus, and safe to rely on by Guid. |
| **Cast expectation** | What Rock should show for a Cast giver (frequency, journey stage, alerts). Every load checks it. |
| **Companion** | A generated household on one of Core 3's two extra campuses. |
| **Connection status** | A person's place in the journey: Prospect, Participant, Known, Connected, Engaged, or Leading. |
| **Core 1, Core 3, Full 7** | The three datasets. The number is how many campuses each has. |
| **Crowd** | The generated households that give Full 7 its volume (about 25,000 people). See [The Crowd and personas](crowd.md). |
| **Dataset** | One installable set of sample data. |
| **Featured family** | A Cast household with a bio for each person. These are the families most used in testing. |
| **Giving alert** | A note Rock raises about a gift: a large gift, a smaller gift than usual, or a gift that is late. |
| **Giving unit** | The people Rock counts as one giver: a couple who give together, or one person who gives on their own. |
| **Journey stage** | Rock's label for where a giver stands: Non-Giver, New, Consistent, Occasional, Lapsed, or Former. |
| **Legacy** | Data that came from Rock's earlier sample data (version 1.14.1). Legacy Cast families keep their Guids, and their new photos were made from the old ones. |
| **Loader** | The library in `loader/` that installs a package into a fresh Rock database, generating every date for the install day. The loader CLI uses it today; a Rock block will use it next. |
| **Package** | A zip the builder writes for one dataset and one exact Rock: data files only, never SQL, with a checksum for every file in `package.json`. |
| **Persona** | A behavior pattern, such as a weekly giver or a holiday-only attender. |
| **Photo ethnicity** | A value used only to generate photos that look like a family. It is never written to Rock. |
| **Pool** | A set of ready-made items the builder draws from: the address pool, or the Core 3 photo pool. |
| **Profile** | The blended target numbers the builder uses, in `calibration/profiles/`. |
| **Relative date** | A date stored as `yearsAgo` plus a month and day, or as `daysAgo`, instead of a fixed date. |
| **Seed** | The text every random choice starts from. The same seed and anchor give the same data. |
| **Source** | One anonymous set of measured ratios, labeled `source-1`, `source-2`, and so on. |
| **Test purpose** | The reason a Cast household exists: the features or edge cases it exercises. |
| **Validation** | Running the calibration scripts on generated data and grading the results against the profile. |
| **Version guard** | The checks that catch Rock changing code the builder ports: the lock check against GitHub and the parity test before a release. |
