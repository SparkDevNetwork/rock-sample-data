// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using System.Data;
using System.Globalization;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /*
        11/16/26 - CLAUDE

        Refunds of gifts (decisions 262 and D-uncommon-1). Generated households' gifts get refunds at the profile's rate
        per 1,000 gifts, counted in the same 12 month windows calibration counts (each ends on the first of a month).
        Each refund is one of the six kinds in configuration/refunds.json:

        1. duplicate: the giver gave the same gift online twice, minutes apart. The second gift is refunded in full.
        2. incorrect-amount: a one-time online gift to one fund was typed with an extra zero. The extra is refunded,
           so the gift nets to what the giver meant.
        3. one-fund: the first line of a gift to two funds is refunded, since Rock's refund takes the lines in order.
        4. earlier-year: a gift from late in one year is refunded in full early in the next.
        5. insufficient-funds and returned-check: the bank returned a check, and staff refunded it in full with Rock's
           Insufficient Funds or Returned Check reason. A check has no gateway, so Rock's refund only records it.

        No refund is of an in-kind gift, which was never cash, or of a gift in a foreign currency, which Rock refuses.

        A refund never changes what a gift gave to the building campaign, so the pledges stay as planned. Staff refund
        on weekdays. Every refund comes on or before Rock's last weekly journey stage run, because Rock's journey
        procedure counts every refund in the database, whatever its date. The Cast gets only the refunds its household
        files design. Refunds are planned after every gift, from streams of their own, so the gifts are the same with
        or without them.

        Reason: Rock's refund screens, batches, statements, and giving analytics have refunds to show, at the rate a
        real church has them.
    */

    internal sealed partial class GivingLoader
    {
        private static readonly Guid RefundReasonTypeGuid = new Guid( "61FE3A58-9F4F-472F-A4E0-5116EB90A323" );

        /// <summary>
        /// The refund kinds the loader knows, by their key in configuration/refunds.json.
        /// </summary>
        private static readonly Dictionary<string, RefundKind> RefundKinds = new Dictionary<string, RefundKind>
        {
            ["duplicate"] = RefundKind.Duplicate,
            ["incorrect-amount"] = RefundKind.IncorrectAmount,
            ["one-fund"] = RefundKind.OneFund,
            ["earlier-year"] = RefundKind.EarlierYear,
            ["insufficient-funds"] = RefundKind.InsufficientFunds,
            ["returned-check"] = RefundKind.ReturnedCheck
        };

        private enum RefundKind
        {
            Duplicate,
            IncorrectAmount,
            OneFund,
            EarlierYear,
            InsufficientFunds,
            ReturnedCheck
        }

        /// <summary>
        /// True when a refund kind returns the whole gift, and false when it returns part of it.
        /// </summary>
        public static bool IsWholeGiftRefund( string kindKey ) => RefundKinds.TryGetValue( kindKey, out var kind ) && kind is not ( RefundKind.IncorrectAmount or RefundKind.OneFund );

        /// <summary>
        /// True when a refund kind is a check the bank returned.
        /// </summary>
        public static bool IsReturnedCheckRefund( string kindKey ) => RefundKinds.TryGetValue( kindKey, out var kind ) && kind is RefundKind.InsufficientFunds or RefundKind.ReturnedCheck;

        private HashSet<int> _taxDeductibleAccountIds;
        private HashSet<int> _campaignAccountIds;
        private RefundWriter _refunds;
        private string _refundLine;

        /// <summary>
        /// Every gift Rock's Giving Automation job counted, as it saw them over time. Filled by <see cref="Plan"/>.
        /// </summary>
        public GiftTimeline Gifts { get; private set; }

        /// <summary>
        /// A planned gift, with what a refund of it needs. <see cref="Lines"/> are its detail rows, in Id order.
        /// </summary>
        private sealed record PlannedGift( int TransactionId, DataRow Row, List<DataRow> Lines, int BatchId, LoadedHousehold Household, LoadedPerson Giver, CastGiving Pattern, Wallet Wallet, Gift Gift, bool IsOnline,
            int? ForeignCurrencyCodeValueId );

        /// <summary>
        /// The next free Ids, which a duplicate gift takes before the refunds do.
        /// </summary>
        private sealed class GiftIds
        {
            public int Batch;
            public int PaymentDetail;
            public int Transaction;
            public int Detail;
        }

        /// <summary>
        /// A refund chosen for a gift: its kind, when staff made it, and for a duplicate, when the second gift came.
        /// </summary>
        private sealed record RefundChoice( PlannedGift Gift, RefundKind Kind, RefundKindDefinition Definition, DateTime RefundAt, DateTime? DuplicateAt, SeededRandom Random );

        /// <summary>
        /// The gift as Rock's Giving Automation job sees it, or null when the job does not count it. The rule is Rock's
        /// GetGivingAutomationSourceTransactionQuery: a contribution with a line in a tax-deductible account and a
        /// positive line. <paramref name="net"/> is what is left after a partial refund, when there was one. Amounts get
        /// the database's two decimal places, so values Rock prints from them (median, IQR) read the same as when read
        /// back from the database.
        /// </summary>
        private RockGift RockGiftFor( int transactionId, LoadedPerson giver, DateTime when, bool isOnline, string currency, bool isScheduled,
            IEnumerable<KeyValuePair<int, decimal>> amounts, IEnumerable<KeyValuePair<int, decimal>> net = null )
        {
            var lines = amounts.ToList();
            if ( !lines.Any( a => _taxDeductibleAccountIds.Contains( a.Key ) ) || !lines.Any( a => a.Value > 0 ) )
            {
                return null;
            }

            return new RockGift( transactionId, giver.AliasId, giver.GivingId, giver.CampusId, when,
                DefinedValueId( isOnline ? PaymentMethods.WebsiteSourceGuid : OnsiteSourceGuid ), _payments.CurrencyId( currency ), isScheduled,
                ( net ?? lines ).Where( a => a.Value != 0 ).OrderBy( a => a.Key ).Select( a => (a.Key, a.Value + 0.00m) ).ToList() );
        }

        /// <summary>
        /// Chooses the refunds, adds each duplicate gift to the gift rows, raises each mistyped amount, and plans the
        /// refund rows. Changes <paramref name="qualifying"/> to what Rock counts on install day, and returns each
        /// changed gift as Rock saw it before its refund.
        /// </summary>
        private List<RefundedGift> PlanRefunds( List<LoadedHousehold> households, List<PlannedGift> gifts, HashSet<string> castHouseholdKeys, GiftIds ids, DataTable paymentDetails, DataTable transactions,
            DataTable details, Dictionary<int, (decimal Amount, int Count)> batchTotals, int? gatewayId, int contributionId, List<RockGift> qualifying )
        {
            var refunded = new List<RefundedGift>();
            var config = _source.Refunds;
            if ( config == null )
            {
                return refunded;
            }

            var definitions = config.Kinds.ToDictionary( k => RefundKinds.TryGetValue( k.Key, out var kind ) ? kind : throw new InvalidDataException( $"configuration/refunds.json has the kind {k.Key}, which the loader does not know." ) );
            var reasonIds = DefinedValueIds( RefundReasonTypeGuid );
            var missingReason = config.Kinds.FirstOrDefault( k => !reasonIds.ContainsKey( k.Reason ) );
            if ( missingReason != null )
            {
                throw new InvalidDataException( $"configuration/refunds.json gives {missingReason.Key} the reason {missingReason.Reason}, which is not one of Rock's refund reasons." );
            }

            var lastRun = GivingAnalyticsLoader.LastJourneyRun( GivingAnalyticsLoader.JobRunAt( _dates ) );
            var measured = _profile.Number( "giving.refundsPerThousandGifts", double.NaN );
            var rate = double.IsNaN( measured ) ? config.PerThousandGiftsWithoutProfile : measured;

            var choices = CastRefunds( gifts, castHouseholdKeys, definitions, lastRun );
            choices.AddRange( GeneratedRefunds( gifts, castHouseholdKeys, definitions, rate, lastRun, choices ) );

            var requests = new List<(RefundChoice Choice, RefundableTransaction Original, decimal Amount, string Summary)>();
            foreach ( var choice in choices.OrderBy( c => c.RefundAt ) )
            {
                var gift = choice.Gift;
                var amounts = gift.Gift.Amounts.ToList();
                var total = amounts.Sum( a => a.Value );
                var currencyId = _payments.CurrencyId( gift.Gift.Currency );
                var cardTypeId = _payments.CardType( gift.Gift.Currency, gift.Wallet ) as int?;
                var sourceId = DefinedValueId( gift.IsOnline ? PaymentMethods.WebsiteSourceGuid : OnsiteSourceGuid );
                RefundableTransaction Original( int id, DateTime when, IEnumerable<KeyValuePair<int, decimal>> lines ) => new RefundableTransaction( id, gift.Giver.AliasId, when, contributionId, sourceId,
                    gift.IsOnline ? gatewayId : null, gift.IsOnline ? ( string ) gift.Row["TransactionCode"] : null, gift.ForeignCurrencyCodeValueId, currencyId, cardTypeId, gift.Wallet.Masked( gift.Gift.Currency ), false,
                    lines.Select( l => new RefundableLine( l.Key, l.Value ) ).ToList() );
                RockGift Before( IEnumerable<KeyValuePair<int, decimal>> lines ) => RockGiftFor( gift.TransactionId, gift.Giver, gift.Gift.When, gift.IsOnline, gift.Gift.Currency, gift.Gift.ScheduleIndex.HasValue, lines );

                switch ( choice.Kind )
                {
                    case RefundKind.Duplicate:
                    {
                        // The second gift: one-time, from the same card or bank account, in the same day's batch.
                        var when = choice.DuplicateAt.Value;
                        var paymentId = ids.PaymentDetail++;
                        _payments.AddPaymentDetail( paymentDetails, paymentId, gift.Gift.Currency, gift.Wallet, choice.Random.NextGuid(), _foreignKey, when, when );
                        var id = ids.Transaction++;
                        var code = PaymentMethods.TransactionCode( choice.Random );
                        transactions.Rows.Add( id, gift.BatchId, when, code, contributionId, sourceId, DBNull.Value, gift.Giver.AliasId, gatewayId ?? ( object ) DBNull.Value, paymentId,
                            true, when.Date.AddDays( 1 ), false, SundayCalendar.SundayOnOrAfter( when.Date ), int.Parse( when.ToString( "yyyyMMdd", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture ),
                            choice.Random.NextGuid(), _foreignKey, when, when );
                        foreach ( var amount in amounts )
                        {
                            details.Rows.Add( ids.Detail++, id, amount.Key, amount.Value, choice.Random.NextGuid(), _foreignKey, when, when );
                        }

                        var totals = batchTotals[gift.BatchId];
                        batchTotals[gift.BatchId] = (totals.Amount + total, totals.Count + 1);
                        requests.Add( (choice, Original( id, when, amounts ) with { TransactionCode = code }, total, Summary( choice, amounts, total, total )) );
                        if ( RockGiftFor( id, gift.Giver, when, true, gift.Gift.Currency, false, amounts ) is { } duplicate )
                        {
                            refunded.Add( new RefundedGift( gift.Giver.GivingId, choice.RefundAt, duplicate ) );
                        }

                        break;
                    }

                    case RefundKind.IncorrectAmount:
                    {
                        // The gift came in at the typed amount; the refund brings it back to what the giver meant.
                        var intended = amounts[0].Value;
                        var entered = intended * choice.Definition.MistakeFactor;
                        var typed = new[] { new KeyValuePair<int, decimal>( amounts[0].Key, entered ) };
                        gift.Lines[0]["Amount"] = entered;
                        var totals = batchTotals[gift.BatchId];
                        batchTotals[gift.BatchId] = (totals.Amount + entered - intended, totals.Count);
                        requests.Add( (choice, Original( gift.TransactionId, gift.Gift.When, typed ), entered - intended, Summary( choice, amounts, entered, entered - intended )) );
                        if ( Before( typed ) is { } before )
                        {
                            refunded.Add( new RefundedGift( gift.Giver.GivingId, choice.RefundAt, before ) );
                        }

                        break;
                    }

                    case RefundKind.OneFund:
                    {
                        requests.Add( (choice, Original( gift.TransactionId, gift.Gift.When, amounts ), amounts[0].Value, Summary( choice, amounts, total, amounts[0].Value )) );
                        if ( Before( amounts ) is { } before )
                        {
                            refunded.Add( new RefundedGift( gift.Giver.GivingId, choice.RefundAt, before ) );
                            var index = qualifying.FindIndex( g => g.Id == gift.TransactionId );
                            qualifying[index] = RockGiftFor( gift.TransactionId, gift.Giver, gift.Gift.When, gift.IsOnline, gift.Gift.Currency, gift.Gift.ScheduleIndex.HasValue, amounts, amounts.Skip( 1 ) );
                        }

                        break;
                    }

                    case RefundKind.EarlierYear:
                    case RefundKind.InsufficientFunds:
                    case RefundKind.ReturnedCheck:
                    {
                        requests.Add( (choice, Original( gift.TransactionId, gift.Gift.When, amounts ), total, Summary( choice, amounts, total, total )) );
                        if ( Before( amounts ) is { } before )
                        {
                            refunded.Add( new RefundedGift( gift.Giver.GivingId, choice.RefundAt, before ) );
                            qualifying.RemoveAll( g => g.Id == gift.TransactionId );
                        }

                        break;
                    }
                }
            }

            var refundedBy = StaffAliasId( config.RefundedBy, households );
            _refunds = new RefundWriter( _payments, _foreignKey, _dates.Anchor, (ids.Batch, ids.PaymentDetail, ids.Transaction, ids.Detail) );
            foreach ( var (choice, original, amount, summary) in requests )
            {
                _refunds.Add( new RefundRequest( original, amount, reasonIds[choice.Definition.Reason], summary, choice.RefundAt, refundedBy ) );
            }

            var byKind = string.Join( ", ", definitions.Keys.Select( k => $"{choices.Count( c => c.Kind == k ):N0} {definitions[k].Key}" ) );
            var rateSource = double.IsNaN( measured ) ? "the configuration's default, since the profile has no rate" : "the profile";
            _refundLine = $"  Refunds: {_refunds.Count:N0} (${-_refunds.Total:N0}): {byKind}, at {rate:0.##} per 1,000 gifts from {rateSource}";
            return refunded;
        }

        /// <summary>
        /// The refunds the Cast's household files design. Each refunds the pattern's last gift on or before its day that
        /// its kind can refund, the kind's shortest delay after the gift.
        /// </summary>
        private List<RefundChoice> CastRefunds( List<PlannedGift> gifts, HashSet<string> castHouseholdKeys, Dictionary<RefundKind, RefundKindDefinition> definitions, DateTime lastRun )
        {
            var choices = new List<RefundChoice>();
            foreach ( var patternGifts in gifts.Where( g => castHouseholdKeys.Contains( g.Household.Cast.Key ) && g.Pattern.Refunds.Count > 0 ).GroupBy( g => g.Pattern ) )
            {
                foreach ( var refund in patternGifts.Key.Refunds )
                {
                    var kind = RefundKinds.TryGetValue( refund.Kind, out var known ) && definitions.ContainsKey( known )
                        ? known
                        : throw new InvalidDataException( $"{patternGifts.First().Household.Cast.Key}: a refund of kind {refund.Kind}, which configuration/refunds.json does not have." );
                    var near = _dates.Resolve( refund.Near ).Value.Date.AddDays( 1 );
                    var choice = patternGifts
                        .Where( g => g.Gift.When < near )
                        .OrderByDescending( g => g.Gift.When )
                        .Select( g => Choose( g, kind, definitions[kind], lastRun, isShortestDelay: true ) )
                        .FirstOrDefault( c => c != null )
                        ?? throw new InvalidDataException( $"{patternGifts.First().Household.Cast.Key}: {patternGifts.Key.Person} has no gift a {refund.Kind} refund can have on or before {near.AddDays( -1 ):yyyy-MM-dd}." );
                    choices.Add( choice );
                }
            }

            return choices;
        }

        /// <summary>
        /// Generated households' refunds: in each 12 month window calibration counts, the rate's share of the window's
        /// gifts, less the Cast's refunds in it, split among the kinds by their shares. Each kind takes the gifts it can
        /// refund in a stable shuffled order, one refund per gift, and one per giver in each window.
        /// </summary>
        private List<RefundChoice> GeneratedRefunds( List<PlannedGift> gifts, HashSet<string> castHouseholdKeys, Dictionary<RefundKind, RefundKindDefinition> definitions, double rate, DateTime lastRun, List<RefundChoice> cast )
        {
            var generated = gifts.Where( g => !castHouseholdKeys.Contains( g.Household.Cast.Key ) ).ToList();
            var candidates = definitions.Keys.ToDictionary( kind => kind, kind => generated
                .Select( g => Choose( g, kind, definitions[kind], lastRun, isShortestDelay: false ) )
                .Where( c => c != null )
                .OrderBy( c => SeededRandom.StableGuid( $"{_seed}:refund-order:{c.Kind}:{c.Gift.Household.Cast.Guid}:{c.Gift.Pattern.Person}:{c.Gift.Gift.When:o}" ) )
                .ToList() );

            // Calibration counts the 12 months before the first of the install month; the days since then count too.
            var monthStart = new DateTime( _dates.Anchor.Year, _dates.Anchor.Month, 1 );
            var windows = new List<(DateTime Start, DateTime End)> { (monthStart, _dates.Anchor.Date.AddDays( 1 )) };
            for ( var end = monthStart; end > _calendar.HistoryStart; end = end.AddMonths( -12 ) )
            {
                windows.Add( (end.AddMonths( -12 ), end) );
            }

            var chosen = new List<RefundChoice>();
            var chosenGifts = new HashSet<int>();
            foreach ( var (start, end) in windows )
            {
                var giftsInWindow = gifts.Count( g => g.Gift.When >= start && g.Gift.When < end );
                var target = ( int ) Math.Round( rate * giftsInWindow / 1000, MidpointRounding.AwayFromZero ) - cast.Count( c => c.RefundAt >= start && c.RefundAt < end );
                var givers = new HashSet<int>();
                foreach ( var (kind, count) in SplitByShare( target, definitions ) )
                {
                    var taken = 0;
                    foreach ( var choice in candidates[kind].Where( c => c.RefundAt >= start && c.RefundAt < end ) )
                    {
                        if ( taken == count )
                        {
                            break;
                        }

                        if ( chosenGifts.Contains( choice.Gift.TransactionId ) || !givers.Add( choice.Gift.Giver.PersonId ) )
                        {
                            continue;
                        }

                        chosenGifts.Add( choice.Gift.TransactionId );
                        chosen.Add( choice );
                        taken++;
                    }
                }
            }

            return chosen;
        }

        /// <summary>
        /// Splits a count among the kinds by their shares, giving leftover units to the largest remainders.
        /// </summary>
        private static List<(RefundKind Kind, int Count)> SplitByShare( int total, Dictionary<RefundKind, RefundKindDefinition> definitions )
        {
            if ( total <= 0 )
            {
                return new List<(RefundKind, int)>();
            }

            var shareTotal = definitions.Values.Sum( d => d.Share );
            var exact = definitions.ToDictionary( d => d.Key, d => total * d.Value.Share / shareTotal );
            var counts = exact.ToDictionary( e => e.Key, e => ( int ) Math.Floor( e.Value ) );
            foreach ( var kind in exact.OrderByDescending( e => e.Value - Math.Floor( e.Value ) ).ThenBy( e => e.Key ).Take( total - counts.Values.Sum() ).Select( e => e.Key ) )
            {
                counts[kind]++;
            }

            return counts.Select( c => (c.Key, c.Value) ).ToList();
        }

        /// <summary>
        /// A refund of the kind for the gift, or null when the kind cannot refund it: a duplicate needs an online gift,
        /// an incorrect amount a one-time online gift to one fund, a one-fund refund a gift to two funds, an earlier
        /// year refund a gift whose refund falls in the next year, and a returned check a check. None touches a building
        /// campaign line, except a duplicate, whose refund takes back exactly what it added. No kind refunds an in-kind
        /// gift or a gift in a foreign currency. Staff refund on a weekday, during office hours, on or before Rock's last
        /// journey run.
        /// </summary>
        private RefundChoice Choose( PlannedGift gift, RefundKind kind, RefundKindDefinition definition, DateTime lastRun, bool isShortestDelay )
        {
            // Cheap tests first: a seeded stream for every gift and kind would slow Full 7.
            var latestDay = gift.Gift.When.Date.AddDays( definition.DelayDays.Max + 2 );
            if ( gift.Gift.When >= lastRun || ( kind == RefundKind.EarlierYear && latestDay.Year == gift.Gift.When.Year ) || gift.ForeignCurrencyCodeValueId.HasValue || gift.Gift.Currency == "Non-Cash Asset" )
            {
                return null;
            }

            // Every account a gift uses is looked up by now, so the campaign's campus accounts are all known.
            _campaignAccountIds ??= _accountIds.Where( a => a.Key.Fund == _source.Campaign?.Fund ).Select( a => a.Value ).ToHashSet();
            var amounts = gift.Gift.Amounts.ToList();
            var isCampaign = amounts.Select( a => _campaignAccountIds.Contains( a.Key ) ).ToList();
            var isEligible = kind switch
            {
                RefundKind.Duplicate => gift.IsOnline,
                RefundKind.IncorrectAmount => gift.IsOnline && !gift.Gift.ScheduleIndex.HasValue && amounts.Count == 1 && amounts[0].Value <= definition.MaximumIntendedAmount && !isCampaign[0],
                RefundKind.OneFund => amounts.Count >= 2 && !isCampaign[0],
                RefundKind.InsufficientFunds or RefundKind.ReturnedCheck => gift.Gift.Currency == "Check" && !isCampaign.Contains( true ),
                _ => !isCampaign.Contains( true )
            };
            if ( !isEligible )
            {
                return null;
            }

            var random = new SeededRandom( $"{_seed}:refunds:{kind}:{gift.Household.Cast.Guid}:{gift.Pattern.Person}:{gift.Gift.When:o}" );
            var day = RefundDay( gift.Gift.When, isShortestDelay ? definition.DelayDays.Min : random.Between( definition.DelayDays.Min, definition.DelayDays.Max ) );
            var refundAt = day.AddMinutes( random.Between( 9 * 60, 17 * 60 - 1 ) ).AddSeconds( random.Between( 0, 59 ) ).AddMilliseconds( random.Between( 0, 999 ) );
            DateTime? duplicateAt = kind != RefundKind.Duplicate ? null : DuplicateTime( gift, random );
            var isOnTime = refundAt <= lastRun && refundAt > gift.Gift.When
                && ( kind != RefundKind.Duplicate || duplicateAt.HasValue )
                && ( kind != RefundKind.EarlierYear || refundAt.Year > gift.Gift.When.Year );
            return isOnTime ? new RefundChoice( gift, kind, definition, refundAt, duplicateAt, random ) : null;
        }

        /// <summary>
        /// The day staff refund a gift made at <paramref name="giftAt"/>, <paramref name="delayDays"/> later: moved to the
        /// next weekday when it falls on a weekend, Christmas Day, or New Year's Day, when the office is closed.
        /// </summary>
        public static DateTime RefundDay( DateTime giftAt, int delayDays ) => OfficeDay( giftAt.Date.AddDays( delayDays ) );

        /// <summary>
        /// The first day on or after <paramref name="day"/> that the church office is open: a weekday that is not
        /// Christmas Day or New Year's Day.
        /// </summary>
        private static DateTime OfficeDay( DateTime day )
        {
            while ( day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || ( day.Month, day.Day ) is (12, 25) or (1, 1) )
            {
                day = day.AddDays( 1 );
            }

            return day;
        }

        /// <summary>
        /// When a duplicate of the gift came, or null when none could. After a one-time gift, the giver sent the form
        /// again minutes later, the same day. After a scheduled gift, which the gateway runs overnight, the giver gave the
        /// same gift by hand at least an hour later that day, between 7am and 9pm, not knowing the schedule had run.
        /// </summary>
        private static DateTime? DuplicateTime( PlannedGift gift, SeededRandom random )
        {
            if ( !gift.Gift.ScheduleIndex.HasValue )
            {
                var again = gift.Gift.When.AddMinutes( random.Between( 2, 40 ) );
                return again.Date == gift.Gift.When.Date ? again : null;
            }

            var earliest = Math.Max( 7 * 60, ( int ) gift.Gift.When.TimeOfDay.TotalMinutes + 60 );
            return earliest > 21 * 60 ? null : gift.Gift.When.Date.AddMinutes( random.Between( earliest, 21 * 60 ) );
        }

        /// <summary>
        /// A summary from the kind's list, with its amounts, funds, and check number filled in. A duplicate of a
        /// scheduled gift has summaries of its own.
        /// </summary>
        private string Summary( RefundChoice choice, List<KeyValuePair<int, decimal>> amounts, decimal entered, decimal refunded )
        {
            static string Money( decimal amount ) => amount.ToString( "C", CultureInfo.GetCultureInfo( "en-US" ) );

            var summaries = choice.Kind == RefundKind.Duplicate && choice.Gift.Gift.ScheduleIndex.HasValue && choice.Definition.ScheduledSummaries.Count > 0
                ? choice.Definition.ScheduledSummaries
                : choice.Definition.Summaries;
            return choice.Random.Pick( summaries )
                .Replace( "{intended}", Money( amounts[0].Value ) )
                .Replace( "{entered}", Money( entered ) )
                .Replace( "{refunded}", Money( refunded ) )
                .Replace( "{fund}", FundName( amounts[0].Key ) )
                .Replace( "{kept}", string.Join( " and ", amounts.Skip( 1 ).Select( a => FundName( a.Key ) ) ) )
                .Replace( "{check}", choice.Gift.Row["TransactionCode"] as string ?? string.Empty );
        }

        /// <summary>
        /// The alias of the staff member with this key in configuration/staff.json, or null when the dataset has none.
        /// </summary>
        private int? StaffAliasId( string staffKey, List<LoadedHousehold> households )
        {
            var guid = _source.Staff?.Staff.FirstOrDefault( s => s.Key == staffKey )?.Person;
            return guid == null ? null : households.SelectMany( h => h.Members ).FirstOrDefault( m => m.Cast.Guid == guid )?.AliasId;
        }
    }
}
