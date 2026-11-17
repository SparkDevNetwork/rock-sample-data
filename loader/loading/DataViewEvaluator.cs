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
        private static readonly Guid FamilyAdultRoleGuid = new Guid( "2639F9A5-2AAE-4E48-A8C3-4FFE86681E42" );
        private static readonly Guid FamilyChildRoleGuid = new Guid( "C8B1814F-6AA7-4055-B2D7-48FE20429CB9" );
        private const string FamilyMembersTransform = "Rock.Reporting.DataTransform.Person.FamilyMembersTransform";

        /// <summary>
        /// Rock's weeks start on Monday unless a site changes its first day of the week, which a new install does not.
        /// </summary>
        private const DayOfWeek FirstDayOfWeek = DayOfWeek.Monday;

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
        /// The Person defined value properties a property filter may name, with the chosen values' Guids.
        /// </summary>
        private static readonly HashSet<string> DefinedValueProperties = new HashSet<string> { "RecordStatusValueId", "ConnectionStatusValueId" };

        /// <summary>
        /// The Person text properties a property filter may test for "is not blank".
        /// </summary>
        private static readonly HashSet<string> TextProperties = new HashSet<string> { "Email" };

        private static readonly Dictionary<string, int> MemberStatuses = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase ) { ["Inactive"] = 0, ["Active"] = 1, ["Pending"] = 2 };

        private readonly RockDatabase _db;
        private readonly DateTime _today;
        private readonly IReadOnlyDictionary<string, DataViewDefinition> _views;
        private readonly Func<DataViewFilterDefinition, string> _selection;
        private readonly Dictionary<string, Dictionary<int, DateTime?>> _viewResults = new Dictionary<string, Dictionary<int, DateTime?>>();
        private HashSet<int> _people;

        /// <param name="db">The database being loaded.</param>
        /// <param name="today">The day the data view runs: the install day.</param>
        /// <param name="views">The views an Existing Data View filter may point at, by key.</param>
        /// <param name="selection">A leaf's Rock selection when the configuration builds it at load, such as a steps
        /// filter's. Null, or a null answer, reads the leaf's own selection.</param>
        public DataViewEvaluator( RockDatabase db, DateTime today, IReadOnlyDictionary<string, DataViewDefinition> views = null, Func<DataViewFilterDefinition, string> selection = null )
        {
            _db = db;
            _today = today.Date;
            _views = views;
            _selection = selection;
        }

        /// <summary>
        /// Every person in a whole data view: its filters, then its post-filter transformation, as Rock's engine runs it.
        /// </summary>
        public Dictionary<int, DateTime?> EvaluateView( DataViewDefinition view )
        {
            if ( !_viewResults.TryGetValue( view.Key, out var result ) )
            {
                result = Evaluate( view.Filter );
                if ( view.Transform != null )
                {
                    result = Transform( view, result );
                }

                _viewResults[view.Key] = result;
            }

            return new Dictionary<int, DateTime?>( result );
        }

        /// <summary>
        /// Every person in the view, with when they first matched it, or null when that cannot be told.
        /// </summary>
        public Dictionary<int, DateTime?> Evaluate( DataViewFilterDefinition filter )
        {
            switch ( filter.ExpressionType )
            {
                case 0:
                    return filter.DataView != null ? OtherView( filter ) : Leaf( filter.Component, _selection?.Invoke( filter ) ?? filter.Selection );

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

        /// <summary>
        /// A filter that points at another data view: Rock's Existing Data View filter (everyone in it, each since they first
        /// matched it), its Not in Existing Data View filter, and the person filters that take a pledge or scheduled gift view.
        /// </summary>
        private Dictionary<int, DateTime?> OtherView( DataViewFilterDefinition filter )
        {
            if ( _views == null || !_views.TryGetValue( filter.DataView, out var view ) )
            {
                throw new InvalidDataException( $"A data view points at {filter.DataView} with {filter.Component}, but that is not a data view in any configuration file." );
            }

            switch ( filter.Component )
            {
                case "Rock.Reporting.DataFilter.OtherDataViewFilter":
                    return EvaluateView( view );

                case "Rock.Reporting.DataFilter.NotInOtherDataViewFilter":
                    var inView = EvaluateView( view );
                    return People().Where( id => !inView.ContainsKey( id ) ).ToDictionary( id => id, id => ( DateTime? ) null );

                case "Rock.Reporting.DataFilter.Person.FinancialPledgeDataViewFilter":
                    // Rock matches the person named on each pledge, by any of their aliases, and not their spouse.
                    return Ids( $@"
SELECT DISTINCT [pa].[PersonId] AS [Id]
FROM [FinancialPledge] AS [e]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [e].[PersonAliasId]
WHERE {EntityWhere( view, "FinancialPledge" )}" );

                case "Rock.Reporting.DataFilter.Person.FinancialScheduledTransactionDataViewFilter":
                    // Rock's Combine Giving option also matches everyone in the giving group of each gift's person.
                    var combineGiving = ( filter.Selection ?? string.Empty ).Split( '|' ).ElementAtOrDefault( 1 ) is string combine
                        && combine.Equals( "True", StringComparison.OrdinalIgnoreCase );
                    var where = EntityWhere( view, "FinancialScheduledTransaction" );
                    var givingGroups = !combineGiving ? string.Empty : $@"
UNION
SELECT [p].[Id]
FROM [Person] AS [p]
WHERE [p].[GivingGroupId] IN (
    SELECT [gp].[GivingGroupId]
    FROM [FinancialScheduledTransaction] AS [e]
    INNER JOIN [PersonAlias] AS [gpa] ON [gpa].[Id] = [e].[AuthorizedPersonAliasId]
    INNER JOIN [Person] AS [gp] ON [gp].[Id] = [gpa].[PersonId]
    WHERE [gp].[GivingGroupId] IS NOT NULL AND {where} )";
                    return Ids( $@"
SELECT [pa].[PersonId] AS [Id]
FROM [FinancialScheduledTransaction] AS [e]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [e].[AuthorizedPersonAliasId]
WHERE {where}{givingGroups}" );

                default:
                    throw new InvalidDataException( $"A data view points at {filter.DataView} with {filter.Component}, which the loader cannot evaluate." );
            }
        }

        /// <summary>
        /// A SQL condition on [e], a row of the table, from the filters of a pledge or scheduled gift data view.
        /// </summary>
        private string EntityWhere( DataViewDefinition view, string table )
        {
            var entity = view.EntityType ?? "Rock.Model.Person";
            if ( entity != $"Rock.Model.{table}" || view.Transform != null )
            {
                throw new InvalidDataException( $"Data view {view.Key} returns {entity}, but the filter that points at it needs plain Rock.Model.{table} rows." );
            }

            return EntityCondition( view, view.Filter );
        }

        private string EntityCondition( DataViewDefinition view, DataViewFilterDefinition filter )
        {
            var children = filter.Children.Select( c => EntityCondition( view, c ) ).ToList();
            return filter.ExpressionType switch
            {
                0 => EntityLeaf( view, filter.Component, filter.Selection ),
                1 => "( " + string.Join( " AND ", children ) + " )",
                2 => "( " + string.Join( " OR ", children ) + " )",
                _ => "NOT ( " + string.Join( " OR ", children ) + " )"
            };
        }

        /// <summary>
        /// One filter of a pledge or scheduled gift view: Rock's pledge Account filter (any of the accounts), or a property
        /// filter on IsActive (Rock's selection: the property, Equal To or Not Equal To, and True or False).
        /// </summary>
        private string EntityLeaf( DataViewDefinition view, string component, string selection )
        {
            if ( component == "Rock.Reporting.DataFilter.FinancialPledge.AccountFilter" )
            {
                var accountIds = selection.Split( '|' )[0].Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries )
                    .Select( g => _db.IdByGuid( "FinancialAccount", Guid.Parse( g ) ) ).Where( id => id.HasValue ).Select( id => id.Value ).ToList();
                return accountIds.Count == 0 ? "1 = 0" : $"[e].[AccountId] IN ( {string.Join( ", ", accountIds )} )";
            }

            if ( component == "Rock.Reporting.DataFilter.PropertyFilter" )
            {
                var values = JsonSerializer.Deserialize<List<string>>( selection );
                if ( values != null && values.Count == 3 && values[0] == "IsActive" && bool.TryParse( values[2], out var flag ) && ( values[1] == "1" || values[1] == "2" ) )
                {
                    return $"[e].[IsActive] = {( flag == ( values[1] == "1" ) ? 1 : 0 )}";
                }
            }

            throw new InvalidDataException( $"Data view {view.Key} uses {component} with {selection}, which the loader cannot evaluate." );
        }

        /// <summary>
        /// Rock's Family Members transformation: everyone who is an adult or child in a family with someone the filters found.
        /// </summary>
        private Dictionary<int, DateTime?> Transform( DataViewDefinition view, Dictionary<int, DateTime?> found )
        {
            if ( view.Transform != FamilyMembersTransform )
            {
                throw new InvalidDataException( $"Data view {view.Key} uses the transformation {view.Transform}, which the loader cannot evaluate." );
            }

            var roles = $"{_db.RequireIdByGuid( "GroupTypeRole", FamilyAdultRoleGuid )}, {_db.RequireIdByGuid( "GroupTypeRole", FamilyChildRoleGuid )}";
            var memberships = _db.Query( $"SELECT [GroupId], [PersonId], [IsArchived] FROM [GroupMember] WHERE [GroupRoleId] IN ( {roles} )" )
                .Select( r => (Group: ( int ) r["GroupId"], Person: ( int ) r["PersonId"], IsArchived: ( bool ) r["IsArchived"]) )
                .ToList();

            // Rock finds each family through any family member row, archived or not, and returns only current members.
            var families = memberships.Where( m => found.ContainsKey( m.Person ) ).Select( m => m.Group ).ToHashSet();
            var people = People();
            return memberships.Where( m => !m.IsArchived && families.Contains( m.Group ) && people.Contains( m.Person ) )
                .Select( m => m.Person ).Distinct().ToDictionary( id => id, id => ( DateTime? ) null );
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

                case "Rock.Reporting.DataFilter.Person.StepsTakenFilter":
                    return StepsTakenFilter( selection );

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

            // A name that is a person attribute's key filters on that attribute.
            if ( values != null && values.Count >= 2 && AttributeFilter( values ) is Dictionary<int, DateTime?> byAttribute )
            {
                return byAttribute;
            }

            // A text property "is not blank" (Rock's ComparisonType.IsNotBlank): not null, and not empty once trimmed.
            if ( values != null && values.Count >= 2 && TextProperties.Contains( values[0] ) && values[1] == "64" )
            {
                return Ids( $"SELECT [p].[Id] FROM [Person] AS [p] WHERE [p].[{values[0]}] IS NOT NULL AND LTRIM( RTRIM( [p].[{values[0]}] ) ) <> ''" );
            }

            // A defined value property: any of the chosen values, a comma list of their Guids.
            if ( values != null && values.Count == 2 && DefinedValueProperties.Contains( values[0] ) )
            {
                var ids = values[1].Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( v => _db.RequireIdByGuid( "DefinedValue", Guid.Parse( v ) ) ).ToList();
                return ids.Count == 0 ? throw new InvalidDataException( $"configuration/group-syncs.json has the property filter {selection}, which chooses no {values[0]} value." )
                    : Ids( $"SELECT [p].[Id] FROM [Person] AS [p] WHERE [p].[{values[0]}] IN ( {string.Join( ", ", ids )} )" );
            }

            if ( values == null || values.Count < 2 || !EnumProperties.TryGetValue( values[0], out var names ) )
            {
                throw new InvalidDataException( $"configuration/group-syncs.json has the property filter {selection}. The loader can evaluate only {string.Join( ", ", EnumProperties.Keys.Concat( DefinedValueProperties ) )}, and whether Email is blank." );
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

        /*
            11/16/26 - CLAUDE

            Rock's property filter reads a person attribute through its field type. A single select matches the chosen
            values. A date compares the attribute's date with a date, or with "CURRENT:<days>", today moved by that many
            days, using Rock's comparison numbers. Rock reads the date from ValueAsDateTime, which the loader's attribute
            value hook fills after the last part, so this reads Value, the text that column comes from.

            Reason: The personalization segments rest on giving views that filter Rock's giving journey stage and Last
            Gave attributes. Verify runs Rock's own segment update to catch any difference.
        */

        /// <summary>
        /// Rock's PropertyFilter on a person attribute, or null when the name is not a person attribute's key.
        /// </summary>
        private Dictionary<int, DateTime?> AttributeFilter( List<string> values )
        {
            var attribute = _db.Query( @"
SELECT TOP 1 [a].[Id], [ft].[Class]
FROM [Attribute] AS [a]
INNER JOIN [FieldType] AS [ft] ON [ft].[Id] = [a].[FieldTypeId]
WHERE [a].[Key] = @Key AND ISNULL( [a].[EntityTypeQualifierColumn], '' ) = ''
    AND [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' )", P( "@Key", values[0] ) ).FirstOrDefault();
            if ( attribute == null )
            {
                return null;
            }

            var people = People();
            var stored = _db.Query( "SELECT [EntityId], [Value] FROM [AttributeValue] WHERE [AttributeId] = @Id AND [Value] IS NOT NULL AND [Value] <> ''", P( "@Id", attribute["Id"] ) )
                .Select( r => (Person: ( int ) r["EntityId"], Value: ( ( string ) r["Value"] ).Trim()) )
                .Where( s => people.Contains( s.Person ) )
                .ToList();
            var fieldType = ( string ) attribute["Class"];
            if ( fieldType == "Rock.Field.Types.SelectSingleFieldType" && values.Count == 2 )
            {
                var chosen = values[1].Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( v => v.Trim() ).ToHashSet( StringComparer.OrdinalIgnoreCase );
                return stored.Where( s => chosen.Contains( s.Value ) ).Select( s => s.Person ).Distinct().ToDictionary( id => id, id => ( DateTime? ) null );
            }

            if ( fieldType == "Rock.Field.Types.DateFieldType" && values.Count == 3 && int.TryParse( values[1], out var comparison ) && Comparisons.TryGetValue( comparison, out var compare ) )
            {
                var compareTo = values[2].StartsWith( "CURRENT", StringComparison.OrdinalIgnoreCase )
                    ? _today.AddDays( int.TryParse( values[2].Split( ':' ).ElementAtOrDefault( 1 ), out var offset ) ? offset : 0 )
                    : DateTime.Parse( values[2], CultureInfo.InvariantCulture ).Date;
                int Day( DateTime date ) => ( int ) ( date.Date - DateTime.MinValue ).TotalDays;
                return stored.Where( s => DateTime.TryParse( s.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date ) && compare( Day( date ), Day( compareTo ) ) )
                    .Select( s => s.Person ).Distinct().ToDictionary( id => id, id => ( DateTime? ) null );
            }

            throw new InvalidDataException( $"A data view filters the person attribute {values[0]} ({fieldType}) with {JsonSerializer.Serialize( values )}, which the loader cannot evaluate." );
        }

        /// <summary>
        /// Rock's StepsTakenFilter: program|step types|statuses|started in|completed in|campuses. Everyone with a step of one
        /// of the types, in one of the statuses, in the program. The loader evaluates only steps started and completed any
        /// time, at any campus.
        /// </summary>
        private Dictionary<int, DateTime?> StepsTakenFilter( string selection )
        {
            var parts = ( selection ?? string.Empty ).Split( '|' );
            if ( parts.Length < 5 || !Guid.TryParse( parts[0], out var program ) || !parts[3].StartsWith( "All", StringComparison.Ordinal )
                || !parts[4].StartsWith( "All", StringComparison.Ordinal ) || ( parts.Length > 5 && parts[5].Trim().Length > 0 ) )
            {
                throw new InvalidDataException( $"The steps filter {selection} needs a program, and steps started and completed any time at any campus. The loader evaluates nothing else." );
            }

            string Guids( string list ) => string.Join( ", ", list.Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( g => $"'{Guid.Parse( g )}'" ) );
            var types = parts[1].Trim().Length == 0 ? string.Empty : $" AND [st].[Guid] IN ( {Guids( parts[1] )} )";
            var statuses = parts[2].Trim().Length == 0 ? string.Empty : $" AND [ss].[Guid] IN ( {Guids( parts[2] )} )";
            return Ids( $@"
SELECT DISTINCT [pa].[PersonId] AS [Id]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepProgram] AS [sp] ON [sp].[Id] = [st].[StepProgramId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
LEFT JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
WHERE [sp].[Guid] = '{program}'{types}{statuses}" );
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
            /// RockDateTimeHelper.CalculateDateRangeFromDelimitedValues for "Last" and "Previous" in days, weeks, months, or
            /// years, saved with commas for pipes. A week starts on Rock's first day of the week.
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

                    case "Week":
                        var intoWeek = ( ( int ) today.DayOfWeek - ( int ) FirstDayOfWeek + 7 ) % 7;
                        end = today.AddDays( -intoWeek ).AddDays( 7 * roundUp );
                        start = end.AddDays( -7 * units );
                        break;

                    default:
                        throw new InvalidDataException( $"configuration/group-syncs.json: the date range in {selection} must count days, weeks, months, or years." );
                }

                // Rock never lets Last or Previous reach past tomorrow.
                var cutoff = today.AddDays( 1 );
                return (start, end > cutoff ? cutoff : end);
            }
        }
    }
}
