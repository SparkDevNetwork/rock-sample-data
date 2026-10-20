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
    /// Sets up serving teams in Rock from configuration/serving.json: one "{campus} Serving Teams"
    /// parent per campus under Rock's Serving Teams group, and every team at every campus with its
    /// rooms, service times, staffing, and background check requirement.
    /// Like <see cref="ConfigurationLoader"/>, every step finds existing rows by Guid first.
    /// </summary>
    internal sealed class ServingConfigurationLoader
    {
        private static readonly Guid RequiresBackgroundCheckAttributeGuid = new Guid( "6DC6E992-4CAF-4C9F-B11D-5918D244BD40" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;
        private readonly Dictionary<string, CampusPlan> _campusPlans;

        public ServingConfigurationLoader( RockDatabase db, SampleDataSource source, string foreignKey, Dictionary<string, CampusPlan> campusPlans )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
            _campusPlans = campusPlans;
        }

        /// <summary>
        /// Runs every serving setup step and returns the Rock Ids later steps need.
        /// </summary>
        public ServingSetup Load()
        {
            var config = _source.Serving;
            var setup = new ServingSetup
            {
                GroupTypeId = _db.RequireIdByGuid( "GroupType", config.GroupType.Guid ),
                LeaderRoleId = _db.RequireIdByGuid( "GroupTypeRole", config.Roles.Leader.Guid ),
                MemberRoleId = _db.RequireIdByGuid( "GroupTypeRole", config.Roles.Member.Guid ),
                RootParentId = _db.RequireIdByGuid( "Group", config.Parents.Root.Guid )
            };

            var mainCampusKey = _source.Campuses.Campuses.Single( c => c.RockDefault ).Key;
            var castTeams = _source.ReadCastServingTeams();
            var requirementTypeId = _db.RequireIdByGuid( "GroupRequirementType", config.BackgroundCheckRequirementTypeGuid );
            var backgroundCheckAttributeId = _db.RequireIdByGuid( "Attribute", RequiresBackgroundCheckAttributeGuid );
            var positions = 0;

            foreach ( var campus in _source.Campuses.Campuses.Where( c => _campusPlans.ContainsKey( c.Key ) ) )
            {
                var plan = _campusPlans[campus.Key];
                var parentName = config.Parents.CampusNameFormat.Replace( "{campus}", campus.Name );
                var parentId = EnsureGroup( config.Parents.Campuses[campus.Key], parentName, null, setup.GroupTypeId, setup.RootParentId, plan.CampusId, 0, false );
                setup.CampusParentIds[campus.Key] = parentId;

                var order = 0;
                foreach ( var team in config.Teams )
                {
                    var guid = campus.Key == mainCampusKey && castTeams.TryGetValue( team.Key, out var castTeam ) ? castTeam.Guid : TeamGuid( campus.Key, team.Key );
                    var groupId = EnsureGroup( guid, team.Name, team.Description, setup.GroupTypeId, parentId, plan.CampusId, order++, team.RequiresBackgroundCheck );
                    setup.TeamGroupIds[(campus.Key, team.Key)] = groupId;

                    var services = TeamServices( team, plan );
                    var positionOrder = 0;
                    foreach ( var position in team.Positions )
                    {
                        var roomId = position.CheckInGroup != null ? plan.RoomIds[position.CheckInGroup] : plan.ServingRoomIds[position.Room];
                        var groupLocationId = ConfigurationLoader.EnsureGroupLocation( _db, _foreignKey, groupId, roomId );
                        _db.Execute( "UPDATE [GroupLocation] SET [Order] = @Order WHERE [Id] = @Id", P( "@Order", positionOrder++ ), P( "@Id", groupLocationId ) );
                        ConfigurationLoader.SetGroupLocationSchedules( _db, groupLocationId, services.Select( s => s.ScheduleId ).ToList() );
                        SetCapacity( groupLocationId, services, position.Capacity );
                        setup.Positions.Add( new ServingPositionInfo( campus.Key, team.Key, groupId, roomId, groupLocationId, position.Capacity ) );
                        positions++;
                    }

                    SetBackgroundCheck( groupId, team.RequiresBackgroundCheck, requirementTypeId, backgroundCheckAttributeId );
                }
            }

            Console.WriteLine( $"  Serving teams: {config.Teams.Count} at each of {setup.CampusParentIds.Count} campus(es), {positions} positions" );
            return setup;
        }

        /// <summary>
        /// The services a team serves at its campus: every Sunday service, or only the student service.
        /// </summary>
        internal static List<ServicePlan> TeamServices( ServingTeamDefinition team, CampusPlan plan ) =>
            team.Services == "student" ? new List<ServicePlan> { plan.StudentService } : plan.Services;

        /// <summary>
        /// The Guid of a team that has no fixed Guid. It depends only on the campus and team, so it is
        /// the same on every install and in every dataset.
        /// </summary>
        internal static Guid TeamGuid( string campusKey, string teamKey ) => new SeededRandom( $"serving-team:{campusKey}:{teamKey}" ).NextGuid();

        /// <summary>
        /// Adds a serving group, or moves and renames the existing one with this Guid. Rock's own Greeters,
        /// Ushers, and Children's groups become the main campus's teams this way.
        /// </summary>
        private int EnsureGroup( Guid guid, string name, string description, int groupTypeId, int parentId, int campusId, int order, bool mustMeetRequirements )
        {
            var id = _db.IdByGuid( "Group", guid );
            if ( id != null )
            {
                _db.Execute( @"
UPDATE [Group]
SET [Name] = @Name, [Description] = @Description, [GroupTypeId] = @GroupTypeId, [ParentGroupId] = @ParentId, [CampusId] = @CampusId, [Order] = @Order,
    [SchedulingMustMeetRequirements] = @MustMeet, [IsActive] = 1, [IsPublic] = 1, [IsArchived] = 0
WHERE [Id] = @Id",
                    P( "@Name", name ), P( "@Description", description ), P( "@GroupTypeId", groupTypeId ), P( "@ParentId", parentId ), P( "@CampusId", campusId ),
                    P( "@Order", order ), P( "@MustMeet", mustMeetRequirements ), P( "@Id", id ) );
                return id.Value;
            }

            return Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Group] ( [IsSystem], [ParentGroupId], [GroupTypeId], [CampusId], [Name], [Description], [IsSecurityRole], [IsActive], [IsPublic], [Order], [SchedulingMustMeetRequirements], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @ParentId, @GroupTypeId, @CampusId, @Name, @Description, 0, 1, 1, @Order, @MustMeet, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                P( "@ParentId", parentId ), P( "@GroupTypeId", groupTypeId ), P( "@CampusId", campusId ), P( "@Name", name ), P( "@Description", description ),
                P( "@Order", order ), P( "@MustMeet", mustMeetRequirements ), P( "@Guid", guid ), P( "@ForeignKey", _foreignKey ) ) );
        }

        /// <summary>
        /// Sets the minimum, desired, and maximum people for a position at each of its services.
        /// </summary>
        private void SetCapacity( int groupLocationId, List<ServicePlan> services, int[] capacity )
        {
            foreach ( var scheduleId in services.Select( s => s.ScheduleId ).Distinct() )
            {
                _db.Execute( @"
INSERT INTO [GroupLocationScheduleConfig] ( [GroupLocationId], [ScheduleId], [MinimumCapacity], [DesiredCapacity], [MaximumCapacity] )
VALUES ( @GroupLocationId, @ScheduleId, @Minimum, @Desired, @Maximum )",
                    P( "@GroupLocationId", groupLocationId ), P( "@ScheduleId", scheduleId ), P( "@Minimum", capacity[0] ), P( "@Desired", capacity[1] ), P( "@Maximum", capacity[2] ) );
            }
        }

        /// <summary>
        /// Adds Rock's Background Check Required requirement to a team that needs it, and sets the Serving
        /// Team type's Requires Background Check attribute to match, so the two never disagree on screen.
        /// </summary>
        private void SetBackgroundCheck( int groupId, bool required, int requirementTypeId, int attributeId )
        {
            if ( required )
            {
                _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [GroupRequirement] WHERE [GroupId] = @GroupId AND [GroupRequirementTypeId] = @TypeId )
    INSERT INTO [GroupRequirement] ( [GroupId], [GroupRequirementTypeId], [MustMeetRequirementToAddMember], [AppliesToAgeClassification], [AllowLeadersToOverride], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( @GroupId, @TypeId, 0, 0, 0, NEWID(), @ForeignKey, GETDATE(), GETDATE() )",
                    P( "@GroupId", groupId ), P( "@TypeId", requirementTypeId ), P( "@ForeignKey", _foreignKey ) );
            }

            _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [AttributeValue] WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId )
    INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( 0, @AttributeId, @EntityId, @Value, 1, NEWID(), @ForeignKey, GETDATE(), GETDATE() )
