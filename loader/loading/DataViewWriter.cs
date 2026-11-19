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

            // Rock declares this filter in the DataSelect namespace, though its file is with the data filters. Its class
            // name is what Rock's filter container looks it up by, so the DataFilter name would leave Rock unable to run it.
            ["Rock.Reporting.DataSelect.Person.InRegistrationInstanceFilter"] = new Guid( "1F51DA3B-22FE-4093-9DAA-5492B5FB17DA" )
        };

        /// <summary>
        /// The filters whose selection is Rock's JSON with the other view's Id. Every other filter that points at a view
        /// holds its Guid.
        /// </summary>
        private static readonly HashSet<string> IdSelectionFilters = new HashSet<string>
        {
            "Rock.Reporting.DataFilter.OtherDataViewFilter",
            "Rock.Reporting.DataFilter.NotInOtherDataViewFilter"
        };

        private readonly RockDatabase _db;
        private readonly CategoryConfiguration _categories;
        private readonly string _foreignKey;
        private readonly IReadOnlyDictionary<string, DataViewDefinition> _views;

        /// <param name="views">The views a leaf may point at, by key, when any leaf does.</param>
        public DataViewWriter( RockDatabase db, CategoryConfiguration categories, string foreignKey, IReadOnlyDictionary<string, DataViewDefinition> views = null )
        {
            _db = db;
            _categories = categories;
            _foreignKey = foreignKey;
            _views = views;
        }

        /// <summary>
        /// Registers each data filter component the views use that this Rock has not registered yet, as Rock's start-up would.
        /// </summary>
        /// <param name="file">The configuration file the views come from, for messages.</param>
        public void RegisterComponents( IEnumerable<DataViewDefinition> views, string file )
        {
            var components = views.SelectMany( v => Leaves( v.Filter ) ).Select( f => f.Component ).Distinct().ToList();
            foreach ( var component in components.Where( c => _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", c ) ) == null ) )
            {
                if ( !ComponentGuids.TryGetValue( component, out var guid ) )
                {
                    throw new InvalidDataException( $"{file} uses {component}, which this Rock has not registered." );
                }

                RockComponents.Register( _db, component, guid, _foreignKey );
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
            // Rock's save stamps the person who made the view on it and on each of its filters.
            int? makerAliasId = view.CreatedBy == null ? null
                : _db.Scalar( "SELECT [PrimaryAliasId] FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", view.CreatedBy.Value ) ) as int?
                    ?? throw new InvalidDataException( $"Data view {view.Key} was made by {view.CreatedBy}, who is not a loaded person." );
            var viewId = ( int ) _db.Scalar( @"
INSERT INTO [DataView] ( [IsSystem], [Name], [Description], [CategoryId], [EntityTypeId], [TransformEntityTypeId], [PersistedScheduleIntervalMinutes], [PersistedScheduleId],
    [IncludeDeceased], [DisableUseOfReadOnlyContext], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Name, @Description, @CategoryId, @EntityTypeId, @TransformId, @Interval, @ScheduleId, 0, 0, @Guid, @ForeignKey, @Now, @Now, @MakerAliasId, @MakerAliasId )",
                P( "@Name", view.Name ), P( "@Description", view.Description ), P( "@CategoryId", _db.RequireIdByGuid( "Category", category.Guid ) ),
                P( "@EntityTypeId", entityTypeId ), P( "@TransformId", transformId ), P( "@Interval", view.Persisted?.IntervalMinutes ), P( "@ScheduleId", scheduleId ),
                P( "@Guid", view.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ), P( "@MakerAliasId", makerAliasId ) );

            var rootId = WriteFilter( view.Filter, null, viewId, random, now, selection, makerAliasId );
            _db.Execute( "UPDATE [DataView] SET [DataViewFilterId] = @FilterId WHERE [Id] = @Id", P( "@FilterId", rootId ), P( "@Id", viewId ) );
            return viewId;
        }

        /// <summary>
        /// The selection of a leaf that points at another data view, which this load must have written by now.
        /// </summary>
        public string ViewSelection( DataViewFilterDefinition filter )
        {
            if ( !IdSelectionFilters.Contains( filter.Component ) )
            {
                // A related data view filter's settings start with the view's Guid. A filter with more settings, such as
                // Rock's group data view filter (Guid, member status, role type), gives them with a {dataView} placeholder.
                var guid = View( filter ).Guid.ToString();
                return filter.Selection?.Replace( "{dataView}", guid ) ?? guid;
            }

            // Rock's data view editor saves "Use Persisted" on by default.
            return $"{{\"DataViewId\":{RelatedId( filter )},\"UsePersisted\":true}}";
        }

        private DataViewDefinition View( DataViewFilterDefinition filter )
        {
            return _views != null && _views.TryGetValue( filter.DataView, out var view ) ? view
                : throw new InvalidDataException( $"A data view points at {filter.DataView}, which is not a data view in any configuration file." );
        }

        private int RelatedId( DataViewFilterDefinition filter )
        {
            return _db.IdByGuid( "DataView", View( filter ).Guid )
                ?? throw new InvalidDataException( $"A data view points at {filter.DataView}, which this load has not written yet." );
        }

        private int WriteFilter( DataViewFilterDefinition filter, int? parentId, int viewId, SeededRandom random, DateTime now, Func<DataViewFilterDefinition, string> selection, int? makerAliasId )
        {
            var id = ( int ) _db.Scalar( @"
INSERT INTO [DataViewFilter] ( [ExpressionType], [ParentId], [EntityTypeId], [Selection], [DataViewId], [RelatedDataViewId], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime],
    [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
OUTPUT INSERTED.[Id]
VALUES ( @ExpressionType, @ParentId, ( SELECT [Id] FROM [EntityType] WHERE [Name] = @Component ), @Selection, @DataViewId, @RelatedId, @Guid, @ForeignKey, @Now, @Now,
    @MakerAliasId, @MakerAliasId )",
                P( "@ExpressionType", filter.ExpressionType ), P( "@ParentId", parentId ), P( "@Component", filter.Component ), P( "@Selection", filter.Component == null ? null : selection( filter ) ),
                P( "@RelatedId", filter.DataView == null ? null : RelatedId( filter ) ),
                P( "@DataViewId", viewId ), P( "@Guid", random.NextGuid() ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ), P( "@MakerAliasId", makerAliasId ) );
            foreach ( var child in filter.Children )
            {
                WriteFilter( child, id, viewId, random, now, selection, makerAliasId );
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
    }
}
