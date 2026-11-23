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

using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Rock's three Tithing Overview metrics. Each run reads the year of gifts up to the run's day, by family, and compares
    /// each family's total with a tenth of the census median family income for the family's postal code.
    /// </summary>
    internal sealed partial class RockMetricsLoader
    {
        private enum TithingCount
        {
            /// <summary>
            /// The percent of families who gave more than the tithe.
            /// </summary>
            Percent,

            /// <summary>
            /// The families who gave the tithe or more.
            /// </summary>
            TithingHouseholds,

            /// <summary>
            /// The families whose gifts add up to zero or more.
            /// </summary>
            GivingHouseholds
        }

        /// <summary>
        /// Each family's gifts by day, read once for all three metrics.
        /// </summary>
        private List<(int? Campus, int Family, DateTime Day, bool IsLate, decimal Amount)> _familyGifts;

        /// <summary>
        /// A tenth of each family's census median income, for families with a mapped postal code that has one.
        /// </summary>
        private Dictionary<int, decimal> _familyTithes;

        /// <summary>
        /// Rock's Tithing Overview metrics, by the campus of the person who gave. Rock's SQL counts only families with a
        /// postal code that has a median income above zero.
        /// </summary>
        private List<MetricValueRow> Tithing( RockMetric metric, TithingCount count )
        {
            if ( metric.Runs.Count == 0 )
            {
                return new List<MetricValueRow>();
            }

            ReadFamilyGifts( metric );
            var random = new SeededRandom( $"{_seed}:rock-metric-runs:{metric.Key}" );
            var values = new List<MetricValueRow>();
            foreach ( var run in metric.Runs )
            {
                var saved = SavedAfter( run, random );
                var first = run.Date.AddDays( -365 );
                var families = _familyGifts
                    .Where( g => g.Day >= first && g.Day <= run.Date && !( g.Day == run.Date && g.IsLate ) && _familyTithes.ContainsKey( g.Family ) )
                    .GroupBy( g => (g.Campus, g.Family) )
                    .Select( g => (g.Key.Campus, Giving: g.Sum( x => x.Amount ), Tithe: _familyTithes[g.Key.Family]) );
                foreach ( var campus in families.GroupBy( f => f.Campus ) )
                {
                    var value = count switch
                    {
                        TithingCount.Percent => Stored( Convert.ToDecimal( ( double ) campus.Count( f => f.Giving > f.Tithe ) / campus.Count() * 100 ) ),
                        TithingCount.TithingHouseholds => campus.Count( f => f.Giving >= f.Tithe ),
                        _ => campus.Count( f => f.Giving >= 0 )
                    };
                    values.Add( new MetricValueRow( run, saved, value, new[] { campus.Key } ) );
                }
            }

            return values;
        }

        /// <summary>
        /// Reads every family's tax-deductible gifts by day, from a year before the first run, and each family's tithe.
        /// The three metrics share one schedule, so the first one read serves all three.
        /// </summary>
        private void ReadFamilyGifts( RockMetric metric )
        {
            if ( _familyGifts != null )
            {
                return;
            }

            _familyGifts = _db.Query( @"
SELECT [p].[PrimaryCampusId], [p].[PrimaryFamilyId], [ft].[TransactionDateKey] AS [DateKey],
    CAST( CASE WHEN CAST( [ft].[TransactionDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END AS bit ) AS [IsLate],
    SUM( [ftd].[Amount] ) AS [Amount]
FROM [Person] AS [p]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [p].[Id]
INNER JOIN [FinancialTransaction] AS [ft] ON [ft].[AuthorizedPersonAliasId] = [pa].[Id]
INNER JOIN [FinancialTransactionDetail] AS [ftd] ON [ftd].[TransactionId] = [ft].[Id]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [ftd].[AccountId]
INNER JOIN [AnalyticsSourceDate] AS [asd] ON [asd].[DateKey] = [ft].[TransactionDateKey]
WHERE [fa].[IsTaxDeductible] = 1
    AND [p].[RecordTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @PersonRecordType )
    AND [p].[PrimaryFamilyId] IS NOT NULL
    AND [ft].[TransactionDateKey] BETWEEN @FromKey AND @ToKey
GROUP BY [p].[PrimaryCampusId], [p].[PrimaryFamilyId], [ft].[TransactionDateKey], CASE WHEN CAST( [ft].[TransactionDateTime] AS time ) > @RunTime THEN 1 ELSE 0 END",
                P( "@RunTime", metric.Runs[0].TimeOfDay ), P( "@PersonRecordType", PersonRecordTypeGuid ),
                P( "@FromKey", DateKey( metric.Runs[0].Date.AddDays( -365 ) ) ), P( "@ToKey", DateKey( metric.Runs[metric.Runs.Count - 1].Date ) ) )
                .Select( r => (r["PrimaryCampusId"] as int?, ( int ) r["PrimaryFamilyId"], DateTime.ParseExact( r["DateKey"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture ),
                    ( bool ) r["IsLate"], ( decimal ) r["Amount"]) )
                .ToList();

            // Rock's SQL takes the first mapped location's postal code, and a tenth of its census median family income.
            _familyTithes = _db.Query( @"
SELECT [g].[Id], ( SELECT [FamiliesMedianIncome] * .1 FROM [AnalyticsSourcePostalCode] WHERE [PostalCode] = [z].[PostalCode] ) AS [Tithe]
FROM [Group] AS [g]
CROSS APPLY (
    SELECT TOP 1 LEFT( [l].[PostalCode], 5 ) AS [PostalCode]
    FROM [Location] AS [l]
    INNER JOIN [GroupLocation] AS [gl] ON [gl].[LocationId] = [l].[Id] AND [gl].[GroupId] = [g].[Id] AND [gl].[IsMappedLocation] = 1 ) AS [z]
WHERE [g].[Id] IN ( SELECT [PrimaryFamilyId] FROM [Person] ) AND [z].[PostalCode] IS NOT NULL AND [z].[PostalCode] <> ''" )
                .Where( r => r["Tithe"] != null && Convert.ToDecimal( r["Tithe"] ) > 0 )
                .ToDictionary( r => ( int ) r["Id"], r => Convert.ToDecimal( r["Tithe"] ) );
        }
    }
}
