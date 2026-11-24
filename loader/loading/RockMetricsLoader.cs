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
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Rock's Calculate Metrics job gives each metric a value at each run of its schedule, from install week on. The
        loader writes the values the job would have saved at every earlier run, from the rows the load wrote: Rock's own
        rule for the metric, read at the run's time, over the rows that existed then. A weekly window is the run's day
        and the seven before it, as Rock's SQL has it, and on the run's own day only what came before the run. The
        weekly metrics read years of rows, so the loader counts them itself, set-based, in a few queries. The Insights
        metrics read only the Person table, so the loader runs Rock's own SQL for them, with the people set as they were
        at the run. Rock keeps no history of a person's fields, so a person's campus and fields are today's
        (decisions 234 and 347). The schedules are Rock's own, read from Rock's schedule rows.

        Reason: Issue 55. Every Rock metric chart shows history on install day, and Rock's own job, running after
        install, finds nothing to fill in.
    */

    /// <summary>
    /// Writes the history of the metrics Rock ships, from configuration/rock-metrics.json.
    /// </summary>
    internal sealed partial class RockMetricsLoader
    {
        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly RockMetricsConfiguration _configuration;
        private readonly DateTime _installed;
        private readonly string _foreignKey;
        private readonly string _seed;

        /// <param name="installed">The moment of install: the anchor day at the time the load runs.</param>
        public RockMetricsLoader( RockDatabase db, SampleDataSource source, RockMetricsConfiguration configuration, DateTime installed, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _configuration = configuration;
            _installed = installed;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// One value as Rock's Calculate Metrics job, or a staff member, saved it: the moment it is for, when it was saved,
        /// the value, and the entity of each of the metric's partitions, in the partitions' order.
        /// </summary>
        internal sealed record MetricValueRow( DateTime At, DateTime Saved, decimal Value, int?[] Entities );

        /// <summary>
        /// A metric as this Rock has it, and the values the loader works out for it.
        /// </summary>
        private sealed class RockMetric
        {
            public string Key { get; init; }

            public int Id { get; init; }

            public string Title { get; init; }

            public string SourceSql { get; set; }

            public int? MeasurementClassificationValueId { get; init; }

            /// <summary>
            /// The partitions' entity types, in the partitions' order. Null is Rock's default partition.
            /// </summary>
            public List<(int Id, int? EntityTypeId)> Partitions { get; init; }

            /// <summary>
            /// Each run of its schedule in the history window, oldest first. Empty for a metric staff type by hand.
            /// </summary>
            public List<DateTime> Runs { get; init; }

            public List<MetricValueRow> Values { get; set; } = new List<MetricValueRow>();
        }

        /// <summary>
        /// Works out and writes every configured metric this Rock has. Returns how many metrics and values.
        /// </summary>
        public (int Metrics, int Values) Load()
        {
            var metrics = new List<RockMetric>();
            foreach ( var history in _configuration.Metrics )
            {
                var metric = Read( history.Guid, history.Key, schedule => schedule.Runs( _installed.AddDays( -7 * history.Weeks ), _installed ) );
                if ( metric == null )
                {
                    continue;
                }

                // The church's own SQL for the metric, saved as staff save it in Rock's metric editor, before any value.
                if ( history.SourceSql != null )
                {
                    metric.SourceSql = string.Join( "\r\n", history.SourceSql );
                    _db.Execute( "UPDATE [Metric] SET [SourceSql] = @Sql WHERE [Id] = @Id", P( "@Sql", metric.SourceSql ), P( "@Id", metric.Id ) );
                }

                metrics.Add( metric );
            }

            // Total Weekend Attendance adds up the other weekend metrics, so Rock's job runs it last, and so does this.
            foreach ( var metric in metrics.OrderBy( m => m.Key == WeekendTotalKey ) )
            {
                metric.Values = Values( metric, metrics );
            }

            if ( _configuration.Insights != null )
            {
                metrics.AddRange( LoadInsights() );
            }

            var written = Write( metrics );
            return (metrics.Count, written);
        }

        /// <summary>
        /// Reads a metric from Rock, or returns null, after saying so, when this Rock does not have it.
        /// </summary>
        private RockMetric Read( Guid guid, string key, Func<MetricSchedule, List<DateTime>> runs )
        {
            var row = _db.Query( @"
SELECT [m].[Id], [m].[Title], [m].[SourceSql], [m].[MeasurementClassificationValueId], [s].[Name] AS [ScheduleName], [s].[iCalendarContent]
FROM [Metric] AS [m]
LEFT JOIN [Schedule] AS [s] ON [s].[Id] = [m].[ScheduleId]
WHERE [m].[Guid] = @Guid", P( "@Guid", guid ) ).SingleOrDefault();
            if ( row == null )
            {
                _db.Log.Write( $"    Skipped metric {key}: this Rock does not have it" );
                return null;
            }

            var id = ( int ) row["Id"];
            var schedule = row["iCalendarContent"] is string iCalendar ? MetricSchedule.Parse( row["ScheduleName"] as string ?? key, iCalendar ) : null;
            return new RockMetric
            {
                Key = key,
                Id = id,
                Title = ( string ) row["Title"],
                SourceSql = row["SourceSql"] as string,
                MeasurementClassificationValueId = row["MeasurementClassificationValueId"] as int?,
                Partitions = _db.Query( "SELECT [Id], [EntityTypeId] FROM [MetricPartition] WHERE [MetricId] = @Id ORDER BY [Order], [Id]", P( "@Id", id ) )
                    .Select( r => (( int ) r["Id"], r["EntityTypeId"] as int?) ).ToList(),
                Runs = schedule == null ? new List<DateTime>() : runs( schedule )
            };
        }

        /// <summary>
        /// The rule for each configured metric.
        /// </summary>
        private List<MetricValueRow> Values( RockMetric metric, List<RockMetric> all )
        {
            return metric.Key switch
            {
                "total-childrens-attendance" => Attendance( metric, AttendanceArea.Children ),
                "total-students-attendance" => Attendance( metric, AttendanceArea.Students ),
                "total-volunteer-attendance" => Attendance( metric, AttendanceArea.Volunteers ),
                "prayer-requests" => PrayerRequests( metric ),
                "prayers" => Prayers( metric ),
                "giving" => Giving( metric ),
                "weekly-era-wins" => EraWins( metric ),
                "weekly-era-losses" => EraLosses( metric ),
                "baptisms" => Baptisms( metric ),
                "tithing-percent" => Tithing( metric, TithingCount.Percent ),
                "tithing-households" => Tithing( metric, TithingCount.TithingHouseholds ),
                "giving-households" => Tithing( metric, TithingCount.GivingHouseholds ),
                "total-adult-attendance" => AdultAttendance( metric ),
                "salvations" => Salvations( metric ),
                WeekendTotalKey => WeekendTotal( metric, all ),
                _ => throw new InvalidDataException( $"{SampleDataSource.RockMetricsConfigurationPath}: the loader has no rule for metric {metric.Key}." )
            };
        }

        /// <summary>
        /// Writes every value and its partitions, and sets each scheduled metric's last run to its newest run, so Rock's
        /// job next runs it at the next scheduled time. Returns how many values.
        /// </summary>
        private int Write( List<RockMetric> metrics )
        {
            var values = TableFactory.Create( "Id:int", "MetricValueType:int", "YValue:decimal", "MetricId:int", "MetricValueDateTime:date", "MetricValueDateKey:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var partitions = TableFactory.Create( "Id:int", "MetricPartitionId:int", "EntityId:int", "MetricValueId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var nextValueId = NextId( "MetricValue" );
            var nextPartitionId = NextId( "MetricValuePartition" );

            foreach ( var metric in metrics )
            {
                var random = new SeededRandom( $"{_seed}:rock-metric:{metric.Key}" );
                foreach ( var value in metric.Values.OrderBy( v => v.At ).ThenBy( v => v.Saved ) )
                {
                    values.Rows.Add( nextValueId, 0, value.Value, metric.Id, value.At, int.Parse( value.At.ToString( "yyyyMMdd" ) ), random.NextGuid(), _foreignKey, value.Saved, value.Saved );

                    // Rock gives a metric with only its default partition one partition value, with no entity.
                    var entities = value.Entities.Length == 0 && metric.Partitions.Count == 1 && metric.Partitions[0].EntityTypeId == null ? new int?[] { null } : value.Entities;
                    for ( var position = 0; position < entities.Length && position < metric.Partitions.Count; position++ )
                    {
                        partitions.Rows.Add( nextPartitionId++, metric.Partitions[position].Id, Value( entities[position] ), nextValueId, random.NextGuid(), _foreignKey, value.Saved, value.Saved );
                    }

                    nextValueId++;
                }

                if ( metric.Runs.Count > 0 )
                {
                    _db.Execute( "UPDATE [Metric] SET [LastRunDateTime] = @LastRun WHERE [Id] = @Id", P( "@LastRun", metric.Runs[metric.Runs.Count - 1] ), P( "@Id", metric.Id ) );
                }
            }

            _db.BulkInsert( "MetricValue", values, keepIdentity: true );
            _db.BulkInsert( "MetricValuePartition", partitions, keepIdentity: true );
            return values.Rows.Count;
        }

        /*
            11/24/26 - CLAUDE

            Rock Cleanup sets each campus's average weekend attendance every night: the average of the Total
            Weekend Attendance metric's campus values whose week's Sunday falls in the last 4 weeks, rounded to a
            whole number. Rock finds the metric by its measurement classification, so this does too. Once the
            metric has history, the first Rock Cleanup would set the field, so the load sets it the same way. The
            window ends on the install day, which is the day Rock Cleanup would see. Weeks to check back is the
            job's setting, and 4 is its default.

            Reason: Mirrors RockCleanup.UpdateCampusAverageWeekendAttendance (see validation/rock-lock.json).
        */

        /// <summary>
        /// Rock Cleanup's default for how many past weeks a campus's average weekend attendance covers.
        /// </summary>
        private const int AverageWeekendAttendanceWeeks = 4;

        /// <summary>
        /// Sets each campus's average weekend attendance as Rock Cleanup would, and returns how many campuses got one.
        /// </summary>
        public int UpdateCampusAverageWeekendAttendance()
        {
            return _db.Execute( @"
DECLARE @TotalWeekendAttendanceMetricId INT = ( SELECT TOP 1 [m].[Id] FROM [Metric] AS [m] INNER JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [m].[MeasurementClassificationValueId] WHERE [dv].[Guid] = 'b24acb41-8b75-41dc-9b47-f289d8c9f04f' );
DECLARE @CampusEntityTypeId INT = ( SELECT TOP 1 [Id] FROM [EntityType] WHERE [Guid] = '00096bed-9587-415e-8ad4-4e076ae8fbf0' );

WITH [AverageAttendance] AS (
    SELECT [mvp].[EntityId] AS [CampusId]
        , CAST( ROUND( AVG( [mv].[YValue] ), 0 ) AS INT ) AS [WeekendAttendance]
    FROM [MetricValue] AS [mv]
    INNER JOIN [MetricValuePartition] AS [mvp] ON [mvp].[MetricValueId] = [mv].[Id]
    INNER JOIN [MetricPartition] AS [mp] ON [mp].[Id] = [mvp].[MetricPartitionId]
    INNER JOIN [AnalyticsSourceDate] AS [asd] ON [asd].[DateKey] = [mv].[MetricValueDateKey]
    WHERE [mv].[MetricId] = @TotalWeekendAttendanceMetricId
        AND [mp].[EntityTypeId] = @CampusEntityTypeId
        AND [asd].[SundayDate] >= @StartDate
    GROUP BY [mvp].[EntityId]
)
UPDATE [c]
SET [c].[AverageWeekendAttendance] = [aa].[WeekendAttendance]
FROM [Campus] AS [c]
INNER JOIN [AverageAttendance] AS [aa] ON [aa].[CampusId] = [c].[Id];",
                P( "@StartDate", _installed.Date.AddDays( -7 * AverageWeekendAttendanceWeeks ) ) );
        }

        /// <summary>
        /// The job saves a value a few seconds after the run's scheduled time.
        /// </summary>
        private static DateTime SavedAfter( DateTime run, SeededRandom random ) => run.AddSeconds( random.Between( 2, 40 ) );

        /// <summary>
        /// Rock keeps two decimal places of a value, and SQL Server's client drops the rest.
        /// </summary>
        private static decimal Stored( decimal value ) => Math.Truncate( value * 100 ) / 100;

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );

        private static object Value( object value ) => value ?? DBNull.Value;
    }
}
