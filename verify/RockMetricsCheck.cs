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

using Microsoft.Data.SqlClient;

using RockSampleData.Loader;
using RockSampleData.Loader.Database;
using RockSampleData.Loader.Loading;

namespace RockSampleData.Verify
{
    /*
        11/23/26 - CLAUDE

        The loader works out the history of Rock's own metrics itself (issue 55). The proof is Rock's own metric SQL. For
        each metric's newest run, the check sets the loaded database back to that moment inside a transaction it rolls
        back: rows dated after the run are hidden from Rock's queries, and the people are as they were then, by the
        loader's one rule for who was here (decision 347). Then it runs each metric's SQL from Rock's Metric table with
        Rock's clock set to the run, and every value must match the load. It works on any day, unlike a check that waits
        for a metric's run day. Then Rock's Calculate Metrics job runs for real, and it may add only runs after the load.

        Reason: Each value is what Rock's own SQL gives at that run, and Rock's first run after install fills in no past.
    */

    /// <summary>
    /// Checks the history of Rock's own metrics against Rock's own SQL and Rock's own Calculate Metrics job.
    /// </summary>
    internal static class RockMetricsCheck
    {
        private const string SqlSourceGuid = "6A1E1A1B-A636-4E12-B90C-D7FD1BDAE764";
        private const string WorshipAreaGuid = "235BAE2B-5760-4763-AADF-3938F34BA100";

