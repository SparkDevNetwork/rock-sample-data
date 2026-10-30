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

using System.Text.Json;

using Microsoft.Data.SqlClient;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/28/26 - CLAUDE

        The loader cannot run Rock's data view engine, so this class answers "who is in this person data view
        today" in SQL, for the few data filter components the church's views use. Each filter follows Rock's own
        GetExpression for that component (Rock 20 and 21 match), including the sliding date range math in
        RockDateTimeHelper, with the install day as "today". A component or option it does not know is an error,
        never a guess. The verify tool runs Rock's real Group Sync over every load and fails on any difference.

        Reason: The loader fills each synced group with what Rock's Group Sync job would put there.
    */

    /// <summary>
    /// Finds the people in a person data view, and for each one, when they first matched it when that can be told.
    /// </summary>
    internal sealed class DataViewEvaluator
    {
        private static readonly Guid NamelessRecordTypeGuid = new Guid( "721300ED-1267-4DA0-B4F2-6C6B5B17B1C5" );
        private static readonly Guid SchoolGradesGuid = new Guid( "24E5A79F-1E62-467A-AD5D-0D10A2328B4D" );

        /// <summary>
        /// Rock's ComparisonType values a count can use.
        /// </summary>
        private static readonly Dictionary<int, Func<int, int, bool>> Comparisons = new Dictionary<int, Func<int, int, bool>>
        {
            [0x1] = ( count, value ) => count == value,
            [0x2] = ( count, value ) => count != value,
            [0x80] = ( count, value ) => count > value,
            [0x100] = ( count, value ) => count >= value,
            [0x200] = ( count, value ) => count < value,
            [0x400] = ( count, value ) => count <= value
        };

        private static readonly Dictionary<string, int> ComparisonNames = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase )
        {
            ["EqualTo"] = 0x1, ["NotEqualTo"] = 0x2, ["GreaterThan"] = 0x80, ["GreaterThanOrEqualTo"] = 0x100, ["LessThan"] = 0x200, ["LessThanOrEqualTo"] = 0x400
        };

        /// <summary>
        /// The Person enum properties a property filter may name, with each value's number.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, int>> EnumProperties = new Dictionary<string, Dictionary<string, int>>
        {
            ["EmailPreference"] = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase ) { ["EmailAllowed"] = 0, ["NoMassEmails"] = 1, ["DoNotEmail"] = 2 },
            ["AgeClassification"] = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase ) { ["Unknown"] = 0, ["Adult"] = 1, ["Child"] = 2 }
        };

        /// <summary>
        /// The Person text properties a property filter may test for "is not blank".
        /// </summary>
        private static readonly HashSet<string> TextProperties = new HashSet<string> { "Email" };

        private static readonly Dictionary<string, int> MemberStatuses = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase ) { ["Inactive"] = 0, ["Active"] = 1, ["Pending"] = 2 };

        private readonly RockDatabase _db;
        private readonly DateTime _today;
        private HashSet<int> _people;

        /// <param name="db">The database being loaded.</param>
        /// <param name="today">The day the data view runs: the install day.</param>
        public DataViewEvaluator( RockDatabase db, DateTime today )
        {
            _db = db;
            _today = today.Date;
        }

        /// <summary>
        /// Every person in the view, with when they first matched it, or null when that cannot be told.
        /// </summary>
        public Dictionary<int, DateTime?> Evaluate( DataViewFilterDefinition filter )
        {
            switch ( filter.ExpressionType )
            {
                case 0:
                    return Leaf( filter.Component, filter.Selection );

                case 1:
                    return filter.Children.Select( Evaluate ).Aggregate( ( all, next ) => all.Keys.Where( next.ContainsKey ).ToDictionary( id => id, id => Latest( all[id], next[id] ) ) );

                case 2:
                    var any = new Dictionary<int, DateTime?>();
                    foreach ( var child in filter.Children.Select( Evaluate ) )
                    {
                        foreach ( var pair in child )
                        {
                            any[pair.Key] = any.TryGetValue( pair.Key, out var since ) ? Earliest( since, pair.Value ) : pair.Value;
                        }
                    }

                    return any;

                default:
                    var matched = filter.Children.SelectMany( c => Evaluate( c ).Keys ).ToHashSet();
                    var left = LeftSince( filter.Children );
                    return People().Where( id => !matched.Contains( id ) ).ToDictionary( id => id, id => left.TryGetValue( id, out var since ) ? since : ( DateTime? ) null );
            }
        }

        /// <summary>
        /// For "none of these" over one "attended at least once in the last N days" filter: the day each person's last
        /// visit fell out of the window. That is when they started to match. Otherwise nothing can be told.
        /// </summary>
        private Dictionary<int, DateTime?> LeftSince( List<DataViewFilterDefinition> children )
        {
            var none = new Dictionary<int, DateTime?>();
            if ( children.Count != 1 || children[0].Component != "Rock.Reporting.DataFilter.Person.GroupTypeAttendanceFilter" )
            {
                return none;
            }

            var attendance = AttendanceSelection.Parse( children[0].Selection, _today );
            if ( attendance.Comparison != 0x100 || attendance.Count != 1 )
            {
                return none;
            }

            var window = attendance.End - attendance.Start;
            foreach ( var row in _db.Query( $@"
SELECT [pa].[PersonId], MAX( [o].[OccurrenceDate] ) AS [Last]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[DidAttend] = 1 AND [g].[GroupTypeId] IN ( {GroupTypeIds( attendance.GroupTypes )} )
GROUP BY [pa].[PersonId]" ) )
            {
                none[( int ) row["PersonId"]] = (( DateTime ) row["Last"]).Add( window );
            }

            return none;
        }

        private Dictionary<int, DateTime?> Leaf( string component, string selection )
        {
            switch ( component )
            {
                case "Rock.Reporting.DataFilter.PropertyFilter":
                    return PropertyFilter( selection );

                case "Rock.Reporting.DataFilter.Person.InGroupFilter":
                    return InGroupFilter( selection );

                case "Rock.Reporting.DataFilter.Person.InGroupGroupTypeFilter":
                    return InGroupGroupTypeFilter( selection );

                case "Rock.Reporting.DataFilter.Person.GroupTypeFirstAttendanceFilter":
                    return FirstAttendanceFilter( selection );

                case "Rock.Reporting.DataFilter.Person.GroupTypeAttendanceFilter":
                    return AttendanceFilter( selection );

                case "Rock.Reporting.DataFilter.Person.GradeFilter":
                    return GradeFilter( selection );

                case "Rock.Reporting.DataSelect.Person.InRegistrationInstanceFilter":
                    return RegistrationFilter( selection );

                default:
                    throw new InvalidDataException( $"configuration/group-syncs.json uses the data filter {component}, which the loader cannot evaluate." );
            }
        }

        /// <summary>
        /// Rock's PropertyFilter on a Person enum property: a JSON list of the property name and its chosen values.
        /// </summary>
        private Dictionary<int, DateTime?> PropertyFilter( string selection )
        {
            var values = JsonSerializer.Deserialize<List<string>>( selection );

            // A text property "is not blank" (Rock's ComparisonType.IsNotBlank): not null, and not empty once trimmed.
            if ( values != null && values.Count >= 2 && TextProperties.Contains( values[0] ) && values[1] == "64" )
            {
                return Ids( $"SELECT [p].[Id] FROM [Person] AS [p] WHERE [p].[{values[0]}] IS NOT NULL AND LTRIM( RTRIM( [p].[{values[0]}] ) ) <> ''" );
            }

            if ( values == null || values.Count < 2 || !EnumProperties.TryGetValue( values[0], out var names ) )
            {
                throw new InvalidDataException( $"configuration/group-syncs.json has the property filter {selection}. The loader can evaluate only {string.Join( " and ", EnumProperties.Keys )}, and whether Email is blank." );
            }

            var chosen = values[1].Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries )
                .Select( v => names.TryGetValue( v.Trim(), out var number ) ? number : throw new InvalidDataException( $"configuration/group-syncs.json: {v} is not a {values[0]} value." ) )
                .ToList();
            if ( chosen.Count == 0 )
            {
                // Rock's single select filter with nothing chosen does not filter.
                return People().ToDictionary( id => id, id => ( DateTime? ) null );
            }

            return Ids( $"SELECT [p].[Id] FROM [Person] AS [p] WHERE [p].[{values[0]}] IN ( {string.Join( ", ", chosen )} )" );
        }

        /// <summary>
        /// Rock's InGroupFilter: groups|roles|include child groups|member status. Each person's first time added to one of the groups.
        /// </summary>
        private Dictionary<int, DateTime?> InGroupFilter( string selection )
        {
            var parts = selection.Split( '|' );
            if ( parts.Length < 2 || parts.Length > 7 || ( parts.Length > 2 && parts[2].Equals( "True", StringComparison.OrdinalIgnoreCase ) ) )
            {
                throw new InvalidDataException( $"configuration/group-syncs.json has the in-group filter {selection}. The loader evaluates only named groups, without child groups or added-on dates." );
            }

            var groupIds = parts[0].Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( g => _db.RequireIdByGuid( "Group", Guid.Parse( g ) ) );
            return Since( $@"
SELECT [gm].[PersonId] AS [Id], MIN( [gm].[DateTimeAdded] ) AS [Since]
FROM [GroupMember] AS [gm]
WHERE [gm].[IsArchived] = 0 AND [gm].[GroupId] IN ( {string.Join( ", ", groupIds )} ){Roles( parts[1] )}{MemberStatus( parts.ElementAtOrDefault( 3 ) )}
GROUP BY [gm].[PersonId]" );
        }

        /// <summary>
        /// Rock's InGroupGroupTypeFilter: group type|roles|member status|group active. Rock leaves out archived groups.
        /// </summary>
        private Dictionary<int, DateTime?> InGroupGroupTypeFilter( string selection )
        {
            var parts = selection.Split( '|' );
            if ( parts.Length < 2 )
            {
                throw new InvalidDataException( $"configuration/group-syncs.json has the group type filter {selection}, which needs at least a group type and roles." );
            }

            var isActive = parts.ElementAtOrDefault( 3 ) is string active && bool.TryParse( active, out var value ) ? $" AND [g].[IsActive] = {( value ? 1 : 0 )}" : string.Empty;
            return Since( $@"
SELECT [gm].[PersonId] AS [Id], MIN( [gm].[DateTimeAdded] ) AS [Since]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
WHERE [gm].[IsArchived] = 0 AND [g].[IsArchived] = 0 AND [g].[GroupTypeId] = {GroupTypeIds( new[] { Guid.Parse( parts[0] ) } )}{Roles( parts[1] )}{MemberStatus( parts.ElementAtOrDefault( 2 ) )}{isActive}
GROUP BY [gm].[PersonId]" );
        }

        /// <summary>
        /// Rock's GroupTypeFirstAttendanceFilter: people whose first attended occurrence of the group types falls in the
        /// date range. That first date is when they started to match.
        /// </summary>
        private Dictionary<int, DateTime?> FirstAttendanceFilter( string selection )
        {
            var attendance = AttendanceSelection.Parse( selection, _today );
            return Since( $@"
SELECT [pa].[PersonId] AS [Id], MIN( [o].[OccurrenceDate] ) AS [Since]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[DidAttend] = 1 AND [g].[GroupTypeId] IN ( {GroupTypeIds( attendance.GroupTypes )} )
GROUP BY [pa].[PersonId]
HAVING MIN( [o].[OccurrenceDate] ) >= @Start AND MIN( [o].[OccurrenceDate] ) < @End", P( "@Start", attendance.Start ), P( "@End", attendance.End ) );
        }

        /// <summary>
        /// Rock's GroupTypeAttendanceFilter: people whose count of attended occurrences of the group types in the date range
        /// compares to the number. A person with none counts zero.
        /// </summary>
        private Dictionary<int, DateTime?> AttendanceFilter( string selection )
        {
            var attendance = AttendanceSelection.Parse( selection, _today );
            if ( attendance.IncludeChildGroupTypes )
            {
                throw new InvalidDataException( $"configuration/group-syncs.json has the attendance filter {selection}. The loader does not evaluate child check-in areas." );
            }

            var counts = _db.Query( $@"
SELECT [pa].[PersonId], COUNT(*) AS [Count]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[DidAttend] = 1 AND [g].[GroupTypeId] IN ( {GroupTypeIds( attendance.GroupTypes )} ) AND [o].[OccurrenceDate] >= @Start AND [o].[OccurrenceDate] < @End
GROUP BY [pa].[PersonId]", P( "@Start", attendance.Start ), P( "@End", attendance.End ) )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( int ) r["Count"] );
            var compare = Comparisons[attendance.Comparison];
            return People().Where( id => compare( counts.GetValueOrDefault( id ), attendance.Count ) ).ToDictionary( id => id, id => ( DateTime? ) null );
        }

        /// <summary>
        /// Rock's GradeFilter: comparison|school grade defined value. A grade is an offset from the current school year,
        /// which ends on the grade transition date: Rock moves to the next year the day after it.
        /// </summary>
        private Dictionary<int, DateTime?> GradeFilter( string selection )
        {
            var parts = selection.Split( '|' );
            var comparison = parts.Length == 2 && int.TryParse( parts[0], out var number ) ? number : 0;
            var grades = _db.Query( "SELECT [dv].[Guid], [dv].[Value] FROM [DefinedValue] AS [dv] INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId] WHERE [dt].[Guid] = @Grades",
                P( "@Grades", SchoolGradesGuid ) ).ToDictionary( r => ( Guid ) r["Guid"], r => int.Parse( ( string ) r["Value"] ) );
            if ( comparison == 0 || !Guid.TryParse( parts[1], out var gradeGuid ) || !grades.TryGetValue( gradeGuid, out var offset ) )
            {
                throw new InvalidDataException( $"The grade filter {selection} needs a comparison and a school grade." );
            }

            var next = grades.Values.Where( v => v < offset ).DefaultIfEmpty( -1 ).Max();
            var transition = ( _db.Scalar( "SELECT ISNULL( NULLIF( [av].[Value], '' ), [a].[DefaultValue] ) FROM [Attribute] AS [a] LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] WHERE [a].[Key] = 'GradeTransitionDate' AND [a].[EntityTypeId] IS NULL" ) as string ?? "6/1" ).Split( '/' );
            var graduation = new DateTime( _today.Year, int.Parse( transition[0] ), int.Parse( transition[1] ) );
            var schoolYear = graduation < _today ? _today.Year + 1 : _today.Year;
            var years = $"( [p].[GraduationYear] - {schoolYear} )";
            var rule = comparison switch
            {
                0x1 => $"{years} <= {offset} AND {years} > {next}",
                0x80 => $"{years} <= {next} AND {years} >= 0",
                0x100 => $"{years} <= {offset} AND {years} >= 0",
                0x200 => $"{years} > {offset}",
                0x400 => $"{years} > {next}",
                _ => throw new InvalidDataException( $"The grade filter {selection} uses a comparison the loader does not evaluate." )
            };
            return Ids( $"SELECT [p].[Id] FROM [Person] AS [p] WHERE {rule}" );
        }

        /// <summary>
        /// Rock's InRegistrationInstanceFilter: a JSON selection of the template's Id, the instance's Guid or null for
        /// any instance of the template, registrant (2) or registrar (1), and the wait list (true, false, or null for both).
        /// </summary>
        private Dictionary<int, DateTime?> RegistrationFilter( string selection )
        {
            using var document = JsonDocument.Parse( selection );
            var root = document.RootElement;
            var templateId = root.GetProperty( "RegistrationTemplateId" ).GetInt32();
            var instance = root.GetProperty( "RegistrationInstanceGuid" ).ValueKind == JsonValueKind.Null ? ( Guid? ) null : root.GetProperty( "RegistrationInstanceGuid" ).GetGuid();
            var isRegistrar = root.GetProperty( "RegistrationType" ).GetInt32() == 1;
            var onWaitList = root.GetProperty( "OnWaitList" ).ValueKind == JsonValueKind.Null ? ( bool? ) null : root.GetProperty( "OnWaitList" ).GetBoolean();
            var instanceRule = instance.HasValue ? $" AND [ri].[Guid] = '{instance.Value}'" : string.Empty;
            var waitListRule = onWaitList.HasValue ? $" AND [rr].[OnWaitList] = {( onWaitList.Value ? 1 : 0 )}" : string.Empty;
            var person = isRegistrar ? "[r].[PersonAliasId]" : "[rr].[PersonAliasId]";
            return Ids( $@"
SELECT DISTINCT [pa].[PersonId] AS [Id]
FROM [RegistrationRegistrant] AS [rr]
INNER JOIN [Registration] AS [r] ON [r].[Id] = [rr].[RegistrationId]
INNER JOIN [RegistrationInstance] AS [ri] ON [ri].[Id] = [r].[RegistrationInstanceId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = {person}
WHERE [ri].[RegistrationTemplateId] = {templateId}{instanceRule}{waitListRule}" );
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

        private Dictionary<int, DateTime?> Ids( string sql )
        {
            var people = People();
            return _db.Query( sql ).Select( r => ( int ) r["Id"] ).Where( people.Contains ).ToDictionary( id => id, id => ( DateTime? ) null );
        }

        private Dictionary<int, DateTime?> Since( string sql, params SqlParameter[] parameters )
        {
            var people = People();
            return _db.Query( sql, parameters ).Where( r => people.Contains( ( int ) r["Id"] ) )
                .ToDictionary( r => ( int ) r["Id"], r => r["Since"] is DateTime since ? since : ( DateTime? ) null );
        }

        private string GroupTypeIds( IEnumerable<Guid> guids ) => string.Join( ", ", guids.Select( g => _db.RequireIdByGuid( "GroupType", g ) ) );

        private string Roles( string roleGuids )
        {
            var ids = roleGuids.Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( r => _db.RequireIdByGuid( "GroupTypeRole", Guid.Parse( r ) ) ).ToList();
            return ids.Count == 0 ? string.Empty : $" AND [gm].[GroupRoleId] IN ( {string.Join( ", ", ids )} )";
        }

        private static string MemberStatus( string status )
        {
            if ( string.IsNullOrWhiteSpace( status ) )
            {
                return string.Empty;
            }

            var value = int.TryParse( status, out var number ) ? number : MemberStatuses.TryGetValue( status, out var named ) ? named
                : throw new InvalidDataException( $"configuration/group-syncs.json: {status} is not a group member status." );
            return $" AND [gm].[GroupMemberStatus] = {value}";
        }

        private static DateTime? Latest( DateTime? a, DateTime? b ) => a.HasValue && b.HasValue ? ( a > b ? a : b ) : a ?? b;

        private static DateTime? Earliest( DateTime? a, DateTime? b ) => a.HasValue && b.HasValue ? ( a < b ? a : b ) : a ?? b;

        /// <summary>
        /// The selection both group type attendance filters share: group types|comparison|count|sliding date range|include child group types.
        /// </summary>
        private sealed class AttendanceSelection
        {
            public List<Guid> GroupTypes { get; private set; }

            public int Comparison { get; private set; }

            public int Count { get; private set; }

            public DateTime Start { get; private set; }

            public DateTime End { get; private set; }

            public bool IncludeChildGroupTypes { get; private set; }

            public static AttendanceSelection Parse( string selection, DateTime today )
            {
                var parts = selection.Split( '|' );
                if ( parts.Length < 4 )
                {
                    throw new InvalidDataException( $"configuration/group-syncs.json has the attendance filter {selection}, which needs group types, a comparison, a count, and a date range." );
                }

                var comparison = int.TryParse( parts[1], out var number ) ? number : ComparisonNames.TryGetValue( parts[1], out var named ) ? named : 0x100;
                if ( !Comparisons.ContainsKey( comparison ) )
                {
                    throw new InvalidDataException( $"configuration/group-syncs.json: comparison {parts[1]} in {selection} is not one the loader evaluates." );
                }

                var (start, end) = SlidingRange( parts[3], today, selection );
                return new AttendanceSelection
                {
                    GroupTypes = parts[0].Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( Guid.Parse ).ToList(),
                    Comparison = comparison,
                    Count = int.TryParse( parts[2], out var count ) ? count : 0,
                    Start = start,
                    End = end,
                    IncludeChildGroupTypes = parts.Length > 4 && parts[4].Equals( "True", StringComparison.OrdinalIgnoreCase )
                };
            }

            /// <summary>
            /// RockDateTimeHelper.CalculateDateRangeFromDelimitedValues for "Last" and "Previous" in days, months, or years,
            /// saved with commas for pipes. Weeks are left out, because they depend on the site's first day of the week.
            /// </summary>
            private static (DateTime Start, DateTime End) SlidingRange( string range, DateTime today, string selection )
            {
                var parts = range.Split( ',' );
                var mode = parts[0];
                var units = parts.Length > 1 && int.TryParse( parts[1], out var n ) ? n : 1;
                var unit = parts.ElementAtOrDefault( 2 );
                if ( mode != "Last" && mode != "Previous" )
                {
                    throw new InvalidDataException( $"configuration/group-syncs.json: the date range in {selection} must be Last or Previous." );
                }

                var roundUp = mode == "Last" ? 1 : 0;
                DateTime end;
                DateTime start;
                switch ( unit )
                {
                    case "Day":
                        end = today.AddDays( roundUp );
                        start = end.AddDays( -units );
                        break;

                    case "Month":
                        end = new DateTime( today.Year, today.Month, 1 ).AddMonths( roundUp );
                        start = end.AddMonths( -units );
                        break;

                    case "Year":
                        end = new DateTime( today.Year, 1, 1 ).AddYears( roundUp );
                        start = end.AddYears( -units );
                        break;

                    default:
                        throw new InvalidDataException( $"configuration/group-syncs.json: the date range in {selection} must count days, months, or years." );
                }

                // Rock never lets Last or Previous reach past tomorrow.
                var cutoff = today.AddDays( 1 );
                return (start, end > cutoff ? cutoff : end);
            }
        }
    }
}
