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

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/21/26 - CLAUDE

        The sample church uses only the security roles Rock ships. A role is a group, so membership is
        a GroupMember row in the role's group, with the Security Role group type's default role, as
        Rock's role editor adds it. Security role groups keep no history, so nothing else is written.

        Reason: Cast staff can sign in and see what their roles allow, such as notes (decision 79).
    */

    /// <summary>
    /// Adds people to the security roles Rock ships.
    /// </summary>
    internal sealed class SecurityRolesLoader
    {
        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly DateTime _now = DateTime.Now;
        private readonly HashSet<(int GroupId, int PersonId)> _members = new HashSet<(int, int)>();
        private readonly Dictionary<Guid, (int GroupId, int GroupTypeId, int RoleId)> _roles = new Dictionary<Guid, (int, int, int)>();

        public SecurityRolesLoader( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Adds each Cast person to the roles their securityRoles list names. Returns the number of memberships.
        /// </summary>
        public int LoadCast( SampleDataSource source, IEnumerable<LoadedPerson> people )
        {
            var configuration = source.SecurityRoles;
            if ( configuration == null )
            {
                return 0;
            }

            var roles = configuration.Roles.ToDictionary( r => r.Name, r => r.Guid );
            var notRoles = configuration.NotRoles.Select( r => r.Name ).ToHashSet();
            var count = 0;
            foreach ( var person in people )
            {
                foreach ( var name in person.Cast.SecurityRoles ?? new List<string>() )
                {
                    if ( notRoles.Contains( name ) )
                    {
                        continue;
                    }

                    if ( !roles.TryGetValue( name, out var guid ) )
                    {
                        throw new InvalidOperationException( $"{person.Cast.Key} lists security role \"{name}\", which configuration/security-roles.json does not map to a role Rock ships." );
                    }

                    count += Add( person.PersonId, guid ) ? 1 : 0;
                }
            }

            return count;
        }

        /// <summary>
        /// Adds a person to the Rock security role with this group Guid. Returns false when they are already in it.
        /// </summary>
        public bool Add( int personId, Guid roleGroupGuid )
        {
            var role = Role( roleGroupGuid );
            if ( !_members.Add( (role.GroupId, personId) ) )
            {
                return false;
            }

            _db.Execute( @"
INSERT INTO [GroupMember] ( [IsSystem], [GroupId], [GroupTypeId], [PersonId], [GroupRoleId], [GroupMemberStatus], [DateTimeAdded], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @GroupId, @GroupTypeId, @PersonId, @GroupRoleId, 1, @Now, NEWID(), @ForeignKey, @Now, @Now )",
                P( "@GroupId", role.GroupId ), P( "@GroupTypeId", role.GroupTypeId ), P( "@PersonId", personId ), P( "@GroupRoleId", role.RoleId ),
                P( "@Now", _now ), P( "@ForeignKey", _foreignKey ) );
            return true;
        }

        /// <summary>
        /// A role group's Id, group type, and the group type's default role. The group must already be in Rock.
        /// </summary>
        private (int GroupId, int GroupTypeId, int RoleId) Role( Guid guid )
        {
            if ( _roles.TryGetValue( guid, out var role ) )
            {
                return role;
            }

            var row = _db.Query( @"
SELECT [g].[Id], [g].[GroupTypeId], [gt].[DefaultGroupRoleId]
FROM [Group] AS [g]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [g].[Guid] = @Guid AND [g].[IsSecurityRole] = 1", P( "@Guid", guid ) ).SingleOrDefault()
                ?? throw new InvalidOperationException( $"Security role {guid} is not in this Rock. The sample church uses only the roles Rock ships." );

            role = (Convert.ToInt32( row["Id"] ), Convert.ToInt32( row["GroupTypeId"] ), Convert.ToInt32( row["DefaultGroupRoleId"] ));
            _roles[guid] = role;
            return role;
        }
    }
}