        /// <summary>
        /// Each SQL metric's newest values equal Rock's own SQL run as of that moment, and each typed metric's adult
        /// counts equal the people in the service.
        /// </summary>
        public static CheckResult Values( SqlServer sql, string database, SampleDataSource source )
        {
            var configuration = source.ReadRockMetrics();
            if ( configuration == null )
            {
                return CheckResult.From( "rock-metrics", "This package has no history for Rock's metrics", new List<string>() );
            }

            var problems = new List<string>();
            // The church's own SQL metrics in reporting.json are checked the same way.
            var guids = configuration.Metrics.Select( m => m.Guid )
                .Concat( ( source.Reporting?.Metrics ?? new List<Loader.Model.MetricDefinition>() ).Where( m => m.SourceSql != null ).Select( m => m.Guid ) )
                .Select( g => $"'{g}'" ).ToList();
            var categories = ( configuration.Insights?.Categories ?? new List<Guid>() ).Select( g => $"'{g}'" ).DefaultIfEmpty( "NULL" );
            var metrics = sql.Query( database, $@"
SELECT [m].[Id], [m].[Title], [m].[SourceSql], [m].[LastRunDateTime]
FROM [Metric] AS [m]
WHERE [m].[SourceValueTypeId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '{SqlSourceGuid}' ) AND [m].[ScheduleId] IS NOT NULL
    AND ( [m].[Guid] IN ( {string.Join( ", ", guids )} )
        OR [m].[Id] IN ( SELECT [mc].[MetricId] FROM [MetricCategory] AS [mc] INNER JOIN [Category] AS [c] ON [c].[Id] = [mc].[CategoryId] WHERE [c].[Guid] IN ( {string.Join( ", ", categories )} ) ) )" );

            foreach ( var metric in metrics.Where( m => m["LastRunDateTime"] == null ) )
            {
                problems.Add( $"{metric["Title"]} has no last run, so it has no history." );
            }

            // Each metric's newest run, and its newest run at least a quarter before that, so the windows and who was here
            // are proven on history too.
            var judged = metrics.Where( m => m["LastRunDateTime"] != null )
                .SelectMany( m => new[] { ( DateTime ) m["LastRunDateTime"], sql.Scalar( database, "SELECT MAX( [MetricValueDateTime] ) FROM [MetricValue] WHERE [MetricId] = @Id AND [MetricValueDateTime] <= @Before",
                        new SqlParameter( "@Id", m["Id"] ), new SqlParameter( "@Before", ( ( DateTime ) m["LastRunDateTime"] ).AddDays( -90 ) ) ) as DateTime? }
                    .Where( r => r != null ).Select( r => (Metric: m, Run: r.Value) ) )
                .ToList();

            var values = 0;
            foreach ( var run in judged.GroupBy( j => j.Run ) )
            {
                using var db = new RockDatabase( sql.ConnectionString( database ) );
                db.BeginTransaction();
                try
                {
                    SetDatabaseAsOf( db, run.Key );
                    foreach ( var (metric, _) in run )
                    {
                        values += Compare( db, ( int ) metric["Id"], ( string ) metric["Title"], ( string ) metric["SourceSql"], run.Key, problems );
                    }
                }
                finally
                {
                    db.Rollback();
                }
            }

            problems.AddRange( AdultCounts( sql, database, configuration ) );
            return CheckResult.From( "rock-metrics", problems.Count == 0
                ? $"Rock's own SQL, run as of {judged.Count} runs of the {metrics.Count} metrics (each one's newest, and one a quarter before), gives all {values:N0} of their values, and every typed adult count is the people in its service"
                : "A Rock metric's history does not match Rock's own SQL", problems );
        }

        /// <summary>
        /// Rock's Calculate Metrics job, run once now as Rock runs it every 15 minutes, changes no value the load wrote,
        /// adds a value only for a run after a metric's last run, and reports no error.
        /// </summary>
        public static CheckResult Job( RockRelease rock, SqlServer sql, string database )
        {
            const string valuesSql = @"
SELECT [v].[Id], [v].[MetricId], [v].[MetricValueDateTime], [v].[YValue], [m].[Title], [m].[LastRunDateTime]
FROM [MetricValue] AS [v] INNER JOIN [Metric] AS [m] ON [m].[Id] = [v].[MetricId]";
            var before = sql.Query( database, valuesSql ).ToDictionary( r => ( int ) r["Id"] );
            var lastRuns = sql.Query( database, "SELECT [Id], [Title], [LastRunDateTime] FROM [Metric]" ).ToDictionary( r => ( int ) r["Id"], r => r["LastRunDateTime"] as DateTime? );
            var result = RockRunner.Run( rock, sql, database, "calculate-metrics" ).Trim();
            var after = sql.Query( database, valuesSql ).ToDictionary( r => ( int ) r["Id"] );

            var problems = new List<string>();
            problems.AddRange( before.Keys.Except( after.Keys ).Select( id => $"Rock's job removed {before[id]["Title"]}'s value for {before[id]["MetricValueDateTime"]:g}." ) );
            problems.AddRange( before.Keys.Intersect( after.Keys ).Where( id => !Equals( before[id]["YValue"], after[id]["YValue"] ) )
                .Select( id => $"Rock's job changed {after[id]["Title"]}'s value for {after[id]["MetricValueDateTime"]:g} from {before[id]["YValue"]} to {after[id]["YValue"]}." ) );
            var added = after.Keys.Except( before.Keys ).Select( id => after[id] ).ToList();
            problems.AddRange( added.Where( v => lastRuns.GetValueOrDefault( ( int ) v["MetricId"] ) is DateTime last && ( DateTime ) v["MetricValueDateTime"] <= last )
                .Select( v => $"Rock's job filled in {v["Title"]} for {v["MetricValueDateTime"]:g}, which is not after its last run, {lastRuns[( int ) v["MetricId"]]:g}." )
                .Take( 20 ) );
            var notes = added.GroupBy( v => ( string ) v["Title"] ).Select( g => $"Rock's job added {g.Count()} values for {g.Key}, for {string.Join( ", ", g.Select( v => ( ( DateTime ) v["MetricValueDateTime"] ).ToString( "g", CultureInfo.InvariantCulture ) ).Distinct() )}." ).ToList();
            return CheckResult.From( "calculate-metrics", problems.Count == 0
                ? $"Rock's Calculate Metrics job kept all {before.Count:N0} loaded values and added {added.Count:N0}, none before a metric's last run ({result})"
                : "Rock's Calculate Metrics job would change the loaded metric history", problems, notes );
        }

        /// <summary>
        /// Sets the loaded database back to a moment: Rock's metric queries no longer see the check-ins, prayer requests,
        /// prayers, or gifts dated after it, and the people are as they were then. Call it inside a transaction.
        /// </summary>
        private static void SetDatabaseAsOf( RockDatabase db, DateTime run )
        {
            // Who was here comes from the rows as loaded, so it is read before any is hidden.
            db.Execute( "CREATE TABLE #RockMetricsFamily ( [GroupId] INT NOT NULL PRIMARY KEY, [At] DATETIME NULL );" );
            db.Execute( "INSERT INTO #RockMetricsFamily ( [GroupId], [At] )" + FamilyArrivals.Sql, FamilyArrivals.Parameters() );
            db.Execute( @"
UPDATE [Attendance] SET [DidAttend] = 0 WHERE [DidAttend] = 1 AND [StartDateTime] > @Run;
UPDATE [PrayerRequest] SET [IsActive] = 0 WHERE [IsActive] = 1 AND [CreatedDateTime] > @Run;
UPDATE [Interaction] SET [Operation] = 'Later' WHERE [Operation] = 'Prayed' AND [InteractionDateTime] > @Run;
UPDATE [FinancialTransaction] SET [TransactionDateKey] = NULL WHERE [TransactionDateTime] > @Run;", new SqlParameter( "@Run", run ) );
            RockMetricsLoader.SetPeopleAsOf( db, run, "#RockMetricsFamily" );
        }

        /// <summary>
        /// Compares one metric's loaded values at a run with Rock's own SQL. Returns how many values it compared.
        /// </summary>
        private static int Compare( RockDatabase db, int metricId, string title, string sourceSql, DateTime run, List<string> problems )
        {
            var partitions = Convert.ToInt32( db.Scalar( "SELECT COUNT(*) FROM [MetricPartition] WHERE [MetricId] = @Id", new SqlParameter( "@Id", metricId ) ) );
            var rock = RockMetricsLoader.RunRockSql( db, sourceSql, run )
                .GroupBy( r => Key( r.Entities.Take( partitions ) ) )
                .ToDictionary( g => g.Key, g => g.Sum( r => r.Value ) );
            var loaded = db.Query( @"
SELECT [v].[Id], [v].[YValue], [vp].[EntityId]
FROM [MetricValue] AS [v]
INNER JOIN [MetricValuePartition] AS [vp] ON [vp].[MetricValueId] = [v].[Id]
INNER JOIN [MetricPartition] AS [mp] ON [mp].[Id] = [vp].[MetricPartitionId]
WHERE [v].[MetricId] = @Id AND [v].[MetricValueDateTime] = @Run
ORDER BY [v].[Id], [mp].[Order], [mp].[Id]", new SqlParameter( "@Id", metricId ), new SqlParameter( "@Run", run ) )
                .GroupBy( r => ( int ) r["Id"] )
                .ToDictionary( g => Key( g.Select( r => r["EntityId"] as int? ) ), g => ( decimal ) ( g.First()["YValue"] ?? 0m ) );

            var found = new List<string>();
            found.AddRange( rock.Keys.Except( loaded.Keys ).Select( k => $"{title} at {run:g}: Rock's SQL gives {rock[k]:0.##} for {k}, and the load has no value." ) );
            found.AddRange( loaded.Keys.Except( rock.Keys ).Select( k => $"{title} at {run:g}: the load has {loaded[k]:0.##} for {k}, and Rock's SQL gives no value." ) );
            found.AddRange( rock.Keys.Intersect( loaded.Keys ).Where( k => Math.Abs( rock[k] - loaded[k] ) >= 0.01m )
                .Select( k => $"{title} at {run:g}: Rock's SQL gives {rock[k]:0.####} for {k}, and the load has {loaded[k]:0.##}." ) );
            problems.AddRange( found.Take( 5 ) );
            if ( found.Count > 5 )
            {
                problems.Add( $"{title} at {run:g}: and {found.Count - 5} more." );
            }

            return loaded.Count;
        }

        /// <summary>
        /// A value's partition entities, as text: "campus 1, schedule 4" becomes "1,4", and no entity is "none".
        /// </summary>
        private static string Key( IEnumerable<int?> entities )
        {
            var text = string.Join( ",", entities.Select( e => e?.ToString( CultureInfo.InvariantCulture ) ?? "none" ) );
            return text.Length == 0 ? "none" : text;
        }

        /// <summary>
        /// Each Total Adult Attendance value is the people the load has in that service, and each service with an adult
        /// count has a Salvations value too, since staff type both at once.
        /// </summary>
        private static List<string> AdultCounts( SqlServer sql, string database, Loader.Model.RockMetricsConfiguration configuration )
        {
            var adults = configuration.Metrics.FirstOrDefault( m => m.Key == "total-adult-attendance" );
            var salvations = configuration.Metrics.FirstOrDefault( m => m.Key == "salvations" );
            if ( adults == null || salvations == null )
            {
                return new List<string>();
            }

            return sql.Query( database, $@"
WITH [Typed] AS (
    SELECT [m].[Guid] AS [Metric], [v].[MetricValueDateTime] AS [Sunday], [v].[YValue],
        MAX( CASE WHEN [et].[Name] = 'Rock.Model.Campus' THEN [vp].[EntityId] END ) AS [CampusId],
        MAX( CASE WHEN [et].[Name] = 'Rock.Model.Schedule' THEN [vp].[EntityId] END ) AS [ScheduleId]
    FROM [MetricValue] AS [v]
    INNER JOIN [Metric] AS [m] ON [m].[Id] = [v].[MetricId]
    INNER JOIN [MetricValuePartition] AS [vp] ON [vp].[MetricValueId] = [v].[Id]
    INNER JOIN [MetricPartition] AS [mp] ON [mp].[Id] = [vp].[MetricPartitionId]
    INNER JOIN [EntityType] AS [et] ON [et].[Id] = [mp].[EntityTypeId]
    WHERE [m].[Guid] IN ( '{adults.Guid}', '{salvations.Guid}' )
    GROUP BY [m].[Guid], [v].[Id], [v].[MetricValueDateTime], [v].[YValue]
),
[Worship] AS (
    SELECT [o].[SundayDate], [a].[CampusId], [o].[ScheduleId], COUNT(*) AS [People]
    FROM [Attendance] AS [a]
    INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    WHERE [gt].[Guid] = '{WorshipAreaGuid}' AND [a].[DidAttend] = 1
    GROUP BY [o].[SundayDate], [a].[CampusId], [o].[ScheduleId]
)
SELECT TOP 10 CONCAT( 'Total Adult Attendance for ', FORMAT( [t].[Sunday], 'yyyy-MM-dd' ), ', campus ', [t].[CampusId], ', schedule ', [t].[ScheduleId], ' is ',
    FORMAT( [t].[YValue], '0' ), ', but the load has ', ISNULL( [w].[People], 0 ), ' people in that service.' ) AS [Problem]
FROM [Typed] AS [t]
LEFT JOIN [Worship] AS [w] ON [w].[SundayDate] = [t].[Sunday] AND [w].[CampusId] = [t].[CampusId] AND [w].[ScheduleId] = [t].[ScheduleId]
WHERE [t].[Metric] = '{adults.Guid}' AND [t].[YValue] <> ISNULL( [w].[People], 0 )
UNION ALL
SELECT TOP 10 CONCAT( 'The service on ', FORMAT( [t].[Sunday], 'yyyy-MM-dd' ), ', campus ', [t].[CampusId], ', schedule ', [t].[ScheduleId], ' has an adult count and no Salvations value.' )
FROM [Typed] AS [t]
WHERE [t].[Metric] = '{adults.Guid}'
    AND NOT EXISTS ( SELECT 1 FROM [Typed] AS [s] WHERE [s].[Metric] = '{salvations.Guid}' AND [s].[Sunday] = [t].[Sunday] AND [s].[CampusId] = [t].[CampusId] AND [s].[ScheduleId] = [t].[ScheduleId] )" )
                .Select( r => ( string ) r["Problem"] ).ToList();
        }
    }
}
