# Pledges

The plan for pledges in the sample data, and why. The decisions are 97 to 105 in [Decisions](decisions.md). Pieces 1 and 2 of the build order are built: the fund and its Rock settings, and the Cast's pledges and campaign gifts. The rest is not built yet. Each later piece adds its numbers to `configuration/campaign.json`, so they can be tuned in one place.

## The idea

Rock Solid Church is in the middle of a two-year building campaign called **Built on the Rock**. People and businesses at every campus made pledges, and their gifts show how each pledge is going.

The name comes from the parable of the wise builder in Matthew 7:24-25, the man who "built his house on the rock." It fits a church named Rock Solid, and it fits a building campaign. The campaign pays for a new Student Center at the Phoenix campus and more kids rooms at every campus.

The campaign is always about 40% through on install day. That is the most useful moment for a demo: every pledge has a story, some are ahead, some are behind, and Rock's progress screens are half full.

## What Rock has

Rock stores a pledge as a `FinancialPledge`: a person, an account, a total amount, a frequency, and a start and end date. Rock's save hook adds only a history entry, written against the pledge itself, which no Rock screen shows. These screens use pledges:

| Screen | Where | What it needs |
|---|---|---|
| Pledge List and Pledge Detail | Finance > Pledges | Pledges |
| Pledge Analytics | Finance > Pledge Analytics | Pledges, plus gifts to the pledged account and its child accounts |
| Person profile, Contributions tab | The Giving Configuration block | The person's pledges |
| (Public) Create Pledge page, and Pledge under Give on the external site | Rock's shipped Pledge Entry block | The block's Account and Pledge Date Range settings |
| Contribution statements | The statement template's pledge settings | The pledge accounts, and "include gifts to child accounts" |

One fact shapes the whole plan. **Pledge Analytics counts every gift to the pledged account and its child accounts, with no date limit** unless the user sets one. The sample church has more than ten years of Building Fund gifts. If people pledged to the Building Fund, every pledge would show hundreds of percent complete. So the campaign gets its own fund (decision 98).

## The campaign

| Item | Value |
|---|---|
| Name | Built on the Rock |
| What it builds | A Student Center at the Phoenix campus, and more kids rooms at every campus |
| Length | Two years (104 weeks) |
| Commitment Sunday | The Sunday 40 weeks before install day |
| Campaign dates | From Commitment Sunday to two years later, so install day is about 38% through |
| Quiet phase | Staff and lead givers pledge 6 to 10 weeks before Commitment Sunday |
| Goal | Set at load to a round number a little under the pledged total, so the campaign has passed its goal. It shows in the fund's description, since Rock has no goal field. |

Dates are counted back from install day, like the rest of the data, so the campaign is the same distance along on every install.

The building is not finished, so no rooms change. Check-in and serving keep the rooms they have today.

### The fund

- A new top-level account, **Built on the Rock**, after the Building Fund. It is public and tax deductible, with a description and the campaign dates as the account's start and end dates.
- It has one child account per campus, like the General and Building Funds (decision 21). Pledges go to the parent account. Gifts post to the giver's campus account. Pledge Analytics and statements both roll child accounts up into the parent.
- The **Building Fund keeps running** for upkeep. Givers who give to it today still do. Campaign gifts are new gifts or new lines on top (decision 99).

### Rock settings

- **Pledge pages.** On both (Public) Create Pledge and the external site's Give > Pledge page, the Pledge Entry block's Account is the campaign fund, and its pledge dates are the campaign dates. Anyone can make a test pledge on install.
- **Pledge frequency.** Both pledge pages show the frequency choice, but a pledger may leave it blank (decision 109).
- **Contribution statement.** Rock's default statement template shows pledges for the campaign fund and includes gifts to its child accounts. A pledger's statement shows the pledge, what they have given, and what is left.
- **No history.** Rock's pledge save hook writes its history against the pledge record, not the person, and no Rock screen shows it, so the loader writes none (decision D-pledges-cast-3).

