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
        11/23/26 - CLAUDE

        A new Rock's check-in works, but on Rock's defaults: no area prints a label, rooms have no limits, and the
        test schedules reach only Rock's first 8 rooms. This step sets up what a church sets on purpose, the way
        Rock's own check-in screens save it: template settings as the template's attribute values, labels as
        RelatedEntity rows from each area to Rock's next-gen labels, room limits on each Location, a second room
        with Rock's Balance strategy on its area, and the Test Group in every kids and student room at every campus
        on both of Rock's test schedules. After serving's setup, a volunteer test area does the same for every room a
        serving team serves in. Supervisor PINs are logins, so they load in the logins step, only when the person
        installing gives a password, like every other login.

        Reason: Check-in should look like a real church's and be testable any day, at any time (decisions
        367 to 371).
    */

    /// <summary>
    /// Sets up check-in from the alpha.19 parts of checkin.json. Each part is skipped when an older package lacks it.
    /// </summary>
    internal sealed class CheckInSetupLoader
    {
        private const string AreaCheckInLabelPurposeKey = "AREA-CHECK-IN-LABEL";
        private const string LocationSelectionStrategyKey = "core_LocationSelectionStrategy";
        private const string BalanceStrategy = "1";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;
        private readonly Dictionary<string, CampusPlan> _campusPlans;
        private readonly Dictionary<string, int> _checkInGroupIds;

        public CheckInSetupLoader( RockDatabase db, SampleDataSource source, string foreignKey, ConfigurationLoader configuration )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
            _campusPlans = configuration.CampusPlans;
            _checkInGroupIds = configuration.CheckInGroupIdsByKey;
        }

        /// <summary>
        /// Runs every setup step the package has. Serving reads the second rooms, so this runs before serving's setup.
        /// </summary>
        public void Load()
        {
            var checkIn = _source.CheckIn;
            var parts = new List<string>();
            if ( checkIn.TemplateSettings != null )
            {
                parts.Add( $"{SetTemplateSettings( checkIn.TemplateSettings )} template settings" );
            }

            if ( checkIn.Labels != null )
            {
                parts.Add( $"{AttachLabels( checkIn )} area labels" );
            }

            if ( checkIn.RoomFirmThreshold != null )
            {
                parts.Add( $"{SetRoomLimits( checkIn )} room limits" );
                parts.Add( $"{AddSecondRooms( checkIn )} second rooms" );
            }

            if ( checkIn.TestCheckIn != null )
            {
                parts.Add( $"Test Group in {PlaceTestGroup( checkIn )} rooms" );
            }

            if ( parts.Count > 0 )
            {
                _db.Log.Write( $"  Check-in setup: {string.Join( ", ", parts )}" );
            }
        }

        /// <summary>
        /// Sets each configured setting of Rock's Weekly Service Check-in template.
        /// </summary>
        private int SetTemplateSettings( Dictionary<string, string> settings )
        {
            var template = _source.CheckIn.Template;
            var templateId = _db.RequireIdByGuid( "GroupType", template.Guid );
            foreach ( var (key, value) in settings )
            {
                var attributeId = _db.Scalar( @"
SELECT TOP 1 [a].[Id]
FROM [Attribute] AS [a]
WHERE [a].[Key] = @Key AND [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.GroupType' )
    AND [a].[EntityTypeQualifierColumn] = 'GroupTypePurposeValueId'
    AND [a].[EntityTypeQualifierValue] = ( SELECT CAST( [GroupTypePurposeValueId] AS NVARCHAR( 20 ) ) FROM [GroupType] WHERE [Id] = @TemplateId )",
                    P( "@Key", key ), P( "@TemplateId", templateId ) )
                    ?? throw new InvalidOperationException( $"Rock has no check-in template setting \"{key}\" (configuration/checkin.json templateSettings)." );
                SetAttributeValue( attributeId, templateId, value );
            }

            return settings.Count;
        }

        /// <summary>
        /// Attaches each area's labels in order, and the Serving Team area's from serving.json.
        /// </summary>
        private int AttachLabels( CheckInConfiguration checkIn )
        {
            var areas = checkIn.Areas.Where( a => a.Labels != null ).Select( a => (a.Guid, a.Labels) ).ToList();
            if ( _source.Serving.CheckIn?.Labels is { } servingLabels )
            {
                areas.Add( (_source.Serving.GroupType.Guid, servingLabels) );
            }

            return areas.Sum( a => AttachAreaLabels( _db.RequireIdByGuid( "GroupType", a.Guid ), a.Labels ) );
        }

        /// <summary>
        /// Attaches labels to one area in order, as Rock's Check-in Areas and Groups block does. Returns how many.
        /// </summary>
        private int AttachAreaLabels( int areaId, IReadOnlyList<string> keys )
        {
            var groupTypeEntityTypeId = _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.GroupType'" );
            var labelEntityTypeId = _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.CheckInLabel'" );
            var order = 0;
            foreach ( var key in keys )
            {
                var label = _source.CheckIn.Labels.TryGetValue( key, out var value ) ? value : throw new InvalidDataException( $"configuration/checkin.json has no label \"{key}\"." );
                _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [RelatedEntity] WHERE [SourceEntityTypeId] = @SourceTypeId AND [SourceEntityId] = @AreaId AND [TargetEntityTypeId] = @TargetTypeId AND [TargetEntityId] = @LabelId AND [PurposeKey] = @Purpose )
    INSERT INTO [RelatedEntity] ( [SourceEntityTypeId], [SourceEntityId], [TargetEntityTypeId], [TargetEntityId], [PurposeKey], [IsSystem], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( @SourceTypeId, @AreaId, @TargetTypeId, @LabelId, @Purpose, 0, @Order, NEWID(), @ForeignKey, GETDATE(), GETDATE() )",
                    P( "@SourceTypeId", groupTypeEntityTypeId ), P( "@AreaId", areaId ), P( "@TargetTypeId", labelEntityTypeId ),
                    P( "@LabelId", _db.RequireIdByGuid( "CheckInLabel", label.Guid ) ), P( "@Purpose", AreaCheckInLabelPurposeKey ), P( "@Order", order++ ), P( "@ForeignKey", _foreignKey ) );
            }

            return keys.Count;
        }

        /*
            11/23/26 - CLAUDE

            Rock ships a test area only for kids and students. Volunteers get the same pattern: a Volunteer Test Area
            under Rock's Volunteer Check-in template, built like Rock's Check-in Test Area (attendance rule None, no
            purpose, scheduling off, one Member role), with one test group per campus in every room a serving team
            serves in, on Rock's two test schedules. A test schedule on a real team's room would put a slot in the
            Group Scheduler every day, show in the Schedule Toolbox, and let auto-schedule assign volunteers to it.

            Reason: Volunteer check-in can be tested any day, at any time, without touching real scheduling
            (decision 371).
        */

        /// <summary>
        /// Adds the volunteer test area, linked under Rock's Volunteer Check-in template, and each campus's test group in
        /// every room a serving team serves in. Runs after serving's setup, which places the teams.
        /// </summary>
        public void LoadVolunteerTest( ServingSetup serving )
        {
            var test = _source.CheckIn.TestCheckIn;
            var area = test?.VolunteerArea;
            if ( area == null )
            {
                return;
            }

            var areaId = _db.IdByGuid( "GroupType", area.Guid ) ?? Convert.ToInt32( _db.Scalar( @"
INSERT INTO [GroupType] ( [IsSystem], [Name], [Description], [GroupTerm], [GroupMemberTerm], [AllowMultipleLocations], [ShowInGroupList], [ShowInNavigation], [TakesAttendance], [AttendanceRule], [AttendancePrintTo], [Order], [LocationSelectionMode], [IsSchedulingEnabled], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Name, @Description, 'Group', 'Member', 1, 0, 0, 1, 0, 0, 0, 0, 0, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                P( "@Name", area.Name ), P( "@Description", area.Description ), P( "@Guid", area.Guid ), P( "@ForeignKey", _foreignKey ) ) );
            var roleId = _db.IdByGuid( "GroupTypeRole", area.MemberRole ) ?? Convert.ToInt32( _db.Scalar( @"
INSERT INTO [GroupTypeRole] ( [IsSystem], [GroupTypeId], [Name], [Order], [IsLeader], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @AreaId, 'Member', 0, 0, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                P( "@AreaId", areaId ), P( "@Guid", area.MemberRole ), P( "@ForeignKey", _foreignKey ) ) );
            _db.Execute( "UPDATE [GroupType] SET [DefaultGroupRoleId] = @RoleId WHERE [Id] = @Id", P( "@RoleId", roleId ), P( "@Id", areaId ) );
            _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [GroupTypeAssociation] WHERE [GroupTypeId] = @TemplateId AND [ChildGroupTypeId] = @AreaId )
    INSERT INTO [GroupTypeAssociation] ( [GroupTypeId], [ChildGroupTypeId] ) VALUES ( @TemplateId, @AreaId )",
                P( "@TemplateId", _db.RequireIdByGuid( "GroupType", area.Template.Guid ) ), P( "@AreaId", areaId ) );
            if ( area.Labels != null && _source.CheckIn.Labels != null )
            {
                AttachAreaLabels( areaId, area.Labels );
            }

            var scheduleIds = test.Schedules.Select( s => _db.RequireIdByGuid( "Schedule", s.Guid ) ).ToList();
            var campusNames = _source.Campuses.Campuses.ToDictionary( c => c.Key, c => c.Name );
            var rooms = 0;
            foreach ( var (campusKey, plan) in _campusPlans )
            {
                var groupGuid = SeededRandom.StableGuid( $"volunteer-test-group:{campusKey}" );
                var groupId = _db.IdByGuid( "Group", groupGuid ) ?? Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Group] ( [IsSystem], [GroupTypeId], [CampusId], [Name], [IsSecurityRole], [IsActive], [Order], [IsPublic], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @AreaId, @CampusId, @Name, 0, 1, 0, 1, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                    P( "@AreaId", areaId ), P( "@CampusId", plan.CampusId ), P( "@Name", area.GroupNameFormat.Replace( "{campus}", campusNames[campusKey] ) ),
                    P( "@Guid", groupGuid ), P( "@ForeignKey", _foreignKey ) ) );

                foreach ( var roomId in serving.Positions.Where( p => p.Campus == campusKey ).Select( p => p.LocationId ).Distinct().OrderBy( id => id ) )
                {
                    var groupLocationId = ConfigurationLoader.EnsureGroupLocation( _db, _foreignKey, groupId, roomId );
                    foreach ( var scheduleId in scheduleIds )
                    {
                        _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [GroupLocationSchedule] WHERE [GroupLocationId] = @GroupLocationId AND [ScheduleId] = @ScheduleId )
    INSERT INTO [GroupLocationSchedule] ( [GroupLocationId], [ScheduleId] ) VALUES ( @GroupLocationId, @ScheduleId )",
                            P( "@GroupLocationId", groupLocationId ), P( "@ScheduleId", scheduleId ) );
                    }

                    rooms++;
                }
            }

            _db.Log.Write( $"  Volunteer test check-in: {area.Name} under {area.Template.Name}, a test group at each of {_campusPlans.Count} campuses, in {rooms} serving rooms" );
        }

        /// <summary>
        /// Gives every campus's room for each check-in group its soft limit and its firm limit, where it has one.
        /// </summary>
        private int SetRoomLimits( CheckInConfiguration checkIn )
        {
            var count = 0;
            foreach ( var plan in _campusPlans.Values )
            {
                foreach ( var (groupKey, roomId) in plan.RoomIds )
                {
                    SetLimits( checkIn, groupKey, roomId );
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Adds each large group's second room at every campus, beside its first, on the same services and with the
        /// same limits, and turns on Rock's Balance strategy for the group's area.
        /// </summary>
        private int AddSecondRooms( CheckInConfiguration checkIn )
        {
            var count = 0;
            foreach ( var group in checkIn.Groups.Where( g => g.SecondRoom != null ) )
            {
                foreach ( var plan in _campusPlans.Values )
                {
                    var firstRoomId = plan.RoomIds[group.Key];
                    var row = _db.Query( "SELECT [ParentLocationId], [LocationTypeValueId] FROM [Location] WHERE [Id] = @Id", P( "@Id", firstRoomId ) ).Single();
                    var roomId = _db.Scalar( "SELECT TOP 1 [Id] FROM [Location] WHERE [ParentLocationId] = @ParentId AND [Name] = @Name ORDER BY [Id]", P( "@ParentId", row["ParentLocationId"] ), P( "@Name", group.SecondRoom ) )
                        ?? _db.Scalar( @"
INSERT INTO [Location] ( [ParentLocationId], [Name], [IsActive], [LocationTypeValueId], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @ParentId, @Name, 1, @TypeId, NEWID(), @ForeignKey, GETDATE(), GETDATE() )",
                            P( "@ParentId", row["ParentLocationId"] ), P( "@Name", group.SecondRoom ), P( "@TypeId", row["LocationTypeValueId"] ), P( "@ForeignKey", _foreignKey ) );
                    var secondRoomId = Convert.ToInt32( roomId );
                    SetLimits( checkIn, group.Key, secondRoomId );

                    // The second room meets when the first does.
                    var groupId = _checkInGroupIds[group.Key];
                    var firstId = ConfigurationLoader.EnsureGroupLocation( _db, _foreignKey, groupId, firstRoomId );
                    var secondId = ConfigurationLoader.EnsureGroupLocation( _db, _foreignKey, groupId, secondRoomId );
                    ConfigurationLoader.SetGroupLocationSchedules( _db, secondId, _db.Query( "SELECT [ScheduleId] FROM [GroupLocationSchedule] WHERE [GroupLocationId] = @Id", P( "@Id", firstId ) ).Select( r => ( int ) r["ScheduleId"] ).ToList() );
                    _db.Execute( "UPDATE [GroupLocation] SET [Order] = 1 WHERE [Id] = @Id", P( "@Id", secondId ) );
                    plan.SecondRoomIds[group.Key] = secondRoomId;
                    count++;
                }

                var area = checkIn.Areas.Single( a => a.Groups.Contains( group.Key ) );
                var attributeId = _db.Scalar( "SELECT TOP 1 [Id] FROM [Attribute] WHERE [Key] = @Key AND [EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.GroupType' )", P( "@Key", LocationSelectionStrategyKey ) )
                    ?? throw new InvalidOperationException( $"Rock has no check-in area setting \"{LocationSelectionStrategyKey}\"." );
                SetAttributeValue( attributeId, _db.RequireIdByGuid( "GroupType", area.Guid ), BalanceStrategy );
            }

            return count;
        }

        /// <summary>
        /// Sets a room's soft limit and firm limit from the group it holds.
        /// </summary>
        private void SetLimits( CheckInConfiguration checkIn, string groupKey, int roomId )
        {
            _db.Execute( "UPDATE [Location] SET [SoftRoomThreshold] = @Soft, [FirmRoomThreshold] = @Firm WHERE [Id] = @Id",
                P( "@Soft", checkIn.RoomSoftThreshold.TryGetValue( groupKey, out var soft ) ? soft : null ),
                P( "@Firm", checkIn.RoomFirmThreshold.TryGetValue( groupKey, out var firm ) ? firm : null ),
                P( "@Id", roomId ) );
        }

        /// <summary>
        /// Puts Rock's Test Group in every room that holds a kids or student check-in group, at every campus, on
        /// both test schedules. Rock's first 8 rooms already have it, and keep it as Rock ships it. Returns the rooms.
        /// </summary>
        private int PlaceTestGroup( CheckInConfiguration checkIn )
        {
            var test = checkIn.TestCheckIn;
            var testGroupId = _db.RequireIdByGuid( "Group", test.Group.Guid );
            var scheduleIds = test.Schedules.Select( s => _db.RequireIdByGuid( "Schedule", s.Guid ) ).ToList();
            var areaIds = checkIn.Areas.Select( a => _db.RequireIdByGuid( "GroupType", a.Guid ) ).ToList();
            var rooms = _db.Query( $@"
SELECT DISTINCT [gl].[LocationId]
FROM [GroupLocation] AS [gl]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gl].[GroupId]
WHERE [g].[GroupTypeId] IN ( {string.Join( ", ", areaIds )} )
ORDER BY [gl].[LocationId]" ).Select( r => ( int ) r["LocationId"] ).ToList();

            foreach ( var roomId in rooms )
            {
                var groupLocationId = ConfigurationLoader.EnsureGroupLocation( _db, _foreignKey, testGroupId, roomId );
                foreach ( var scheduleId in scheduleIds )
                {
                    _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [GroupLocationSchedule] WHERE [GroupLocationId] = @GroupLocationId AND [ScheduleId] = @ScheduleId )
    INSERT INTO [GroupLocationSchedule] ( [GroupLocationId], [ScheduleId] ) VALUES ( @GroupLocationId, @ScheduleId )",
                        P( "@GroupLocationId", groupLocationId ), P( "@ScheduleId", scheduleId ) );
                }
            }

            return rooms.Count;
        }

        /*
            11/23/26 - CLAUDE

            A kiosk's supervisor screen asks for a PIN, which Rock checks as a login of its PIN Authentication
            provider whose user name is the PIN. Each campus's kids director has one. A campus with no Cast kids
            director uses the leader of its Kids Ministry team, the person who runs kids check-in there.

            Reason: Supervisor mode is how a check-in volunteer reprints a label or overrides a full room (decision
            367).
        */

        /// <summary>
        /// The supervisor PIN login of each campus in the dataset, made the week before the install day.
        /// </summary>
        public List<PlannedLogin> PlanSupervisors( PeopleLoader people, IReadOnlyList<ServingTeamPlan> servingPlans, DateTime anchor )
        {
            var checkIn = _source.CheckIn;
            var logins = new List<PlannedLogin>();
            foreach ( var supervisor in checkIn.Supervisors?.Where( s => _campusPlans.ContainsKey( s.Campus ) ) ?? Enumerable.Empty<SupervisorDefinition>() )
            {
                var personGuid = supervisor.Person
                    ?? servingPlans.Single( p => p.Campus == supervisor.Campus && p.Team == checkIn.SupervisorTeam ).Members.First( m => m.IsLeader ).Person;
                var person = people.People.TryGetValue( personGuid, out var loaded ) ? loaded : throw new InvalidOperationException( $"The {supervisor.Campus} check-in supervisor {personGuid} is not loaded." );
                logins.Add( new PlannedLogin( person, supervisor.Pin, anchor.Date.AddDays( -7 ).AddHours( 10 ), null, true, SeededRandom.StableGuid( $"check-in-supervisor:{supervisor.Campus}" ) ) );
            }

            return logins;
        }

        /// <summary>
        /// Sets an attribute value the way Rock's save hook stores it, adding it when the entity has none yet.
        /// </summary>
        private void SetAttributeValue( object attributeId, int entityId, string value )
        {
            _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [AttributeValue] WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId )
    INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [ValueAsNumeric], [ValueAsDateTime], [ValueAsBoolean], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( 0, @AttributeId, @EntityId, @Value, @ValueAsNumeric, @ValueAsDateTime, @ValueAsBoolean, 1, NEWID(), @ForeignKey, GETDATE(), GETDATE() )
ELSE
    UPDATE [AttributeValue] SET [Value] = @Value, [ValueAsNumeric] = @ValueAsNumeric, [ValueAsDateTime] = @ValueAsDateTime, [ValueAsBoolean] = @ValueAsBoolean, [IsPersistedValueDirty] = 1 WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId",
                new[] { P( "@AttributeId", attributeId ), P( "@EntityId", entityId ), P( "@Value", value ), P( "@ForeignKey", _foreignKey ) }.Concat( AttributeValueHook.Parameters( value, DateTime.Today.Year ) ).ToArray() );
        }
    }
}
