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

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the Cast's benevolence requests in configuration/benevolence.json against the loaded database: who asked,
    /// the case worker, the type, the day, the status, each result's type and amount, and the signed agreement.
    /// </summary>
    internal static class CastBenevolenceExpectations
    {
        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var config = source.Benevolence;
            if ( config == null )
            {
                return 0;
            }

            var checks = 0;
            foreach ( var story in config.Cast )
            {
                var request = db.Query( @"
SELECT [r].[Id], [p].[Guid] AS [Requester], [cw].[Guid] AS [CaseWorker], [t].[Guid] AS [Type], [s].[Value] AS [Status], [r].[RequestDateTime]
FROM [BenevolenceRequest] AS [r]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[RequestedByPersonAliasId]
LEFT JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
LEFT JOIN [PersonAlias] AS [cwa] ON [cwa].[Id] = [r].[CaseWorkerPersonAliasId]
LEFT JOIN [Person] AS [cw] ON [cw].[Id] = [cwa].[PersonId]
INNER JOIN [BenevolenceType] AS [t] ON [t].[Id] = [r].[BenevolenceTypeId]
INNER JOIN [DefinedValue] AS [s] ON [s].[Id] = [r].[RequestStatusValueId]
WHERE [r].[Guid] = @Guid", P( "@Guid", story.Guid ) ).FirstOrDefault();

                checks++;
                if ( request == null )
                {
                    failures.Add( $"{story.Name}: expected the {story.Key} benevolence request, Rock has none" );
                    continue;
                }

                var type = config.Types.Single( t => t.Key == story.Type );
                var problems = new List<string>();
                if ( !Equals( request["Requester"], guids.Rock( story.Requester ) ) )
                {
                    problems.Add( "another requester" );
                }

                if ( !Equals( request["CaseWorker"], guids.Rock( config.CaseWorker.Person ) ) )
                {
                    problems.Add( $"a case worker other than {config.CaseWorker.Name}" );
                }

                if ( !Equals( request["Type"], type.Guid ) )
                {
                    problems.Add( $"a type other than {type.Name}" );
                }

                if ( ( string ) request["Status"] != story.Status )
                {
                    problems.Add( $"status {request["Status"]}, not {story.Status}" );
                }

                var results = db.Query( @"
SELECT [dv].[Value] AS [Type], [res].[Amount]
FROM [BenevolenceResult] AS [res]
INNER JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [res].[ResultTypeValueId]
WHERE [res].[BenevolenceRequestId] = @Id
ORDER BY [res].[Id]", P( "@Id", request["Id"] ) )
                    .Select( r => $"{r["Type"]} {( r["Amount"] is decimal amount ? amount.ToString( "0.00" ) : "no amount" )}" )
                    .ToList();
                var expected = story.Results.Select( r => $"{r.Type} {( r.Amount.HasValue ? r.Amount.Value.ToString( "0.00" ) : "no amount" )}" ).ToList();
                if ( !results.SequenceEqual( expected ) )
                {
                    problems.Add( $"results {( results.Count == 0 ? "none" : string.Join( ", ", results ) )}, not {string.Join( ", ", expected )}" );
                }

                var agreements = Convert.ToInt32( db.Scalar( "SELECT COUNT(*) FROM [BenevolenceRequestDocument] WHERE [BenevolenceRequestId] = @Id", P( "@Id", request["Id"] ) ) );
                if ( ( agreements > 0 ) != story.Agreement )
                {
                    problems.Add( story.Agreement ? "no signed agreement" : "an agreement the story does not have" );
                }

                if ( problems.Count > 0 )
                {
                    failures.Add( $"{story.Name}: the {story.Key} benevolence request shows {string.Join( "; ", problems )}" );
                }
            }

            return checks;
        }
    }
}
