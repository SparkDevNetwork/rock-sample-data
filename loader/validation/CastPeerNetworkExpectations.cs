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
    /// Checks the Cast's peer network stories against the loaded database (decision 332): the people each story's
    /// profile must show rising, and that every Cast person who left a group sees its people falling.
    /// </summary>
    internal static class CastPeerNetworkExpectations
    {
        private static readonly Guid GroupConnectionTypeGuid = new Guid( "CB51DC46-FBDB-43DA-B7F3-60E7C6E70F40" );

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            if ( source.PeerNetwork == null )
            {
                return 0;
            }

            var checks = 0;
            foreach ( var household in source.ReadHouseholds() )
            {
                foreach ( var person in household.Members.Where( m => m.Expect?.PeerNetwork != null ) )
                {
                    var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                    var expect = person.Expect.PeerNetwork;
                    var rows = Rows( db, "[s].[Guid] = @Guid", P( "@Guid", guids.Value( person.Guid ) ) );

                    foreach ( var key in expect.Includes )
                    {
                        checks++;
                        var other = household.Members.FirstOrDefault( m => m.Key == key );
                        var shown = other == null ? new List<PeerRow>() : rows.Where( r => r.TargetGuid == guids.Rock( other.Guid ) ).ToList();
                        if ( shown.Count == 0 || !shown.Any( r => r.Trend > 0 ) )
                        {
                            failures.Add( $"{name}: expected {other?.NickName ?? other?.FirstName ?? key} in their peer network, rising, Rock shows {( shown.Count == 0 ? "no row" : "no rising row" )}" );
                        }
                    }

                    if ( expect.CurrentGroupsRising )
                    {
                        checks++;
                        var peers = CurrentGroupPeers( db, guids.Value( person.Guid ) );
                        var missing = peers.Where( p => !rows.Any( r => r.TargetGuid == p.TargetGuid && r.GroupId == p.GroupId && r.Trend > 0 ) ).ToList();
                        if ( peers.Count == 0 || missing.Count > 0 )
                        {
                            failures.Add( peers.Count == 0
                                ? $"{name}: expected people from their current groups in their peer network, Rock shows they share no group with Peer Network on"
                                : $"{name}: expected each of the {peers.Count} people in their current groups in their peer network, rising, Rock shows {missing.Count} of them missing or not rising ({string.Join( ", ", missing.Take( 3 ).Select( m => m.Name ) )})" );
                        }
                    }
                }
            }

            // Every Cast person who left a group, or whose group closed, sees its people falling, and at least one has.
            checks++;
            var castGuids = source.ReadHouseholds().SelectMany( h => h.Members ).Select( m => guids.Rock( m.Guid ) ).ToHashSet();
            var left = Rows( db, @"[r].[RelationshipTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @GroupConnection ) AND [r].[RelationshipEndDate] IS NOT NULL",
                P( "@GroupConnection", GroupConnectionTypeGuid ) ).Where( r => castGuids.Contains( r.SourceGuid ) ).ToList();
            // A relationship that began after the run before the last one is new at the last run, so Rock shows it rising.
            var notFalling = left.Where( r => r.Trend >= 0 && r.Previous > 0 ).ToList();
            if ( left.Count == 0 || notFalling.Count > 0 )
            {
                failures.Add( left.Count == 0
                    ? "Cast peer networks: expected a Cast person who left a group to see its people falling, Rock shows no Cast person with an ended group relationship"
                    : $"Cast peer networks: expected every ended group relationship of a Cast person to fall, Rock shows {notFalling.Count} of {left.Count} not falling (first: {notFalling[0].SourceName} and {notFalling[0].Name} in {notFalling[0].Caption})" );
            }

            return checks;
        }

        private sealed record PeerRow( Guid SourceGuid, string SourceName, Guid TargetGuid, string Name, int? GroupId, string Caption, decimal Score, decimal Previous, int Trend );

        private sealed record GroupPeer( Guid TargetGuid, string Name, int GroupId );

        /// <summary>
        /// The PeerNetwork rows that match a filter on the row [r] and its source person [s].
        /// </summary>
        private static List<PeerRow> Rows( RockDatabase db, string filter, params Microsoft.Data.SqlClient.SqlParameter[] parameters )
        {
            return db.Query( $@"
SELECT [s].[Guid] AS [SourceGuid], [s].[NickName] + ' ' + [s].[LastName] AS [SourceName], [t].[Guid] AS [TargetGuid], [t].[NickName] + ' ' + [t].[LastName] AS [Name],
    [r].[RelatedEntityId], [r].[Caption], [r].[RelationshipScore], [r].[RelationshipScoreLastUpdateValue], [r].[RelationshipTrend]
FROM [PeerNetwork] AS [r]
INNER JOIN [Person] AS [s] ON [s].[Id] = [r].[SourcePersonId]
INNER JOIN [Person] AS [t] ON [t].[Id] = [r].[TargetPersonId]
WHERE {filter}", parameters )
                .Select( r => new PeerRow( ( Guid ) r["SourceGuid"], ( string ) r["SourceName"], ( Guid ) r["TargetGuid"], ( string ) r["Name"], r["RelatedEntityId"] as int?, r["Caption"] as string,
                    ( decimal ) r["RelationshipScore"], ( decimal ) r["RelationshipScoreLastUpdateValue"], ( int ) r["RelationshipTrend"] ) )
                .ToList();
        }

        /// <summary>
        /// Everyone the person shares a current group with, in groups whose type has Peer Network on: both are active
        /// members of an active group, in roles Peer Network counts.
        /// </summary>
        private static List<GroupPeer> CurrentGroupPeers( RockDatabase db, object personGuid )
        {
            return db.Query( @"
SELECT DISTINCT [t].[Guid], [t].[NickName] + ' ' + [t].[LastName] AS [Name], [g].[Id] AS [GroupId]
FROM [GroupMember] AS [sm]
INNER JOIN [Person] AS [s] ON [s].[Id] = [sm].[PersonId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [sm].[GroupId] AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId] AND [gt].[IsPeerNetworkEnabled] = 1
INNER JOIN [GroupTypeRole] AS [sr] ON [sr].[Id] = [sm].[GroupRoleId] AND [sr].[IsExcludedFromPeerNetwork] = 0
INNER JOIN [GroupMember] AS [tm] ON [tm].[GroupId] = [g].[Id] AND [tm].[PersonId] <> [sm].[PersonId] AND [tm].[IsArchived] = 0 AND [tm].[GroupMemberStatus] <> 0
INNER JOIN [GroupTypeRole] AS [tr] ON [tr].[Id] = [tm].[GroupRoleId] AND [tr].[IsExcludedFromPeerNetwork] = 0
INNER JOIN [Person] AS [t] ON [t].[Id] = [tm].[PersonId]
WHERE [s].[Guid] = @Guid AND [sm].[IsArchived] = 0 AND [sm].[GroupMemberStatus] <> 0", P( "@Guid", personGuid ) )
                .Select( r => new GroupPeer( ( Guid ) r["Guid"], ( string ) r["Name"], ( int ) r["GroupId"] ) )
                .ToList();
        }
    }
}
