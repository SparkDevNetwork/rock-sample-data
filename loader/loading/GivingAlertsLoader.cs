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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;
using static RockSampleData.Loader.Loading.RockGivingAutomation;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Creates the sample church's giving alert types and writes the alerts Rock's Giving Automation job would
    /// have raised over the last 12 months, replaying the job night by night.
    /// </summary>
    /// <remarks>
    /// Each night at 10pm Rock checks every gift since its last run against each alert type (large gift,
    /// smaller than usual, a gift later than usual), using the giving unit's last 12 months as of that night.
    /// Then it checks every giving unit that gave in the last year for a gift that has not come (late gift).
    /// The rules are ported in <see cref="RockGivingAutomation"/>.
    /// </remarks>
    internal sealed class GivingAlertsLoader
    {
        /// <summary>
        /// How far back the alert types (and so the alerts) go.
        /// </summary>
        private const int HistoryMonths = 12;

        /*
            10/16/26 - CLAUDE

            The replay starts a month before the history it keeps. On the first night every giver who had
            already stopped would get their one late-gift alert at once, a burst a church only sees the day it
            sets up alerts. The warm-up month absorbs that burst: its alerts count for Rock's once-per-lapse and
            repeat rules but are not written.

            Reason: The kept 12 months look like steady, ordinary alert traffic.
        */
        private const int WarmUpMonths = 1;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;
        private readonly DateTime _runAt;
        private readonly Dictionary<string, List<RockGift>> _gifts;

        public GivingAlertsLoader( RockDatabase db, SampleDataSource source, string foreignKey, DateTime runAt, Dictionary<string, List<RockGift>> gifts )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
            _runAt = runAt;
            _gifts = gifts;
        }

        public void Load()
        {
            var firstNight = _runAt.AddMonths( -HistoryMonths );
            var alertTypes = CreateAlertTypes( firstNight );
            var alerts = Replay( alertTypes, firstNight );

            var rows = TableFactory.Create( "TransactionId:int", "PersonAliasId:int", "GivingId:string", "AlertTypeId:int", "Amount:decimal", "AmountCurrentMedian:decimal", "AmountCurrentIqr:decimal",
                "AmountIqrMultiplier:decimal", "FrequencyCurrentMean:decimal", "FrequencyCurrentStandardDeviation:decimal", "FrequencyDifferenceFromMean:decimal", "FrequencyZScore:decimal",
                "ReasonsKey:string", "AlertDateTime:date", "AlertDateKey:int", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );
            foreach ( var a in alerts )
            {
                rows.Rows.Add( a.TransactionId ?? ( object ) DBNull.Value, a.PersonAliasId, a.GivingId, a.AlertTypeId,
                    Scaled( a.Amount, 2 ), Scaled( a.AmountCurrentMedian, 2 ), Scaled( a.AmountCurrentIqr, 2 ),
                    Scaled( a.AmountIqrMultiplier, 1 ), Scaled( a.FrequencyCurrentMean, 1 ), Scaled( a.FrequencyCurrentStandardDeviation, 1 ),
                    Scaled( a.FrequencyDifferenceFromMean, 1 ), Scaled( a.FrequencyZScore, 1 ),
                    a.ReasonsKey, a.AlertDateTime, int.Parse( a.AlertDateTime.ToString( "yyyyMMdd" ) ), a.AlertDateTime, a.AlertDateTime, Guid.NewGuid(), _foreignKey );
            }

            _db.BulkInsert( "FinancialTransactionAlert", rows, keepIdentity: false );

            var counts = alertTypes.Select( t => $"{t.Name} {alerts.Count( a => a.AlertTypeId == t.Rules.Id ):N0}" );
            Console.WriteLine( $"  Giving alerts: {alerts.Count:N0} over {HistoryMonths} months ({string.Join( ", ", counts )})" );
        }

        /// <summary>
        /// Rock stores alert numbers in decimal(18,2) and decimal(6,1) columns, and Rock's data layer cuts extra
        /// digits off (it does not round), so the loader does the same.
        /// </summary>
        private static object Scaled( decimal? value, int digits )
        {
            if ( !value.HasValue )
            {
                return DBNull.Value;
            }

            var factor = digits == 1 ? 10m : 100m;
            return decimal.Truncate( value.Value * factor ) / factor;
        }

        /// <summary>
        /// Writes the alert types from configuration/giving-alert-types.json, created as of the first night.
        /// </summary>
        private List<(string Name, AlertTypeRules Rules, bool IsLate)> CreateAlertTypes( DateTime createdAt )
        {
            var created = new List<(string, AlertTypeRules, bool)>();
            foreach ( var definition in _source.GivingAlertTypes.AlertTypes.OrderBy( t => t.Order ) )
            {
                var kind = definition.AlertType == "gratitude" ? AlertKind.Gratitude : AlertKind.FollowUp;
                var id = ( int ) _db.Scalar( @"
INSERT INTO [FinancialTransactionAlertType] ( [Name], [AlertType], [ContinueIfMatched], [RepeatPreventionDuration], [FrequencySensitivityScale], [AmountSensitivityScale],
    [MinimumGiftAmount], [MaximumGiftAmount], [MinimumMedianGiftAmount], [MaximumMedianGiftAmount], [MaximumDaysSinceLastGift], [SendBusEvent], [Order], [IncludeChildFinancialAccounts],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @AlertType, @ContinueIfMatched, @RepeatPreventionDuration, @FrequencySensitivityScale, @AmountSensitivityScale,
    @MinimumGiftAmount, @MaximumGiftAmount, @MinimumMedianGiftAmount, @MaximumMedianGiftAmount, @MaximumDaysSinceLastGift, 0, @Order, 0,
    @Guid, @CreatedDateTime, @CreatedDateTime, @ForeignKey )",
                    P( "@Name", definition.Name ), P( "@AlertType", ( int ) kind ), P( "@ContinueIfMatched", definition.ContinueIfMatched ), P( "@RepeatPreventionDuration", definition.RepeatPreventionDuration ),
                    P( "@FrequencySensitivityScale", definition.FrequencySensitivityScale ), P( "@AmountSensitivityScale", definition.AmountSensitivityScale ),
                    P( "@MinimumGiftAmount", definition.MinimumGiftAmount ), P( "@MaximumGiftAmount", definition.MaximumGiftAmount ),
                    P( "@MinimumMedianGiftAmount", definition.MinimumMedianGiftAmount ), P( "@MaximumMedianGiftAmount", definition.MaximumMedianGiftAmount ),
                    P( "@MaximumDaysSinceLastGift", definition.MaximumDaysSinceLastGift ), P( "@Order", definition.Order ), P( "@Guid", definition.Guid ),
                    P( "@CreatedDateTime", createdAt ), P( "@ForeignKey", _foreignKey ) );

                var rules = new AlertTypeRules( id, kind, definition.ContinueIfMatched, definition.RepeatPreventionDuration, definition.FrequencySensitivityScale, definition.AmountSensitivityScale,
                    definition.MinimumGiftAmount, definition.MaximumGiftAmount, definition.MinimumMedianGiftAmount, definition.MaximumMedianGiftAmount, definition.MaximumDaysSinceLastGift, null, null );
                created.Add( (definition.Name, rules, kind == AlertKind.FollowUp && definition.FrequencySensitivityScale.HasValue) );
            }

            return created;
        }

        /// <summary>
        /// Replays Rock's nightly run from the first night through the job's last run, and returns every alert raised.
        /// </summary>
        private List<Alert> Replay( List<(string Name, AlertTypeRules Rules, bool IsLate)> alertTypes, DateTime firstNight )
        {
            var allAlerts = new List<Alert>();
            var alertsByUnit = _gifts.Keys.ToDictionary( k => k, _ => new List<Alert>() );
            var metricsCache = new Dictionary<(string Unit, int AlertTypeId), (int Lo, int Hi, AlertMetrics Metrics)>();

            // The night each gift is first seen: Rock's 10pm run on or after it.
            var unitsByNight = _gifts
                .SelectMany( u => u.Value.Select( g => (Unit: u.Key, Night: g.When <= g.When.Date.AddHours( 22 ) ? g.When.Date.AddHours( 22 ) : g.When.Date.AddDays( 1 ).AddHours( 22 )) ) )
                .GroupBy( x => x.Night )
                .ToDictionary( g => g.Key, g => g.Select( x => x.Unit ).Distinct().ToList() );

            AlertMetrics Metrics( string unit, AlertTypeRules alertType, List<RockGift> unitGifts, int lo, int hi )
            {
                if ( metricsCache.TryGetValue( (unit, alertType.Id), out var cached ) && cached.Lo == lo && cached.Hi == hi )
                {
                    return cached.Metrics;
                }

                var metrics = ComputeMetricsForAlertType( alertType, unitGifts.GetRange( lo, hi - lo ) );
                metricsCache[(unit, alertType.Id)] = (lo, hi, metrics);
                return metrics;
            }

            void Raise( Alert alert )
            {
                if ( alert.AlertDateTime >= firstNight )
                {
                    allAlerts.Add( alert );
                }

                alertsByUnit[alert.GivingId].Add( alert );
            }

            for ( var now = firstNight.AddMonths( -WarmUpMonths ).Date.AddHours( 22 ); now <= _runAt; now = now.AddDays( 1 ) )
            {
                var oneYearAgo = now.AddMonths( -12 );
                var oneWeekAgo = now.AddDays( -7 );

                // Recent gifts: units with a gift since last night (ProcessRecentTxnAlertsPerGivingId).
                foreach ( var unit in unitsByNight.GetValueOrDefault( now ) ?? new List<string>() )
                {
                    var unitGifts = _gifts[unit];
                    var (lo, hi) = Window( unitGifts, oneYearAgo, now );
                    var recentAlerts = alertsByUnit[unit].Where( a => a.AlertDateTime > oneYearAgo ).ToList();
                    var window = unitGifts.GetRange( lo, hi - lo );
                    var allowFollowUp = AllowAlerts( AlertKind.FollowUp, recentAlerts, now, null, null );
                    var allowGratitude = AllowAlerts( AlertKind.Gratitude, recentAlerts, now, null, null );

                    for ( var i = 0; i < window.Count && ( allowFollowUp || allowGratitude ); i++ )
                    {
                        var transaction = window[i];
                        if ( transaction.When < oneWeekAgo )
                        {
                            continue;
                        }

                        var daysSinceLastTransaction = ( transaction.When - ( i > 0 ? window[i - 1].When : transaction.When ) ).TotalDays;
                        var raised = false;
                        foreach ( var (_, alertType, _) in alertTypes )
                        {
                            var alert = BuildRecentAlert( alertType, transaction, daysSinceLastTransaction, Metrics( unit, alertType, unitGifts, lo, hi ), recentAlerts, allowFollowUp, allowGratitude, now );
                            if ( alert == null )
                            {
                                continue;
                            }

                            Raise( alert );
                            recentAlerts.Add( alert );
                            raised = true;
                            if ( !alertType.ContinueIfMatched )
                            {
                                break;
                            }
                        }

                        if ( raised )
                        {
                            allowFollowUp = AllowAlerts( AlertKind.FollowUp, recentAlerts, now, null, null );
                            allowGratitude = AllowAlerts( AlertKind.Gratitude, recentAlerts, now, null, null );
                        }
                    }
                }

                // Late gifts: every unit that gave in the last year (ProcessLateTxnAlerts).
                var excluded = new HashSet<string>();
                foreach ( var (_, alertType, _) in alertTypes.Where( t => t.IsLate ) )
                {
                    foreach ( var (unit, unitGifts) in _gifts )
                    {
                        var (lo, hi) = Window( unitGifts, oneYearAgo, now );
                        if ( hi == lo || excluded.Contains( unit ) )
                        {
                            continue;
                        }

                        var recentOfType = alertsByUnit[unit].Where( a => a.AlertTypeId == alertType.Id && a.AlertDateTime > oneYearAgo ).OrderByDescending( a => a.AlertDateTime ).ToList();
                        var alert = BuildLateAlert( alertType, Metrics( unit, alertType, unitGifts, lo, hi ), recentOfType, now );
                        if ( alert == null )
                        {
                            continue;
                        }

                        Raise( alert );
                        if ( !alertType.ContinueIfMatched )
                        {
                            excluded.Add( unit );
                        }
                    }
                }
            }

            return allAlerts;
        }

        /// <summary>
        /// The range of a unit's gifts (in date order) from <paramref name="from"/> up to and including <paramref name="to"/>.
        /// </summary>
        private static (int Lo, int Hi) Window( List<RockGift> ordered, DateTime from, DateTime to )
        {
            int Bound( Func<RockGift, bool> isBefore )
            {
                int low = 0, high = ordered.Count;
                while ( low < high )
                {
                    var mid = ( low + high ) / 2;
                    if ( isBefore( ordered[mid] ) )
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid;
                    }
                }

                return low;
            }

            return (Bound( g => g.When < from ), Bound( g => g.When <= to ));
        }
    }
}
