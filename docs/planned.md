# Planned work

What we plan to add to Rock Sample Data but have not built yet. If something you need is missing, or you have an idea, see [Contributing](contributing.md) to propose it. For what is already done, see [How it works](how-it-works.md) and [Meet the Cast](../cast/families/README.md).

## People and attendance

- **Duplicates in the generated crowd:** about 1% of people with a second, Pending record, often under a nickname, with no birth date and a shared email or mobile, some open, some merged, and some set aside. The Cast's duplicates are built (see [Duplicates](duplicates.md)).
- **Check-in relationships,** such as a grandparent who may check in a grandchild, loaded from the Cast's known relationships.
- **More Cast families** whenever a test or demo needs a case the Cast does not cover yet.
- **Student Night volunteers,** so the Student Ministry team serves at Wednesday Student Night as it does on Sundays.


## Other Rock features

- **Learning (LMS):** learning programs, courses, and classes, with people enrolled, activities completed, and program completions, so Rock's learning screens have real data.
- **Event registrations**: one cancelled mission trip spot with a refunded deposit, financial batch history for registration payments, and the registration emails (confirmations, reminders, payment reminders, and signature receipts) as communication history. Everything else is built: the setup, the students, kids, and adults who registered, their payments, payment plans, wait lists, and placements, the signed camp and mission trip releases, the notes and history, reminders, and data views. See [Event registrations](registrations.md).
- **Adaptive message view history.** A year of "Viewed" interactions in Rock's Adaptive Messages channel, so each adaptation shows its views and saturation works on install day. The segments, messages, and adaptations are built (see [Adaptive messages](adaptive-messages.md)). It waits for Interaction Data's website sessions.

- **The church's app:** pages and content for the Rock Solid Church App, which starts as a stub for push notifications. SMS notifications for note mentions wait for it too. Mentions already wait in each staff member's bell in Rock (see [Notes](../loader/README.md#notes)).

## Installing and quality

- **Install from inside Rock.** Rock's Sample Data Installer block is built, so you choose a dataset in Rock and install it. It waits for a Rock release. It has the field for the sample login password. Next it shows each dataset's database space from the card's `databaseSpaceMegabytes`, in a later Rock change.
- **A stable release.** Each release already builds, checks, and publishes the datasets as prereleases. The first stable release comes next, so Rock installs a tested version by default.
- **Closer to real churches.** Today 91% of measured values fall within tolerance of real church patterns. We keep closing the gaps that remain, such as first-time visitors.
