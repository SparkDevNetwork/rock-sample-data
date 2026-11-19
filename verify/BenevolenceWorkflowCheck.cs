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

using Microsoft.Data.SqlClient;

using RockSampleData.Loader;

namespace RockSampleData.Verify
{
    /*
        11/19/26 - CLAUDE

        The Request Started workflow is not persisted, so a load leaves nothing of its runs to compare, and Rock's Entity
        Property Set action loads and saves the request in a context of its own, so a rolled-back launch cannot show what
        it did. The check therefore uses what verify loads are for: a throwaway copy. It clears the case worker on up to
        25 of the load's requests, as on a request entered without one, launches the workflow on each with Rock's own code,
        and reads back the case worker Rock saved.

        Reason: Decision D-benevolence-6. Only Rock's own run proves the type's entity type, Lava, and action work.
    */

    /// <summary>
    /// Launches the benevolence Request Started workflow with Rock's own code on the load's requests, each with its case
    /// worker cleared, and expects Rock to give every one to the configured case worker with no error.
    /// </summary>
    internal static class BenevolenceWorkflowCheck
    {
        public static CheckResult Run( RockRelease rock, SqlServer sql, string database, string foreignKey, SampleDataSource source )
        {
            var caseWorker = source.Benevolence?.CaseWorker?.Person;
            var requests = sql.Query( database, @"
SELECT TOP 25 [r].[Id], [bw].[Id] AS [TriggerId]
FROM [BenevolenceRequest] AS [r]
INNER JOIN [BenevolenceWorkflow] AS [bw] ON [bw].[BenevolenceTypeId] = [r].[BenevolenceTypeId] AND [bw].[TriggerType] = 0
WHERE [r].[ForeignKey] = @ForeignKey
ORDER BY [r].[Id]", new SqlParameter( "@ForeignKey", foreignKey ) );
            if ( caseWorker == null || requests.Count == 0 )
            {
                return CheckResult.From( "benevolence-workflow-launch", "This load wrote no benevolence requests with a Request Started workflow", new List<string>() );
            }

            var ids = string.Join( ",", requests.Select( r => Convert.ToInt32( r["Id"] ) ) );
            sql.Execute( database, $"UPDATE [BenevolenceRequest] SET [CaseWorkerPersonAliasId] = NULL WHERE [Id] IN ( {ids} )" );
            var errors = RockRunner.Run( rock, sql, database, "launch-benevolence-workflows", requests.Select( r => $"{r["Id"]}\t{r["TriggerId"]}" ) )
                .Split( '\n', StringSplitOptions.RemoveEmptyEntries )
                .Select( l => l.TrimEnd( '\r' ).Split( '\t' ) )
                .ToDictionary( p => p[0], p => p.Length > 1 ? p[1] : string.Empty );
            var saved = sql.Query( database, $@"
SELECT [r].[Id], [p].[Guid]
FROM [BenevolenceRequest] AS [r]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[CaseWorkerPersonAliasId]
LEFT JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [r].[Id] IN ( {ids} )" ).ToDictionary( r => r["Id"].ToString(), r => r["Guid"] as Guid? );

            var problems = new List<string>();
            foreach ( var request in requests )
            {
                var id = request["Id"].ToString();
                if ( !errors.TryGetValue( id, out var error ) )
                {
                    problems.Add( $"Rock launched nothing for benevolence request {id}." );
                }
                else if ( error.Length > 0 )
                {
                    problems.Add( $"Rock's Request Started workflow reported an error on request {id}: {error}" );
                }
                else if ( saved.GetValueOrDefault( id ) != caseWorker.Value )
                {
                    problems.Add( $"Rock's Request Started workflow left request {id} with case worker {saved.GetValueOrDefault( id )?.ToString() ?? "nobody"}." );
                }
            }

            return CheckResult.From( "benevolence-workflow-launch", problems.Count == 0
                    ? $"Rock's Request Started workflow gave all {requests.Count} launched requests, each without a case worker, to the case worker, with no error"
                    : "Rock's Request Started workflow does not give requests to the case worker", problems );
        }
    }
}
