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

namespace RockSampleData.Verify
{
    /*
        11/19/26 - CLAUDE

        Rock's Calculate Group Requirements job keeps each member's requirement result: it inserts one for a member who
        has none, deletes one for a member it no longer applies to, and moves a result between Meets, Meets With Warning,
        and Not Met, setting the night each state began. It also stamps the last-checked time on every result it looks at,
        which is the only change it makes to a result that is already right. So the check runs the real job and compares
        every result before and after, leaving that time out.

        Reason: Decision D-checks-1. A requirement result the job would change is one the loader got wrong, and Rock's
        scheduler and group screens read the stored result, not the rule.
    */

    /// <summary>
    /// Runs Rock's Calculate Group Requirements job on a loaded database and expects it to insert, delete, and change no
    /// requirement result. The job reads data views that count from today, so it is judged only when today is the anchor.
    /// </summary>
    internal static class GroupRequirementsCheck
    {
        private const string Columns = "[Id], [GroupMemberId], [GroupRequirementId], [GroupMemberRequirementState], [RequirementMetDateTime], [RequirementWarningDateTime], [RequirementFailDateTime], [WasOverridden], [WasManuallyCompleted], [DoesNotMeetWorkflowId], [WarningWorkflowId]";

        public static CheckResult Run( RockRelease rock, SqlServer sql, string database, DateTime anchor )
        {
            if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
            {
                return CheckResult.From( "group-requirements", "Group requirements were not judged: today's date is not the loads' anchor", new List<string>
                {
                    $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. Rock's requirement data views count from today, so run it again on one day."
                } );
            }

            var before = Results( sql, database );
            var output = RockRunner.Run( rock, sql, database, "group-requirements" );
            var after = Results( sql, database );

            var problems = new List<string>();
            foreach ( var (id, row) in after.Where( r => !before.ContainsKey( r.Key ) ) )
            {
                problems.Add( $"Rock inserted a result for {Who( sql, database, row )}: {StateName( row["GroupMemberRequirementState"] )}." );
            }

            foreach ( var (id, row) in before.Where( r => !after.ContainsKey( r.Key ) ) )
            {
                problems.Add( $"Rock deleted the result for {Who( sql, database, row )}, which was {StateName( row["GroupMemberRequirementState"] )}." );
            }

            foreach ( var (id, row) in before.Where( r => after.ContainsKey( r.Key ) ) )
            {
                var changed = row.Keys.Where( k => !Equals( row[k], after[id][k] ) ).ToList();
                if ( changed.Count > 0 )
                {
                    problems.Add( $"Rock changed {Who( sql, database, row )}: " + string.Join( ", ", changed.Select( k => $"{k} {Show( row[k] )} to {Show( after[id][k] )}" ) ) + "." );
                }
            }

            var states = before.Values.GroupBy( r => StateName( r["GroupMemberRequirementState"] ) ).OrderBy( g => g.Key ).Select( g => $"{g.Count():N0} {g.Key}" );
            var notes = new List<string> { $"Rock's job: {output.Trim().Replace( "\r", string.Empty ).Replace( "\n", " " )}" };
            return CheckResult.From( "group-requirements", problems.Count == 0
                    ? $"Rock's Calculate Group Requirements job changed none of the {before.Count:N0} requirement results ({string.Join( ", ", states )})"
                    : "Rock's Calculate Group Requirements job would change the requirement results",
                problems.Take( 25 ).Concat( problems.Count > 25 ? new[] { $"...and {problems.Count - 25:N0} more." } : Array.Empty<string>() ).ToList(), notes );
        }

        private static Dictionary<int, Dictionary<string, object>> Results( SqlServer sql, string database )
        {
            return sql.Query( database, $"SELECT {Columns} FROM [GroupMemberRequirement]" ).ToDictionary( r => Convert.ToInt32( r["Id"] ) );
        }

        private static string Who( SqlServer sql, string database, Dictionary<string, object> row )
        {
            var name = sql.Scalar( database, @"
SELECT ISNULL( [p].[NickName], [p].[FirstName] ) + ' ' + [p].[LastName] + ' in ' + [g].[Name]
FROM [GroupMember] AS [gm]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
WHERE [gm].[Id] = @Id", new Microsoft.Data.SqlClient.SqlParameter( "@Id", row["GroupMemberId"] ) ) as string;
            return name ?? $"group member {row["GroupMemberId"]}";
        }

        private static string StateName( object state ) => Convert.ToInt32( state ) switch
        {
            0 => "met",
            1 => "not met",
            _ => "met with a warning"
        };

        private static string Show( object value ) => value is DateTime date ? date.ToString( "yyyy-MM-dd HH:mm" ) : value is DBNull or null ? "none" : value.ToString();
    }
}
