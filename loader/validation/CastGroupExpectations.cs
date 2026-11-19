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
using RockSampleData.Loader.Loading;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks that each Cast family's small group memberships fit when it came: none starts, and no request is made,
    /// before four weeks after the family started coming, and none lasts past six weeks after it stopped (issue 37).
    /// </summary>
    internal static class CastGroupExpectations
    {
        private static readonly Guid SmallGroupTypeGuid = new Guid( "50FCFB30-F51A-49DF-86F4-2B176EA1820B" );

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        /// <param name="anchor">The load's install day, which the families' relative dates count from.</param>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, DateTime anchor, List<string> failures )
        {
            var window = source.SmallGroups?.Membership.AttendanceWindow;
            if ( window == null )
            {
                return 0;
            }

            var dates = new AnchorDates( anchor );
            var checks = 0;
            foreach ( var household in source.ReadHouseholds().Where( h => h.Attendance?.Started != null || h.Attendance?.Ended != null ) )
            {
                var started = dates.Resolve( household.Attendance.Started );
                var ended = dates.Resolve( household.Attendance.Ended );
                var joinFrom = started?.AddDays( window.JoinAfterStartDays );
                var leaveBy = ended?.AddDays( window.LeaveAfterEndDays.Max );
                checks++;

                foreach ( var person in household.Members )
                {
                    var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                    foreach ( var row in db.Query( @"
SELECT [g].[Name], [gm].[GroupMemberStatus], COALESCE( [gm].[DateTimeAdded], [gm].[CreatedDateTime] ) AS [Added],
    CASE WHEN [gm].[GroupMemberStatus] = 0 OR [gm].[IsArchived] = 1 THEN ISNULL( [gm].[InactiveDateTime], [gm].[ArchivedDateTime] ) END AS [MemberEnded],
    CASE WHEN [g].[IsActive] = 0 OR [g].[IsArchived] = 1 THEN ISNULL( [g].[InactiveDateTime], [g].[ArchivedDateTime] ) END AS [GroupEnded]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [p].[Guid] = @Guid AND [gt].[Guid] = @Type", P( "@Guid", guids.Value( person.Guid ) ), P( "@Type", SmallGroupTypeGuid ) ) )
                    {
                        var group = ( string ) row["Name"];
                        var added = ( ( DateTime ) row["Added"] ).Date;
                        var isPending = Convert.ToInt32( row["GroupMemberStatus"] ) == 2;
                        var until = new[] { row["MemberEnded"] as DateTime?, row["GroupEnded"] as DateTime? }.Where( d => d.HasValue ).Select( d => d.Value.Date ).DefaultIfEmpty( DateTime.MaxValue ).Min();
                        if ( added < joinFrom )
                        {
                            failures.Add( $"{name}: {( isPending ? "asked to join" : "joined" )} {group} on {added:yyyy-MM-dd}, before four weeks after the family started coming on {started:yyyy-MM-dd}" );
                        }

                        if ( leaveBy < anchor && ( isPending || until > leaveBy ) )
                        {
                            failures.Add( $"{name}: {( isPending ? "has a request to join" : "is still in" )} {group} after the family stopped coming on {ended:yyyy-MM-dd}" );
                        }
                    }
                }
            }

            return checks;
        }
    }
}
