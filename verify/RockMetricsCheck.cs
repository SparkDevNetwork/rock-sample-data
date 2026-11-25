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
        private const string PersonAnalyticsCategoryGuid = "C1524D2E-3E8F-3D83-45F8-526B749D79F0";
        private const string PersonEntityTypeGuid = "72657ED8-D16E-492E-AC12-144C5E7567E7";
        private const string IsEraGuid = "CE5739C5-2156-E2AB-48E5-1337C38B935E";
        private const string EraStartDateGuid = "A106610C-A7A1-469E-4097-9DE6400FDFC2";
        private const string EraEndDateGuid = "4711D67E-7526-9582-4A8E-1CD7BBE1B3A2";

        /*
            11/24/26 - CLAUDE

            Rock's eRA SQL reads each person's Currently an eRA, eRA Start Date, and eRA End Date as they are when it
            runs. Rock's eRA job sets them on the day it moves a person and writes a history line that says so: entering
            sets Currently an eRA and the Start Date and deletes the End Date, and exiting deletes Currently an eRA and
            sets the End Date, keeping the Start Date (Rock/Jobs/CalculateFamilyAnalytics.cs). So the history lines give
            each person's three values at any moment. The check first holds today's values to the lines, then sets the
            values back to each run with them, as it hides the rows dated after the run.

            Reason: A person who left eRA, or came back, after a run would otherwise drop out of Rock's SQL for that run.
            On Full 7, a run a quarter back lost up to 8 of a campus's 20 wins that way.
        */

        /// <summary>
        /// Fills #RockMetricsEra with each person's eRA values at @At, from the history lines Rock's eRA job wrote by then.
        /// </summary>
        private const string EraStateSql = @"
CREATE TABLE #RockMetricsEra ( [PersonId] INT NOT NULL PRIMARY KEY, [IsEra] BIT NOT NULL, [StartDate] DATE NULL, [EndDate] DATE NULL );
WITH [Lines] AS (
    SELECT [h].[EntityId] AS [PersonId], [h].[Verb], CAST( [h].[CreatedDateTime] AS date ) AS [Day],
        ROW_NUMBER() OVER ( PARTITION BY [h].[EntityId] ORDER BY [h].[CreatedDateTime] DESC, [h].[Id] DESC ) AS [FromLast]
    FROM [History] AS [h]
    WHERE [h].[CategoryId] = ( SELECT [Id] FROM [Category] WHERE [Guid] = @PersonAnalytics )
        AND [h].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Guid] = @PersonEntity )
        AND [h].[Verb] IN ( 'ENTERED', 'EXITED' ) AND [h].[CreatedDateTime] <= @At
)
INSERT INTO #RockMetricsEra ( [PersonId], [IsEra], [StartDate], [EndDate] )
SELECT [PersonId],
    MAX( CASE WHEN [FromLast] = 1 AND [Verb] = 'ENTERED' THEN 1 ELSE 0 END ),
    MAX( CASE WHEN [Verb] = 'ENTERED' THEN [Day] END ),
    MAX( CASE WHEN [FromLast] = 1 AND [Verb] = 'EXITED' THEN [Day] END )
