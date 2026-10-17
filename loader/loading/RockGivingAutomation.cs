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

using System.Globalization;

namespace RockSampleData.Loader.Loading
{
    /*
        10/16/26 - CLAUDE

        A line-for-line port of the math in Rock's Giving Automation job, so the loader can fill Rock's
        giving attributes exactly as the job would. Keep it a faithful copy: same order of operations,
        same types (decimal versus double), same rounding, same string formats. Do not "improve" it.

        Rock sources this mirrors (paths in the SparkDevNetwork/Rock repository):
        - Rock/Jobs/GivingAutomation.cs (CalculateAttributesPerGivingId, the 24 hour window, the 5 gift minimum)
        - Rock/Jobs/GivingAutomation.cs (RecentTxnAlertBuilder and LateTxnAlertBuilder, for the alert history replay)
        - Rock/Financial/GivingAutomationHelper.cs (GetFrequencyStats, GetQuartileRanges, GetPercentScheduled,
          GetPreferredSourceGuid, GetPreferredCurrencyGuid, QuartileRanges, ComputeMetricsForAlertType,
          AllowFollowUpAlerts, AllowGratitudeAlerts, GetAmountIqrCount, GetFrequencyDeviationCount)
        - Rock.Migrations/.../spGivingAutomation_UpdateGivingJourneyStages.sql (JourneyStage, for the history replay)

        The version guard (see the builder README) fails when any of these change in Rock, so this port is
        checked and updated before a release.

        Reason: Giving analytics on a fresh install must match what Rock itself computes.
    */

    /// <summary>
    /// One qualifying gift, as Rock's job sees it.
    /// </summary>
    /// <remarks>
    /// <see cref="Details"/> holds the gift's amount per account (refunds applied, zero lines dropped), as Rock's
    /// FinancialTransactionView.GetTransactionDetails returns them, and <see cref="TotalAmount"/> is their sum.
    /// </remarks>
    internal sealed record RockGift( int Id, int PersonAliasId, string GivingId, int? CampusId, DateTime When, int? SourceTypeValueId, int? CurrencyTypeValueId, bool IsScheduled, List<(int AccountId, decimal Amount)> Details )
    {
        public decimal TotalAmount { get; } = Details.Sum( d => d.Amount );
    }

    /// <summary>
    /// The port of Rock's giving automation math.
    /// </summary>
    internal static class RockGivingAutomation
    {
        /// <summary>
        /// Gifts within this many hours of the last kept gift count as one giving event.
        /// </summary>
        public const int TransactionWindowDurationHours = 24;

        /// <summary>
        /// Classification needs at least this many gifts in 12 months (unless the first gift is over a year old).
        /// </summary>
        public const int MinimumTransactionCountForClassifications = 5;

        /// <summary>
        /// Rock's frequency labels (FinancialGivingAnalyticsFrequencyLabel).
        /// </summary>
        public enum FrequencyLabel
        {
            Weekly = 1,
            BiWeekly = 2,
            Monthly = 3,
            Quarterly = 4,
            Erratic = 5,
            Undetermined = 6
        }

        /// <summary>
        /// The result of <see cref="GetFrequencyStats"/>.
        /// </summary>
        public sealed record FrequencyStats( decimal MeanDays, decimal StdDevDays, FrequencyLabel Label, DateTime? NextExpectedGiftDate, DateTime? LastTransactionDateTime );

