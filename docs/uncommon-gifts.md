# Uncommon gifts

How returned checks and in-kind gifts work in the sample data, and why. The decisions are 288 and 289 in [Decisions](decisions.md). The numbers live in `configuration/refunds.json` and `configuration/in-kind-gifts.json`, so they can be tuned in one place.

## The idea

Most gifts are cash, checks, cards, and bank transfers, all in dollars. A real church also sees a few uncommon ones. A check bounces. A family gives stock, a car, or a piano. The sample data has a few of each, written the way staff enter them in Rock. So Rock's screens, statements, and giving analytics treat them as they would at a real church.

Each part reads the same Rock code in Rock 20.0.10 and 21.0.2.

Every gift is in US dollars, the church's own currency. A gift in another currency is too rare at most churches to be worth showing.

## Returned checks

### What Rock has

Staff record a check the bank returned with the Refund button in the Transaction Detail block, the same dialog they use for any refund (see [Refunds](refunds.md)). Rock ships two refund reasons for it: Insufficient Funds and Returned Check. A check has no gateway, so Rock's refund only records it. It calls no gateway and gets no gateway code. Like any refund, it lands in the day's refund batch, which for a check is " Check - Refund".

### How they are made

Returned checks are two of the refund kinds in `configuration/refunds.json`. They share the profile's refund rate, 2.54 for every 1,000 gifts, with the other kinds. Calibration counts every negative gift, and a church records a returned check as one, so a returned check is part of that rate, not added to it.

| Kind | Reason | Share of refunds | Days after the check | What happened |
|---|---|---|---|---|
| insufficient-funds | Insufficient Funds | 4% | 3 to 10 | The bank returned the check for insufficient funds. |
| returned-check | Returned Check | 3% | 3 to 12 | The bank returned the check for another reason: a closed account, a stop payment, or a missing signature. |

Each refunds a whole check, from the Sunday offering or the mail. On 2026-11-17, at the default seed, Full 7 had 16 returned checks among its 237 refunds, and Core 1 and Core 3 had only the Cast's. A check with a building campaign line is never returned, so every pledge stays as planned. A summary can name the check number, such as "The bank returned check 1042 for insufficient funds." The Cast gets only the returned checks its household files design.

### The Cast's story

Javelina Wash Coffee, Natalie Silva's coffee shop, mails a business check every month. After a slow month at the shop, one check came back for insufficient funds, about five months before install. Priya Raman recorded it three days later, or on the next weekday when that fell on a weekend, as a refund with the Insufficient Funds reason. The business has no giving expectation, so nothing a test relies on changes. Every load checks the refund.

### Bank transfers that bounce

The sample data has none. Rock records a bounced bank transfer another way. Its Download Payments job asks the gateway for the day's payments. For a bounced transfer, it writes a reversal: a negative transaction with the gift's own code and the gateway's status, and no refund row. Rock's Giving Automation job leaves a reversal out and keeps counting the gift. The sample church's gateway is Rock's Test Gateway, which cannot download a reversal, so the church could not have one.

## In-kind gifts

### What Rock has

Staff enter an in-kind gift in the Transaction Detail block. It is paid with the Non-Cash Asset currency type. It has one of Rock's Non-Cash Asset Types: Property, Stocks And Bonds, Vehicles, or Other. Its summary says what was given, and its amount is its fair value. These screens use it:

| Screen | What it shows |
|---|---|
| Transaction Detail | The asset type and the summary. |
| Batch List and Batch Detail | The day's "Non-Cash Gifts" batch at the giver's campus. |
| Contribution statements | A Non-Cash Gifts section after the cash gifts, with the summary as each gift's description. |
| Giving Overview and giving analytics | The gift, counted like any other. Rock's Giving Automation job filters no currency. |
| Pledge Analytics and the statement's pledge section | The gift, counted toward a pledge. The default statement template's Include Non-Cash Gifts setting is on, so the statement agrees with Rock's pledge screens. |

### How they are made

| Kind | Asset type | Who gives | Value |
|---|---|---|---|
| stock | Stocks And Bonds | The largest givers now and then, mostly in the fall (decision 43), and stock gifts to the building campaign | Set by the giving pattern or the pledge |
| vehicle | Vehicles | Generated households that give, at 1 per 1,000 households each year | $800 to $12,000, by the vehicle |
| equipment | Other | Generated households that give, at 1.5 per 1,000 households each year | $500 to $4,000, by the item, such as a piano, a refrigerator for the food pantry, or a sound mixer |

Vehicles and equipment go to the General Fund at the giver's campus, on a weekday during office hours. Calibration measures non-cash gifts at 1.3% of dollars and under 0.1% of gifts. The largest givers' stock gives about that, and vehicles and equipment add a little. On 2026-11-17, at the default seed, Full 7 had 23 stock gifts, 2 vehicles, and 10 pieces of equipment over its three years, and non-cash gifts were 1.1% of the last year's dollars. Core 3, with about a hundred giving households, had none of the vehicles or equipment. Calibration has no rate for vehicles or equipment, so those counts are designed. Rock's fourth type, Property, is left out, since a gift of land is rare and large.

No in-kind gift is ever refunded, because it was never cash. The Cast gets only the in-kind gifts its household files design.

### The Cast's story

William and Catherine Price gave $25,000 in stock to the Built on the Rock campaign, 30 weeks after Commitment Sunday (see [Pledges](pledges.md)). It is a non-cash gift of Stocks And Bonds with its summary, and it counts toward their pledge on their statement. Every load checks it.

## Checks

1. **Invariants** (`validation/invariants.sql`): `returned-check-is-a-whole-check` and `non-cash-gift-is-rocks`.
2. **Cast expectations:** Javelina Wash Coffee's returned check, and the Price family's stock gift.
3. **The refund check** in every load's log covers returned checks, as it does every refund.
4. **Validation** grades the refund rate and the non-cash share of gifts and dollars.
5. **The anchor sweep** holds the Cast giving stories on 60 install days, the returned check and the stock gift among them.
6. **The load log** counts each kind: "In-kind gifts" by kind, and the refunds by kind.
7. **The Rock version guard** (`validation/rock-lock.json`) fingerprints the Rock code this rests on: how a statement splits cash and non-cash gifts. A Rock change to it fails `node validation/check-rock-lock.js`.

## Later

- **Bounced bank transfers,** if the sample church ever uses a gateway that downloads payments.
