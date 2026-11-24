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

using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Rock's 26 Insights metrics count active people by campus, by age range, gender, connection status, marital
        status, race, ethnicity, record status, and how complete each record is, on the 1st of each month. They read only
        the Person table and two tables beside it, so the loader runs Rock's own SQL for each run, with the people as they
        were then: inside a savepoint it hides the people who had not come yet (decision 347's rule, or born later) by
        marking them deceased, which every Insights query leaves out, and sets each person's age and age bracket to that
        day's. Then it rolls back to the savepoint. Every other field is today's, since Rock keeps no history of it.

        Reason: Rock's own SQL gives each value, with all its rounding and grouping, and only who was here and how old
        they were come from the loader.
    */

    /// <summary>
    /// Rock's Insights metrics, each run of Rock's own SQL as of that run.
    /// </summary>
    internal sealed partial class RockMetricsLoader
    {
        private const string AsOfSavepoint = "RockMetricsAsOf";

        private static readonly Guid SqlSourceGuid = new Guid( "6A1E1A1B-A636-4E12-B90C-D7FD1BDAE764" );

        /// <summary>
        /// The clock in Rock's metric SQL: Rock's own RockGetDate function, or SQL Server's GETDATE.
        /// </summary>
        private static readonly Regex Clock = new Regex( @"(?:\[?dbo\]?\s*\.\s*)?\[?RockGetDate\]?\s*\(\s*\)|\bGETDATE\s*\(\s*\)", RegexOptions.IgnoreCase );

        /// <summary>
        /// Works out every Insights metric's values. Returns the metrics.
        /// </summary>
        private List<RockMetric> LoadInsights()
        {
            var months = _configuration.Insights.Months;
            var metrics = _db.Query( $@"
SELECT DISTINCT [m].[Guid]
FROM [Metric] AS [m]
INNER JOIN [MetricCategory] AS [mc] ON [mc].[MetricId] = [m].[Id]
INNER JOIN [Category] AS [c] ON [c].[Id] = [mc].[CategoryId]
WHERE [m].[SourceValueTypeId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @SqlSource ) AND [m].[ScheduleId] IS NOT NULL
    AND [c].[Guid] IN ( {string.Join( ", ", _configuration.Insights.Categories.Select( ( _, i ) => $"@Category{i}" ) )} )",
                _configuration.Insights.Categories.Select( ( g, i ) => P( $"@Category{i}", g ) ).Append( P( "@SqlSource", SqlSourceGuid ) ).ToArray() )
                .Select( r => ( Guid ) r["Guid"] )
                .OrderBy( g => g )
                .Select( g => Read( g, $"insights-{g.ToString().ToLowerInvariant()}", schedule => schedule.Runs( _installed.AddMonths( -months ), _installed ) ) )
                .Where( m => m != null && !string.IsNullOrWhiteSpace( m.SourceSql ) )
                .ToList();

            _db.Execute( "CREATE TABLE #RockMetricsFamily ( [GroupId] INT NOT NULL PRIMARY KEY, [At] DATETIME NULL );" );
            _db.Execute( "INSERT INTO #RockMetricsFamily ( [GroupId], [At] )" + FamilyArrivals.Sql, FamilyArrivals.Parameters() );

            foreach ( var run in metrics.SelectMany( m => m.Runs ).Distinct().OrderBy( r => r ) )
            {
                _db.Save( AsOfSavepoint );
                SetPeopleAsOf( _db, run, "#RockMetricsFamily" );
                foreach ( var metric in metrics.Where( m => m.Runs.Contains( run ) ) )
                {
                    var random = new SeededRandom( $"{_seed}:rock-metric-runs:{metric.Key}:{run:yyyyMMddHHmm}" );
                    var saved = SavedAfter( run, random );
                    metric.Values.AddRange( RunRockSql( _db, metric.SourceSql, run ).Select( r => new MetricValueRow( run, saved, Stored( r.Value ), r.Entities ) ) );
                }

                _db.RollbackTo( AsOfSavepoint );
            }

            _db.Execute( "DROP TABLE #RockMetricsFamily;" );
            return metrics;
        }

        /// <summary>
        /// Sets the Person table as it was on a run's day: people whose family had not come yet, or who were not born yet,
        /// are marked deceased, and everyone's age and age bracket are that day's. Call it inside a savepoint.
        /// <paramref name="families"/> is a table of family arrivals, GroupId and At, from <see cref="FamilyArrivals.Sql"/>.
        /// </summary>
        internal static void SetPeopleAsOf( RockDatabase db, DateTime run, string families )
        {
            db.Execute( $@"
UPDATE [p] SET [IsDeceased] = 1
FROM [Person] AS [p]
LEFT JOIN {families} AS [f] ON [f].[GroupId] = [p].[PrimaryFamilyId]
WHERE [p].[IsDeceased] = 0 AND ( CAST( [f].[At] AS date ) > @Day OR [p].[BirthDate] > @Day );
UPDATE [p] SET [Age] = [x].[Age], [AgeBracket] = CASE WHEN [x].[Age] < 6 THEN 1 WHEN [x].[Age] < 13 THEN 2 WHEN [x].[Age] < 18 THEN 3 WHEN [x].[Age] < 25 THEN 4
    WHEN [x].[Age] < 35 THEN 5 WHEN [x].[Age] < 45 THEN 6 WHEN [x].[Age] < 55 THEN 7 WHEN [x].[Age] < 65 THEN 8 ELSE 9 END
FROM [Person] AS [p]
CROSS APPLY ( SELECT DATEDIFF( year, [p].[BirthDate], @Day ) - CASE WHEN DATEADD( year, DATEDIFF( year, [p].[BirthDate], @Day ), [p].[BirthDate] ) > @Day THEN 1 ELSE 0 END AS [Age] ) AS [x]
WHERE [p].[BirthDate] IS NOT NULL AND [p].[BirthDate] <= @Day;", P( "@Day", run.Date ) );
        }

        /// <summary>
        /// Runs a metric's own SQL as Rock's job reads it, with Rock's clock set to the run: the first column is the value,
        /// zero when null, and each later column is the entity of the next partition.
        /// </summary>
        internal static List<(decimal Value, int?[] Entities)> RunRockSql( RockDatabase db, string sql, DateTime run )
        {
            var rows = new List<(decimal, int?[])>();
            using var command = db.CreateCommand( Clock.Replace( sql, "@RockSampleDataRunTime" ), new SqlParameter( "@RockSampleDataRunTime", run ) );
            using var reader = command.ExecuteReader();
            while ( reader.Read() )
            {
                var value = reader.IsDBNull( 0 ) ? 0m : Convert.ToDecimal( reader.GetValue( 0 ) );
                var entities = Enumerable.Range( 1, reader.FieldCount - 1 ).Select( i => reader.GetValue( i ) as int? ).ToArray();
                rows.Add( (value, entities) );
            }

            return rows;
        }
    }
}
