# Refunds

How refunds of gifts work in the sample data, and why. The decisions are D-refunds-1 to D-refunds-4 in [Decisions](decisions.md). The numbers live in `configuration/refunds.json`, so they can be tuned in one place.

## The idea

A refund returns part or all of a gift. Every church makes a few. Someone gives twice by mistake, types an extra zero, gives to the wrong fund, or asks for a gift back. The sample data has refunds at the rate real churches have them, so Rock's refund screens, refund batches, statements, and giving analytics all have something to show.

## What Rock has

Rock stores a refund as a new transaction with negative amounts. A `FinancialTransactionRefund` row shares the refund's Id and links it to the original gift. These screens use refunds:

| Screen | Where | What it shows |
|---|---|---|
| Transaction Detail | Finance > Transactions, then a gift | The gift's refunds. On a refund: its reason, its summary, and the original gift. |
| Batch List and Batch Detail | Finance > Batches | The day's refund batch, such as " VMD - Refund", with a negative control amount. |
| Contribution statements | Finance > Statements | Nothing: Rock's default statement template has Hide Refunded Transactions on, so a refunded gift is left off. |
| Giving Overview and giving analytics | A person's Giving tab | Each gift's amount after its refunds. A gift refunded in full stops counting as a gift. |

Staff make a refund from the Transaction Detail block, which calls `FinancialTransactionService.ProcessRefund`. Rock 20.0.10 and 21.0.2 have the same code. It does this:

1. It refuses a refund of a refund, a refund of a gift in a foreign currency, and a refund of more than is left of the gift after its earlier refunds.
2. It writes a new transaction with the original's type, person, gateway, and source. Through a gateway, Rock's Test Gateway gives the refund a code of "T" and the time to the millisecond. A check or cash gift has no gateway, so its refund has no code.
3. The refund's payment detail copies the original's currency, card type, and masked number.
4. The amount comes off the original's lines in order, each line up to its own amount, as negative lines on the same accounts. So a partial refund of a gift to two funds takes the first fund first.
5. The reason is one of Rock's five shipped refund reasons: Duplicate Payment, Incorrect Amount, Insufficient Funds, Refund Requested, and Returned Check. The summary is free text.
6. The refund lands in the day's batch that `FinancialBatchService` finds or makes. The block's default batch suffix is " - Refund", and its name has no prefix, so a Visa refund lands in " VMD - Refund". Rock's name starts with a space, and the sample data keeps it. The batch has no campus, and its control amount is the sum of its refunds.

The loader's refund writer (`loader/loading/RefundWriter.cs`) does exactly this. It throws instead of writing a refund Rock would refuse.

## How refunds are made

### How many

Generated households get refunds at the profile's `giving.refundsPerThousandGifts`, which calibration part 1 measures: 2.54 refunds for every 1,000 gifts. If a profile has no rate, the loader uses 2, from `configuration/refunds.json`, and says so in the load log.

The loader counts gifts in the same 12-month windows calibration counts, each ending on the first of a month, and plans that window's share of refunds. So validation grades the rate, and it lands close to 2.54.

Only generated households get refunds at the rate: Core 3's companions and Full 7's Crowd. The Cast gets only the refunds its household files design, so Cast stories never change by chance. Core 1 holds only the Cast, so it has the one Cast refund.

### The four kinds

| Kind | Reason | Share | Days after the gift | What happens |
|---|---|---|---|---|
| duplicate | Duplicate Payment | 55% | 1 to 3 | The giver gave the same gift online twice. The second gift is refunded in full. |
| incorrect-amount | Incorrect Amount | 25% | 1 to 4 | A one-time online gift to one fund was typed with an extra zero. The extra is refunded, so the gift nets to what the giver meant. |
| one-fund | Refund Requested | 10% | 2 to 14 | The first fund of a gift to two funds is refunded, as the giver asked. |
| earlier-year | Refund Requested | 10% | 21 to 60 | A gift from late in one year is refunded in full early in the next. |