        /// <summary>
        /// Port of GivingAutomationHelper.GetFrequencyStats.
        /// </summary>
        public static FrequencyStats GetFrequencyStats( List<DateTime> orderedTransactionDateTimes, int transactionWindowDurationHours )
        {
            var dayGaps = new List<decimal>();
            DateTime? lastKeptDate = null;

            foreach ( var dt in orderedTransactionDateTimes )
            {
                if ( !lastKeptDate.HasValue )
                {
                    lastKeptDate = dt;
                    continue;
                }

                if ( ( dt - lastKeptDate.Value ).TotalHours < transactionWindowDurationHours )
                {
                    continue;
                }

                dayGaps.Add( ( decimal ) ( dt - lastKeptDate.Value ).TotalDays );
                lastKeptDate = dt;
            }

            decimal meanDays = dayGaps.Any() ? dayGaps.Average() : 0;
            double mean = ( double ) meanDays;

            double variance = dayGaps.Any() ? dayGaps.Average( d => Math.Pow( ( double ) d - mean, 2 ) ) : 0;

            decimal stdDevDays = ( decimal ) Math.Sqrt( variance );
            double stdDev = ( double ) stdDevDays;

            var nextExpectedGiftDate = lastKeptDate.HasValue ? lastKeptDate.Value.AddDays( mean ) : ( DateTime? ) null;

            var label = FrequencyLabel.Undetermined;

            if ( mean >= 4.5 && mean <= 8.5 && stdDev < 7 )
            {
                label = FrequencyLabel.Weekly;
            }
            else if ( mean >= 9 && mean <= 17 && stdDev < 10 )
            {
                label = FrequencyLabel.BiWeekly;
            }
            else if ( mean >= 25 && mean <= 35 && stdDev < 10 )
            {
                label = FrequencyLabel.Monthly;
            }
            else if ( mean >= 80 && mean <= 110 && stdDev < 15 )
            {
                label = FrequencyLabel.Quarterly;
            }
            else if ( mean / 2 < stdDev )
            {
                label = FrequencyLabel.Erratic;
            }

            return new FrequencyStats( meanDays, stdDevDays, label, nextExpectedGiftDate, lastKeptDate );
        }

        /// <summary>
        /// Port of GivingAutomationHelper.GetQuartileRanges plus QuartileRanges.MedianAmount and IQRAmount.
        /// </summary>
        public static (decimal Median, decimal Iqr) GetMedianAndIqr( IEnumerable<decimal> transactionAmounts )
        {
            var ranges = GetQuartileRanges( transactionAmounts );
            var q1 = Median( GetQuartileRanges( ranges.Q1 ).Q2 );
            var q3 = Median( GetQuartileRanges( ranges.Q3 ).Q2 );
            return (Median( ranges.Q2 ), q3 - q1);
        }

        private static decimal Median( List<decimal> q2MedianRange )
        {
            return q2MedianRange.Any() ? q2MedianRange.Average() : 0.00M;
        }

        private static (List<decimal> Q1, List<decimal> Q2, List<decimal> Q3) GetQuartileRanges( IEnumerable<decimal> transactionAmounts )
        {
            var orderedValues = transactionAmounts.OrderBy( a => a ).ToList();
            var count = orderedValues.Count;

            if ( count <= 2 )
            {
                return (new List<decimal>(), orderedValues, new List<decimal>());
            }

            var lastMidIndex = count / 2;
            var isSingleMidIndex = count % 2 != 0;
            var firstMidIndex = isSingleMidIndex ? lastMidIndex : lastMidIndex - 1;

            var medianValues = isSingleMidIndex ?
                orderedValues.GetRange( firstMidIndex, 1 ) :
                orderedValues.GetRange( firstMidIndex, 2 );

            var q1 = orderedValues.GetRange( 0, firstMidIndex );
            var q3 = orderedValues.GetRange( lastMidIndex + 1, count - lastMidIndex - 1 );

            return (q1, medianValues, q3);
        }

        /// <summary>
        /// Port of GivingAutomationHelper.GetPercentScheduled.
        /// </summary>
        public static int GetPercentScheduled( List<RockGift> transactions )
        {
            var txCount = transactions.Count;
            var scheduledCount = transactions.Count( t => t.IsScheduled );

            var asDecimal = ( decimal ) scheduledCount / txCount;
            return ( int ) decimal.Round( asDecimal * 100 );
        }

