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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /*
        11/17/26 - CLAUDE

        In-kind gifts and Canadian checks (decisions D-uncommon-2 and D-uncommon-3), the way staff enter them in
        Rock's Transaction Detail block, which is the same in Rock 20.0.10 and 21.0.2:

        1. An in-kind gift is paid with the Non-Cash Asset currency type and has one of Rock's Non-Cash Asset Types and
           a summary that says what was given. Its amount is its fair value. The largest givers' stock and the
           building campaign's stock gifts were already planned; vehicles and equipment come from generated households
           that give, at a small rate each year, from streams of their own.
        2. No gateway that ships with Rock takes a gift in another currency (GatewayComponent.IsCurrencyCodeSupported
           accepts only the church's own), so a Canadian winter visitor gives a Canadian check. Staff enter it with the
           currency code, the Canadian amount on each line, and the dollars the bank credited at its rate that day. The
           check writer is a generated retiree who gives on Sundays and does not pledge, so no campaign line changes.

        Reason: Rock's statements, transaction screens, and giving analytics have in-kind and foreign currency gifts
        to show, written as Rock's own screens write them.
    */

    internal sealed partial class GivingLoader
    {
        private static readonly Guid NonCashAssetTypesGuid = new Guid( "6B19C65E-FF8F-4ADD-9DA0-48E53FC074A6" );
        private static readonly Guid CurrencyCodesGuid = new Guid( "B9F3D359-4365-4594-BCEE-D23FA824FB81" );

        /// <summary>
        /// The in-kind kinds by key, or null when the package has no configuration/in-kind-gifts.json.
        /// </summary>
        private Dictionary<string, InKindKindDefinition> _inKindKinds;
        private Dictionary<string, int> _assetTypeIds;
        private readonly Dictionary<string, (int Count, decimal Total)> _inKindTotals = new Dictionary<string, (int, decimal)>();

        private int? _foreignCurrencyCodeId;
        private int _foreignCurrencyHouseholds;
        private int _foreignCurrencyGifts;
        private decimal _foreignCurrencyTotal;
        private decimal _foreignCurrencyChurchTotal;

        /// <summary>
        /// Reads the in-kind kinds and the Rock asset types they name. Throws when a kind names an asset type Rock does
        /// not ship, or when there is no stock kind for the gifts the giving patterns plan.
        /// </summary>
        private void LoadInKindKinds()
        {
            var config = _source.InKindGifts;
            if ( config == null )
            {
                return;
            }

            _assetTypeIds = DefinedValueIds( NonCashAssetTypesGuid );
            var unknown = config.Kinds.FirstOrDefault( k => !_assetTypeIds.ContainsKey( k.AssetType ) );
            if ( unknown != null )
            {
                throw new InvalidDataException( $"configuration/in-kind-gifts.json gives {unknown.Key} the asset type {unknown.AssetType}, which is not one of Rock's Non-Cash Asset Types." );
            }

            _inKindKinds = config.Kinds.ToDictionary( k => k.Key );
            if ( !_inKindKinds.TryGetValue( "stock", out var stock ) || stock.Items.Count == 0 )
            {
                throw new InvalidDataException( "configuration/in-kind-gifts.json needs a stock kind with summaries, for the stock gifts the giving patterns plan." );
            }
        }

        /// <summary>
        /// The vehicles and equipment a generated household gives, through its first giving pattern: in each year the
        /// pattern gives, each kind's chance per 1,000 households, on a weekday in office hours, at a fair value in the
        /// item's range. The Cast and businesses get none.
        /// </summary>
        private IEnumerable<Gift> InKindGifts( PatternPlan plan, HashSet<string> castHouseholdKeys )
        {
            var household = plan.Household;
            if ( _inKindKinds == null || castHouseholdKeys.Contains( household.Cast.Key ) || household.Cast.Kind == "business" || household.Cast.Giving[0] != plan.Pattern )
            {
                yield break;
            }

            var first = plan.Started > _calendar.HistoryStart ? plan.Started : _calendar.HistoryStart;
            var last = plan.Ended.HasValue && plan.Ended < _calendar.HistoryEnd ? plan.Ended.Value : _calendar.HistoryEnd;
            foreach ( var kind in _inKindKinds.Values.Where( k => k.PerThousandGivingHouseholdsPerYear > 0 && k.Items.Count > 0 ) )
            {
                var random = new SeededRandom( $"{_seed}:in-kind:{household.Cast.Guid}:{kind.Key}" );
                for ( var year = first.Year; year <= last.Year; year++ )
                {
                    if ( !random.Chance( kind.PerThousandGivingHouseholdsPerYear / 1000 ) )
                    {
                        continue;
                    }

                    var item = random.Pick( kind.Items );
                    var when = OfficeDay( new DateTime( year, 1, 1 ).AddDays( random.Between( 0, 364 ) ) ).AddHours( random.Between( 9, 16 ) ).AddMinutes( random.Between( 0, 59 ) );
                    var value = Math.Round( ( decimal ) random.Between( ( double ) item.Value.Min, ( double ) item.Value.Max ) / 25, MidpointRounding.AwayFromZero ) * 25;
                    if ( when >= first && when <= last )
                    {
                        yield return new Gift( when, false, "Non-Cash Asset", null, new Dictionary<int, decimal> { [AccountId( kind.Fund, household.CampusKey )] = value }, 0, kind.Key, item.Summary );
                    }
                }
            }
        }

        /// <summary>
        /// Fills in what staff typed for a non-cash gift: its asset type and a summary, which Rock's statement prints as
        /// the gift's description. A stock gift's summary comes from a stream of its own.
        /// </summary>
        private void SetInKind( DataRow transactionRow, Gift gift, LoadedHousehold household, CastGiving pattern )
        {
            if ( _inKindKinds == null )
            {
                return;
            }

            var kind = _inKindKinds[gift.InKind ?? "stock"];
            transactionRow["NonCashAssetTypeValueId"] = _assetTypeIds[kind.AssetType];
            transactionRow["Summary"] = gift.Summary ?? new SeededRandom( $"{_seed}:in-kind-summary:{household.Cast.Guid}:{pattern.Person}:{gift.When:o}" ).Pick( kind.Items ).Summary;
            var totals = _inKindTotals.GetValueOrDefault( kind.Key );
            _inKindTotals[kind.Key] = (totals.Count + 1, totals.Total + gift.Amounts.Values.Sum());
        }

        /// <summary>
        /// The giving patterns whose winter checks are in the foreign currency: the checks of a few generated retirees
        /// who give on Sundays and do not take part in the building campaign, chosen in a stable shuffled order. Also
        /// finds the currency's Rock Id.
        /// </summary>
        private HashSet<CastGiving> ForeignCurrencyPatterns( IEnumerable<PatternPlan> plans, List<LoadedHousehold> households, HashSet<string> castHouseholdKeys )
        {
            var chosen = new HashSet<CastGiving>();
            var config = _source.ForeignCurrency;
            if ( config == null )
            {
                return chosen;
            }

            _foreignCurrencyCodeId = DefinedValueIds( CurrencyCodesGuid ).TryGetValue( config.CurrencyCode, out var id )
                ? id
                : throw new InvalidDataException( $"configuration/foreign-currency.json names the currency {config.CurrencyCode}, which is not one of Rock's Currency Codes." );

            var eligible = plans
                .Where( p => !castHouseholdKeys.Contains( p.Household.Cast.Key ) && p.Household.Cast.Kind != "business"
                    && p.Channel.Onsite && p.Channel.Currency == "Check"
                    && p.Giver.BirthDate.HasValue && _dates.AgeOn( p.Giver.BirthDate.Value ) >= config.MinimumGiverAge
                    && ( Campaign == null || !Campaign.ContainsKey( p.Pattern ) ) )
                .ToList();
            var generated = households.Count( h => !castHouseholdKeys.Contains( h.Cast.Key ) );
            var count = generated == 0 ? 0 : Math.Max( config.MinimumHouseholds, ( int ) Math.Round( generated * config.HouseholdsPerThousand / 1000, MidpointRounding.AwayFromZero ) );
            var families = eligible
                .Select( p => p.Household )
                .Distinct()
                .OrderBy( h => SeededRandom.StableGuid( $"{_seed}:foreign-currency:{h.Cast.Guid}" ) )
                .Take( count )
                .Select( h => h.FamilyId )
                .ToHashSet();
            foreach ( var plan in eligible.Where( p => families.Contains( p.Household.FamilyId ) ) )
            {
                chosen.Add( plan.Pattern );
            }

            _foreignCurrencyHouseholds = families.Count;
            return chosen;
        }

        /// <summary>
        /// For a winter visitor's Sunday check in one of their months: the round amount the giver wrote in the foreign
        /// currency on each line, by account. Each of the gift's own lines becomes the dollars the bank credited at its
        /// rate that day. Returns null for any other gift, which stays in dollars.
        /// </summary>
        private Dictionary<int, decimal> ForeignCurrencyLines( Gift gift )
        {
            var config = _source.ForeignCurrency;
            if ( !gift.Onsite || gift.Currency != "Check" || !config.Months.Contains( gift.When.Month ) )
            {
                return null;
            }

            var rate = Math.Round( ( decimal ) new SeededRandom( $"{_seed}:exchange-rate:{config.CurrencyCode}:{gift.When:yyyy-MM-dd}" ).Between( ( double ) config.Rate.Min, ( double ) config.Rate.Max ), 4 );
            var foreign = new Dictionary<int, decimal>();
            foreach ( var accountId in gift.Amounts.Keys.ToList() )
            {
                foreign[accountId] = RoundGift( gift.Amounts[accountId] / rate, exact: false );
                gift.Amounts[accountId] = Math.Round( foreign[accountId] * rate, 2, MidpointRounding.AwayFromZero );
            }

            _foreignCurrencyGifts++;
            _foreignCurrencyTotal += foreign.Values.Sum();
            _foreignCurrencyChurchTotal += gift.Amounts.Values.Sum();
            return foreign;
        }

        /// <summary>
        /// The load log's lines for in-kind gifts and foreign currency checks, or none when the package has neither.
        /// </summary>
        private IEnumerable<string> UncommonGiftLines()
        {
            if ( _inKindKinds != null )
            {
                var byKind = string.Join( ", ", _inKindKinds.Keys.Select( k => $"{_inKindTotals.GetValueOrDefault( k ).Count:N0} {k} (${_inKindTotals.GetValueOrDefault( k ).Total:N0})" ) );
                yield return $"  In-kind gifts: {_inKindTotals.Values.Sum( t => t.Count ):N0}: {byKind}";
            }

            if ( _foreignCurrencyCodeId.HasValue )
            {
                var code = _source.ForeignCurrency.CurrencyCode;
                yield return _foreignCurrencyGifts == 0
                    ? $"  {code} checks: none (no generated winter visitors)"
                    : $"  {code} checks: {_foreignCurrencyGifts:N0} ({code} {_foreignCurrencyTotal:N0}, ${_foreignCurrencyChurchTotal:N2} as credited) from {_foreignCurrencyHouseholds:N0} household{( _foreignCurrencyHouseholds == 1 ? "" : "s" )}";
            }
        }

        /// <summary>
        /// The Ids of a Rock defined type's values, by value.
        /// </summary>
        private Dictionary<string, int> DefinedValueIds( Guid definedTypeGuid )
        {
            return _db.Query( @"
SELECT [dv].[Id], [dv].[Value]
FROM [DefinedValue] AS [dv]
INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId]
WHERE [dt].[Guid] = @Guid", RockDatabase.P( "@Guid", definedTypeGuid ) ).ToDictionary( r => ( string ) r["Value"], r => ( int ) r["Id"] );
        }
    }
}