So 35% of refunds are partial. A duplicate of a one-time gift came minutes after it, when the giver sent the form twice. A duplicate of a scheduled gift came later the same day, between 7am and 9pm, when the giver gave by hand, not knowing the schedule had already run. Each refund's summary comes from the kind's list in `configuration/refunds.json`.

### When and who

Staff refund on weekdays between 9am and 5pm. A refund that would fall on a weekend, Christmas Day, or New Year's Day comes the next weekday, when the office is open. Priya Raman, the Finance Director, makes every refund, so Rock records her as each refund's creator.

Every refund comes on or before Rock's last weekly journey stage run, the Tuesday night before install. Rock's journey stage procedure subtracts every refund in the database, whatever its date. A later refund would make that last run see a refund Rock had not seen yet.

### What a refund never does

1. It never changes what a gift gave to the building campaign, so every pledge stays as planned (see [Pledges](pledges.md)). A duplicate's refund takes back exactly what the duplicate added.
2. A gift has at most one refund, and a giver has at most one refund in each 12-month window.
3. The open batch of unmatched checks has no refunds, because its checks have no giver yet.

## Giving analytics

Rock's Giving Automation job runs every night, and its journey stage run every Tuesday. Each run reads the gifts as they stand that night. A refund changes a gift after the fact. A duplicate counted until it was refunded, and a mistyped gift counted at its full amount until then. Rock kept what those runs decided, such as a Large Gift alert on a gift typed with an extra zero.

So the loader keeps a timeline of the gifts: what Rock sees on install day, with refunds applied, plus each refunded gift as it stood before its refund (`loader/loading/GiftTimeline.cs`). The replays of Rock's runs read the gifts as of each run:

1. **Giving attributes** are as of the night after the giver's last gift. A refund is not a new gift, so Rock does not reclassify a giver for one.
2. **Journey stages** replay every Tuesday with the gifts as of that night.
3. **Giving alerts** replay every night with the gifts as of that night.
4. **Give steps** follow the same weekly journey replay.

Every load the CLI or the verify tool runs checks the plan against Rock. Rock's installer block skips it, as it skips the Cast expectations (decision 217). For each giving unit with a refund, the gifts the plan has on install day must equal the gifts Rock reads from the database, with Rock's own refund rule applied. The log says "Refund check" with the result, beside the journey stage check.

## The Cast's story

Gabriel Hernandez, the young guitar player on the Phoenix Worship Team, gives every Monday by a scheduled card gift. About seven weeks before install, he also gave the same gift by hand that evening. Priya refunded the second gift the next day, with the reason Duplicate Payment. His story is not about giving, so his journey stage stays Consistent. Every load checks both: the refund, made the next day, of a gift with a twin, and his stage.

His household file names the refund (`refunds` on his giving pattern), and his pattern now names card, so his gifts are online on every install day.

## Checks

1. **Invariants** (`validation/invariants.sql`): `refund-is-rocks`, `refund-returns-what-was-paid`, `refund-batch-holds-only-refunds`, `refund-is-of-a-gift`, and `batch-control-amount-is-its-total`. `transaction-has-positive-details` now leaves out refunds, which are negative by design.
2. **Cast expectations:** Gabriel Hernandez's duplicate refund and his journey stage.
3. **The refund check and the journey stage check** in every load's log.
4. **Validation** grades `giving.refundsPerThousandGifts`, which it skipped before refunds existed.
5. **The anchor sweep** holds the Cast giving stories, Gabriel's refund among them, on 60 install days.

## Shared with registrations

The refund writer takes any transaction, not only a gift. Registration Follow-ups will use it for the cancelled mission trip spot's refunded deposit. A registration payment's refund keeps each line's registration, as Rock's does. Rock also writes a "$300.00 Refund" line on the registration's history, which Registration Follow-ups will add. Until then, the invariant `refund-is-of-a-gift` holds every refund to a gift.

## Later

Other uncommon transactions, chosen from what Rock stores and what calibration shows: a gift in a foreign currency (which Rock cannot refund), a returned check, or an in-kind gift.