## How pledges are made

Every pledge is a pledge plus the gifts that follow it. The gifts come from the same wallet, campus, and habits as the person's other giving, so the giving screens and the pledge screens agree.

### Who pledges

These are designed targets, not measured ones (decision 101). Today's calibration profile already sets 12% of giving units with a pledge, as an assumption that the sample data must use pledges.

| Who | Share who pledge |
|---|---|
| All giving units | About 12%, the profile's target |
| Consistent and growing givers with 2 or more years at the church | About 35% |
| Occasional, new, and lapsing givers | A few percent |
| Businesses | About 30% |
| Staff | Every staff household, in the quiet phase |

Couples who give as one unit make one pledge. People who give separately (decision 42) pledge separately, or one pledges and the other does not.

### How much

- **Most pledges are one to one and a half years of the unit's giving,** spread over the two years. Amounts round to friendly numbers, such as $1,200, $2,500, and $5,000.
- **Lead gifts.** The top 2% of givers by annual giving pledge two to four years of giving. Together, lead gifts make up about 40% of all pledged dollars. Real campaigns depend on a few large gifts, and Pledge Analytics should show that.
- **Business pledges** follow the business's giving size and are often one-time or quarterly.

### When

- About 10% are quiet-phase pledges from staff and lead givers, 6 to 10 weeks before Commitment Sunday.
- About 70% are made on Commitment Sunday weekend.
- About 15% come in the 6 weeks after.
- About 5% come later, mostly from new people. A late pledge starts on the day it was made.

Every pledge ends on the campaign's last day.

### Frequency

A pledge's frequency follows the giver's habit: a weekly giver pledges weekly, a monthly giver pledges monthly. About 10% are one-time and about 5% are yearly. About 10% leave the frequency blank, since Rock's Pledge Entry block makes it optional.

### Follow-through

Each pledge gets one follow-through pattern. Pledge Analytics then shows every state Rock can show.

| Pattern | Share | What the gifts look like |
|---|---|---|
| Ahead or paid in full | About 15% | Paid in full in one gift, or giving more than the pace |
| On pace | About 50% | Gifts at the pledged frequency, within a few percent of the pace |
| Behind | About 20% | Gifts that started and slowed, or skip periods |
| Stopped | About 10% | Gave for a few months, then stopped |
| Not started | About 5% | Pledged and has not given yet |

Plus **gifts without a pledge.** About 5% of giving units who did not pledge give to the campaign, usually once or a few times. Pledge Analytics lists them as gifts with no pledge.

### The gifts

- A pledger who already gives on the same rhythm adds a **campaign line to their usual gift**, in the same transaction. This is how most people give to a campaign.
- **The line is sized from the gifts the giver really gave** since the pledge started: the pledge's share for that time, spread over those gifts, times the follow-through factor in `configuration/campaign.json` (ahead 1.5, behind 0.55). So an on-pace giver is on pace on any install day, however many Sundays they missed. A catch-up check after a missed Sunday carries the weeks it covers (decision D-pledges-cast-2).
- **An extra campaign gift** rides on the giver's next usual hand-given gift within a month, as people add to their check. Otherwise it is a gift of its own, given the way they usually give. A pledge paid in full is one such gift, the week after pledging.
- Pledgers who give through a **scheduled transaction** get a campaign line on that schedule from the pledge's start date. Rock shows it on their Contributions tab.
- One-time and yearly pledges are paid by their own gifts.
- The largest givers sometimes pay with stock, as they already do for other gifts (decision 43).
- Campaign gifts count in Rock's giving analytics, as they would at a real church. A one-time gift that pays a large pledge in full can raise a large gift alert.

## The Cast's stories

Every Cast story fits the family's existing giving. The Cast validator and the load's Cast expectations check each one.

### People

