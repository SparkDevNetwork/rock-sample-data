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

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Rock keeps no history of who was a record when, so a metric's past runs count today's people whose family had
        come by that day (decision 347). The homepage metrics, the Insights metrics, and verify's check of both read this
        one rule, so they agree on who was here at any run.

        Reason: One rule for "who was here then" across every metric with history.
    */

    /// <summary>
    /// The day each family first came: its first check-in, gift, request to serve, or group membership. A family with
    /// none has always been there, and has no row. Rock's relationship groups, Known Relationships and Peer Network, are
    /// left out: Rock makes one for every person, and the load adds each on install day, so they would make every family
    /// that never checked in, gave, or asked to serve look new today.
    /// </summary>
    internal static class FamilyArrivals
    {
        private static readonly Guid FamilyGroupTypeGuid = new Guid( "790E3215-3B10-442B-AF69-616C0DCB998E" );
        private static readonly Guid KnownRelationshipsGroupTypeGuid = new Guid( "E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF" );
        private static readonly Guid PeerNetworkGroupTypeGuid = new Guid( "8C0E5852-F08F-4327-9AA5-87800A6AB53E" );

        /// <summary>
        /// A query with two columns, the family's GroupId and the moment it came, At. It needs <see cref="Parameters"/>.
        /// It is one SELECT, so a caller can put INSERT INTO a temp table in front of it.
        /// </summary>
        public const string Sql = @"
SELECT [fm].[GroupId], MIN( [f].[At] ) AS [At]
FROM [GroupMember] AS [fm]
INNER JOIN (
    SELECT [pa].[PersonId], MIN( [a].[StartDateTime] ) AS [At]
    FROM [Attendance] AS [a] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    WHERE [a].[DidAttend] = 1
    GROUP BY [pa].[PersonId]
    UNION ALL
    SELECT [pa].[PersonId], MIN( [t].[TransactionDateTime] )
    FROM [FinancialTransaction] AS [t] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [t].[AuthorizedPersonAliasId]
    GROUP BY [pa].[PersonId]
    UNION ALL
    SELECT [pa].[PersonId], MIN( [r].[CreatedDateTime] )
    FROM [ConnectionRequest] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
    GROUP BY [pa].[PersonId]
    UNION ALL
    SELECT [gm].[PersonId], MIN( [gm].[DateTimeAdded] )
    FROM [GroupMember] AS [gm]
    WHERE [gm].[GroupTypeId] <> ( SELECT [Id] FROM [GroupType] WHERE [Guid] = @ArrivalFamilyType )
        AND [gm].[GroupTypeId] NOT IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( @ArrivalKnownRelationships, @ArrivalPeerNetwork ) )
    GROUP BY [gm].[PersonId]
) AS [f] ON [f].[PersonId] = [fm].[PersonId]
WHERE [fm].[GroupTypeId] = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = @ArrivalFamilyType )
GROUP BY [fm].[GroupId]";

        /// <summary>
        /// The parameters <see cref="Sql"/> needs. Each call makes new ones, since a parameter joins one command only.
        /// </summary>
        public static SqlParameter[] Parameters() => new[]
        {
            new SqlParameter( "@ArrivalFamilyType", FamilyGroupTypeGuid ),
            new SqlParameter( "@ArrivalKnownRelationships", KnownRelationshipsGroupTypeGuid ),
            new SqlParameter( "@ArrivalPeerNetwork", PeerNetworkGroupTypeGuid )
        };
    }
}
