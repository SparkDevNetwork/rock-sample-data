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
    /// Registers a Rock component class that a base has not registered yet, as Rock's start-up would
    /// (EntityTypeService.RegisterEntityTypes) before anything uses it.
    /// </summary>
    internal static class RockComponents
    {
        /// <summary>
        /// Returns the component's EntityType Id. When the row is missing, adds the one Rock's start-up writes: the class name,
        /// Rock's assembly, a friendly name, and the component's own Guid. When Rock holds the Guid under an old class name, its
        /// start-up renames that row, so this does too.
        /// </summary>
        /// <param name="name">The component's class name, such as Rock.Reporting.DataFilter.PropertyFilter.</param>
        /// <param name="guid">The component's EntityTypeGuid attribute in Rock's source.</param>
        public static int Register( RockDatabase db, string name, Guid guid, string foreignKey )
        {
            if ( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", name ) ) is int id )
            {
                return id;
            }

            var rockAssembly = ( string ) db.Scalar( "SELECT [AssemblyName] FROM [EntityType] WHERE [Name] = 'Rock.Reporting.DataFilter.PropertyFilter'" )
                ?? throw new InvalidOperationException( $"This Rock has no PropertyFilter entity type to read Rock's assembly name from, so {name} cannot be registered." );
            var assemblyName = name + rockAssembly.Substring( rockAssembly.IndexOf( ',' ) );
            if ( db.IdByGuid( "EntityType", guid ) is int renamedId )
            {
                db.Execute( "UPDATE [EntityType] SET [Name] = @Name, [AssemblyName] = @AssemblyName, [IsEntity] = 0, [IsSecured] = 1 WHERE [Id] = @Id",
                    P( "@Name", name ), P( "@AssemblyName", assemblyName ), P( "@Id", renamedId ) );
                return renamedId;
            }

            var className = name.Substring( name.LastIndexOf( '.' ) + 1 );
            return ( int ) db.Scalar( @"
INSERT INTO [EntityType] ( [Name], [AssemblyName], [FriendlyName], [IsEntity], [IsSecured], [IsCommon], [Guid], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @AssemblyName, @FriendlyName, 0, 1, 0, @Guid, @ForeignKey )",
                P( "@Name", name ), P( "@AssemblyName", assemblyName ), P( "@FriendlyName", SplitCase( className ) ), P( "@Guid", guid ), P( "@ForeignKey", foreignKey ) );
        }

        /// <summary>
        /// "GroupTypeFirstAttendanceFilter" becomes "Group Type First Attendance Filter", Rock's friendly name for a type.
        /// </summary>
        private static string SplitCase( string name )
        {
            return string.Concat( name.Select( ( c, i ) => i > 0 && char.IsUpper( c ) ? " " + c : c.ToString() ) );
        }
    }
}
