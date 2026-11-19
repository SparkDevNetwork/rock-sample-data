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
    /// Checks the Cast's background check stories in configuration/background-checks.json against the loaded database:
    /// the latest check's result, its report, its link to the person's request to serve, the person attributes it set,
    /// and the small group leader requirement's result.
    /// </summary>
    internal static class CastBackgroundCheckExpectations
    {
        private static readonly string[] StateNames = { "Met", "NotMet", "MetWithWarning" };

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var config = source.BackgroundChecks;
            if ( config == null )
            {
                return 0;
            }

            var checks = 0;
            foreach ( var story in config.CastExpectations )
            {
                var personId = db.Scalar( "SELECT [Id] FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", guids.Rock( story.Person ) ) );
                if ( personId == null )
                {
                    continue;
                }

                var latest = db.Query( @"
SELECT TOP 1 [bc].[Status], [bc].[RecordFound], [bc].[ResponseDocumentId], [bc].[ConnectionRequestId], CAST( [bc].[ModifiedDateTime] AS DATE ) AS [Finished]
FROM [BackgroundCheck] AS [bc]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [bc].[PersonAliasId]
WHERE [pa].[PersonId] = @PersonId
ORDER BY [bc].[RequestDate] DESC", P( "@PersonId", personId ) ).FirstOrDefault();

                checks++;
                var shown = latest == null ? "None"
                    : ( string ) latest["Status"] != "ReportCompleted" ? "InProgress"
                    : latest["RecordFound"] is true ? "Fail" : "Pass";
                if ( shown != story.Check )
                {
                    failures.Add( $"{story.Name}: expected a background check of {story.Check}, Rock shows {shown}" );
                    continue;
                }

                if ( latest == null )
                {
                    continue;
                }

                if ( shown is "Pass" or "Fail" )
                {
                    checks++;
                    var values = db.Query( @"
SELECT [a].[Key], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
WHERE [av].[EntityId] = @PersonId AND [a].[Key] IN ( 'BackgroundChecked', 'BackgroundCheckDate', 'BackgroundCheckResult' )
    AND [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' )", P( "@PersonId", personId ) )
                        .ToDictionary( r => ( string ) r["Key"], r => ( string ) r["Value"] );
                    var date = values.TryGetValue( "BackgroundCheckDate", out var text ) && DateTime.TryParse( text, out var parsed ) ? parsed.Date : ( DateTime? ) null;
                    if ( latest["ResponseDocumentId"] is not int || values.GetValueOrDefault( "BackgroundCheckResult" ) != shown
                        || values.GetValueOrDefault( "BackgroundChecked" ) != ( shown == "Pass" ? "True" : "False" ) || date != ( DateTime ) latest["Finished"] )
                    {
                        failures.Add( $"{story.Name}: expected a {shown} report and the person attributes it sets, Rock shows "
                            + $"{( latest["ResponseDocumentId"] is int ? "a report" : "no report" )}, Background Checked {values.GetValueOrDefault( "BackgroundChecked" ) ?? "none"}, "
                            + $"Result {values.GetValueOrDefault( "BackgroundCheckResult" ) ?? "none"}, Date {date?.ToString( "yyyy-MM-dd" ) ?? "none"}" );
                    }
                }

                checks++;
                var requestId = db.Scalar( @"
SELECT TOP 1 [cr].[Id]
FROM [ConnectionRequest] AS [cr]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
INNER JOIN [ConnectionStatus] AS [cs] ON [cs].[Id] = [cr].[ConnectionStatusId]
WHERE [pa].[PersonId] = @PersonId AND [cs].[Guid] = @StatusGuid AND [cr].[ConnectionState] = 0", P( "@PersonId", personId ), P( "@StatusGuid", config.Requests.StatusGuid ) );
                var isLinked = requestId != null && Equals( latest["ConnectionRequestId"], requestId );
                if ( isLinked != story.LinkedToRequest )
                {
                    failures.Add( story.LinkedToRequest
                        ? $"{story.Name}: expected the check linked to their request to serve, which waits on it, Rock shows {( requestId == null ? "no such request" : "it unlinked" )}"
                        : $"{story.Name}: expected the check not to be linked to a request to serve" );
                }
            }

            foreach ( var story in config.CastExpectations.Where( s => s.LeaderRequirement != null ) )
            {
                checks++;
                var states = db.Query( @"
SELECT [gmr].[GroupMemberRequirementState]
FROM [GroupMemberRequirement] AS [gmr]
INNER JOIN [GroupRequirement] AS [gr] ON [gr].[Id] = [gmr].[GroupRequirementId]
INNER JOIN [GroupMember] AS [gm] ON [gm].[Id] = [gmr].[GroupMemberId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gr].[Guid] = @RequirementGuid AND [p].[Guid] = @PersonGuid", P( "@RequirementGuid", config.LeaderRequirement.Guid ), P( "@PersonGuid", guids.Rock( story.Person ) ) )
                    .Select( r => StateNames[Convert.ToInt32( r["GroupMemberRequirementState"] )] )
                    .Distinct()
                    .ToList();
                if ( states.Count != 1 || states[0] != story.LeaderRequirement )
                {
                    failures.Add( $"{story.Name}: expected the small group leader requirement {story.LeaderRequirement}, Rock shows {( states.Count == 0 ? "no leader requirement" : string.Join( " and ", states ) )}" );
                }
            }

            return checks;
        }
    }
}
