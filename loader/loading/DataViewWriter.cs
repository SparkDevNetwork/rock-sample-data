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
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Writes data views and their filter trees as Rock's data view editor saves them, filed in a church category.
    /// Group syncs, registrations, and the reporting views all use it.
    /// </summary>
    internal sealed class DataViewWriter
    {
        /// <summary>
        /// The data filter components Rock declares with a fixed EntityType Guid, for any one a base has not registered yet.
        /// Rock registers every component when its web site starts (EntityTypeService.RegisterEntityTypes).
        /// </summary>
        private static readonly Dictionary<string, Guid> ComponentGuids = new Dictionary<string, Guid>
        {
            ["Rock.Reporting.DataFilter.PropertyFilter"] = new Guid( "03F0D6AC-D181-48B6-B4BC-1F2652B55323" ),
            ["Rock.Reporting.DataFilter.Person.InGroupFilter"] = new Guid( "A40B4BD4-FA17-4A82-97CC-2C71805F9D7C" ),
            ["Rock.Reporting.DataFilter.Person.InGroupGroupTypeFilter"] = new Guid( "0E239967-6D33-4205-B19F-08AD8FF6ED0B" ),
            ["Rock.Reporting.DataFilter.Person.GroupTypeAttendanceFilter"] = new Guid( "55E78F48-F849-444A-B285-FDA99E7236E7" ),
            ["Rock.Reporting.DataFilter.Person.GroupTypeFirstAttendanceFilter"] = new Guid( "5748F0E7-E033-4D36-819C-0AA3823A8935" ),
            ["Rock.Reporting.DataFilter.Person.GradeFilter"] = new Guid( "7311CCCF-BBFB-4FF7-AFD0-3D7F2D466C1B" ),
            ["Rock.Reporting.DataFilter.Person.InRegistrationInstanceFilter"] = new Guid( "1F51DA3B-22FE-4093-9DAA-5492B5FB17DA" )
        };

        private readonly RockDatabase _db;
        private readonly CategoryConfiguration _categories;
        private readonly string _foreignKey;

        public DataViewWriter( RockDatabase db, CategoryConfiguration categories, string foreignKey )
        {
            _db = db;
            _categories = categories;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Adds a row for each data filter component the views use that this Rock has not registered yet, the row Rock's
        /// start-up would write: the class name, Rock's assembly, a friendly name, and the component's own Guid. When Rock
        /// already holds the component's Guid under an old class name, its start-up renames that row, so the loader does too.
        /// </summary>
        /// <param name="file">The configuration file the views come from, for messages.</param>
        public void RegisterComponents( IEnumerable<DataViewDefinition> views, string file )
        {
            var components = views.SelectMany( v => Leaves( v.Filter ) ).Select( f => f.Component ).Distinct().ToList();
            var rockAssembly = ( string ) _db.Scalar( "SELECT [AssemblyName] FROM [EntityType] WHERE [Name] = 'Rock.Reporting.DataFilter.PropertyFilter'" );
            foreach ( var component in components.Where( c => _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", c ) ) == null ) )
            {
                if ( !ComponentGuids.TryGetValue( component, out var guid ) || rockAssembly == null )
                {
                    throw new InvalidDataException( $"{file} uses {component}, which this Rock has not registered." );
                }

                var assemblyName = component + rockAssembly.Substring( rockAssembly.IndexOf( ',' ) );
                if ( _db.IdByGuid( "EntityType", guid ) is int renamedId )
                {
                    // Rock's EntityTypeService.RegisterEntityTypes finds a renamed class by its Guid and updates the row.
                    _db.Execute( "UPDATE [EntityType] SET [Name] = @Name, [AssemblyName] = @AssemblyName, [IsEntity] = 0, [IsSecured] = 1 WHERE [Id] = @Id",
                        P( "@Name", component ), P( "@AssemblyName", assemblyName ), P( "@Id", renamedId ) );
                    continue;
                }

                var className = component.Substring( component.LastIndexOf( '.' ) + 1 );
                _db.Execute( @"
INSERT INTO [EntityType] ( [Name], [AssemblyName], [FriendlyName], [IsEntity], [IsSecured], [IsCommon], [Guid], [ForeignKey] )
VALUES ( @Name, @AssemblyName, @FriendlyName, 0, 1, 0, @Guid, @ForeignKey )",
                    P( "@Name", component ), P( "@AssemblyName", assemblyName ),
                    P( "@FriendlyName", SplitCase( className ) ), P( "@Guid", guid ), P( "@ForeignKey", _foreignKey ) );
            }
        }

        /// <summary>
        /// Writes one data view and its filter tree, as Rock's data view editor saves them. Returns its Id.
        /// </summary>
        /// <param name="random">The stream each filter's Guid is drawn from.</param>
        /// <param name="selection">Each leaf's selection string.</param>
        /// <param name="scheduleIds">The named schedules a persisted view may name, by key.</param>
        public int Write( DataViewDefinition view, DateTime now, SeededRandom random, Func<DataViewFilterDefinition, string> selection, IReadOnlyDictionary<string, int> scheduleIds = null )
        {
            var category = _categories.Get( view.Category );
            var entityTypeId = EntityTypeId( view.EntityType ?? "Rock.Model.Person", view );
            int? transformId = view.Transform == null ? null : EntityTypeId( view.Transform, view );
            int? scheduleId = view.Persisted?.Schedule == null ? null
                : scheduleIds != null && scheduleIds.TryGetValue( view.Persisted.Schedule, out var id ) ? id
                : throw new InvalidDataException( $"Data view {view.Key} is persisted on schedule {view.Persisted.Schedule}, which is not a named schedule." );

            /*
                10/30/26 - CLAUDE

                A persisted view's last refresh stays empty. Rock runs a view live until its Update Persisted DataViews job
                persists it, and that job runs every minute and persists any view that has never been refreshed. So the view
                is right on install day, and Rock, not the loader, decides what it holds.

                Reason: The loader cannot run every filter Rock can, so it leaves the persisted values to Rock.
            */
            var viewId = ( int ) _db.Scalar( @"
INSERT INTO [DataView] ( [IsSystem], [Name], [Description], [CategoryId], [EntityTypeId], [TransformEntityTypeId], [PersistedScheduleIntervalMinutes], [PersistedScheduleId],
    [IncludeDeceased], [DisableUseOfReadOnlyContext], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Name, @Description, @CategoryId, @EntityTypeId, @TransformId, @Interval, @ScheduleId, 0, 0, @Guid, @ForeignKey, @Now, @Now )",
                P( "@Name", view.Name ), P( "@Description", view.Description ), P( "@CategoryId", _db.RequireIdByGuid( "Category", category.Guid ) ),
                P( "@EntityTypeId", entityTypeId ), P( "@TransformId", transformId ), P( "@Interval", view.Persisted?.IntervalMinutes ), P( "@ScheduleId", scheduleId ),
                P( "@Guid", view.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

            var rootId = WriteFilter( view.Filter, null, viewId, random, now, selection );
            _db.Execute( "UPDATE [DataView] SET [DataViewFilterId] = @FilterId WHERE [Id] = @Id", P( "@FilterId", rootId ), P( "@Id", viewId ) );
            return viewId;
        }

        private int WriteFilter( DataViewFilterDefinition filter, int? parentId, int viewId, SeededRandom random, DateTime now, Func<DataViewFilterDefinition, string> selection )
        {
            var id = ( int ) _db.Scalar( @"
INSERT INTO [DataViewFilter] ( [ExpressionType], [ParentId], [EntityTypeId], [Selection], [DataViewId], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @ExpressionType, @ParentId, ( SELECT [Id] FROM [EntityType] WHERE [Name] = @Component ), @Selection, @DataViewId, @Guid, @ForeignKey, @Now, @Now )",
                P( "@ExpressionType", filter.ExpressionType ), P( "@ParentId", parentId ), P( "@Component", filter.Component ), P( "@Selection", filter.Component == null ? null : selection( filter ) ),
                P( "@DataViewId", viewId ), P( "@Guid", random.NextGuid() ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
            foreach ( var child in filter.Children )
            {
                WriteFilter( child, id, viewId, random, now, selection );
            }

            return id;
        }

        private int EntityTypeId( string name, DataViewDefinition view )
        {
            return Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", name ) )
                ?? throw new InvalidDataException( $"Data view {view.Key} uses {name}, which this Rock has no entity type for." ) );
        }

        public static IEnumerable<DataViewFilterDefinition> Leaves( DataViewFilterDefinition filter )
        {
            return filter.Component != null ? new[] { filter } : filter.Children.SelectMany( Leaves );
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