ELSE
    UPDATE [AttributeValue] SET [Value] = @Value, [IsPersistedValueDirty] = 1 WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId",
                P( "@AttributeId", attributeId ), P( "@EntityId", groupId ), P( "@Value", required ? "True" : "False" ), P( "@ForeignKey", _foreignKey ) );
        }
    }

    /// <summary>
    /// The Rock Ids later steps need about serving teams, filled by <see cref="ServingConfigurationLoader"/>.
    /// </summary>
    internal sealed class ServingSetup
    {
        public int GroupTypeId { get; set; }

        public int LeaderRoleId { get; set; }

        public int MemberRoleId { get; set; }

        public int RootParentId { get; set; }

        /// <summary>
        /// Each campus's "{campus} Serving Teams" parent group Id, by campus key.
        /// </summary>
        public Dictionary<string, int> CampusParentIds { get; } = new Dictionary<string, int>();

        /// <summary>
        /// Each team's group Id, by campus key and team key.
        /// </summary>
        public Dictionary<(string Campus, string Team), int> TeamGroupIds { get; } = new Dictionary<(string Campus, string Team), int>();

        public List<ServingPositionInfo> Positions { get; } = new List<ServingPositionInfo>();
    }

    /// <summary>
    /// One room a team serves in at one campus.
    /// </summary>
    internal sealed record ServingPositionInfo( string Campus, string Team, int GroupId, int LocationId, int GroupLocationId, int[] Capacity );
}
