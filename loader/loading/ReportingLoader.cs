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
    /// Ministry by Design stage and next-step views, and the communication flow audiences. A view can point at another view
    /// in any configuration file, so this runs after group sync and registrations, and writes each view after the views
    /// it points at.
    /// </summary>
    internal sealed class ReportingLoader
    {
        /// <summary>
        /// Rock's "Persisted DataView Schedules" category, where Rock's data view editor looks for named schedules.
        /// </summary>
        private static readonly Guid PersistedDataViewSchedulesCategory = new Guid( "EEC7A935-BEF2-4450-9CBF-B85CEC6F7FEA" );

        /// <summary>
        /// The filters whose selection is Rock's JSON with the other view's Id. Every other filter that points at a view
        /// holds its Guid.
        /// </summary>
        private static readonly HashSet<string> IdSelectionFilters = new HashSet<string>
        {
            "Rock.Reporting.DataFilter.OtherDataViewFilter",
            "Rock.Reporting.DataFilter.NotInOtherDataViewFilter"
        };

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public ReportingLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        public void Load()
        {
            var configuration = _source.Reporting;
            if ( configuration == null || _source.Categories == null )
            {
                _db.Log.Write( "  Data views: skipped (this package has no configuration/reporting.json)" );
                return;
            }

            var now = DateTime.Now;
            var scheduleIds = WriteSchedules( configuration, now );
            var otherViews = ( _source.GroupSyncs?.DataViews ?? new List<DataViewDefinition>() )
                .Concat( _source.Registrations?.DataViews ?? new List<DataViewDefinition>() )
                .ToDictionary( v => v.Key, v => v.Guid );
            var views = configuration.DataViews.ToDictionary( v => v.Key );
            var writer = new DataViewWriter( _db, _source.Categories, _foreignKey );
            writer.RegisterComponents( configuration.DataViews, SampleDataSource.ReportingConfigurationPath );

            var ids = new Dictionary<string, int>();
            var skipped = new List<string>();
            foreach ( var view in InDependencyOrder( configuration.DataViews ) )
            {
                var missing = Missing( view, views, otherViews, ids, skipped );
                if ( missing != null )
                {
                    skipped.Add( view.Key );
                    _db.Log.Write( $"    Skipped data view {view.Key}: {missing}" );
                    continue;
                }

                ids[view.Key] = writer.Write( view, now, new SeededRandom( $"{_seed}:reporting:{view.Key}" ),
                    f => Selection( f, views, otherViews, ids ), scheduleIds );
            }

            var skippedText = skipped.Count == 0 ? "" : $", {skipped.Count} skipped because this load left out a part they need";
            _db.Log.Write( $"  Data views: {ids.Count} for reporting{skippedText}" );
        }

        /// <summary>
        /// Writes each named schedule in Rock's Persisted DataView Schedules category, running every day at its time.
        /// Returns their Ids by key.
        /// </summary>
        private Dictionary<string, int> WriteSchedules( ReportingConfiguration configuration, DateTime now )
        {
            var ids = new Dictionary<string, int>();
            var categoryId = _db.RequireIdByGuid( "Category", PersistedDataViewSchedulesCategory );
            var scheduleDates = TableFactory.Create( "ScheduleId:int", "StartDateTime:date", "EndDateTime:date", "StartDateKey:int" );
            var today = _dates.Anchor.Date;
            foreach ( var schedule in configuration.NamedSchedules )
            {
                var time = TimeSpan.Parse( schedule.Time );
                var calendar = $"BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTEND:20130501T{time.Add( TimeSpan.FromMinutes( 1 ) ):hhmm}00\r\nDTSTART:20130501T{time:hhmm}00\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\nEND:VCALENDAR";
                var id = ( int ) _db.Scalar( @"
INSERT INTO [Schedule] ( [Name], [Description], [iCalendarContent], [EffectiveStartDate], [CategoryId], [IsActive], [Order], [AutoInactivateWhenComplete], [IsPublic], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @Calendar, '2013-05-01', @CategoryId, 1, 0, 0, 0, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@Name", schedule.Name ), P( "@Description", schedule.Description ), P( "@Calendar", calendar ), P( "@CategoryId", categoryId ),
                    P( "@Guid", schedule.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
                ids[schedule.Key] = id;

                // Rock's Schedule save hook writes a ScheduleDate row for each start from two years before to two years
                // after, and Rock Cleanup rebuilds them nightly, as the calendar's schedules do (CalendarPlanner). The window
                // counts from the install day at midnight, so the same anchor gives the same rows.
                for ( var day = today.AddYears( -2 ).AddDays( -1 ); day < today.AddYears( 2 ); day = day.AddDays( 1 ) )
                {
                    var start = day.Add( time );
                    if ( start < today.AddYears( 2 ) && start.AddMinutes( 1 ) > today.AddYears( -2 ) )
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
        /// Why the view cannot be written in this load, or null when it can: a view it points at was not written, because
        /// a part was left out, or a step type it matches is not in this database, because steps were left out.
        /// </summary>
        private string Missing( DataViewDefinition view, Dictionary<string, DataViewDefinition> views, Dictionary<string, Guid> otherViews,
            Dictionary<string, int> ids, List<string> skipped )
        {
            foreach ( var key in References( view ) )
            {
                if ( views.ContainsKey( key ) )
                {
                    if ( skipped.Contains( key ) )
                    {
                        return $"it points at {key}, which was skipped";
                    }
                }
                else if ( !otherViews.TryGetValue( key, out var guid ) )
                {
                    throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: data view {view.Key} points at {key}, which is not a data view in any configuration file." );
                }
                else if ( _db.IdByGuid( "DataView", guid ) == null )
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
        /// A leaf's selection. A view it points at is written by now, so its Id is known.
        /// </summary>
        private string Selection( DataViewFilterDefinition filter, Dictionary<string, DataViewDefinition> views, Dictionary<string, Guid> otherViews,
            Dictionary<string, int> ids )
        {
            if ( filter.DataView != null )
            {
                var guid = views.TryGetValue( filter.DataView, out var view ) ? view.Guid : otherViews[filter.DataView];
                if ( !IdSelectionFilters.Contains( filter.Component ) )
                {
                    // A related data view filter's settings start with the view's Guid. A filter with more settings, such as
                    // Rock's group data view filter (Guid, member status, role type), gives them with a {dataView} placeholder.
                    return filter.Selection?.Replace( "{dataView}", guid.ToString() ) ?? guid.ToString();
                }

                var id = ids.TryGetValue( filter.DataView, out var written ) ? written : _db.RequireIdByGuid( "DataView", guid );

                // Rock's data view editor saves "Use Persisted" on by default.
                return $"{{\"DataViewId\":{id},\"UsePersisted\":true}}";
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
