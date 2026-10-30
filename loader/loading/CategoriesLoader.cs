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
    /// <summary>
    /// Adds the church's categories from configuration/categories.json (decision 185), parents before
    /// children, so every part can file its rows under them by Guid.
    /// </summary>
    internal sealed class CategoriesLoader
    {
        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;

        public CategoriesLoader( RockDatabase db, SampleDataSource source, string foreignKey )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Writes every category. Returns how many.
        /// </summary>
        public int Load()
        {
            var categories = _source.Categories?.Categories ?? new List<Model.CategoryDefinition>();
            var now = DateTime.Now;
            var ids = new Dictionary<string, int>();
            var written = 0;
            while ( written < categories.Count )
            {
                var ready = categories.Where( c => !ids.ContainsKey( c.Key ) && ( c.Parent == null || ids.ContainsKey( c.Parent ) ) ).ToList();
                if ( ready.Count == 0 )
                {
                    var stuck = categories.First( c => !ids.ContainsKey( c.Key ) );
                    throw new InvalidDataException( $"configuration/categories.json: category '{stuck.Key}' has parent '{stuck.Parent}', which is not a category, or the parents form a loop." );
                }

                foreach ( var category in ready )
                {
                    if ( category.RockShips )
                    {
                        ids[category.Key] = Rename( category, now );
                        WriteSecurity( category, ids[category.Key], now );
                        written++;
                        continue;
                    }

                    var entityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", category.EntityType ) )
                        ?? throw new InvalidDataException( $"configuration/categories.json: category '{category.Key}' holds {category.EntityType}, which this Rock has no entity type for." ) );
                    var qualifierColumn = category.QualifierGroupType.HasValue ? "GroupTypeId" : category.QualifierEntityType != null ? "EntityTypeId" : null;
                    var qualifierValue = category.QualifierGroupType.HasValue ? _db.RequireIdByGuid( "GroupType", category.QualifierGroupType.Value ).ToString()
                        : category.QualifierEntityType != null ? Convert.ToString( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", category.QualifierEntityType ) )
                            ?? throw new InvalidDataException( $"configuration/categories.json: category '{category.Key}' is for {category.QualifierEntityType}, which this Rock has no entity type for." ) )
                        : null;
                    int? parentId = category.Parent == null ? null : ids[category.Parent];

                    /*
                        10/28/26 - CLAUDE

                        Rock's category editor gives a new top-level category order 0, and a child the next order
                        after its siblings (CategoryDetail). The person attribute categories Rock ships are all
                        order 0, so an attribute category is too. Other categories keep the sibling count they
                        have always had, so their rows do not change.

                        Reason: An attribute category matches the ones Rock ships and its own editor.
                    */
                    var order = category.QualifierEntityType != null && parentId == null ? 0 : Convert.ToInt32( _db.Scalar( @"
SELECT COUNT(*)
FROM [Category]
WHERE [EntityTypeId] = @EntityTypeId AND ISNULL( [ParentCategoryId], 0 ) = ISNULL( @ParentId, 0 )",
                        P( "@EntityTypeId", entityTypeId ), P( "@ParentId", parentId ) ) );

                    ids[category.Key] = ( int ) _db.Scalar( @"
INSERT INTO [Category] ( [IsSystem], [ParentCategoryId], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Name], [IconCssClass], [Description],
    [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @ParentId, @EntityTypeId, @QualifierColumn, @QualifierValue, @Name, @Icon, @Description, @Order, @Guid, @ForeignKey, @Now, @Now )",
                        P( "@ParentId", parentId ), P( "@EntityTypeId", entityTypeId ), P( "@QualifierColumn", qualifierColumn ),
                        P( "@QualifierValue", qualifierValue ), P( "@Name", category.Name ), P( "@Icon", category.IconCssClass ), P( "@Description", category.Description ),
                        P( "@Order", order ), P( "@Guid", category.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
                    WriteSecurity( category, ids[category.Key], now );
                    written++;
                }
            }

            return written;
        }

        /// <summary>
        /// Renames a category Rock ships, found by its Guid, and returns its Id. Rock's own code finds it by Guid too,
        /// so a view a later Rock update adds still lands in it.
        /// </summary>
        private int Rename( Model.CategoryDefinition category, DateTime now )
        {
            var id = _db.IdByGuid( "Category", category.Guid )
                ?? throw new InvalidDataException( $"configuration/categories.json: category '{category.Key}' is one Rock ships, but this Rock has no category {category.Guid}." );
            _db.Execute( "UPDATE [Category] SET [Name] = @Name, [Description] = @Description, [ModifiedDateTime] = @Now WHERE [Id] = @Id",
                P( "@Name", category.Name ), P( "@Description", category.Description ), P( "@Now", now ), P( "@Id", id ) );
            return id;
        }

        /// <summary>
        /// Writes the category's security rules as Rock's security editor saves them: for each action, each allowed role
        /// in order, then "deny All Users" when the action is locked. Data views and reports in the category inherit them.
        /// </summary>
        private void WriteSecurity( Model.CategoryDefinition category, int categoryId, DateTime now )
        {
            if ( category.Security.Count == 0 )
            {
                return;
            }

            var roles = _source.SecurityRoles?.Roles
                ?? throw new InvalidDataException( $"configuration/categories.json: category '{category.Key}' has security, but this package has no configuration/security-roles.json." );
            var categoryEntityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Category'" ) );
            foreach ( var rule in category.Security )
            {
                var order = 0;
                foreach ( var roleName in rule.Allow )
                {
                    var role = roles.FirstOrDefault( r => r.RockRole == roleName )
                        ?? throw new InvalidDataException( $"configuration/categories.json: category '{category.Key}' allows {roleName}, which is not a role in configuration/security-roles.json." );
                    InsertAuth( categoryEntityTypeId, categoryId, order++, rule.Action, "A", 0, _db.RequireIdByGuid( "Group", role.Guid ), now );
                }

                if ( rule.DenyAllUsers )
                {
                    InsertAuth( categoryEntityTypeId, categoryId, order, rule.Action, "D", 1, null, now );
                }
            }
        }

        private void InsertAuth( int entityTypeId, int entityId, int order, string action, string allowOrDeny, int specialRole, int? groupId, DateTime now )
        {
            _db.Execute( @"
INSERT INTO [Auth] ( [EntityTypeId], [EntityId], [Order], [Action], [AllowOrDeny], [SpecialRole], [GroupId], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( @EntityTypeId, @EntityId, @Order, @Action, @AllowOrDeny, @SpecialRole, @GroupId, NEWID(), @Now, @Now, @ForeignKey )",
                P( "@EntityTypeId", entityTypeId ), P( "@EntityId", entityId ), P( "@Order", order ), P( "@Action", action ), P( "@AllowOrDeny", allowOrDeny ),
                P( "@SpecialRole", specialRole ), P( "@GroupId", groupId ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
        }
    }
}
