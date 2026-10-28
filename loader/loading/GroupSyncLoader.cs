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
    /*
        10/28/26 - CLAUDE

        Each sync is written as Rock's Group Sync job leaves it after a run on install day: the group holds exactly
        the people in its data view, and LastRefreshDateTime is now. A group another step already filled (the
        Volunteers and Small Group Leaders lists copy the teams) must already match its view, or the load stops,
        so a sync can never quietly remove someone a Cast story put there. An empty group is filled with the view's
        people, each added when the job would have added them. The job's first real run then adds and removes
        nobody, so its welcome and exit emails and its logins wait for a person to join or leave later.

        Reason: Decision D-group-sync-3. The loader sends nothing; the syncs act only on later changes.
    */

    /// <summary>
    /// Writes the church's data views and group syncs from configuration/group-syncs.json, and fills each synced group.
    /// </summary>
    internal sealed class GroupSyncLoader
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
            ["Rock.Reporting.DataFilter.Person.GroupTypeFirstAttendanceFilter"] = new Guid( "5748F0E7-E033-4D36-819C-0AA3823A8935" )
        };

        /// <summary>
        /// Rock's Group Sync job runs every 20 minutes, so a sync with no interval adds a person about that long after they match.
        /// </summary>
        private const int JobMinutes = 20;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public GroupSyncLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes every data view and sync, and fills each synced group.
        /// </summary>
        /// <param name="listIds">The communication lists, by list key, for lists that are not per campus.</param>
        public void Load( IReadOnlyDictionary<string, int> listIds )
        {
            var configuration = _source.GroupSyncs;
            if ( configuration == null || _source.Categories == null )
            {
                _db.Log.Write( "  Group sync: skipped (this package has no configuration/group-syncs.json)" );
                return;
            }

            var now = DateTime.Now;
            RegisterComponents( configuration );
            var viewIds = configuration.DataViews.ToDictionary( v => v.Key, v => WriteDataView( v, now ) );
            var evaluator = new DataViewEvaluator( _db, _dates.Anchor );
            var welcomeId = _db.RequireIdByGuid( "SystemCommunication", configuration.WelcomeCommunicationGuid );
            var exitId = _db.RequireIdByGuid( "SystemCommunication", configuration.ExitCommunicationGuid );
            var summary = new List<string>();

            foreach ( var sync in configuration.Syncs )
            {
                var view = configuration.DataViews.FirstOrDefault( v => v.Key == sync.DataView )
                    ?? throw new InvalidDataException( $"configuration/group-syncs.json: sync {sync.Key} names data view {sync.DataView}, which is not in the file." );
                var target = Target( sync, listIds );
                var inView = evaluator.Evaluate( view.Filter );
                var members = _db.Query( "SELECT DISTINCT [PersonId] FROM [GroupMember] WHERE [GroupId] = @GroupId AND [GroupRoleId] = @RoleId AND [IsArchived] = 0",
                    P( "@GroupId", target.GroupId ), P( "@RoleId", target.RoleId ) ).Select( r => ( int ) r["PersonId"] ).ToHashSet();

                if ( members.Count > 0 )
                {
                    RequireMatch( sync, target.Name, inView.Keys, members );
                    summary.Add( $"{target.Name} {members.Count:N0} (as planned)" );
                }
                else
                {
                    AddMembers( sync, target, inView, now );
                    summary.Add( $"{target.Name} {inView.Count:N0}" );
                }

                _db.Execute( @"
INSERT INTO [GroupSync] ( [GroupId], [GroupTypeRoleId], [SyncDataViewId], [WelcomeSystemCommunicationId], [ExitSystemCommunicationId], [AddUserAccountsDuringSync],
    [ScheduleIntervalMinutes], [LastRefreshDateTime], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @GroupId, @RoleId, @DataViewId, @WelcomeId, @ExitId, @CreateLogin, @Interval, @Now, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@GroupId", target.GroupId ), P( "@RoleId", target.RoleId ), P( "@DataViewId", viewIds[view.Key] ),
                    P( "@WelcomeId", sync.Welcome ? welcomeId : ( int? ) null ), P( "@ExitId", sync.Exit ? exitId : ( int? ) null ), P( "@CreateLogin", sync.CreateLogin ),
                    P( "@Interval", sync.IntervalMinutes ), P( "@Now", now ), P( "@Guid", sync.Guid ), P( "@ForeignKey", _foreignKey ) );
            }

            _db.Log.Write( $"  Group syncs: {configuration.Syncs.Count} from {viewIds.Count} data views ({string.Join( ", ", summary )})" );
        }

        /// <summary>
        /// Adds a row for each data filter component the views use that this Rock has not registered yet, the row Rock's
        /// start-up would write: the class name, Rock's assembly, a friendly name, and the component's own Guid.
        /// </summary>
        private void RegisterComponents( GroupSyncConfiguration configuration )
        {
            var components = configuration.DataViews.SelectMany( v => Leaves( v.Filter ) ).Select( f => f.Component ).Distinct().ToList();
            var rockAssembly = ( string ) _db.Scalar( "SELECT [AssemblyName] FROM [EntityType] WHERE [Name] = 'Rock.Reporting.DataFilter.PropertyFilter'" );
            foreach ( var component in components.Where( c => _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", c ) ) == null ) )
            {
                if ( !ComponentGuids.TryGetValue( component, out var guid ) || rockAssembly == null )
                {
                    throw new InvalidDataException( $"configuration/group-syncs.json uses {component}, which this Rock has not registered." );
                }

                var className = component.Substring( component.LastIndexOf( '.' ) + 1 );
                _db.Execute( @"
INSERT INTO [EntityType] ( [Name], [AssemblyName], [FriendlyName], [IsEntity], [IsSecured], [IsCommon], [Guid], [ForeignKey] )
VALUES ( @Name, @AssemblyName, @FriendlyName, 0, 1, 0, @Guid, @ForeignKey )",
                    P( "@Name", component ), P( "@AssemblyName", component + rockAssembly.Substring( rockAssembly.IndexOf( ',' ) ) ),
                    P( "@FriendlyName", SplitCase( className ) ), P( "@Guid", guid ), P( "@ForeignKey", _foreignKey ) );
            }
        }

        /// <summary>
        /// Writes one data view and its filter tree, as Rock's data view editor saves them. Returns its Id.
        /// </summary>
        private int WriteDataView( DataViewDefinition view, DateTime now )
        {
            var category = _source.Categories.Get( view.Category );
            var viewId = ( int ) _db.Scalar( @"
INSERT INTO [DataView] ( [IsSystem], [Name], [Description], [CategoryId], [EntityTypeId], [IncludeDeceased], [DisableUseOfReadOnlyContext], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Name, @Description, @CategoryId, ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' ), 0, 0, @Guid, @ForeignKey, @Now, @Now )",
                P( "@Name", view.Name ), P( "@Description", view.Description ), P( "@CategoryId", _db.RequireIdByGuid( "Category", category.Guid ) ),
                P( "@Guid", view.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

            var random = new SeededRandom( $"{_seed}:group-sync:{view.Key}" );
            var rootId = WriteFilter( view.Filter, null, viewId, random, now );
            _db.Execute( "UPDATE [DataView] SET [DataViewFilterId] = @FilterId WHERE [Id] = @Id", P( "@FilterId", rootId ), P( "@Id", viewId ) );
            return viewId;
        }

        private int WriteFilter( DataViewFilterDefinition filter, int? parentId, int viewId, SeededRandom random, DateTime now )
        {
            var id = ( int ) _db.Scalar( @"
INSERT INTO [DataViewFilter] ( [ExpressionType], [ParentId], [EntityTypeId], [Selection], [DataViewId], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @ExpressionType, @ParentId, ( SELECT [Id] FROM [EntityType] WHERE [Name] = @Component ), @Selection, @DataViewId, @Guid, @ForeignKey, @Now, @Now )",
                P( "@ExpressionType", filter.ExpressionType ), P( "@ParentId", parentId ), P( "@Component", filter.Component ), P( "@Selection", filter.Selection ),
                P( "@DataViewId", viewId ), P( "@Guid", random.NextGuid() ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
            foreach ( var child in filter.Children )
            {
                WriteFilter( child, id, viewId, random, now );
            }

            return id;
        }

        /// <summary>
        /// The synced group, its type's default role (the role Rock's sync panel suggests), and its name. Only group types
        /// that allow sync qualify, because Rock's group detail hides the sync panel on every other type.
        /// </summary>
        private (int GroupId, int GroupTypeId, int RoleId, string Name) Target( GroupSyncDefinition sync, IReadOnlyDictionary<string, int> listIds )
        {
            var groupId = sync.Target.List != null
                ? listIds.TryGetValue( sync.Target.List, out var listId ) ? listId : throw new InvalidDataException( $"configuration/group-syncs.json: sync {sync.Key} targets list {sync.Target.List}, which is not a list for all campuses in configuration/communications.json." )
                : _db.RequireIdByGuid( "Group", sync.Target.Group ?? throw new InvalidDataException( $"configuration/group-syncs.json: sync {sync.Key} has no target list or group." ) );
            var row = _db.Query( @"
SELECT [g].[GroupTypeId], [gt].[DefaultGroupRoleId], [g].[Name], [gt].[AllowGroupSync]
FROM [Group] AS [g]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [g].[Id] = @Id", P( "@Id", groupId ) ).Single();
            if ( !( bool ) row["AllowGroupSync"] || !( row["DefaultGroupRoleId"] is int roleId ) )
            {
                throw new InvalidDataException( $"configuration/group-syncs.json: sync {sync.Key} targets {row["Name"]}, whose group type does not allow group sync or has no default role." );
            }

            return (groupId, ( int ) row["GroupTypeId"], roleId, ( string ) row["Name"]);
        }

        /// <summary>
        /// Stops the load when a group another step filled differs from its sync's view, naming a few people each way.
        /// </summary>
        private void RequireMatch( GroupSyncDefinition sync, string groupName, IEnumerable<int> inView, HashSet<int> members )
        {
            var view = inView.ToHashSet();
            var missing = view.Where( id => !members.Contains( id ) ).ToList();
            var extra = members.Where( id => !view.Contains( id ) ).ToList();
            if ( missing.Count == 0 && extra.Count == 0 )
            {
                return;
            }

            string Names( List<int> ids ) => string.Join( ", ", ids.Take( 5 ).Select( id => _db.Scalar( "SELECT [NickName] + ' ' + [LastName] FROM [Person] WHERE [Id] = @Id", P( "@Id", id ) ) ) );
            throw new InvalidOperationException( $"Group sync {sync.Key}: {groupName} holds {members.Count} people but its data view finds {view.Count}. "
                + $"Rock's job would add {missing.Count} ({Names( missing )}) and remove {extra.Count} ({Names( extra )}). Make the list's rule and the view agree." );
        }

        /// <summary>
        /// Adds each person in the view as an active member, added when the job would have added them: one run after
        /// they first matched, or now when that cannot be told.
        /// </summary>
        private void AddMembers( GroupSyncDefinition sync, (int GroupId, int GroupTypeId, int RoleId, string Name) target, Dictionary<int, DateTime?> inView, DateTime now )
        {
            var nextId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [GroupMember]" ) );
            var random = new SeededRandom( $"{_seed}:group-sync:{sync.Key}:members" );
            var rows = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date",
                "CommunicationPreference:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var person in inView.OrderBy( p => p.Key ) )
            {
                var since = person.Value;
                var personId = person.Key;
                var added = since.HasValue ? since.Value.AddMinutes( sync.IntervalMinutes ?? JobMinutes ) : now;
                if ( added > now )
                {
                    added = now;
                }

                // Rock's job adds a member with no communication preference of their own, so they get their person preference.
                rows.Rows.Add( nextId++, false, target.GroupId, target.GroupTypeId, personId, target.RoleId, 1, added, 0, random.NextGuid(), _foreignKey, added, added );
            }

            _db.BulkInsert( "GroupMember", rows, keepIdentity: true );
        }

        private static IEnumerable<DataViewFilterDefinition> Leaves( DataViewFilterDefinition filter )
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
