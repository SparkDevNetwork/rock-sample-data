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

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the tags in configuration/tags.json against the loaded database (decision D-tags-1): each tag is where Rock's
    /// tag bar finds it, Rock's Staff tag holds exactly the loaded staff, and each Cast person a story tags carries it.
    /// </summary>
    internal static class CastTagExpectations
    {
        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, List<string> failures )
        {
            var tags = source.Tags?.Tags;
            if ( tags == null )
            {
                return 0;
            }

            bool IsLoaded( Guid castGuid ) => db.Scalar( "SELECT 1 FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", guids.Value( castGuid ) ) ) != null;
            var categories = source.Categories?.Categories.ToDictionary( c => c.Key, c => c.Guid ) ?? new Dictionary<string, Guid>();
            var checks = 0;
            foreach ( var tag in tags.Where( t => t.Maker == null || IsLoaded( t.Maker.Person ) ) )
            {
                checks++;

                // Rock's tag bar on a person profile reads active Person tags, the viewer's own personal tags and every
                // organizational one, and Rock's Person Tag data filter finds a tag by its Guid.
                var row = db.Query( @"
SELECT [owner].[Guid] AS [Owner], [c].[Guid] AS [Category]
FROM [Tag] AS [t]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [t].[OwnerPersonAliasId]
LEFT JOIN [Person] AS [owner] ON [owner].[Id] = [pa].[PersonId]
LEFT JOIN [Category] AS [c] ON [c].[Id] = [t].[CategoryId]
WHERE [t].[Guid] = @Guid AND [t].[IsActive] = 1 AND [t].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' )",
                    P( "@Guid", tag.Guid ) ).FirstOrDefault();
                var label = tag.Owner == null ? tag.Name : $"{tag.Owner.Name}'s {tag.Name}";
                if ( row == null )
                {
                    failures.Add( $"Tag {label}: expected an active tag on people, Rock has none" );
                    continue;
                }

                var owner = row["Owner"] as Guid?;
                if ( tag.Owner != null ? owner != guids.Rock( tag.Owner.Person ) : owner != null )
                {
                    failures.Add( $"Tag {label}: expected it to belong to {tag.Owner?.Name ?? "no one"}, Rock shows {( owner == null ? "an organizational tag" : "another owner" )}" );
                }

                var category = row["Category"] as Guid?;
                if ( tag.Category != null && category != categories.GetValueOrDefault( tag.Category ) )
                {
                    failures.Add( $"Tag {label}: expected it in the {tag.Category} category, Rock shows {( category == null ? "no category" : "another one" )}" );
                }

                var carriers = db.Query( "SELECT [ti].[EntityGuid] FROM [TaggedItem] AS [ti] INNER JOIN [Tag] AS [t] ON [t].[Id] = [ti].[TagId] WHERE [t].[Guid] = @Guid", P( "@Guid", tag.Guid ) )
                    .Select( r => ( Guid ) r["EntityGuid"] )
                    .ToHashSet();
                if ( tag.Staff )
                {
                    checks++;
                    var staff = ( source.Staff?.Staff ?? new List<Model.StaffMember>() ).Where( s => IsLoaded( s.Person ) ).Select( s => guids.Rock( s.Person ) ).ToHashSet();
                    if ( !staff.SetEquals( carriers ) )
                    {
                        failures.Add( $"Tag {label}: expected exactly the {staff.Count} staff this dataset loads, Rock shows {carriers.Count} people, {staff.Count( carriers.Contains )} of them staff" );
                    }
                }

                foreach ( var person in tag.Cast.Where( c => IsLoaded( c.Person ) ) )
                {
                    checks++;
                    if ( !carriers.Contains( guids.Rock( person.Person ) ) )
                    {
                        failures.Add( $"{person.Name}: expected the {label} tag on their profile, Rock shows none" );
                    }
                }
            }

            return checks;
        }
    }
}