| Household | Pledge | What it shows |
|---|---|---|
| Price | $150,000, quarterly, quiet phase | A lead gift, ahead of pace, with a $25,000 stock gift. |
| Decker | $6,000, monthly, quiet phase | A staff pledge. Ted's monthly card schedule gets a campaign line. On pace. |
| Hayes | $10,400, weekly | Long-time members on pace, a campaign line on every weekly gift. |
| Carter | $5,000, one-time | Margaret paid it in full in Walter's memory, added to her monthly check. Complete. |
| Brooks | $2,600, weekly | A steady weekly giver, on pace, still giving to the Building Fund too. |
| Lee | $5,200, weekly | A large family behind pace. |
| Hernandez | $1,000, weekly | Gabriel gives ahead of pace and added a $600 gift, so he has given more than he pledged. Complete. |
| Shaw | $1,200, bi-weekly | A late pledge, 24 weeks after Commitment Sunday, from a family that joined after it. It starts the day they pledged. |
| Ortiz | $2,400, monthly | Pledged on Commitment Sunday and stopped when the family stopped coming. Stopped. |
| Sullivan | $3,600, monthly, Chris only | A couple who give separately. Chris pledged; Amanda gave to the campaign twice with no pledge. |
| Dawson | $520, no frequency | Pledged and has not given yet. Not started. |
| Bell | No pledge | One $1,000 campaign gift by ACH. A gift without a pledge. |
| Marble | $5,200, weekly | Bill pledges at home, and his business pledges too. |

### Businesses

| Business | Pledge | What it shows |
|---|---|---|
| Saguaro Ridge Glass and Screen | $16,000, quarterly, by check | A business lead gift on pace. Its owner, Bill Marble, also pledged at home. |
| Mesquite Tree Bookkeeping | $10,000, one-time | Paid in full the week after Commitment Sunday. |
| Desert Bloom Landscaping | $4,800, monthly, by bank transfer | A small business on pace. |
| Cholla Creek Hardware | No pledge | One $500 campaign gift by check. |
| Copper Canyon Video | No pledge | A new giver that did not pledge. |

## Checks

- **Cast expectations.** Each Cast campaign entry has an expected state on install day, checked on every load, like the giving expectations (decision 46). The states are complete, ahead, on pace, behind, stopped, not started, and gifts only. A pledge is on pace within one gift, or a tenth of the pledge, of where it should be. It is stopped when it is behind and its giver gave nothing to the campaign for 60 days (decision D-pledges-cast-1).
- **Invariants.** Every pledge is on the campaign's parent account, inside the campaign dates, for a person who exists. Every campaign gift posts to a campus child account. No pledge is a duplicate for the same giving unit.
- **Validation.** `giving.pctUnitsWithPledge` comes off the skipped list in `validation/tolerances.json` and is graded against the profile's 12%. Campaign gifts add dollars, so the giving measures that pass today must still pass. If one fails, pledge sizes come down before the targets change.
- **Rock Cleanup** must find nothing to fix in pledge or campaign data.
- **No ported code.** Pledge Analytics is a stored procedure that reads the data as it is, so there is nothing for the version guard to watch.

## Build order

Each piece passes its checks before the next one starts.

1. **The fund and settings.** `configuration/campaign.json`, the campaign account and its campus accounts, the two pledge pages, and the statement template. Built.
2. **Cast pledges.** A `campaign` list in the Cast household files, the campaign gifts, and the Cast expectations. Built.
3. **Crowd and business pledges.** The generated pledges and gifts, and validation of the pledge rate.
4. **Docs and the site.** How it works, Verification, the glossary, the roadmap, and the public site's Church page, which can name the campaign.

## Later

- **A campaign graphic** in `graphics/initiatives/built-on-the-rock/`, like Starting Point, used as the account image and on the lobby screens.
- **A finished past campaign,** so statements and analytics show a campaign that ended.
- **The Student Center itself,** with its rooms, once the campaign is over. It could host the midweek student night on the parked list.
- **Campaign communications,** such as a Commitment Sunday email, once communications are built.
