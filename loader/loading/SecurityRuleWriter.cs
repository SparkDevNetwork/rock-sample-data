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

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Writes security rules on a category, data view, or report as Rock's security editor saves them: for each action,
    /// each allowed role in order, then "deny All Users" when the action is locked to those roles.
    /// </summary>
    internal sealed class SecurityRuleWriter
    {
        private readonly RockDatabase _db;
        private readonly SecurityRoleConfiguration _roles;
        private readonly string _foreignKey;

        public SecurityRuleWriter( RockDatabase db, SecurityRoleConfiguration roles, string foreignKey )
        {
            _db = db;
            _roles = roles;
            _foreignKey = foreignKey;
        }

        /// <param name="entityType">The secured item's entity, such as Rock.Model.Category.</param>
        /// <param name="what">The item, for messages.</param>
        public void Write( string entityType, int entityId, List<CategorySecurityRule> rules, DateTime now, string what )
        {
            if ( rules == null || rules.Count == 0 )
            {
                return;
            }

            var roles = _roles?.Roles
                ?? throw new InvalidDataException( $"{what} has security, but this package has no configuration/security-roles.json." );
            var entityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", entityType ) ) );
            foreach ( var rule in rules )
            {
                var order = 0;
                foreach ( var roleName in rule.Allow )
                {
                    var role = roles.FirstOrDefault( r => r.RockRole == roleName )
                        ?? throw new InvalidDataException( $"{what} allows {roleName}, which is not a role in configuration/security-roles.json." );
                    Insert( entityTypeId, entityId, order++, rule.Action, "A", 0, _db.RequireIdByGuid( "Group", role.Guid ), now );
                }

                if ( rule.DenyAllUsers )
                {
                    Insert( entityTypeId, entityId, order, rule.Action, "D", 1, null, now );
                }
            }
        }

        private void Insert( int entityTypeId, int entityId, int order, string action, string allowOrDeny, int specialRole, int? groupId, DateTime now )
        {
            _db.Execute( @"
INSERT INTO [Auth] ( [EntityTypeId], [EntityId], [Order], [Action], [AllowOrDeny], [SpecialRole], [GroupId], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( @EntityTypeId, @EntityId, @Order, @Action, @AllowOrDeny, @SpecialRole, @GroupId, NEWID(), @Now, @Now, @ForeignKey )",
                P( "@EntityTypeId", entityTypeId ), P( "@EntityId", entityId ), P( "@Order", order ), P( "@Action", action ), P( "@AllowOrDeny", allowOrDeny ),
                P( "@SpecialRole", specialRole ), P( "@GroupId", groupId ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
        }
    }
}
