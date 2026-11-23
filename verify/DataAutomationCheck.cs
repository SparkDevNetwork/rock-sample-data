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

using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

namespace RockSampleData.Verify
{
    /*
        11/23/26 - CLAUDE

        The load saves Rock's six Data Automation rules, configured and off (decision D-integrity-7). Rock reads each from
        JSON in its system setting, and a property it does not know reads as the class default with no error. So the
        runner reads each rule with Rock's own settings class and writes it back with Rock's own serializer, and the check
        expects exactly what the load saved. Then the runner runs Rock's Data Automation job, as Rock's scheduler runs it
        every Tuesday at 4 AM, and the check expects it to change no person and no family. With every rule off, the job
        runs only its gender auto-fill, which must find no one it can fill.

        Reason: Rock itself proves the rules read as configured and that the job's first run changes nothing.
    */

    /// <summary>
    /// Reads each Data Automation rule back through Rock and runs Rock's Data Automation job, expecting the rules exactly
    /// as saved, all off, and no change to any person or family.
    /// </summary>
    internal static class DataAutomationCheck
    {
        private const string PeopleSql = @"
SELECT [p].[Id], CONCAT( [p].[Gender], '|', [p].[RecordStatusValueId], '|', [p].[RecordStatusReasonValueId], '|', [p].[ReviewReasonValueId], '|', [p].[ConnectionStatusValueId], '|',
    [p].[PrimaryCampusId], '|', [p].[PrimaryFamilyId], '|', CONVERT( VARCHAR( 30 ), [p].[ModifiedDateTime], 126 ) ) AS [State], [p].[NickName] + ' ' + [p].[LastName] AS [Name]
FROM [Person] AS [p]";

        private const string FamiliesSql = @"
SELECT [g].[Id], CONCAT( [g].[CampusId], '|', [g].[StatusValueId], '|', [g].[IsActive], '|', CONVERT( VARCHAR( 30 ), [g].[ModifiedDateTime], 126 ), '|',
    ( SELECT STRING_AGG( CONCAT( [gm].[PersonId], ':', [gm].[GroupRoleId], ':', [gm].[GroupMemberStatus] ), ',' ) WITHIN GROUP ( ORDER BY [gm].[PersonId] ) FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [g].[Id] ) ) AS [State],
    [g].[Name]
FROM [Group] AS [g]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId] AND [gt].[Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E'";

        public static CheckResult Run( RockRelease rock, SqlServer sql, string database, string foreignKey )
        {
            var saved = sql.Query( database, "SELECT [Key], [DefaultValue] FROM [Attribute] WHERE [EntityTypeId] IS NULL AND [EntityTypeQualifierColumn] = 'SystemSetting' AND [Key] LIKE 'core[_]DataAutomation%' AND [ForeignKey] = @ForeignKey",
                    new SqlParameter( "@ForeignKey", foreignKey ) )
                .ToDictionary( r => ( string ) r["Key"], r => ( string ) r["DefaultValue"] );
            if ( saved.Count == 0 )
            {
                return CheckResult.From( "data-automation", "This load saved no Data Automation rules", new List<string>() );
            }

            var people = State( sql, database, PeopleSql );
            var families = State( sql, database, FamiliesSql );
            var output = RockRunner.Run( rock, sql, database, "data-automation" ).Replace( "\r", string.Empty ).Split( '\n', StringSplitOptions.RemoveEmptyEntries );
            var peopleAfter = State( sql, database, PeopleSql );
            var familiesAfter = State( sql, database, FamiliesSql );

            var problems = new List<string>();
            var read = output.Where( l => l.StartsWith( "SETTING\t", StringComparison.Ordinal ) ).Select( l => l.Split( '\t', 3 ) ).ToDictionary( p => p[1], p => p[2] );
            foreach ( var (key, json) in saved )
            {
                if ( !read.TryGetValue( key, out var rockJson ) || rockJson == "unreadable" )
                {
                    problems.Add( $"Rock could not read {key} with its settings class." );
                }
                else if ( !JsonNode.DeepEquals( JsonNode.Parse( json ), JsonNode.Parse( rockJson ) ) )
                {
                    problems.Add( $"Rock reads {key} differently than the load saved it. Saved: {json} Rock: {rockJson}" );
                }
                else if ( JsonNode.Parse( rockJson )?["IsEnabled"]?.GetValue<bool>() != false )
                {
                    problems.Add( $"Rock reads {key} as on." );
                }
            }

            problems.AddRange( Changes( people, peopleAfter, "person" ) );
            problems.AddRange( Changes( families, familiesAfter, "family" ) );
            var result = output.FirstOrDefault( l => l.StartsWith( "RESULT\t", StringComparison.Ordinal ) )?.Substring( 7 ) ?? "no result";
            return CheckResult.From( "data-automation", problems.Count == 0
                ? $"Rock reads all {saved.Count} Data Automation rules as saved, all off, and its Data Automation job changed none of {people.Count:N0} people and {families.Count:N0} families ({result})"
                : "Rock's Data Automation rules or job do not match the load", problems.Take( 20 ).ToList() );
        }

        private static Dictionary<int, (string State, string Name)> State( SqlServer sql, string database, string query )
        {
            return sql.Query( database, query ).ToDictionary( r => ( int ) r["Id"], r => ( ( string ) r["State"], r["Name"] as string ) );
        }

        private static IEnumerable<string> Changes( Dictionary<int, (string State, string Name)> before, Dictionary<int, (string State, string Name)> after, string kind )
        {
            return before.Keys.Except( after.Keys ).Select( id => $"Rock's Data Automation job removed the {kind} {before[id].Name}." )
                .Concat( after.Keys.Except( before.Keys ).Select( id => $"Rock's Data Automation job added the {kind} {after[id].Name}." ) )
                .Concat( before.Keys.Intersect( after.Keys ).Where( id => before[id].State != after[id].State )
                    .Select( id => $"Rock's Data Automation job changed the {kind} {after[id].Name}: was {before[id].State}, now {after[id].State}." ) );
        }
    }
}
