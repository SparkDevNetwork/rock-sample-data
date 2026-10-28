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

## Full 7 on Rock 21.0.2

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

## The biggest costs

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

4. **Check images: 9.6 seconds (4%).** About 7.6 seconds of it is drawing 7,178 check images in code. The bulk copy is under 2 seconds.

5. **File growth: about 8.5 seconds (4%).** 306 growths of 64 MB each, with instant file initialization off. This depends on the server, not the loader.

The rest, about 46 seconds, is spread across 17 steps, and none of them takes more than 6 seconds. Most of it is planning in code: generating the Crowd, attendance, gifts, and giving analytics, and planning each communication.

## Candidate fixes

Rough sizes: small is under a day, medium is a few days, large is a week or more. The savings are estimates from the measurements above, not measured.

1. **Rebuild the communication indexes once (small).** Defer the rebuild across the whole Communications step, and rebuild once at its end, before Cast expectations reads the tables. Nothing in between reads these tables except `MAX( [Id] )` on the clustered key, which stays on. Another way is to turn the indexes off only when a copy is large next to the table it goes into. The last rebuild round costs about 37 seconds, so this saves about **80 seconds**: Full 7 would take about **2 minutes 40 seconds**, inside the 2 to 3 minute target. It should also shrink the log, because three of the four rebuild rounds go away. Verify must still pass, and the timing record will show the result.
2. **Fewer communication rows (medium, needs a decision).** Share one `InteractionSession` among a person's opens and clicks on the same day, and check whether every "Sent" `History` line is needed. Removing about 2 million rows would save roughly 15 to 20 seconds and a few GB. It changes the data, so it needs a decision and a validation run.
3. **Load the communication tables with their indexes on (small to try).** Measure whether copying into `Interaction` and the others with indexes on beats copying with them off and rebuilding once. With fix 1 in, the rebuild still costs about 37 seconds.
4. **Draw check images in parallel (small).** Drawing is in code and each image is independent, so it could use every core. It saves about 5 seconds. Each image's random choices must still come from its own seeded stream.
5. **Plan in parallel (large).** About 46 seconds is planning in code, spread over many steps. Each step plans from earlier steps' results, and every plan must stay seeded, so this is a large change for a modest gain.

Fix 1 alone should meet the 2 to 3 minute target. The strategy's "under a minute" would need fixes 1 to 3 and more. Full 7 loaded in about 55 seconds before the communications slices, and communications now writes about 7 million of its 9.4 million rows.

Things the loader should not change: a church's recovery model or file sizes. Pre-growing the files would save the 8.5 seconds of growth, but those are the church's server settings.
