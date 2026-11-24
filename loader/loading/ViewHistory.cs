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

namespace RockSampleData.Loader.Loading
{
    /*
        11/24/26 - CLAUDE

        A data view metric's past weeks need the view as it was at each run, and Rock counts a view only as it is today.
        This replays a person data view at a past moment by walking its own filters. Attendance windows slide back to
        the run's day, and only steps completed by then count. A filter on a person's fields, such as record status or
        adult, reads today's value, since Rock keeps no history of those (decisions 234 and 347), and comes from the
        loader's DataViewEvaluator, which verify holds to Rock's own engine. Each kind of row is read once, so every run
        of every metric is a few lookups. A filter it cannot replay is an error, never a guess.

        Reason: Each Ministry by Design metric follows its view's own definition, and at today it gives exactly what the
        evaluator gives, which verify checks.
    */

    /// <summary>
    /// Replays a person data view as it was at a past moment.
    /// </summary>
    internal sealed class ViewHistory
    {
        private const string AttendanceFilter = "Rock.Reporting.DataFilter.Person.GroupTypeAttendanceFilter";
        private const string StepsFilter = "Rock.Reporting.DataFilter.Person.StepsTakenFilter";
        private const string PropertyFilter = "Rock.Reporting.DataFilter.PropertyFilter";

        private static readonly Guid NamelessRecordTypeGuid = new Guid( "721300ED-1267-4DA0-B4F2-6C6B5B17B1C5" );

        private readonly RockDatabase _db;
        private readonly IReadOnlyDictionary<string, DataViewDefinition> _views;
        private readonly Func<DataViewFilterDefinition, string> _selection;
        private readonly DataViewEvaluator _today;
        private readonly Dictionary<DataViewFilterDefinition, HashSet<int>> _state = new Dictionary<DataViewFilterDefinition, HashSet<int>>();
        private readonly Dictionary<string, Dictionary<int, List<DateTime>>> _attendance = new Dictionary<string, Dictionary<int, List<DateTime>>>();
        private readonly Dictionary<string, Dictionary<int, DateTime>> _steps = new Dictionary<string, Dictionary<int, DateTime>>();
        private readonly Dictionary<string, HashSet<int>> _viewsAt = new Dictionary<string, HashSet<int>>();
        private DateTime _cachedAt;
        private HashSet<int> _people;

        /// <param name="today">The install day, for the filters on a person's fields.</param>
        /// <param name="selection">A leaf's Rock selection when the configuration builds it at load, such as a steps filter's.</param>
        public ViewHistory( RockDatabase db, DateTime today, IReadOnlyDictionary<string, DataViewDefinition> views, Func<DataViewFilterDefinition, string> selection )
        {
            _db = db;
            _views = views;
            _selection = selection;
            _today = new DataViewEvaluator( db, today, views, selection );
        }

        /// <summary>
        /// The people in a view at a moment.
        /// </summary>
        public HashSet<int> PeopleAt( DataViewDefinition view, DateTime at )
        {
            if ( view.Transform != null )
            {
                throw new InvalidDataException( $"Data view {view.Key} has a transformation, which the loader cannot replay for a past week." );
            }

            // A view another view points at is read once per moment. Runs come in order, so one moment is kept at a time.
            if ( at != _cachedAt )
            {
                _viewsAt.Clear();
                _cachedAt = at;
            }

            if ( !_viewsAt.TryGetValue( view.Key, out var people ) )
            {
                people = Evaluate( view.Filter, at );
                _viewsAt[view.Key] = people;
            }

            return people;
        }

        private HashSet<int> Evaluate( DataViewFilterDefinition filter, DateTime at )
        {
            switch ( filter.ExpressionType )
            {
                case 0:
                    return Leaf( filter, at );

                case 1:
                    return filter.Children.Select( c => Evaluate( c, at ) ).Aggregate( ( all, next ) => all.Where( next.Contains ).ToHashSet() );

                case 2:
                    return filter.Children.SelectMany( c => Evaluate( c, at ) ).ToHashSet();

                default:
                    var matched = filter.Children.SelectMany( c => Evaluate( c, at ) ).ToHashSet();
                    return People().Where( id => !matched.Contains( id ) ).ToHashSet();
            }
        }

        private HashSet<int> Leaf( DataViewFilterDefinition filter, DateTime at )
        {
            if ( filter.DataView != null )
            {
                var view = _views != null && _views.TryGetValue( filter.DataView, out var found ) ? found
                    : throw new InvalidDataException( $"A data view points at {filter.DataView}, which is not a data view in any configuration file." );
                var inView = PeopleAt( view, at );
                return filter.Component switch
                {
                    "Rock.Reporting.DataFilter.OtherDataViewFilter" => inView,
                    "Rock.Reporting.DataFilter.NotInOtherDataViewFilter" => People().Where( id => !inView.Contains( id ) ).ToHashSet(),
                    _ => throw new InvalidDataException( $"A data view points at {filter.DataView} with {filter.Component}, which the loader cannot replay for a past week." )
                };
            }

            var selection = _selection?.Invoke( filter ) ?? filter.Selection;
            return filter.Component switch
            {
                AttendanceFilter => Attended( selection, at ),
                StepsFilter => Stepped( selection, at ),
                PropertyFilter => State( filter ),
                _ => throw new InvalidDataException( $"The data filter {filter.Component} cannot be replayed for a past week." )
            };
        }

