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

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/24/26 - CLAUDE

        A Ministry by Design metric that counts with SQL keeps the church's SQL in Rock, which Rock's Calculate Metrics job
        runs from install on. Each value before install is what that SQL gives at the run, from the rows the load wrote.
        The SQL reads years of attendance, so the loader reads each kind of row once and counts every run from it here,
        by the same rule. Each SQL counts only rows from before its run, and gives every active campus a value, zero
        included.

        Reason: The history and Rock's own runs after install are one line, and verify runs each metric's SQL as of a
        past run to prove it.
    */

    /// <summary>
    /// The Ministry by Design metrics that count with SQL: each run's value at each active campus.
    /// </summary>
    internal sealed class MinistrySqlMetrics
    {
        private static readonly Guid ServingAreaPurposeGuid = new Guid( "36A554CE-7815-41B9-A435-93F3D52A2828" );
        private static readonly Guid WorshipAreaGuid = new Guid( "235BAE2B-5760-4763-AADF-3938F34BA100" );
        private static readonly Guid ActiveRecordGuid = new Guid( "618F906C-C33D-4FA3-8AEF-E58CB7B63F1E" );
        private static readonly Guid SmallGroupTypeGuid = new Guid( "50FCFB30-F51A-49DF-86F4-2B176EA1820B" );
        private static readonly Guid ServingTeamTypeGuid = new Guid( "2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4" );

        private readonly RockDatabase _db;
        private List<int> _campuses;
        private Dictionary<int, List<DateTime>> _worship;
        private Dictionary<int, int?> _activeAdults;
        private GroupMembershipHistory _memberships;

        public MinistrySqlMetrics( RockDatabase db )
        {
            _db = db;
        }

        /// <summary>
        /// Each run's value at each active campus, for the metric with this key.
        /// </summary>
        public Dictionary<DateTime, List<(int? Campus, decimal Value)>> Values( string key, List<DateTime> runs )
        {
            return key switch
            {
                "first-time-guests" => FirstTimeGuests( runs ),
                "back-in-the-last-30-days" => BackInTheLast30Days( runs ),
                "benevolence-requests" => BenevolenceRequests( runs ),
                "group-connection-rate" => RegularAttendersIn( SmallGroupTypeGuid, runs ),
                "serving-rate" => RegularAttendersIn( ServingTeamTypeGuid, runs ),
                _ => throw new InvalidDataException( $"{SampleDataSource.ReportingConfigurationPath}: the loader has no rule for the SQL metric {key}." )
            };
        }

        /// <summary>
        /// The people whose first visit to a weekend service came in the 7 days before the run, at the campus of that visit.
        /// A visit is an attended check-in or worship service in an area that counts as a weekend service, other than serving.
        /// </summary>
        private Dictionary<DateTime, List<(int?, decimal)>> FirstTimeGuests( List<DateTime> runs )
        {
            var firsts = _db.Query( @"
SELECT [CampusId], [StartDateTime]
FROM (
    SELECT [a].[CampusId], [a].[StartDateTime], ROW_NUMBER() OVER ( PARTITION BY [pa].[PersonId] ORDER BY [a].[StartDateTime], [a].[Id] ) AS [Visit]
    FROM [Attendance] AS [a]
    INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    WHERE [a].[DidAttend] = 1 AND [gt].[AttendanceCountsAsWeekendService] = 1
        AND ISNULL( [gt].[GroupTypePurposeValueId], 0 ) <> ISNULL( ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @ServingArea ), -1 )
) AS [v]
WHERE [Visit] = 1", P( "@ServingArea", ServingAreaPurposeGuid ) )
                .Select( r => (Campus: r["CampusId"] as int?, At: ( DateTime ) r["StartDateTime"]) )
                .ToList();
            return ByCampus( runs, run => firsts.Where( f => f.At >= run.AddDays( -7 ) && f.At < run ).Select( f => f.Campus ) );
        }

        /// <summary>
        /// The active adults who came back to worship in the 30 days before the run after 90 or more days away, having come
        /// 10 or more times in the 18 months before they came back, by their campus.
        /// </summary>
        private Dictionary<DateTime, List<(int?, decimal)>> BackInTheLast30Days( List<DateTime> runs )
        {
            var visits = Worship();
            var campuses = ActiveAdults();

            // Each return: a visit 90 or more days after the one before it, with 10 or more visits in the 18 months before it.
            var returns = visits.Where( v => campuses.ContainsKey( v.Key ) )
                .SelectMany( v => v.Value.Select( ( at, i ) => (Person: v.Key, At: at, Index: i) )
                    .Where( x => x.Index > 0 && v.Value[x.Index - 1] <= x.At.AddDays( -90 )
                        && v.Value.Take( x.Index ).Count( e => e >= x.At.AddMonths( -18 ) ) >= 10 ) )
                .ToList();
            return ByCampus( runs, run => returns.Where( r => r.At >= run.AddDays( -30 ) && r.At < run ).Select( r => r.Person ).Distinct().Select( p => campuses[p] ) );
        }

        /// <summary>
        /// The percent of regular attending adults who were active members of the group type's active groups at the run, by
        /// campus, to one decimal place. A regular attender came to worship 4 or more times in the 12 weeks before the run.
        /// Membership is read from Rock's group member history at the run. A campus with no regular attenders is zero.
        /// </summary>
        private Dictionary<DateTime, List<(int?, decimal)>> RegularAttendersIn( Guid groupType, List<DateTime> runs )
        {
            var visits = Worship();
            var adults = ActiveAdults();
            _memberships ??= new GroupMembershipHistory( _db );
            _campuses ??= _db.Query( "SELECT [Id] FROM [Campus] WHERE [IsActive] = 1" ).Select( r => ( int ) r["Id"] ).ToList();
            var groupTypeId = _db.RequireIdByGuid( "GroupType", groupType );
            return runs.ToDictionary( run => run, run =>
            {
                var regular = adults.Where( a => visits.TryGetValue( a.Key, out var days ) && Between( days, run.AddDays( -84 ), run ) >= 4 ).ToList();
                var members = _memberships.MembersAt( groupTypeId, Array.Empty<int>(), 1, true, run );
                return _campuses.Select( c =>
                {
                    var here = regular.Where( a => a.Value == c ).ToList();
                    var rate = here.Count == 0 ? 0m : Math.Round( 100m * here.Count( a => members.Contains( a.Key ) ) / here.Count, 1, MidpointRounding.AwayFromZero );
                    return (( int? ) c, rate);
                } ).ToList();
            } );
        }

        /// <summary>
        /// Each person's worship visits, oldest first.
        /// </summary>
        private Dictionary<int, List<DateTime>> Worship()
        {
            return _worship ??= _db.Query( @"
SELECT [pa].[PersonId], [a].[StartDateTime]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[DidAttend] = 1 AND [g].[GroupTypeId] = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = @Worship )", P( "@Worship", WorshipAreaGuid ) )
                .GroupBy( r => ( int ) r["PersonId"] )
                .ToDictionary( g => g.Key, g => g.Select( r => ( DateTime ) r["StartDateTime"] ).OrderBy( d => d ).ToList() );
        }

        /// <summary>
        /// Today's active adults, with the campus Rock counts each under: their primary campus, or else their family's.
        /// </summary>
        private Dictionary<int, int?> ActiveAdults()
        {
            return _activeAdults ??= _db.Query( @"
SELECT [p].[Id], COALESCE( [p].[PrimaryCampusId], [f].[CampusId] ) AS [CampusId]
FROM [Person] AS [p]
LEFT JOIN [Group] AS [f] ON [f].[Id] = [p].[PrimaryFamilyId]
WHERE [p].[RecordStatusValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @Active ) AND [p].[IsDeceased] = 0 AND [p].[AgeClassification] = 1",
                P( "@Active", ActiveRecordGuid ) )
                .ToDictionary( r => ( int ) r["Id"], r => r["CampusId"] as int? );
        }

        /// <summary>
        /// The benevolence requests made in the month before the run, by the request's campus.
        /// </summary>
        private Dictionary<DateTime, List<(int?, decimal)>> BenevolenceRequests( List<DateTime> runs )
        {
            var requests = _db.Query( "SELECT [CampusId], [RequestDateTime] FROM [BenevolenceRequest]" )
                .Select( r => (Campus: r["CampusId"] as int?, At: ( DateTime ) r["RequestDateTime"]) )
                .ToList();
            return ByCampus( runs, run =>
            {
                var start = new DateTime( run.Year, run.Month, 1 ).AddMonths( -1 );
                return requests.Where( r => r.At >= start && r.At < start.AddMonths( 1 ) ).Select( r => r.Campus );
            } );
        }

        /// <summary>
        /// Counts each run's campuses, with a value for every active campus, zero included.
        /// </summary>
        private Dictionary<DateTime, List<(int?, decimal)>> ByCampus( List<DateTime> runs, Func<DateTime, IEnumerable<int?>> counted )
        {
            _campuses ??= _db.Query( "SELECT [Id] FROM [Campus] WHERE [IsActive] = 1" ).Select( r => ( int ) r["Id"] ).ToList();
            return runs.ToDictionary( run => run, run =>
            {
                var counts = counted( run ).Where( c => c.HasValue ).GroupBy( c => c.Value ).ToDictionary( g => g.Key, g => g.Count() );
                return _campuses.Select( c => (( int? ) c, ( decimal ) counts.GetValueOrDefault( c )) ).ToList();
            } );
        }

        /// <summary>
        /// How many of the sorted moments fall from start up to, not including, end.
        /// </summary>
        private static int Between( List<DateTime> moments, DateTime start, DateTime end )
        {
            int LowerBound( DateTime value )
            {
                int low = 0, high = moments.Count;
                while ( low < high )
                {
                    var middle = ( low + high ) / 2;
                    if ( moments[middle] < value )
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

            return LowerBound( end ) - LowerBound( start );
        }
    }
}