        /// <summary>
        /// Port of GetPreferredSourceGuid and GetPreferredCurrencyGuid: the most common value, ties going to the
        /// most recent gift. Returns the defined value Id; the caller turns it into the Guid Rock stores.
        /// </summary>
        public static int? GetPreferred( List<RockGift> transactions, Func<RockGift, int?> valueOf )
        {
            var groups = transactions
                .Where( t => valueOf( t ).HasValue )
                .GroupBy( valueOf )
                .OrderByDescending( g => g.Count() );

            var maxCount = groups.FirstOrDefault()?.Count() ?? 0;
            var preferred = groups
                .Where( g => g.Count() == maxCount )
                .SelectMany( g => g.ToList() )
                .OrderByDescending( t => t.When )
                .FirstOrDefault();

            return preferred == null ? null : valueOf( preferred );
        }

        /// <summary>
        /// Rock's journey stages (GivingJourneyStage).
        /// </summary>
        public enum JourneyStage
        {
            NonGiver = 0,
            New = 1,
            Consistent = 2,
            Occasional = 3,
            Lapsed = 4,
            Former = 5
        }

        /// <summary>
        /// Port of the stage rules in spGivingAutomation_UpdateGivingJourneyStages, with Rock's default settings.
        /// The gift dates must be every qualifying gift of the giving unit, in date order.
        /// </summary>
        public static JourneyStage GetJourneyStage( List<DateTime> orderedGiftDates, DateTime now )
        {
            // Only gifts up to now count. DATEDIFF( DAY, a, b ) counts midnights, so compare dates.
            var upTo = UpperBound( orderedGiftDates, now );
            if ( upTo == 0 )
            {
                return JourneyStage.NonGiver;
            }

            var firstGiftDate = orderedGiftDates[0];
            var lastGiftDate = orderedGiftDates[upTo - 1];
            var start24 = LowerBound( orderedGiftDates, now.AddMonths( -24 ) );
            var giftCount24Months = upTo - start24;

            decimal? meanFrequency = null;
            if ( giftCount24Months >= 2 )
            {
                var firstGiftDate24Months = orderedGiftDates[start24];
                var span = ( decimal ) Days( firstGiftDate24Months, lastGiftDate );
                var daysSinceLast = Days( lastGiftDate, now );
                meanFrequency = daysSinceLast > span / ( giftCount24Months - 1 )
                    ? ( decimal ) Days( firstGiftDate24Months, now ) / giftCount24Months
                    : span / ( giftCount24Months - 1 );
            }

            var sinceLast = Days( lastGiftDate, now );

            if ( Days( firstGiftDate, now ) <= 150 && giftCount24Months >= 1 && giftCount24Months <= 5 )
            {
                return JourneyStage.New;
            }

            if ( sinceLast <= 32 && meanFrequency <= 32 )
            {
                return JourneyStage.Consistent;
            }

            if ( sinceLast <= 150 && meanFrequency <= 94 )
            {
                return JourneyStage.Occasional;
            }

            if ( sinceLast > 150 && meanFrequency < 100 )
            {
                return JourneyStage.Lapsed;
            }

            return JourneyStage.Former;
        }

        #region Alerts

        /// <summary>
        /// Rock's alert kinds (AlertType).
        /// </summary>
        public enum AlertKind
        {
            Gratitude = 0,
            FollowUp = 1
        }

        /// <summary>
        /// The parts of a FinancialTransactionAlertType that the alert rules read. A null account set means every account.
        /// </summary>
        public sealed record AlertTypeRules( int Id, AlertKind Kind, bool ContinueIfMatched, int? RepeatPreventionDuration, decimal? FrequencySensitivityScale, decimal? AmountSensitivityScale,
            decimal? MinimumGiftAmount, decimal? MaximumGiftAmount, decimal? MinimumMedianGiftAmount, decimal? MaximumMedianGiftAmount, int? MaximumDaysSinceLastGift, int? CampusId, HashSet<int> AccountIds );