FROM [Lines]
GROUP BY [PersonId];
DECLARE @IsEra INT = ( SELECT [Id] FROM [Attribute] WHERE [Guid] = @IsEraGuid );
DECLARE @EraStart INT = ( SELECT [Id] FROM [Attribute] WHERE [Guid] = @EraStartGuid );
DECLARE @EraEnd INT = ( SELECT [Id] FROM [Attribute] WHERE [Guid] = @EraEndGuid );";

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

            var (eraPeople, eraProblems) = EraValuesMatchHistory( sql, database );
            problems.AddRange( eraProblems );

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
                ? $"Rock's own SQL, run as of {judged.Count} runs of the {metrics.Count} metrics (each one's newest, and one a quarter before), gives all {values:N0} of their values, every typed adult count is the people in its service, and the eRA job's history gives all {eraPeople:N0} people's eRA values"
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
        /// prayers, or gifts dated after it, the people are as they were then, and so are their eRA values. Call it
        /// inside a transaction.
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

            // Each person's eRA values, as Rock's eRA job had left them by the run. The values Rock saves are text, with
            // the typed copies Rock's SQL reads beside them.
            db.Execute( EraStateSql + @"
DELETE FROM [AttributeValue] WHERE [AttributeId] IN ( @IsEra, @EraStart, @EraEnd );
INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [ValueAsBoolean], [ValueAsDateTime], [Guid], [CreatedDateTime], [ModifiedDateTime] )
SELECT 0, @IsEra, [PersonId], 'True', 1, NULL, NEWID(), @At, @At FROM #RockMetricsEra WHERE [IsEra] = 1
UNION ALL
SELECT 0, @EraStart, [PersonId], CONVERT( varchar(10), [StartDate], 23 ), NULL, [StartDate], NEWID(), @At, @At FROM #RockMetricsEra WHERE [StartDate] IS NOT NULL
UNION ALL
SELECT 0, @EraEnd, [PersonId], CONVERT( varchar(10), [EndDate], 23 ), NULL, [EndDate], NEWID(), @At, @At FROM #RockMetricsEra WHERE [EndDate] IS NOT NULL;", EraParameters( run ) );
        }

        /// <summary>
        /// Today's eRA values must be the ones the eRA job's history lines give, so setting them back to a run rests on
        /// Rock's own record. Returns a line for each person whose values differ, up to five, and how many in all.
        /// </summary>
        private static (int People, List<string> Problems) EraValuesMatchHistory( SqlServer sql, string database )
        {
            using var db = new RockDatabase( sql.ConnectionString( database ) );
            var rows = db.Query( EraStateSql + @"
SELECT [p].[Id], [e].[IsEra], [e].[StartDate], [e].[EndDate],
    [i].[ValueAsBoolean] AS [IsEraNow], CAST( [s].[ValueAsDateTime] AS date ) AS [StartNow], CAST( [x].[ValueAsDateTime] AS date ) AS [EndNow]
FROM [Person] AS [p]
LEFT JOIN #RockMetricsEra AS [e] ON [e].[PersonId] = [p].[Id]
LEFT JOIN [AttributeValue] AS [i] ON [i].[EntityId] = [p].[Id] AND [i].[AttributeId] = @IsEra
LEFT JOIN [AttributeValue] AS [s] ON [s].[EntityId] = [p].[Id] AND [s].[AttributeId] = @EraStart
LEFT JOIN [AttributeValue] AS [x] ON [x].[EntityId] = [p].[Id] AND [x].[AttributeId] = @EraEnd
WHERE [e].[PersonId] IS NOT NULL OR [i].[Id] IS NOT NULL OR [s].[Id] IS NOT NULL OR [x].[Id] IS NOT NULL;", EraParameters( new DateTime( 9999, 12, 31 ) ) );

            static string Text( object isEra, object start, object end ) =>
                $"{( isEra is true ? "an eRA" : "not an eRA" )}, started {( start as DateTime? )?.ToString( "d", CultureInfo.InvariantCulture ) ?? "never"}, ended {( end as DateTime? )?.ToString( "d", CultureInfo.InvariantCulture ) ?? "never"}";
            var differ = rows.Where( r => Text( r["IsEraNow"], r["StartNow"], r["EndNow"] ) != Text( r["IsEra"], r["StartDate"], r["EndDate"] ) ).ToList();
            var problems = differ.Take( 5 )
                .Select( r => $"Person {r["Id"]}'s eRA values are {Text( r["IsEraNow"], r["StartNow"], r["EndNow"] )}, and the eRA job's history says {Text( r["IsEra"], r["StartDate"], r["EndDate"] )}." )
                .ToList();
            if ( differ.Count > 5 )
            {
                problems.Add( $"And {differ.Count - 5} more people whose eRA values are not what the eRA job's history says." );
            }

            return (rows.Count, problems);
        }

        private static SqlParameter[] EraParameters( DateTime at ) => new[]
        {
            new SqlParameter( "@At", at ),
            new SqlParameter( "@PersonAnalytics", PersonAnalyticsCategoryGuid ),
            new SqlParameter( "@PersonEntity", PersonEntityTypeGuid ),
            new SqlParameter( "@IsEraGuid", IsEraGuid ),
            new SqlParameter( "@EraStartGuid", EraStartDateGuid ),
            new SqlParameter( "@EraEndGuid", EraEndDateGuid )
        };

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
