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
    /// <summary>
    /// Writes the church's layered data views from configuration/reporting.json (docs/reporting.md): the foundations, the
    /// Ministry by Design stage and next-step views, and the communication flow audiences, then the reports on them. It
    /// runs in two parts. The views go first, before communications, because the group sync views are built on the
    /// foundations. A view that points at a view a later step writes, such as a registration view, waits for the second
    /// part, which writes it and then the reports after registrations.
    /// </summary>
    internal sealed class ReportingLoader
    {
        /// <summary>
        /// Rock's "Persisted DataView Schedules" category, where Rock's data view editor looks for named schedules.
        /// </summary>
        private static readonly Guid PersistedDataViewSchedulesCategory = new Guid( "EEC7A935-BEF2-4450-9CBF-B85CEC6F7FEA" );

        /// <summary>
        /// Rock's "Metrics" schedule category, where Rock's metric editor looks for named schedules.
        /// </summary>
        private static readonly Guid MetricSchedulesCategory = new Guid( "5A794741-5444-43F0-90D7-48E47276D426" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        /// <summary>
        /// The reporting views, and every view in every configuration file, by key.
        /// </summary>
        private readonly Dictionary<string, DataViewDefinition> _views;
        private readonly Dictionary<string, DataViewDefinition> _allViews;

        private readonly List<DataViewDefinition> _waiting = new List<DataViewDefinition>();
        private readonly List<string> _skipped = new List<string>();
        private int _written;
        private Dictionary<string, int> _scheduleIds;
        private DataViewWriter _writer;
        private SecurityRuleWriter _security;
        private DateTime _now;

        public ReportingLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _views = source.Reporting?.DataViews.ToDictionary( v => v.Key );
            _allViews = source.DataViews();
        }

        private bool IsLoaded => _source.Reporting != null && _source.Categories != null;

        /// <summary>
        /// Writes the named schedules and each view whose views are written, after the views it points at. A view that
        /// points at a view a later step writes waits for <see cref="Finish"/>.
        /// </summary>
        public void WriteViews()
        {
            if ( !IsLoaded )
            {
                _db.Log.Write( "  Data views: skipped (this package has no configuration/reporting.json)" );
                return;
            }

            _now = DateTime.Now;
            _scheduleIds = WriteSchedules( _source.Reporting, _now );
            _writer = new DataViewWriter( _db, _source.Categories, _foreignKey, _allViews );
            _writer.RegisterComponents( _source.Reporting.DataViews, SampleDataSource.ReportingConfigurationPath );
            _security = new SecurityRuleWriter( _db, _source.SecurityRoles, _foreignKey );

            foreach ( var view in InDependencyOrder( _source.Reporting.DataViews ) )
            {
                if ( Waits( view ) )
                {
                    _waiting.Add( view );
                    continue;
                }

                WriteView( view );
            }

            var waitingText = _waiting.Count == 0 ? "" : $", {_waiting.Count} waiting for a later step's views";
            _db.Log.Write( $"  Data views: {_written} for reporting{SkippedText()}{waitingText}" );
        }

        /// <summary>
        /// Writes the views that waited, now that every step that writes views has run, then the reports.
        /// </summary>
        public void Finish()
        {
            if ( !IsLoaded )
            {
                _db.Log.Write( "  Reports: skipped (this package has no configuration/reporting.json)" );
                return;
            }

            if ( _waiting.Count > 0 )
            {
                var before = _written;
                _waiting.ForEach( WriteView );
                _db.Log.Write( $"  Data views: {_written - before} more for reporting{SkippedText()}" );
            }

            var reports = 0;
            foreach ( var report in _source.Reporting.Reports )
            {
                var guid = _allViews.TryGetValue( report.DataView, out var view ) ? view.Guid
                    : throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: report {report.Key} uses data view {report.DataView}, which is not a data view in any configuration file." );
                var viewId = _db.IdByGuid( "DataView", guid );
                if ( viewId == null )
                {
                    _db.Log.Write( $"    Skipped report {report.Key}: its data view {report.DataView} was not written" );
                    continue;
                }

                var reportId = WriteReport( report, viewId.Value, _now );
                _security.Write( "Rock.Model.Report", reportId, report.Security, _now, $"{SampleDataSource.ReportingConfigurationPath}: report {report.Key}" );
                reports++;
            }

            _db.Log.Write( $"  Reports: {reports}" );

            var history = new UsageHistoryWriter( _db, _dates, _seed ).Write( InDependencyOrder( _source.Reporting.DataViews ), References, _source.Reporting.Reports );
            _db.Log.Write( $"  Run history: {history.Views} data views and {history.Reports} reports" );

            var installed = _dates.Anchor.Date.Add( DateTime.Now.TimeOfDay );
            var schedules = _source.Reporting.NamedSchedules.ToDictionary( s => s.Key, s => (s, _scheduleIds[s.Key]) );
            var metrics = new MetricsWriter( _db, _source.Categories, installed, _foreignKey, _seed )
                .Write( _source.Reporting.Metrics, _views, schedules, rule => (StepTypes( rule, null ).Select( t => t.Guid ).ToList(), Program( rule ).Statuses.Complete) );
            _db.Log.Write( $"  Metrics: {metrics.Metrics} with {metrics.Values:N0} weekly values" );
        }

        private string SkippedText() => _skipped.Count == 0 ? "" : $", {_skipped.Count} skipped because this load left out a part they need";

        /// <summary>
        /// Writes one view and its security, or skips it when this load left out a part it needs.
        /// </summary>
        private void WriteView( DataViewDefinition view )
        {
            var missing = Missing( view );
            if ( missing != null )
            {
                _skipped.Add( view.Key );
                _db.Log.Write( $"    Skipped data view {view.Key}: {missing}" );
                return;
            }

            var id = _writer.Write( view, _now, new SeededRandom( $"{_seed}:reporting:{view.Key}" ), Selection, _scheduleIds );
            _security.Write( "Rock.Model.DataView", id, view.Security, _now, $"{SampleDataSource.ReportingConfigurationPath}: data view {view.Key}" );
            _written++;
        }

        /// <summary>
        /// Writes one report and its columns, as Rock's report editor saves them. The report returns its data view's entity.
        /// Returns its Id.
        /// </summary>
        private int WriteReport( ReportDefinition report, int viewId, DateTime now )
        {
            var category = _source.Categories.Get( report.Category );
            var entityTypeId = ( int ) _db.Scalar( "SELECT [EntityTypeId] FROM [DataView] WHERE [Id] = @Id", P( "@Id", viewId ) );
            var reportId = ( int ) _db.Scalar( @"
INSERT INTO [Report] ( [IsSystem], [Name], [Description], [CategoryId], [EntityTypeId], [DataViewId], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Name, @Description, @CategoryId, @EntityTypeId, @DataViewId, @Guid, @ForeignKey, @Now, @Now )",
                P( "@Name", report.Name ), P( "@Description", report.Description ), P( "@CategoryId", _db.RequireIdByGuid( "Category", category.Guid ) ),
                P( "@EntityTypeId", entityTypeId ), P( "@DataViewId", viewId ), P( "@Guid", report.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

            var random = new SeededRandom( $"{_seed}:report:{report.Key}" );
            var order = 0;
            foreach ( var column in report.Columns )
            {
                var (type, componentId, selection) = Column( report, column, entityTypeId );
                _db.Execute( @"
INSERT INTO [ReportField] ( [ReportId], [ReportFieldType], [ShowInGrid], [DataSelectComponentEntityTypeId], [Selection], [ColumnOrder], [SortOrder], [SortDirection],
    [ColumnHeaderText], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @ReportId, @Type, 1, @ComponentId, @Selection, @Order, @SortOrder, @SortDirection, @Header, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@ReportId", reportId ), P( "@Type", type ), P( "@ComponentId", componentId ), P( "@Selection", selection ), P( "@Order", order++ ),
                    P( "@SortOrder", column.Sort ), P( "@SortDirection", column.Descending ? 1 : 0 ), P( "@Header", column.Header ),
                    P( "@Guid", random.NextGuid() ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
            }

            return reportId;
        }

        /// <summary>
        /// A column's Rock field type (0 property, 1 attribute, 2 data select component), its component's entity type, and its
        /// selection: the property name, the attribute's Guid, or the component's settings.
        /// </summary>
        private (int Type, int? ComponentId, string Selection) Column( ReportDefinition report, ReportColumnDefinition column, int entityTypeId )
        {
            if ( column.Property != null )
            {
                return (0, null, column.Property);
            }

            if ( column.Attribute != null )
            {
                var guid = _db.Scalar( @"
SELECT TOP 1 [Guid] FROM [Attribute]
WHERE [EntityTypeId] = @EntityTypeId AND [Key] = @Key AND ISNULL( [EntityTypeQualifierColumn], '' ) = ''",
                    P( "@EntityTypeId", entityTypeId ), P( "@Key", column.Attribute ) )
                    ?? throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: report {report.Key} shows attribute {column.Attribute}, which this Rock does not have." );
                return (1, null, guid.ToString());
            }

            var component = column.Lava != null ? "Rock.Reporting.DataSelect.LiquidSelect" : column.Component
                ?? throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: report {report.Key} has a column with no property, attribute, component, or Lava." );
            var componentId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", component ) )
                ?? throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: report {report.Key} uses {component}, which this Rock has not registered." ) );
            var selection = column.Lava ?? column.Selection;
            if ( column.DataView != null )
            {
                selection = selection.Replace( "{dataView}", _allViews[column.DataView].Guid.ToString() );
            }

            return (2, componentId, selection);
        }

        /// <summary>
        /// Writes each named schedule in Rock's Persisted DataView Schedules category, or its Metrics category, running
        /// every day or every week on its day, at its time. Returns their Ids by key.
        /// </summary>
        private Dictionary<string, int> WriteSchedules( ReportingConfiguration configuration, DateTime now )
        {
            var ids = new Dictionary<string, int>();
            var scheduleDates = TableFactory.Create( "ScheduleId:int", "StartDateTime:date", "EndDateTime:date", "StartDateKey:int" );
            var today = _dates.Anchor.Date;
            foreach ( var schedule in configuration.NamedSchedules )
            {
                var time = TimeSpan.Parse( schedule.Time );
                var weekday = schedule.Day;
                var categoryId = _db.RequireIdByGuid( "Category", schedule.ForMetrics ? MetricSchedulesCategory : PersistedDataViewSchedulesCategory );

                // A weekly schedule starts on its own weekday: the first one on or after the day a daily one starts.
                var first = new DateTime( 2013, 5, 1 );
                while ( weekday != null && first.DayOfWeek != weekday )
                {
                    first = first.AddDays( 1 );
                }

                var rule = weekday == null ? "FREQ=DAILY" : $"FREQ=WEEKLY;BYDAY={weekday.ToString().Substring( 0, 2 ).ToUpperInvariant()}";
                var calendar = $"BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTEND:{first:yyyyMMdd}T{time.Add( TimeSpan.FromMinutes( 1 ) ):hhmm}00\r\nDTSTART:{first:yyyyMMdd}T{time:hhmm}00\r\nRRULE:{rule}\r\nEND:VEVENT\r\nEND:VCALENDAR";
                var id = ( int ) _db.Scalar( @"
INSERT INTO [Schedule] ( [Name], [Description], [iCalendarContent], [EffectiveStartDate], [CategoryId], [IsActive], [Order], [AutoInactivateWhenComplete], [IsPublic], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @Calendar, @First, @CategoryId, 1, 0, 0, 0, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@Name", schedule.Name ), P( "@Description", schedule.Description ), P( "@Calendar", calendar ), P( "@First", first ), P( "@CategoryId", categoryId ),
                    P( "@Guid", schedule.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
                ids[schedule.Key] = id;

                // Rock's Schedule save hook writes a ScheduleDate row for each start from two years before to two years
                // after, and Rock Cleanup rebuilds them nightly, as the calendar's schedules do (CalendarPlanner). The window
                // counts from the install day at midnight, so the same anchor gives the same rows.
                for ( var day = today.AddYears( -2 ).AddDays( -1 ); day < today.AddYears( 2 ); day = day.AddDays( 1 ) )
                {
                    var start = day.Add( time );
                    if ( ( weekday == null || day.DayOfWeek == weekday ) && start < today.AddYears( 2 ) && start.AddMinutes( 1 ) > today.AddYears( -2 ) )
                    {
                        scheduleDates.Rows.Add( id, start, start.AddMinutes( 1 ), int.Parse( start.ToString( "yyyyMMdd" ) ) );
                    }
                }
            }

            _db.BulkInsert( "ScheduleDate", scheduleDates, keepIdentity: false );
            return ids;
        }

        /// <summary>
        /// The views in an order where each comes after every reporting view it points at.
        /// </summary>
        private static List<DataViewDefinition> InDependencyOrder( List<DataViewDefinition> views )
        {
            var keys = views.Select( v => v.Key ).ToHashSet();
            var ordered = new List<DataViewDefinition>();
            var placed = new HashSet<string>();
            while ( ordered.Count < views.Count )
            {
                var ready = views.Where( v => !placed.Contains( v.Key ) && References( v ).Where( keys.Contains ).All( placed.Contains ) ).ToList();
                if ( ready.Count == 0 )
                {
                    var stuck = views.First( v => !placed.Contains( v.Key ) );
                    throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: data view {stuck.Key} is part of a loop of views that point at each other." );
                }

                ordered.AddRange( ready );
                placed.UnionWith( ready.Select( v => v.Key ) );
            }

            return ordered;
        }

        private static IEnumerable<string> References( DataViewDefinition view )
        {
            return DataViewWriter.Leaves( view.Filter ).Where( f => f.DataView != null ).Select( f => f.DataView );
        }

        /// <summary>
        /// Whether the view points at a view in another file that is not written yet, or at a reporting view that waits.
        /// </summary>
        private bool Waits( DataViewDefinition view )
        {
            return References( view ).Any( key => _views.ContainsKey( key )
                ? _waiting.Any( w => w.Key == key )
                : _allViews.TryGetValue( key, out var other ) && _db.IdByGuid( "DataView", other.Guid ) == null );
        }

        /// <summary>
        /// Why the view cannot be written in this load, or null when it can: a view it points at was not written, because
        /// a part was left out, or a step type it matches is not in this database, because steps were left out.
        /// </summary>
        private string Missing( DataViewDefinition view )
        {
            foreach ( var key in References( view ) )
            {
                if ( !_allViews.TryGetValue( key, out var target ) )
                {
                    throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: data view {view.Key} points at {key}, which is not a data view in any configuration file." );
                }

                if ( _skipped.Contains( key ) )
                {
                    return $"it points at {key}, which was skipped";
                }

                if ( !_views.ContainsKey( key ) && _db.IdByGuid( "DataView", target.Guid ) == null )
                {
                    return $"it points at {key}, which this load did not write";
                }
            }

            foreach ( var rule in DataViewWriter.Leaves( view.Filter ).Where( f => f.Steps != null ).Select( f => f.Steps ) )
            {
                var missingType = StepTypes( rule, view ).FirstOrDefault( t => _db.IdByGuid( "StepType", t.Guid ) == null );
                if ( missingType != null )
                {
                    return $"it matches the {missingType.Name} step, which this load did not write";
                }
            }

            return null;
        }

        /// <summary>
        /// A leaf's selection. A view it points at is written by now.
        /// </summary>
        private string Selection( DataViewFilterDefinition filter )
        {
            if ( filter.DataView != null )
            {
                return _writer.ViewSelection( filter );
            }

            if ( filter.Steps != null )
            {
                return StepsSelection( filter.Steps );
            }

            return filter.Selection;
        }

        /// <summary>
        /// Rock's StepsTakenFilter settings: the program, its step types, its Complete status, when they started (any time),
        /// when they were completed, and the campuses (any).
        /// </summary>
        private string StepsSelection( StepsFilterRule rule )
        {
            var program = Program( rule );
            var types = StepTypes( rule, null ).Select( t => t.Guid.ToString() );
            var completed = rule.CompletedInLastDays.HasValue ? $"Last,{rule.CompletedInLastDays},Day,," : "All,,,,";
            return $"{program.Guid}|{string.Join( ",", types )}|{program.Statuses.Complete}|All,,,,|{completed}|";
        }

        private StepProgramDefinition Program( StepsFilterRule rule )
        {
            return _source.Steps?.Programs.FirstOrDefault( p => p.Key == rule.Program )
                ?? throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: a steps filter names program {rule.Program}, which is not in configuration/steps.json." );
        }

        /// <summary>
        /// The step types a steps filter matches: one step type, every type in a stage, or every type in the program.
        /// </summary>
        private List<StepTypeDefinition> StepTypes( StepsFilterRule rule, DataViewDefinition view )
        {
            var types = Program( rule ).StepTypes
                .Where( t => rule.StepType != null ? t.Key == rule.StepType : rule.Stage == null || t.Stage == rule.Stage )
                .ToList();
            return types.Count > 0 ? types
                : throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: data view {view?.Key} has a steps filter that matches no step type in {rule.Program}." );
        }
    }
}