        /// <summary>
        /// One alert as Rock writes it (FinancialTransactionAlert).
        /// </summary>
        public sealed record Alert( int? TransactionId, int PersonAliasId, string GivingId, int AlertTypeId, AlertKind Kind, decimal? Amount, decimal? AmountCurrentMedian, decimal? AmountCurrentIqr,
            decimal? AmountIqrMultiplier, decimal? FrequencyCurrentMean, decimal? FrequencyCurrentStandardDeviation, decimal? FrequencyDifferenceFromMean, decimal? FrequencyZScore, string ReasonsKey, DateTime AlertDateTime );

        /// <summary>
        /// The result of <see cref="ComputeMetricsForAlertType"/>.
        /// </summary>
        public sealed record AlertMetrics( decimal Median, decimal Iqr, FrequencyStats FrequencyStats, RockGift MostRecentTransaction, int TransactionCount );

        /// <summary>
        /// Port of GivingAutomationHelper.ComputeMetricsForAlertType. Note that the amounts are the gifts' account
        /// lines, not the gift totals, exactly as in Rock.
        /// </summary>
        public static AlertMetrics ComputeMetricsForAlertType( AlertTypeRules alertType, List<RockGift> orderedTransactions )
        {
            var accountIds = alertType.AccountIds;
            var transactionsForAlertType = accountIds == null
                ? orderedTransactions
                : orderedTransactions.Where( t => t.Details.Any( d => accountIds.Contains( d.AccountId ) ) ).ToList();

            var amounts = accountIds == null
                ? transactionsForAlertType.SelectMany( t => t.Details ).Select( d => d.Amount ).ToList()
                : transactionsForAlertType.SelectMany( t => t.Details ).Where( d => accountIds.Contains( d.AccountId ) ).Select( d => d.Amount ).ToList();

            var (median, iqr) = GetMedianAndIqr( amounts );
            return new AlertMetrics( median, iqr, GetFrequencyStats( transactionsForAlertType.Select( t => t.When ).ToList(), TransactionWindowDurationHours ),
                transactionsForAlertType.LastOrDefault(), transactionsForAlertType.Count );
        }

