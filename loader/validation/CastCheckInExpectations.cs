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
    /// Checks the Cast's known relationships and check-in stories (decisions 365 and 366):
    /// each line shows on both people, a kiosk search for each family that may check in a child from another family
    /// finds the child, and a child someone brings on Sundays has check-ins that person made.
    /// </summary>
    internal static class CastCheckInExpectations
    {
        private static readonly Guid OwnerRoleGuid = new Guid( "7BC6C12E-0CD1-4DFD-8D5B-1B35AE714C42" );
        private static readonly Guid FamilyGroupTypeGuid = new Guid( "790E3215-3B10-442B-AF69-616C0DCB998E" );

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, bool isAttendanceLoaded, List<string> failures )
        {
            var members = source.ReadHouseholds().Where( h => h.Kind != "business" ).SelectMany( h => h.Members ).ToDictionary( m => m.Guid );
            string Name( Guid guid ) => members.TryGetValue( guid, out var m ) ? $"{m.NickName ?? m.FirstName} {m.LastName}" : guid.ToString();
            bool IsLoaded( Guid guid ) => db.Scalar( "SELECT 1 FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", guids.Value( guid ) ) ) != null;

            var checks = 0;
            foreach ( var owner in members.Values.Where( m => IsLoaded( m.Guid ) ) )
            {
                foreach ( var line in owner.Relationships.Where( r => r.Type != "business" ) )
                {
                    var (role, inverse) = RelationshipsLoader.Roles[line.Type];
                    checks++;
                    if ( !HasRelationship( db, guids.Value( owner.Guid ), guids.Value( line.With ), role ) || !HasRelationship( db, guids.Value( line.With ), guids.Value( owner.Guid ), inverse ) )
                    {
                        failures.Add( $"{Name( owner.Guid )}: expected {Name( line.With )} as \"{line.Type}\" in their Known Relationships, and the other half on {Name( line.With )}" );
                    }

                    if ( line.Type is "can-check-in" or "grandchild" )
                    {
                        checks++;
                        if ( !KioskSearchFinds( db, guids.Value( owner.Guid ), guids.Value( line.With ) ) )
                        {
                            failures.Add( $"{Name( owner.Guid )}: expected a kiosk search for their family to show {Name( line.With )}, Rock's search does not" );
                        }
                    }

                    if ( line.BringsPercent.HasValue && isAttendanceLoaded )
                    {
                        checks++;
                        var count = Convert.ToInt32( db.Scalar( @"
SELECT COUNT(*)
FROM [Attendance] AS [a]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [PersonAlias] AS [by] ON [by].[Id] = [a].[CheckedInByPersonAliasId]
WHERE [pa].[PersonId] = ( SELECT [Id] FROM [Person] WHERE [Guid] = @Child )
    AND [by].[PersonId] = ( SELECT [Id] FROM [Person] WHERE [Guid] = @Adult )
    AND [a].[DidAttend] = 1",
                            P( "@Child", guids.Value( line.With ) ), P( "@Adult", guids.Value( owner.Guid ) ) ) );
                        if ( count == 0 )
                        {
                            failures.Add( $"{Name( owner.Guid )}: expected to have checked in {Name( line.With )} on some Sundays, Rock shows none" );
                        }
                    }
                }
            }

            return checks;
        }

        /// <summary>
        /// True when the member holds the role in the owner's Known Relationships group.
        /// </summary>
        private static bool HasRelationship( RockDatabase db, object ownerGuid, object memberGuid, Guid role )
        {
            return db.Scalar( @"
SELECT 1
FROM [GroupMember] AS [own]
INNER JOIN [GroupMember] AS [m] ON [m].[GroupId] = [own].[GroupId]
WHERE [own].[PersonId] = ( SELECT [Id] FROM [Person] WHERE [Guid] = @Owner )
    AND [own].[GroupRoleId] = ( SELECT [Id] FROM [GroupTypeRole] WHERE [Guid] = @OwnerRole )
    AND [m].[PersonId] = ( SELECT [Id] FROM [Person] WHERE [Guid] = @Member )
    AND [m].[GroupRoleId] = ( SELECT [Id] FROM [GroupTypeRole] WHERE [Guid] = @Role )",
                P( "@Owner", ownerGuid ), P( "@OwnerRole", OwnerRoleGuid ), P( "@Member", memberGuid ), P( "@Role", role ) ) != null;
        }

        /// <summary>
        /// Rock's kiosk family search (DefaultSearchProvider): anyone active in a family member's Known Relationships group
        /// whose role Rock marks Can Check-in shows with the family. True when the adult's family search finds the child.
        /// </summary>
        private static bool KioskSearchFinds( RockDatabase db, object adultGuid, object childGuid )
        {
            return db.Scalar( @"
SELECT 1
FROM [GroupMember] AS [adult]
INNER JOIN [GroupMember] AS [family] ON [family].[GroupId] = [adult].[GroupId]
INNER JOIN [GroupMember] AS [own] ON [own].[PersonId] = [family].[PersonId] AND [own].[GroupRoleId] = ( SELECT [Id] FROM [GroupTypeRole] WHERE [Guid] = @OwnerRole )
INNER JOIN [GroupMember] AS [m] ON [m].[GroupId] = [own].[GroupId] AND [m].[GroupMemberStatus] = 1
INNER JOIN [AttributeValue] AS [canCheckIn] ON [canCheckIn].[EntityId] = [m].[GroupRoleId] AND [canCheckIn].[Value] = 'True'
INNER JOIN [Attribute] AS [at] ON [at].[Id] = [canCheckIn].[AttributeId] AND [at].[Key] = 'CanCheckin'
    AND [at].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.GroupTypeRole' )
WHERE [adult].[PersonId] = ( SELECT [Id] FROM [Person] WHERE [Guid] = @Adult )
    AND [adult].[GroupTypeId] = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = @FamilyType )
    AND [m].[PersonId] = ( SELECT [Id] FROM [Person] WHERE [Guid] = @Child )",
                P( "@OwnerRole", OwnerRoleGuid ), P( "@Adult", adultGuid ), P( "@FamilyType", FamilyGroupTypeGuid ), P( "@Child", childGuid ) ) != null;
        }
    }
}
