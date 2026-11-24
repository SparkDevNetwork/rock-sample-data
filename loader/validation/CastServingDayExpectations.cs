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
    /// Checks each Cast volunteer whose roster names the day they serve or when they joined, such as Drew Kim on Wednesday
    /// nights: they joined the team that day, every request to serve is on that weekday and after they joined, and they
    /// served at least once (decision D-student-volunteers-2).
    /// </summary>
    internal static class CastServingDayExpectations
    {
        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, DateTime anchor, List<string> failures )
        {
            var dates = new AnchorDates( anchor );
            var checks = 0;
            foreach ( var team in source.ReadCastServingTeams().Values )
            {
                foreach ( var member in team.Members.Where( m => m.ServiceDay != null || m.Joined != null ) )
                {
                    var person = P( "@Person", guids.Value( member.Guid ) );
                    var joinedOn = db.Scalar( @"
SELECT TOP 1 [gm].[DateTimeAdded]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [g].[Guid] = @Team AND [p].[Guid] = @Person", P( "@Team", team.Guid ), person ) as DateTime?;
                    if ( joinedOn == null )
                    {
                        continue;
                    }

                    checks++;
                    var requests = db.Query( @"
SELECT [o].[OccurrenceDate], [a].[DidAttend]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [g].[Guid] = @Team AND [p].[Guid] = @Person AND [a].[RequestedToAttend] = 1", P( "@Team", team.Guid ), P( "@Person", guids.Value( member.Guid ) ) )
                        .Select( r => (Date: ( DateTime ) r["OccurrenceDate"], Served: Convert.ToBoolean( r["DidAttend"] )) )
                        .ToList();
                    var expectedJoin = dates.Resolve( member.Joined );
                    var wrongDay = member.ServiceDay == null ? 0 : requests.Count( r => r.Date.DayOfWeek.ToString() != member.ServiceDay );
                    if ( ( expectedJoin.HasValue && joinedOn.Value.Date < expectedJoin.Value.Date ) || wrongDay > 0 || requests.Any( r => r.Date < joinedOn.Value.Date )
                        || !requests.Any( r => r.Served && r.Date <= anchor.Date ) )
                    {
                        failures.Add( $"{member.Name}: expected to join {team.Name} on {expectedJoin:yyyy-MM-dd} or after and serve only on {member.ServiceDay ?? "any day"} since, Rock shows a join on {joinedOn:yyyy-MM-dd}, "
                            + $"{wrongDay} requests on another day, and {requests.Count( r => r.Served && r.Date <= anchor.Date )} times served" );
                    }
                }
            }

            return checks;
        }
    }
}