        /// <summary>
        /// Port of AllowFollowUpAlerts and AllowGratitudeAlerts. <paramref name="kindRepeatPreventionDays"/> is the
        /// follow-up or gratitude setting, matching <paramref name="kind"/>.
        /// </summary>
        public static bool AllowAlerts( AlertKind kind, List<Alert> recentAlerts, DateTime now, int? globalRepeatPreventionDays, int? kindRepeatPreventionDays )
        {
            if ( recentAlerts == null || !recentAlerts.Any() )
            {
                return true;
            }

            if ( globalRepeatPreventionDays.HasValue && ( now - recentAlerts.Max( x => x.AlertDateTime ) ).TotalDays <= globalRepeatPreventionDays.Value )
            {
                return false;
            }

            var lastOfKind = recentAlerts.Where( a => a.Kind == kind ).Select( a => ( DateTime? ) a.AlertDateTime ).DefaultIfEmpty().Max();
            if ( kindRepeatPreventionDays.HasValue && lastOfKind.HasValue && ( now - lastOfKind.Value ).TotalDays <= kindRepeatPreventionDays.Value )
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Port of GivingAutomationHelper.GetAmountIqrCount.
        /// </summary>
        public static decimal GetAmountIqrCount( decimal medianGiftAmount, decimal amountIqr, decimal amount )
        {
            var infinity = 1000;
            var negativeInfinity = 0 - infinity;

            var amountDeviation = amount - medianGiftAmount;
            decimal numberOfAmountIqrs;

            if ( amountDeviation == 0 )
            {
                numberOfAmountIqrs = 0;
            }
            else if ( amountIqr != 0 )
            {
                numberOfAmountIqrs = amountDeviation / amountIqr;
            }
            else
            {
                amountIqr = 0.15m * medianGiftAmount;

                if ( amountIqr == 0 )
                {
                    amountIqr = 100m;
                }

                numberOfAmountIqrs = amountDeviation / amountIqr;
            }

            if ( numberOfAmountIqrs > infinity )
            {
                numberOfAmountIqrs = infinity;
            }
            else if ( numberOfAmountIqrs < negativeInfinity )
            {
                numberOfAmountIqrs = negativeInfinity;
            }

            return numberOfAmountIqrs;
        }

        /// <summary>
        /// Port of GivingAutomationHelper.GetFrequencyDeviationCount. Positive values mean earlier than usual.
        /// </summary>
        public static decimal GetFrequencyDeviationCount( decimal frequencyStdDev, decimal frequencyMean, decimal daysSinceLastTransaction )
        {
            const int FrequencyStdDevsInfinity = 1000;
            const int FrequencyStdDevsNegativeInfinity = 0 - FrequencyStdDevsInfinity;

            var frequencyDeviation = frequencyMean - daysSinceLastTransaction;

            decimal numberOfFrequencyStdDevs;

            if ( frequencyDeviation == 0 )
            {
                numberOfFrequencyStdDevs = 0;
            }
            else if ( frequencyStdDev >= 1 )
            {
                numberOfFrequencyStdDevs = frequencyDeviation / frequencyStdDev;
            }
            else
            {
                frequencyStdDev = 0.15M * frequencyMean;

                if ( frequencyStdDev < 3 )
                {
                    frequencyStdDev = 3;
                }

                numberOfFrequencyStdDevs = frequencyDeviation / frequencyStdDev;
            }

            if ( numberOfFrequencyStdDevs > FrequencyStdDevsInfinity )
            {
                numberOfFrequencyStdDevs = FrequencyStdDevsInfinity;
            }
            else if ( numberOfFrequencyStdDevs < FrequencyStdDevsNegativeInfinity )
            {
                numberOfFrequencyStdDevs = FrequencyStdDevsNegativeInfinity;
            }

            return numberOfFrequencyStdDevs;
        }

        /// <summary>
        /// Port of RecentTxnAlertBuilder.BuildAlert: the alert one alert type raises for one recent gift, or null.
        /// Leaves out the data view restriction, which the sample alert types do not use.
        /// </summary>
        public static Alert BuildRecentAlert( AlertTypeRules alertType, RockGift transaction, double daysSinceLastTransaction, AlertMetrics metrics, List<Alert> recentAlerts,
            bool allowFollowUp, bool allowGratitude, DateTime now )
        {
            if ( recentAlerts.Any( a => a.AlertTypeId == alertType.Id && a.TransactionId == transaction.Id ) )
            {
                return null;
            }

            if ( !allowFollowUp && alertType.Kind == AlertKind.FollowUp )
            {
                return null;
            }

            if ( !allowGratitude && alertType.Kind == AlertKind.Gratitude )
            {
                return null;
            }

            if ( alertType.MaximumDaysSinceLastGift.HasValue && daysSinceLastTransaction > alertType.MaximumDaysSinceLastGift.Value )
            {
                return null;
            }

            if ( alertType.RepeatPreventionDuration.HasValue && recentAlerts.Any( a => a.AlertTypeId == alertType.Id ) )
            {
                var lastAlertOfTypeDate = recentAlerts.Where( a => a.AlertTypeId == alertType.Id ).Max( a => a.AlertDateTime );
                if ( ( now - lastAlertOfTypeDate ).TotalDays <= alertType.RepeatPreventionDuration.Value )
                {
                    return null;
                }
            }

            if ( alertType.CampusId.HasValue && alertType.CampusId != transaction.CampusId )
            {
                return null;
            }

            decimal transactionAmount;
            if ( alertType.AccountIds != null )
            {
                if ( !transaction.Details.Any( d => alertType.AccountIds.Contains( d.AccountId ) ) )
                {
                    return null;
                }

                transactionAmount = transaction.Details.Where( d => alertType.AccountIds.Contains( d.AccountId ) ).Sum( d => d.Amount );
            }
            else
            {
                transactionAmount = transaction.TotalAmount;
            }

            if ( ( alertType.FrequencySensitivityScale.HasValue || alertType.AmountSensitivityScale.HasValue ) && metrics.TransactionCount < MinimumTransactionCountForClassifications )
            {
                return null;
            }

            if ( ( alertType.MinimumGiftAmount.HasValue && transactionAmount < alertType.MinimumGiftAmount.Value )
                || ( alertType.MaximumGiftAmount.HasValue && transactionAmount > alertType.MaximumGiftAmount.Value )
                || ( alertType.MinimumMedianGiftAmount.HasValue && metrics.Median < alertType.MinimumMedianGiftAmount.Value )
                || ( alertType.MaximumMedianGiftAmount.HasValue && metrics.Median > alertType.MaximumMedianGiftAmount.Value ) )
            {
                return null;
            }

            var numberOfAmountIqrs = GetAmountIqrCount( metrics.Median, metrics.Iqr, transactionAmount );
            var meanFrequencyDays = metrics.FrequencyStats.MeanDays;
            var frequencyStdDevDays = metrics.FrequencyStats.StdDevDays;
            var numberOfFrequencyStdDevs = GetFrequencyDeviationCount( frequencyStdDevDays, meanFrequencyDays, Convert.ToDecimal( daysSinceLastTransaction ) );

            var reasons = new List<string>();
            if ( alertType.Kind == AlertKind.Gratitude )
            {
                if ( alertType.AmountSensitivityScale.HasValue && numberOfAmountIqrs >= alertType.AmountSensitivityScale.Value )
                {
                    reasons.Add( "AmountSensitivityScale" );
                }

                if ( alertType.FrequencySensitivityScale.HasValue && numberOfFrequencyStdDevs >= alertType.FrequencySensitivityScale.Value )
                {
                    reasons.Add( "FrequencySensitivityScale" );
                }
            }
            else
            {
                if ( alertType.AmountSensitivityScale.HasValue && numberOfAmountIqrs <= ( alertType.AmountSensitivityScale.Value * -1 ) )
                {
                    reasons.Add( "AmountSensitivityScale" );
                }

                if ( alertType.FrequencySensitivityScale.HasValue && numberOfFrequencyStdDevs <= ( alertType.FrequencySensitivityScale.Value * -1 ) )
                {
                    reasons.Add( "FrequencySensitivityScale" );
                }
            }

            if ( !reasons.Any() )
            {
                if ( alertType.AmountSensitivityScale.HasValue || alertType.FrequencySensitivityScale.HasValue )
                {
                    return null;
                }

                // The "Large Gift" case: no sensitivity rules, just a minimum amount.
                if ( alertType.MinimumGiftAmount.HasValue && transactionAmount >= alertType.MinimumGiftAmount.Value )
                {
                    reasons.Add( "MinimumGiftAmount" );
                }
                else
                {
                    return null;
                }
            }

            return new Alert( transaction.Id, transaction.PersonAliasId, transaction.GivingId, alertType.Id, alertType.Kind, transactionAmount, metrics.Median, metrics.Iqr,
                numberOfAmountIqrs, meanFrequencyDays, frequencyStdDevDays, meanFrequencyDays - Convert.ToDecimal( daysSinceLastTransaction ), numberOfFrequencyStdDevs,
                ReasonsJson( reasons ), now );
        }

        /// <summary>
        /// Port of LateTxnAlertBuilder.BuildAlert: the late-gift alert for one giving unit, or null.
        /// <paramref name="recentAlertsOfThisType"/> must be newest first.
        /// </summary>
        public static Alert BuildLateAlert( AlertTypeRules alertType, AlertMetrics metrics, List<Alert> recentAlertsOfThisType, DateTime now )
        {
            if ( metrics.TransactionCount == 0 || metrics.TransactionCount < MinimumTransactionCountForClassifications )
            {
                return null;
            }

            var mostRecentTransaction = metrics.MostRecentTransaction;
            var frequencyStats = metrics.FrequencyStats;
            if ( mostRecentTransaction == null || !frequencyStats.NextExpectedGiftDate.HasValue || !frequencyStats.LastTransactionDateTime.HasValue )
            {
                return null;
            }

            // Late alerts come at most once since the giver last gave.
            var mostRecentAlertOfThisTypeDateTime = recentAlertsOfThisType?.FirstOrDefault()?.AlertDateTime;
            if ( mostRecentAlertOfThisTypeDateTime.HasValue )
            {
                if ( mostRecentAlertOfThisTypeDateTime.Value > frequencyStats.LastTransactionDateTime.Value )
                {
                    return null;
                }

                if ( alertType.RepeatPreventionDuration.HasValue && ( now - mostRecentAlertOfThisTypeDateTime.Value ).TotalDays <= alertType.RepeatPreventionDuration.Value )
                {
                    return null;
                }
            }

            var daysSinceLastTransaction = ( now - frequencyStats.LastTransactionDateTime.Value ).TotalDays;
            if ( alertType.MaximumDaysSinceLastGift.HasValue && daysSinceLastTransaction > alertType.MaximumDaysSinceLastGift.Value )
            {
                return null;
            }

            if ( ( alertType.CampusId.HasValue && alertType.CampusId != mostRecentTransaction.CampusId )
                || ( alertType.MinimumMedianGiftAmount.HasValue && metrics.Median < alertType.MinimumMedianGiftAmount.Value )
                || ( alertType.MaximumMedianGiftAmount.HasValue && metrics.Median > alertType.MaximumMedianGiftAmount.Value ) )
            {
                return null;
            }

            var numberOfFrequencyStdDevs = GetFrequencyDeviationCount( frequencyStats.StdDevDays, frequencyStats.MeanDays, ( decimal ) daysSinceLastTransaction );
            if ( numberOfFrequencyStdDevs > ( alertType.FrequencySensitivityScale.Value * -1 ) )
            {
                return null;
            }

            return new Alert( null, mostRecentTransaction.PersonAliasId, mostRecentTransaction.GivingId, alertType.Id, alertType.Kind, null, metrics.Median, metrics.Iqr,
                null, frequencyStats.MeanDays, frequencyStats.StdDevDays, null, null, ReasonsJson( new List<string> { "FrequencySensitivityScale" } ), now );
        }

        /// <summary>
        /// Rock's ReasonsKey: the reason names as a JSON array (reasons.ToJson()).
        /// </summary>
        private static string ReasonsJson( List<string> reasons )
        {
            return "[" + string.Join( ",", reasons.Select( r => $"\"{r}\"" ) ) + "]";
        }

        #endregion Alerts

        /// <summary>
        /// Rock's text for a date attribute value (DateTime.ToString( "o" )).
        /// </summary>
        public static string DateValue( DateTime value )
        {
            return value.ToString( "o", CultureInfo.InvariantCulture );
        }

        /// <summary>
        /// Rock's text for a number attribute value. Rock calls ToString() on an en-US server.
        /// </summary>
        public static string NumberValue( decimal value )
        {
            return value.ToString( CultureInfo.InvariantCulture );
        }

        /// <summary>
        /// SQL Server's CONVERT( VARCHAR(30), @date, 126 ), which the journey procedure uses for the change date.
        /// </summary>
        public static string SqlIsoValue( DateTime value )
        {
            return value.Millisecond == 0
                ? value.ToString( "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture )
                : value.ToString( "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture );
        }

        private static int Days( DateTime from, DateTime to )
        {
            return ( to.Date - from.Date ).Days;
        }

        /// <summary>
        /// The number of dates on or before the value.
        /// </summary>
        private static int UpperBound( List<DateTime> ordered, DateTime value )
        {
            int low = 0, high = ordered.Count;
            while ( low < high )
            {
                var mid = ( low + high ) / 2;
                if ( ordered[mid] <= value )
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

        /// <summary>
        /// The index of the first date on or after the value.
        /// </summary>
        private static int LowerBound( List<DateTime> ordered, DateTime value )
        {
            int low = 0, high = ordered.Count;
            while ( low < high )
            {
                var mid = ( low + high ) / 2;
                if ( ordered[mid] < value )
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
    }
}