        /// <summary>
        /// A filter on a person's fields: today's answer.
        /// </summary>
        private HashSet<int> State( DataViewFilterDefinition filter )
        {
            if ( !_state.TryGetValue( filter, out var people ) )
            {
                people = _today.Evaluate( filter ).Keys.ToHashSet();
                _state[filter] = people;
            }

            return people;
        }

        /// <summary>
        /// Rock's GroupTypeAttendanceFilter, with its sliding date range ending on the run's day.
        /// </summary>
        private HashSet<int> Attended( string selection, DateTime at )
        {
            var window = DataViewEvaluator.AttendanceSelection.Parse( selection, at.Date );
            if ( window.IncludeChildGroupTypes )
            {
                throw new InvalidDataException( $"The attendance filter {selection} includes child check-in areas, which the loader does not replay." );
            }

            var key = string.Join( ",", window.GroupTypes.OrderBy( g => g ) );
            if ( !_attendance.TryGetValue( key, out var visits ) )
            {
                visits = _db.Query( $@"
SELECT [pa].[PersonId], [o].[OccurrenceDate]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[DidAttend] = 1 AND [g].[GroupTypeId] IN ( {string.Join( ", ", window.GroupTypes.Select( g => _db.RequireIdByGuid( "GroupType", g ) ) )} )" )
                    .GroupBy( r => ( int ) r["PersonId"] )
                    .ToDictionary( g => g.Key, g => g.Select( r => ( DateTime ) r["OccurrenceDate"] ).OrderBy( d => d ).ToList() );
                _attendance[key] = visits;
            }

            var compare = DataViewEvaluator.Comparisons[window.Comparison];
            return People().Where( id => compare( visits.TryGetValue( id, out var days ) ? CountBetween( days, window.Start, window.End ) : 0, window.Count ) ).ToHashSet();
        }

        /// <summary>
        /// Rock's StepsTakenFilter, counting only the steps a person had taken by the run: each step's completion, or its
        /// start or creation when it has none.
        /// </summary>
        private HashSet<int> Stepped( string selection, DateTime at )
        {
            if ( !_steps.TryGetValue( selection, out var first ) )
            {
                var parts = ( selection ?? string.Empty ).Split( '|' );
                if ( parts.Length < 5 || !Guid.TryParse( parts[0], out var program ) || !parts[3].StartsWith( "All", StringComparison.Ordinal )
                    || !parts[4].StartsWith( "All", StringComparison.Ordinal ) || ( parts.Length > 5 && parts[5].Trim().Length > 0 ) )
                {
                    throw new InvalidDataException( $"The steps filter {selection} needs a program, and steps started and completed any time at any campus. The loader replays nothing else." );
                }

                string Guids( string list ) => string.Join( ", ", list.Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( g => $"'{Guid.Parse( g )}'" ) );
                var types = parts[1].Trim().Length == 0 ? string.Empty : $" AND [st].[Guid] IN ( {Guids( parts[1] )} )";
                var statuses = parts[2].Trim().Length == 0 ? string.Empty : $" AND [ss].[Guid] IN ( {Guids( parts[2] )} )";
                first = _db.Query( $@"
SELECT [pa].[PersonId], MIN( COALESCE( [s].[CompletedDateTime], [s].[StartDateTime], [s].[CreatedDateTime] ) ) AS [First]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepProgram] AS [sp] ON [sp].[Id] = [st].[StepProgramId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
LEFT JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
WHERE [sp].[Guid] = '{program}'{types}{statuses}
GROUP BY [pa].[PersonId]" )
                    .ToDictionary( r => ( int ) r["PersonId"], r => ( DateTime ) r["First"] );
                _steps[selection] = first;
            }

            var people = People();
            return first.Where( p => p.Value <= at && people.Contains( p.Key ) ).Select( p => p.Key ).ToHashSet();
        }

        /// <summary>
        /// How many of the sorted days fall from start up to, not including, end.
        /// </summary>
        private static int CountBetween( List<DateTime> days, DateTime start, DateTime end )
        {
            return LowerBound( days, end ) - LowerBound( days, start );
        }

        private static int LowerBound( List<DateTime> days, DateTime value )
        {
            int low = 0, high = days.Count;
            while ( low < high )
            {
                var middle = ( low + high ) / 2;
                if ( days[middle] < value )
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        /// <summary>
        /// Everyone a Person data view starts from: Rock's default person query leaves out the deceased and nameless people.
        /// </summary>
        private HashSet<int> People()
        {
            return _people ??= _db.Query( @"
SELECT [p].[Id]
FROM [Person] AS [p]
WHERE [p].[IsDeceased] = 0 AND ( [p].[RecordTypeValueId] IS NULL OR [p].[RecordTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @Nameless ) )",
                P( "@Nameless", NamelessRecordTypeGuid ) ).Select( r => ( int ) r["Id"] ).ToHashSet();
        }
    }
}
