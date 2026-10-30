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
        10/30/26 - CLAUDE

        Each Ministry by Design stage has a Rock Metric that counts its stage data view every week, the way Rock's
        Calculate Metrics job would. Rock only counts a view as it is today, so the loader fills the weeks before install
        itself, from the steps: a week's value is the active people with a completed step of the stage by that week.
        That is the stage view's own rule, so the newest value is the view's count less anyone whose first step of the
        stage came after it. Values start the week the stage view was made, as a real church's would. Record status is
        today's, because Rock keeps no history of it (decision 234).

        Reason: Question 15 in docs/reporting.md, settled yes. Dashboards show the trend on install day.
    */

    /// <summary>
    /// Writes the church's metrics and their values, one per scheduled run up to install.
    /// </summary>
    internal sealed class MetricsWriter
    {
        /// <summary>
        /// Rock's "Data View" metric source type.
        /// </summary>
        private static readonly Guid DataViewSourceGuid = new Guid( "2EC60BCF-EF63-4CCC-A970-F152292765D0" );

        private static readonly Guid ActiveRecordGuid = new Guid( "618F906C-C33D-4FA3-8AEF-E58CB7B63F1E" );
        private static readonly Guid NamelessRecordTypeGuid = new Guid( "721300ED-1267-4DA0-B4F2-6C6B5B17B1C5" );

        private readonly RockDatabase _db;
        private readonly CategoryConfiguration _categories;
        private readonly DateTime _installed;
        private readonly string _foreignKey;
        private readonly string _seed;

        /// <param name="installed">The moment of install: the anchor day at the time the load runs.</param>
        public MetricsWriter( RockDatabase db, CategoryConfiguration categories, DateTime installed, string foreignKey, string seed )
        {
            _db = db;
            _categories = categories;
            _installed = installed;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <param name="views">The reporting views, by key.</param>
        /// <param name="schedules">The named schedules, by key, and their Ids.</param>
        /// <param name="stepRule">The step types and the Complete status a steps leaf matches.</param>
        /// <returns>How many metrics and values were written.</returns>
        public (int Metrics, int Values) Write( List<MetricDefinition> metrics, IReadOnlyDictionary<string, DataViewDefinition> views,
            IReadOnlyDictionary<string, (NamedPersistSchedule Schedule, int Id)> schedules, Func<StepsFilterRule, (List<Guid> StepTypes, Guid Complete)> stepRule )
        {
            var values = TableFactory.Create( "Id:int", "MetricValueType:int", "YValue:decimal", "MetricId:int", "MetricValueDateTime:date", "MetricValueDateKey:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var partitions = TableFactory.Create( "Id:int", "MetricPartitionId:int", "MetricValueId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var nextValueId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [MetricValue]" ) );
            var nextPartitionId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [MetricValuePartition]" ) );
            var sourceTypeId = _db.RequireIdByGuid( "DefinedValue", DataViewSourceGuid );
            var written = 0;

            foreach ( var metric in metrics )
            {
                var view = views.TryGetValue( metric.DataView, out var found ) ? found
                    : throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: metric {metric.Key} counts data view {metric.DataView}, which is not a reporting data view." );
                var viewRow = _db.Query( "SELECT [Id], [CreatedDateTime] FROM [DataView] WHERE [Guid] = @Guid", P( "@Guid", view.Guid ) ).SingleOrDefault();
                if ( viewRow == null )
                {
                    _db.Log.Write( $"    Skipped metric {metric.Key}: its data view {metric.DataView} was not written" );
                    continue;
                }

                var schedule = schedules.TryGetValue( metric.Schedule, out var named ) ? named
                    : throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: metric {metric.Key} runs on {metric.Schedule}, which is not a named schedule." );
                var runs = Runs( schedule.Schedule, ( DateTime ) viewRow["CreatedDateTime"] );
                var firstStep = FirstSteps( metric, view, stepRule );

                var made = runs.Count > 0 ? runs[0].AddHours( -1 ) : _installed;
                var metricId = ( int ) _db.Scalar( @"
INSERT INTO [Metric] ( [IsSystem], [Title], [Description], [IconCssClass], [IsCumulative], [SourceValueTypeId], [DataViewId], [ScheduleId], [LastRunDateTime],
    [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Title, @Description, @Icon, 0, @SourceTypeId, @DataViewId, @ScheduleId, @LastRun, @Guid, @ForeignKey, @Made, @Made )",
                    P( "@Title", metric.Title ), P( "@Description", metric.Description ), P( "@Icon", metric.IconCssClass ), P( "@SourceTypeId", sourceTypeId ),
                    P( "@DataViewId", viewRow["Id"] ), P( "@ScheduleId", schedule.Id ), P( "@LastRun", runs.Count > 0 ? runs[runs.Count - 1] : null ),
                    P( "@Guid", metric.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Made", made ) );
                _db.Execute( @"
INSERT INTO [MetricCategory] ( [MetricId], [CategoryId], [Order], [Guid], [ForeignKey] )
VALUES ( @MetricId, @CategoryId, 0, @Guid, @ForeignKey )",
                    P( "@MetricId", metricId ), P( "@CategoryId", _db.RequireIdByGuid( "Category", _categories.Get( metric.Category ).Guid ) ),
                    P( "@Guid", SeededRandom.StableGuid( $"{_seed}:metric-category:{metric.Key}" ) ), P( "@ForeignKey", _foreignKey ) );

                // Rock's metric editor gives a metric that is not split by campus or anything else one required partition.
                var partitionId = ( int ) _db.Scalar( @"
INSERT INTO [MetricPartition] ( [MetricId], [IsRequired], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @MetricId, 1, 0, @Guid, @ForeignKey, @Made, @Made )",
                    P( "@MetricId", metricId ), P( "@Guid", metric.PartitionGuid ), P( "@ForeignKey", _foreignKey ), P( "@Made", made ) );

                var random = new SeededRandom( $"{_seed}:metric-values:{metric.Key}" );
                foreach ( var run in runs )
                {
                    // The job saves a value a moment after its scheduled time.
                    var saved = run.AddSeconds( random.Between( 2, 40 ) );
                    var count = firstStep.Count( first => first <= run );
                    values.Rows.Add( nextValueId, 0, ( decimal ) count, metricId, run, int.Parse( run.ToString( "yyyyMMdd" ) ), random.NextGuid(), _foreignKey, saved, saved );
                    partitions.Rows.Add( nextPartitionId++, partitionId, nextValueId++, random.NextGuid(), _foreignKey, saved, saved );
                }

                written++;
            }

            _db.BulkInsert( "MetricValue", values, keepIdentity: true );
            _db.BulkInsert( "MetricValuePartition", partitions, keepIdentity: true );
            return (written, values.Rows.Count);
        }

        /// <summary>
        /// Each scheduled run from when the view was made up to install.
        /// </summary>
        private List<DateTime> Runs( NamedPersistSchedule schedule, DateTime from )
        {
            var time = TimeSpan.Parse( schedule.Time );
            var weekday = schedule.Day;
            var runs = new List<DateTime>();
            for ( var day = from.Date; day <= _installed.Date; day = day.AddDays( 1 ) )
            {
                var run = day.Add( time );
                if ( ( weekday == null || day.DayOfWeek == weekday ) && run > from && run <= _installed )
                {
                    runs.Add( run );
                }
            }

            return runs;
        }

        /// <summary>
        /// For each person in the view today, when they first completed a step it matches. The view must be a stage view:
        /// active people with any of its steps leaves, which is all a week's value can be read back from.
        /// </summary>
        private List<DateTime> FirstSteps( MetricDefinition metric, DataViewDefinition view, Func<StepsFilterRule, (List<Guid> StepTypes, Guid Complete)> stepRule )
        {
            var leaves = DataViewWriter.Leaves( view.Filter ).ToList();
            var rules = leaves.Where( f => f.Steps != null ).Select( f => stepRule( f.Steps ) ).ToList();
            if ( rules.Count == 0 || leaves.Any( f => f.Steps == null && f.DataView != "active-people" ) || view.Filter.All == null )
            {
                throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: metric {metric.Key} counts {view.Key}. The loader can fill a metric's past weeks only for a view of active people with any of some steps." );
            }

            var matches = string.Join( " OR ", rules.Select( r =>
                $"( [s].[StepStatusId] = {_db.RequireIdByGuid( "StepStatus", r.Complete )} AND [s].[StepTypeId] IN ( {string.Join( ", ", r.StepTypes.Select( t => _db.RequireIdByGuid( "StepType", t ) ) )} ) )" ) );
            return _db.Query( $@"
SELECT MIN( COALESCE( [s].[CompletedDateTime], [s].[StartDateTime], [s].[CreatedDateTime] ) ) AS [First]
FROM [Step] AS [s]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE ( {matches} )
    AND [p].[IsDeceased] = 0
    AND [p].[RecordStatusValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @Active )
    AND ( [p].[RecordTypeValueId] IS NULL OR [p].[RecordTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @Nameless ) )
GROUP BY [pa].[PersonId]", P( "@Active", ActiveRecordGuid ), P( "@Nameless", NamelessRecordTypeGuid ) )
                .Select( r => ( DateTime ) r["First"] ).ToList();
        }
    }
}
