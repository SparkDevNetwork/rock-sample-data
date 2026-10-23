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
    /*
        10/15/26 - CLAUDE

        The spike writes configuration with SQL so it can run outside Rock. The
        final loader lives in Rock and saves these few rows through Rock's
        services instead, which also runs Rock's save hooks and cache updates.
        Every step here checks for existing rows first, so a load can be re-run.

        Reason: Phase 0 proves the data and the speed; the service-based loader comes later.
    */

    /// <summary>
    /// Loads the church configuration: connection statuses, service time slots, campuses with
    /// their campus schedules, funds with per-campus child accounts, and check-in rooms and times.
    /// </summary>
    internal sealed class ConfigurationLoader
    {
        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _dataset;
        private readonly string _foreignKey;

        /// <summary>
        /// Creates a loader for one dataset (for example "core-1").
        /// </summary>
        public ConfigurationLoader( RockDatabase db, SampleDataSource source, string dataset, string foreignKey )
        {
            _db = db;
            _source = source;
            _dataset = dataset;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// The campuses included in this dataset, keyed by campus key, with their Rock Ids.
        /// Filled by <see cref="Load"/>.
        /// </summary>
        public Dictionary<string, int> CampusIds { get; } = new Dictionary<string, int>();

        /// <summary>
        /// What later steps need about each campus: services, rooms, and location. Filled by <see cref="Load"/>.
        /// </summary>
        public Dictionary<string, CampusPlan> CampusPlans { get; } = new Dictionary<string, CampusPlan>();

        /// <summary>
        /// Check-in group Ids by group key (for example "grades-2-3").
        /// </summary>
        public Dictionary<string, int> CheckInGroupIdsByKey { get; private set; } = new Dictionary<string, int>();

        /// <summary>
        /// The adult worship attendance group (Weekend Service).
        /// </summary>
        public int WorshipGroupIdValue { get; private set; }

        /// <summary>
        /// A check-in kiosk device for check-in records, if Rock has one.
        /// </summary>
        public int? KioskDeviceId { get; private set; }

        /// <summary>
        /// Runs every configuration step in order.
        /// </summary>
        public void Load()
        {
            LoadConnectionStatuses();
            var slotIds = LoadSchedules();
            var scheduleTypeIds = LoadScheduleTypes();
            var campuses = _source.Campuses.Campuses.Where( c => c.Datasets.Contains( _dataset ) ).ToList();
            LoadCampuses( campuses );
            LoadCampusSchedules( campuses, slotIds, scheduleTypeIds );
            LoadAccounts( campuses );
            LoadCheckIn( campuses, slotIds );
        }

        /// <summary>
        /// Renames Rock's system connection statuses and adds any new ones.
        /// </summary>
        private void LoadConnectionStatuses()
        {
            var statuses = _source.ConnectionStatuses.Statuses;
            var anyDefault = statuses.First( s => s.RockDefault );
            var definedTypeId = Convert.ToInt32( _db.Scalar( "SELECT [DefinedTypeId] FROM [DefinedValue] WHERE [Guid] = @Guid", P( "@Guid", anyDefault.Guid ) ) );

            foreach ( var status in statuses )
            {
                UpsertDefinedValue( _db, _foreignKey, definedTypeId, status.Guid, status.Name, status.Description, status.Order );
            }

            _db.Log.Write( $"  Connection statuses: {statuses.Count}" );
        }

        /// <summary>
        /// Adds any service time slots Rock does not ship with. Returns slot key to Schedule Id.
        /// </summary>
        private Dictionary<string, int> LoadSchedules()
        {
            var config = _source.Schedules;
            var slotIds = new Dictionary<string, int>();

            // New slots go in the same category as Rock's built-in service times.
            var categoryId = _db.Scalar( "SELECT [CategoryId] FROM [Schedule] WHERE [Guid] = @Guid", P( "@Guid", config.Slots.First( s => s.RockDefault ).Guid ) );

            foreach ( var slot in config.Slots )
            {
                var id = _db.IdByGuid( "Schedule", slot.Guid );
                if ( id == null )
                {
                    id = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Schedule] ( [Name], [iCalendarContent], [CheckInStartOffsetMinutes], [CheckInEndOffsetMinutes], [EffectiveStartDate], [CategoryId], [IsActive], [Order], [AutoInactivateWhenComplete], [IsPublic], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Calendar, @CheckInStart, @CheckInEnd, '2013-05-01', @CategoryId, 1, 0, 0, 1, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                        P( "@Name", slot.Name ),
                        P( "@Calendar", BuildWeeklyCalendar( slot ) ),
                        P( "@CheckInStart", config.CheckInStartOffsetMinutes ),
                        P( "@CheckInEnd", config.CheckInEndOffsetMinutes ),
                        P( "@CategoryId", categoryId ),
                        P( "@Guid", slot.Guid ),
                        P( "@ForeignKey", _foreignKey ) ) );
                }

                slotIds[slot.Key] = id.Value;
            }

            _db.Log.Write( $"  Service time slots: {slotIds.Count}" );
            return slotIds;
        }

        /// <summary>
        /// Adds campus schedule types (such as Student Service) beside Rock's Weekend Service.
        /// Returns type key to Defined Value Id.
        /// </summary>
        private Dictionary<string, int> LoadScheduleTypes()
        {
            var types = _source.Schedules.ScheduleTypes;
            var definedTypeId = Convert.ToInt32( _db.Scalar( "SELECT [DefinedTypeId] FROM [DefinedValue] WHERE [Guid] = @Guid", P( "@Guid", types.First( t => t.RockDefault ).Guid ) ) );
            var ids = new Dictionary<string, int>();
            var order = 0;

            foreach ( var type in types )
            {
                ids[type.Key] = UpsertDefinedValue( _db, _foreignKey, definedTypeId, type.Guid, type.Name, null, order++ );
            }

            return ids;
        }

        /// <summary>
        /// Renames Rock's main campus and adds the other campuses in this dataset, each with a location.
        /// </summary>
        private void LoadCampuses( List<CampusDefinition> campuses )
        {
            var main = campuses.First( c => c.RockDefault );
            var mainRow = _db.Query( "SELECT [Id], [LocationId], [CampusStatusValueId], [CampusTypeValueId] FROM [Campus] WHERE [Guid] = @Guid", P( "@Guid", main.Guid ) ).Single();
            var campusLocationTypeId = _db.Scalar( "SELECT [LocationTypeValueId] FROM [Location] WHERE [Id] = @Id", P( "@Id", mainRow["LocationId"] ) );
            var order = 0;

            foreach ( var campus in campuses )
            {
                int campusId;
                int locationId;

                if ( campus.RockDefault )
                {
                    campusId = Convert.ToInt32( mainRow["Id"] );
                    locationId = Convert.ToInt32( mainRow["LocationId"] );
                    _db.Execute( "UPDATE [Campus] SET [Name] = @Name, [ShortCode] = @ShortCode, [IsActive] = 1, [Order] = @Order WHERE [Id] = @Id",
                        P( "@Name", campus.Name ), P( "@ShortCode", campus.ShortCode ), P( "@Order", order ), P( "@Id", campusId ) );
                    _db.Execute( "UPDATE [Location] SET [Name] = @Name, [GeoPoint] = geography::Point( @Lat, @Lng, 4326 ) WHERE [Id] = @Id",
                        P( "@Name", campus.Name ), P( "@Lat", campus.Location.Latitude ), P( "@Lng", campus.Location.Longitude ), P( "@Id", locationId ) );
                }
                else
                {
                    locationId = _db.IdByGuid( "Location", campus.Location.Guid ) ?? InsertLocation( campus.Name, null, campusLocationTypeId, campus.Location.Guid, campus.Location.Latitude, campus.Location.Longitude, null );
                    campusId = _db.IdByGuid( "Campus", campus.Guid ) ?? Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Campus] ( [IsSystem], [Name], [ShortCode], [LocationId], [CampusStatusValueId], [CampusTypeValueId], [IsActive], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Name, @ShortCode, @LocationId, @StatusId, @TypeId, 1, @Order, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                        P( "@Name", campus.Name ),
                        P( "@ShortCode", campus.ShortCode ),
                        P( "@LocationId", locationId ),
                        P( "@StatusId", mainRow["CampusStatusValueId"] ),
                        P( "@TypeId", mainRow["CampusTypeValueId"] ),
                        P( "@Order", order ),
                        P( "@Guid", campus.Guid ),
                        P( "@ForeignKey", _foreignKey ) ) );
                }

                CampusIds[campus.Key] = campusId;
                order++;
            }

            _db.Log.Write( $"  Campuses: {string.Join( ", ", campuses.Select( c => c.Name ) )}" );
        }

        /// <summary>
        /// Gives each campus its service times through Rock's campus schedules.
        /// </summary>
        private void LoadCampusSchedules( List<CampusDefinition> campuses, Dictionary<string, int> slotIds, Dictionary<string, int> scheduleTypeIds )
        {
            var count = 0;
            foreach ( var campus in campuses )
            {
                var campusId = CampusIds[campus.Key];
                _db.Execute( "DELETE FROM [CampusSchedule] WHERE [CampusId] = @CampusId", P( "@CampusId", campusId ) );

                var order = 0;
                foreach ( var schedule in campus.Schedules )
                {
                    _db.Execute( @"
INSERT INTO [CampusSchedule] ( [CampusId], [ScheduleId], [ScheduleTypeValueId], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @CampusId, @ScheduleId, @TypeId, @Order, NEWID(), @ForeignKey, GETDATE(), GETDATE() )",
                        P( "@CampusId", campusId ),
                        P( "@ScheduleId", slotIds[schedule.Slot] ),
                        P( "@TypeId", scheduleTypeIds[schedule.Type] ),
                        P( "@Order", order++ ),
                        P( "@ForeignKey", _foreignKey ) );
                    count++;
                }
            }

            _db.Log.Write( $"  Campus schedules: {count}" );
        }

        /// <summary>
        /// Adds the funds Rock does not ship, names every fund as configured, turns on campus child accounts for
        /// each per-campus fund, and adds a child account per campus.
        /// </summary>
        private void LoadAccounts( List<CampusDefinition> campuses )
        {
            var count = 0;
            foreach ( var fund in _source.Accounts.Accounts )
            {
                if ( !fund.RockDefault && _db.IdByGuid( "FinancialAccount", fund.Guid ) == null )
                {
                    _db.Execute( @"
INSERT INTO [FinancialAccount] ( [Name], [PublicName], [IsTaxDeductible], [Order], [IsActive], [IsPublic], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @Name, @PublicName, @TaxDeductible, @Order, 1, 1, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                        P( "@Name", fund.Name ),
                        P( "@PublicName", fund.PublicName ),
                        P( "@TaxDeductible", fund.IsTaxDeductible ),
                        P( "@Order", fund.Order ),
                        P( "@Guid", fund.Guid ),
                        P( "@ForeignKey", _foreignKey ) );
                    _db.Log.Write( $"  Fund added: {fund.Name}" );
                }

                /*
                    10/23/26 - CLAUDE

                    The names come from configuration for Rock's own funds too, because the building campaign's
                    fund is Rock's Building Fund, renamed (decision 98). A fund with no public
                    name keeps Rock's.
                */
                var parentId = _db.RequireIdByGuid( "FinancialAccount", fund.Guid );
                var usesChildren = fund.CampusAccounts.Count > 0;
                _db.Execute( @"
UPDATE [FinancialAccount]
SET [Name] = @Name, [PublicName] = COALESCE( @PublicName, [PublicName] ), [IsActive] = 1, [Order] = @Order, [UsesCampusChildAccounts] = @UsesChildren
WHERE [Id] = @Id",
                    P( "@Name", fund.Name ), P( "@PublicName", fund.PublicName ), P( "@Order", fund.Order ), P( "@UsesChildren", usesChildren ), P( "@Id", parentId ) );

                var childOrder = 0;
                foreach ( var child in fund.CampusAccounts.Where( a => CampusIds.ContainsKey( a.Campus ) ) )
                {
                    if ( _db.IdByGuid( "FinancialAccount", child.Guid ) == null )
                    {
                        _db.Execute( @"
INSERT INTO [FinancialAccount] ( [Name], [PublicName], [ParentAccountId], [CampusId], [IsTaxDeductible], [Order], [IsActive], [IsPublic], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @Name, @PublicName, @ParentId, @CampusId, @TaxDeductible, @Order, 1, 1, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                            P( "@Name", child.Name ),
                            P( "@PublicName", child.PublicName ),
                            P( "@ParentId", parentId ),
                            P( "@CampusId", CampusIds[child.Campus] ),
                            P( "@TaxDeductible", fund.IsTaxDeductible ),
                            P( "@Order", childOrder ),
                            P( "@Guid", child.Guid ),
                            P( "@ForeignKey", _foreignKey ) );
                        count++;
                    }

                    childOrder++;
                }
            }

            _db.Log.Write( $"  Campus child accounts added: {count}" );
        }

        /// <summary>
        /// Gives every campus the same building and rooms, places each check-in group in its room at
        /// every campus, and sets each room's schedules to the campus's service times.
        /// </summary>
        private void LoadCheckIn( List<CampusDefinition> campuses, Dictionary<string, int> slotIds )
        {
            var checkIn = _source.CheckIn;
            var roomTypeId = LocationTypeId( "Room" );
            var buildingTypeId = LocationTypeId( "Building" );
            var groupIds = CheckInGroupIds();
            var worshipGroupId = WorshipGroupId();
            var placements = 0;
            var slotStarts = _source.Schedules.Slots.ToDictionary( s => s.Key, s => TimeSpan.Parse( s.Start ) );

            CheckInGroupIdsByKey = groupIds;
            WorshipGroupIdValue = worshipGroupId;
            var kiosk = _db.Scalar( @"
SELECT TOP 1 [d].[Id]
FROM [Device] AS [d]
INNER JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [d].[DeviceTypeValueId]
WHERE [dv].[Value] = 'Check-in Kiosk'
ORDER BY [d].[Id]" );
            KioskDeviceId = kiosk == null ? null : Convert.ToInt32( kiosk );

            foreach ( var campus in campuses )
            {
                var campusLocationId = Convert.ToInt32( _db.Scalar( "SELECT [LocationId] FROM [Campus] WHERE [Id] = @Id", P( "@Id", CampusIds[campus.Key] ) ) );
                var buildingId = ChildLocationId( campusLocationId, checkIn.Building ) ?? InsertLocation( checkIn.Building, campusLocationId, buildingTypeId, Guid.NewGuid(), null, null, null );

                var weekendSlots = campus.Schedules.Where( s => s.Type == "weekend" ).Select( s => slotIds[s.Slot] ).ToList();
                var studentSlots = campus.Schedules.Where( s => s.Type == "student" ).Select( s => slotIds[s.Slot] ).ToList();
                if ( studentSlots.Count == 0 )
                {
                    // Campuses without a midweek student service check students in on Sunday morning.
                    studentSlots.Add( slotIds[checkIn.StudentFallbackSlot] );
                }

                var plan = new CampusPlan
                {
                    Key = campus.Key,
                    CampusId = CampusIds[campus.Key],
                    LocationId = campusLocationId,
                    Services = campus.Schedules.Where( s => s.Type == "weekend" ).Select( s => new ServicePlan( slotIds[s.Slot], slotStarts[s.Slot] ) ).ToList(),
                    StudentService = campus.Schedules.Where( s => s.Type == "student" ).Select( s => new ServicePlan( slotIds[s.Slot], slotStarts[s.Slot] ) ).FirstOrDefault()
                        ?? new ServicePlan( slotIds[checkIn.StudentFallbackSlot], slotStarts[checkIn.StudentFallbackSlot] )
                };
                CampusPlans[campus.Key] = plan;

                foreach ( var group in checkIn.Groups )
                {
                    var threshold = checkIn.RoomSoftThreshold.TryGetValue( group.Key, out var value ) ? value : ( int? ) null;
                    var roomId = ChildLocationId( buildingId, group.Room ) ?? InsertLocation( group.Room, buildingId, roomTypeId, Guid.NewGuid(), null, null, threshold );
                    var groupLocationId = EnsureGroupLocation( _db, _foreignKey, groupIds[group.Key], roomId );
                    SetGroupLocationSchedules( _db, groupLocationId, group.Schedules == "student" ? studentSlots : weekendSlots );
                    plan.RoomIds[group.Key] = roomId;
                    placements++;
                }

                foreach ( var room in _source.Serving.Rooms )
                {
                    plan.ServingRoomIds[room] = ChildLocationId( buildingId, room ) ?? InsertLocation( room, buildingId, roomTypeId, Guid.NewGuid(), null, null, null );
                }

                // Adult worship attendance is recorded against the campus itself.
                var worshipLocationId = EnsureGroupLocation( _db, _foreignKey, worshipGroupId, campusLocationId );
                SetGroupLocationSchedules( _db, worshipLocationId, weekendSlots );
            }

            _db.Log.Write( $"  Check-in room placements: {placements} across {campuses.Count} campus(es), plus {_source.Serving.Rooms.Count} serving rooms per campus" );
        }

        /// <summary>
        /// Finds each configured check-in group by its area and name.
        /// </summary>
        private Dictionary<string, int> CheckInGroupIds()
        {
            var ids = new Dictionary<string, int>();
            foreach ( var area in _source.CheckIn.Areas )
            {
                foreach ( var key in area.Groups )
                {
                    var name = _source.CheckIn.Groups.Single( g => g.Key == key ).Name;
                    var id = _db.Scalar( @"
SELECT TOP 1 [g].[Id]
FROM [Group] AS [g]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [gt].[Guid] = @AreaGuid AND [g].[Name] = @Name
ORDER BY [g].[Id]", P( "@AreaGuid", area.Guid ), P( "@Name", name ) );
                    ids[key] = id == null ? throw new InvalidOperationException( $"Check-in group '{name}' was not found in area '{area.Name}'." ) : Convert.ToInt32( id );
                }
            }

            return ids;
        }

        /// <summary>
        /// Finds the Weekend Service group under Rock's Service Attendance template.
        /// </summary>
        private int WorshipGroupId()
        {
            var worship = _source.CheckIn.Worship;
            var id = _db.Scalar( @"
SELECT TOP 1 [g].[Id]
FROM [Group] AS [g]
INNER JOIN [GroupTypeAssociation] AS [a] ON [a].[ChildGroupTypeId] = [g].[GroupTypeId]
INNER JOIN [GroupType] AS [t] ON [t].[Id] = [a].[GroupTypeId]
WHERE [t].[Guid] = @TemplateGuid AND [g].[Name] = @Name
ORDER BY [g].[Id]", P( "@TemplateGuid", worship.Template.Guid ), P( "@Name", worship.GroupName ) );
            return id == null ? throw new InvalidOperationException( $"Worship group '{worship.GroupName}' was not found." ) : Convert.ToInt32( id );
        }

        /// <summary>
        /// Returns the Id of a location type (for example "Room") from the same defined type as campus locations.
        /// </summary>
        private int? LocationTypeId( string value )
        {
            var id = _db.Scalar( @"
SELECT TOP 1 [dv].[Id]
FROM [DefinedValue] AS [dv]
WHERE [dv].[Value] = @Value
    AND [dv].[DefinedTypeId] = ( SELECT [DefinedTypeId] FROM [DefinedValue] WHERE [Id] = ( SELECT TOP 1 [LocationTypeValueId] FROM [Location] AS [l] INNER JOIN [Campus] AS [c] ON [c].[LocationId] = [l].[Id] ) )",
                P( "@Value", value ) );
            return id == null ? null : Convert.ToInt32( id );
        }

        /// <summary>
        /// Returns the Id of a named child location, or null when there is none.
        /// </summary>
        private int? ChildLocationId( int parentId, string name )
        {
            var id = _db.Scalar( "SELECT TOP 1 [Id] FROM [Location] WHERE [ParentLocationId] = @ParentId AND [Name] = @Name ORDER BY [Id]", P( "@ParentId", parentId ), P( "@Name", name ) );
            return id == null ? null : Convert.ToInt32( id );
        }

        /// <summary>
        /// Inserts a named location and returns its Id.
        /// </summary>
        private int InsertLocation( string name, int? parentId, object typeId, Guid guid, double? latitude, double? longitude, int? softThreshold )
        {
            var geo = latitude.HasValue ? "geography::Point( @Lat, @Lng, 4326 )" : "NULL";
            return Convert.ToInt32( _db.Scalar( $@"
INSERT INTO [Location] ( [ParentLocationId], [Name], [IsActive], [LocationTypeValueId], [GeoPoint], [SoftRoomThreshold], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @ParentId, @Name, 1, @TypeId, {geo}, @Threshold, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                P( "@ParentId", parentId ),
                P( "@Name", name ),
                P( "@TypeId", typeId ),
                P( "@Lat", latitude ),
                P( "@Lng", longitude ),
                P( "@Threshold", softThreshold ),
                P( "@Guid", guid ),
                P( "@ForeignKey", _foreignKey ) ) );
        }

        /// <summary>
        /// Returns the Id of the group's location row, adding it when missing.
        /// </summary>
        internal static int EnsureGroupLocation( RockDatabase db, string foreignKey, int groupId, int locationId )
        {
            var id = db.Scalar( "SELECT TOP 1 [Id] FROM [GroupLocation] WHERE [GroupId] = @GroupId AND [LocationId] = @LocationId", P( "@GroupId", groupId ), P( "@LocationId", locationId ) );
            if ( id != null )
            {
                return Convert.ToInt32( id );
            }

            return Convert.ToInt32( db.Scalar( @"
INSERT INTO [GroupLocation] ( [GroupId], [LocationId], [IsMailingLocation], [IsMappedLocation], [Order], [IsOverflowLocation], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @GroupId, @LocationId, 0, 0, 0, 0, NEWID(), @ForeignKey, GETDATE(), GETDATE() )",
                P( "@GroupId", groupId ), P( "@LocationId", locationId ), P( "@ForeignKey", foreignKey ) ) );
        }

        /// <summary>
        /// Replaces a group location's schedules (and their capacity settings) with the given service times.
        /// </summary>
        internal static void SetGroupLocationSchedules( RockDatabase db, int groupLocationId, List<int> scheduleIds )
        {
            db.Execute( "DELETE FROM [GroupLocationScheduleConfig] WHERE [GroupLocationId] = @Id; DELETE FROM [GroupLocationSchedule] WHERE [GroupLocationId] = @Id", P( "@Id", groupLocationId ) );
            foreach ( var scheduleId in scheduleIds.Distinct() )
            {
                db.Execute( "INSERT INTO [GroupLocationSchedule] ( [GroupLocationId], [ScheduleId] ) VALUES ( @GroupLocationId, @ScheduleId )",
                    P( "@GroupLocationId", groupLocationId ), P( "@ScheduleId", scheduleId ) );
            }
        }

        /// <summary>
        /// Updates a defined value by Guid, or inserts it into the given defined type. Returns its Id.
        /// </summary>
        internal static int UpsertDefinedValue( RockDatabase db, string foreignKey, int definedTypeId, Guid guid, string value, string description, int order )
        {
            var id = db.IdByGuid( "DefinedValue", guid );
            if ( id != null )
            {
                db.Execute( "UPDATE [DefinedValue] SET [Value] = @Value, [Description] = COALESCE( @Description, [Description] ), [Order] = @Order, [IsActive] = 1 WHERE [Id] = @Id",
                    P( "@Value", value ), P( "@Description", description ), P( "@Order", order ), P( "@Id", id ) );
                return id.Value;
            }

            return Convert.ToInt32( db.Scalar( @"
INSERT INTO [DefinedValue] ( [IsSystem], [DefinedTypeId], [Order], [Value], [Description], [IsActive], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @TypeId, @Order, @Value, @Description, 1, @Guid, @ForeignKey, GETDATE(), GETDATE() )",
                P( "@TypeId", definedTypeId ), P( "@Order", order ), P( "@Value", value ), P( "@Description", description ), P( "@Guid", guid ), P( "@ForeignKey", foreignKey ) ) );
        }

        /// <summary>
        /// Builds the iCalendar text for a weekly slot, in the same shape as Rock's built-in service times.
        /// </summary>
        private static string BuildWeeklyCalendar( ScheduleSlot slot )
        {
            var start = TimeSpan.Parse( slot.Start );
            var end = start.Add( TimeSpan.FromMinutes( slot.Minutes ) );
            var day = slot.Day.Substring( 0, 2 ).ToUpperInvariant();
            return $"BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTEND:20130501T{end:hhmm}00\r\nDTSTART:20130501T{start:hhmm}00\r\nRRULE:FREQ=WEEKLY;BYDAY={day}\r\nEND:VEVENT\r\nEND:VCALENDAR";
        }
    }
}

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// What later steps need about one campus.
    /// </summary>
    internal sealed class CampusPlan
    {
        public string Key { get; set; }

        public int CampusId { get; set; }

        public int LocationId { get; set; }

        /// <summary>
        /// The campus's Sunday services, earliest first.
        /// </summary>
        public List<ServicePlan> Services { get; set; } = new List<ServicePlan>();

        /// <summary>
        /// The service students check in at.
        /// </summary>
        public ServicePlan StudentService { get; set; }

        /// <summary>
        /// Room location Ids by check-in group key.
        /// </summary>
        public Dictionary<string, int> RoomIds { get; } = new Dictionary<string, int>();

        /// <summary>
        /// The rooms serving teams use beyond the check-in rooms (serving.json), by room name.
        /// </summary>
        public Dictionary<string, int> ServingRoomIds { get; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// One service: its schedule and its start time.
    /// </summary>
    internal sealed record ServicePlan( int ScheduleId, TimeSpan Start );
}
