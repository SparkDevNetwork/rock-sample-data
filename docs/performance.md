# Performance

Where a load's time and disk space go, and what could make Full 7 install faster. Full 7 SHOULD install in 2 to 3 minutes and MUST install in under 5 ([design spec](design-spec.md)). The [strategy](strategy.md) promises under a minute for the largest dataset.

## How it is measured

The loader times every load step, and the parts of its largest steps, with the wall clock. It reports each timing through its normal progress reporting, so the loader CLI prints them and Rock's installer gets them too:

- **Steps.** Each step ends with a line such as `  (4,412 ms)`.
- **Parts of a step.** People, Attendance, Giving, and Communications time their pieces, such as `  (Bulk email: 75,712 ms)`.
- **Bulk copies and index rebuilds.** After the last step, one line for each bulk copy (table, rows, and time) and each index rebuild.
- **Commit and total.** The commit, then `Done in ... ms.`

Each report that carries a timing has a `Timing` object (see [Using the library](../loader/README.md#using-the-library)). The [verify tool](../verify/README.md#receipts-and-the-publishing-gate) saves every timing in each receipt's `load` section. It also samples the database's used data and log space once a second, and records the file growths SQL Server's default trace saw. So every verify run leaves a record, and a slower load shows up in its receipt.

Timing only reads the clock. It never changes what is loaded.

## After the speed pass

Loader 0.1.0-alpha.15 rebuilds each large table's indexes once instead of after every part that copies into it. Communications defers the rebuilds of `CommunicationRecipient`, `Interaction`, and `InteractionSession` for the whole step, and `History` stays deferred until Logins have written their rows. Deferrals now nest (`RockDatabase.DeferIndexRebuild`), so an email writer's own rebuild only ends its own deferral. A load also fails before it commits if any table's indexes are still off. The rows written are the same, and every foreign key is still checked on every copy.

**Full 7 now loads in about 173 seconds, down from about 238 to 263.** Wall-clock time on the shared test machine swings by 30% or more when other sessions run their own loads. So the fair comparison is paired: three loads of the same package with the loader CLI before the fix (`main`) and three after, alternating, on fresh copies of the trusted base, with check images and no logins.

| Pair | Before | After |
|---|---:|---:|
| 1 | 250 s | 175 s |
| 2 | 423 s (another session loaded Full 7 at the same time) | 173 s |
| 3 | 263 s | 172 s |
| **Median** | **263 s** | **173 s** |

The fix saved 75 to 90 seconds in each fair pair, about a third of the load. Verify loads tell the same story: medians of 172 seconds (four loads, fix for the communication tables only) and 202 seconds (four loads with the `History` change too, while two other sessions ran Full 7 verifies).

| | Before | After |
|---|---:|---:|
| Index rebuilds, whole load (verify medians) | 133 s | 64 to 67 s |
| Rebuilds of the four communication tables | 14 | 4 |
| Peak used data and log (verify) | 19,940 MB | 12,854 MB |
| Data file and log file after the load | 9,160 and 11,336 MB | 5,704 and 7,624 MB |

The peak space fell by about 7 GB, because three of every four rebuild rounds are gone, and under FULL recovery each round logged every row of the indexes it rebuilt.

### Where the time goes now

Medians of the three "after" loads above.

| Step | Median seconds | Share |
|---|---:|---:|
| **Communications** | **105.6** | **61%** |
| &nbsp;&nbsp;Bulk email | 52.6 | 30% |
| &nbsp;&nbsp;Rebuild indexes (once, for the whole step) | 37.1 | 21% |
| &nbsp;&nbsp;One-to-one | 8.7 | 5% |
| &nbsp;&nbsp;Push | 2.7 | 2% |
| &nbsp;&nbsp;Texts | 2.4 | 1% |
| Check images | 12.6 | 7% |
| Serving (about 6 of it rebuilds `Attendance`'s indexes) | 7.6 | 4% |
| Logins (here, only the `History` rebuild) | 6.6 | 4% |
| People | 5.8 | 3% |
| Giving | 5.2 | 3% |
| Giving analytics | 4.9 | 3% |
| Attendance | 4.7 | 3% |
| Every other step | under 3 each | |
| **Total** | **173.2** | |

### Loading with the indexes on: tried, and slower

Instead of turning the communication tables' indexes off and rebuilding them once, the copies could go into the tables with their indexes on. Two alternating pairs of CLI loads compared the two:

| Pair | Rebuild once | Indexes on |
|---|---:|---:|
| 1 | 137 s | 195 s |
| 2 | 227 s | 430 s |

With the indexes on, bulk email alone took 117 and 294 seconds, and the log grew to 18,888 MB against 7,624. It is slower and much larger, so the loader keeps rebuilding once.

## Before the speed pass

The rest of this page describes the load before loader 0.1.0-alpha.15. It is how the speed pass found its target.

Four verify loads of Full 7 on 2026-10-28, on Rock 21.0.2 (`C:\Development\Rock-21`), with the default seed and every part. They ran on a developer laptop with SQL Server 2025 Developer Edition, where other sessions' verify runs sometimes ran at the same time. The totals were 235, 240, 243, and 213 seconds. The median is **238 seconds**, close to the 228 on Full 7's card.

### Each step

Median seconds of the four loads, and each step's share of the median total. The medians of the steps do not add up to the total exactly.

| Step | Median seconds | Share |
|---|---:|---:|
| **Communications** | **175.9** | **74.0%** |
| &nbsp;&nbsp;Bulk email | 75.7 | 31.8% |
| &nbsp;&nbsp;One-to-one | 42.6 | 17.9% |
| &nbsp;&nbsp;Push | 38.7 | 16.3% |
| &nbsp;&nbsp;Texts | 15.5 | 6.5% |
| &nbsp;&nbsp;Flows | 1.0 | 0.4% |
| &nbsp;&nbsp;Setup, lists, and list members | 2.4 | 1.0% |
| Check images | 9.6 | 4.0% |
| Serving (6.1 of it rebuilds Attendance's indexes) | 7.6 | 3.2% |
| People | 5.3 | 2.2% |
| Attendance | 4.8 | 2.0% |
| Giving | 4.4 | 1.9% |
| Giving analytics | 4.1 | 1.7% |
| Small groups | 2.4 | 1.0% |
| Cast expectations | 2.3 | 1.0% |
| Steps | 2.3 | 0.9% |
| Prayer | 2.2 | 0.9% |
| Giving alerts | 2.0 | 0.9% |
| Achievements | 1.6 | 0.7% |
| Configuration | 1.6 | 0.7% |
| Notes | 1.1 | 0.5% |
| Connection requests | 0.7 | 0.3% |
| Calendar | 0.6 | 0.3% |
| Photos | 0.3 | 0.1% |
| Commit | 0.1 | 0.0% |
| **Total** | **237.6** | |

Logins landed on `main` after these four loads, and they are timed as their own step. One verify load with them took 241 seconds, of which Logins took 6.8. Its peak space was 8,925 MB of data and 11,561 MB of log.

### By kind of work

| Work | Median seconds | Share |
|---|---:|---:|
| Rebuilding indexes after large bulk copies | 133.1 | 56% |
| Bulk copies (about 9.4 million rows) | 58.6 | 25% |
| Everything else: generating rows in code, and other SQL | about 46 | 19% |

So the load spends most of its time in SQL Server, not in the loader's own code.

### Peak space

The whole load is one transaction, so SQL Server cannot reuse any of the log until it commits.

| Megabytes | Used before the load | Peak used | File after the load |
|---|---:|---:|---:|
| Data | 361 | 8,680 | 9,160 |
| Log | 16 | 11,260 | 11,336 |
| **Together** | | **19,940** | **20,496** |

That is about 20 GB of disk at the peak, more than half of it log. The four loads agreed within 70 MB. The data and log files grew 306 times in steps of 64 MB, which took about 8.5 seconds in all. These databases use the FULL recovery model, and the server has instant file initialization off, so SQL Server writes zeros over each new 64 MB before the load can use it.

**The recovery model changes the log, not the time.** One more pair of loads, with the loader CLI and check images, went into two copies of the trusted base: one left on FULL recovery and one set to SIMPLE, where SQL Server logs bulk copies and index rebuilds minimally. The FULL load took 220 seconds, and its log grew to 11,336 MB. The SIMPLE load took 241 seconds, and its log grew to only 7,240 MB. The data file reached 9,160 MB in both. That is one load each on a shared machine, so the 21-second gap is within the noise. But SIMPLE recovery clearly does not make the load faster. It only makes the log about a third smaller. Most of the log is the rows themselves, which must be logged in one transaction either way.

### The biggest costs

1. **Rebuilding the communication tables' indexes again and again: 118 seconds (50%).** A bulk copy of 10,000 rows or more turns off the table's nonclustered indexes, and rebuilds them afterward (`RockDatabase.BulkInsert`). Bulk email defers that rebuild until its last chunk, which is right. But each later part copies more than 10,000 rows into the same tables, so each one turns the indexes off and rebuilds them again over the whole table. A rebuild costs about the same whether 15,000 or 1.3 million new rows went in, because it reads every row in the table. `CommunicationRecipient` and `History` are rebuilt 4 times, and `Interaction` and `InteractionSession` 3 times:

   | Part | Rows it adds to these tables | Index rebuild seconds |
   |---|---:|---:|
   | Bulk email | about 6.1 million | 31 |
   | Texts | about 170,000 | 13 |
   | One-to-one | about 460,000 | 35 |
   | Push | about 300,000 | 37 |

   That is why One-to-one and Push take about 40 seconds each, though each writes less than a tenth of bulk email's rows. Texts cost less only because they write no `Interaction` or `InteractionSession` rows.

2. **Bulk copies of communication rows: 42 seconds (17%).** Communications writes about 7 million rows: 1.9 million each in `Interaction` and `InteractionSession`, 1.6 million in `CommunicationRecipient`, and 1.6 million in `History`. Each copy checks every foreign key (`CheckConstraints`), and every row goes into the log. Two choices double the row count: every interaction (an open or a click) gets its own `InteractionSession`, and every sent email adds a "Sent" line to the person's `History`, as Rock's email transport does.

3. **The log: about 11 GB.** The whole load is one transaction, so every row it writes stays in the log until the commit. Under FULL recovery, the repeated index rebuilds are logged in full too. With SIMPLE recovery the log is about 7 GB, so about 4 GB is full logging and the rest is the rows themselves. A church whose disk has less than about 20 GB free, or whose log has a size limit, gets a failed install.

4. **Check images: 9.6 seconds (4%).** About 7.6 seconds of it is drawing 7,178 check images in code, already on every core. The bulk copy is under 2 seconds.

5. **File growth: about 8.5 seconds (4%).** 306 growths of 64 MB each, with instant file initialization off. This depends on the server, not the loader.

The rest, about 46 seconds, is spread across 17 steps, and none of them takes more than 6 seconds. Most of it is planning in code: generating the Crowd, attendance, gifts, and giving analytics, and planning each communication.

## What is left

Full 7 now meets the 2 to 3 minute target. Rough sizes: small is under a day, medium is a few days, large is a week or more. The savings are estimates from the measurements above, not measured.

1. **Fewer communication rows (medium, needs a decision).** Communications still writes about 7 million rows, and bulk email takes about 53 seconds. Sharing one `InteractionSession` among a person's opens and clicks on the same day, and checking whether every "Sent" `History` line is needed, could remove about 2 million rows. That would save roughly 15 to 25 seconds of copying and rebuilding, and a few GB. It changes the data, so it needs a decision and a validation run.
2. **Plan in parallel (large).** About 46 seconds is planning in code, spread over many steps. Each step plans from earlier steps' results, and every plan must stay seeded, so this is a large change for a modest gain.

Check images are not on this list. They take about 12 seconds, and drawing them already uses every core (see the [loader README](../loader/README.md)).

The strategy's "under a minute" would need the row cut above and much more. Full 7 loaded in about 55 seconds before the communications slices, and communications writes about 7 million of its 9.4 million rows.

**Measured on a fast server.** These loads ran on SQL Server 2025 Developer Edition, which has every Enterprise feature. SQL Server rebuilds an index on several cores only in Enterprise and Developer editions. On Standard and Express, which many churches run, each rebuild uses one core, so the index rebuilds (about 50 seconds here) may take longer there.

Things the loader should not change: a church's recovery model or file sizes. Pre-growing the files would save about 6 seconds of file growth here, but those are the church's server settings.
