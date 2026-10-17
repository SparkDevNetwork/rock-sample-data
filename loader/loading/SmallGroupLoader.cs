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

using System.Data;
using System.Globalization;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /*
        10/16/26 - CLAUDE

        Small groups are bulk-copied like people, with Ids assigned up front. Bulk copy skips
        Rock's save hooks, so this loader also writes the group history those hooks would have
        written (GroupHistorical, GroupMemberHistorical, GroupLocationHistorical), the same way
        Rock/Model/Group/GroupMemberHistorical/GroupMemberHistorical.Logic.cs builds a row:
        one row per state, each expiring when the next begins, the last one current.

        Reason: Rock's group history screens need history rows that match the groups.
    */

    /// <summary>
    /// Loads small group plans: schedules, groups, meeting locations, members, attribute values,
    /// group history, and weekly group attendance.
    /// </summary>
    internal sealed class SmallGroupLoader
    {
        /// <summary>
        /// Alisha Marble, a staff member in the Cast, archives the archived groups and members.
        /// </summary>
        private static readonly Guid ArchiverGuid = new Guid( "69DC0FDC-B451-4303-BD91-EF17C0015D23" );

        private static readonly DateTime MaxExpireDateTime = new DateTime( 9999, 1, 1 );

        private readonly RockDatabase _db;
        private readonly SmallGroupRules _rules;
        private readonly SmallGroupSetup _setup;
        private readonly SundayCalendar _calendar;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly string _mainCampusKey;
        private readonly Dictionary<string, int> _campusIds;
        private int _archiverAliasId;

        public SmallGroupLoader( RockDatabase db, SmallGroupRules rules, SmallGroupSetup setup, SundayCalendar calendar, AnchorDates dates, string foreignKey, string seed, string mainCampusKey, Dictionary<string, int> campusIds )
        {
            _db = db;
            _rules = rules;
            _setup = setup;
            _calendar = calendar;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _mainCampusKey = mainCampusKey;
            _campusIds = campusIds;
        }

        /// <summary>
        /// Builds every row for every plan in memory, then bulk-copies each table in foreign key order.
        /// </summary>
        public void Load( IReadOnlyList<SmallGroupPlan> plans, PeopleLoader people )
        {
            var tables = new Tables( _db );
            var households = people.Households.ToDictionary( h => h.Cast.Key );
            _archiverAliasId = people.People[ArchiverGuid].AliasId;

            foreach ( var plan in plans )
            {
                var group = Resolve( plan, households, people );
                AddGroup( tables, group );
                AddMembers( tables, group );
                AddAttendance( tables, group );
            }

            tables.Write( _db );
            Console.WriteLine( $"  Small groups: {plans.Count( p => p.Lifecycle is ActiveLifecycle )} active, {plans.Count( p => p.Lifecycle is InactiveLifecycle )} inactive, {plans.Count( p => p.Lifecycle is ArchivedLifecycle )} archived; "
                + $"members: {tables.Members.Rows.Count:N0}, occurrences: {tables.Occurrences.Rows.Count:N0}, attendance: {tables.Attendance.Rows.Count:N0}" );
        }

        /// <summary>
        /// Resolves a plan's campus, dates, meeting place, and people for this load.
        /// </summary>
        private ResolvedGroup Resolve( SmallGroupPlan plan, Dictionary<string, LoadedHousehold> households, PeopleLoader people )
        {
            var campusKey = plan.Campus == "main" ? _mainCampusKey : plan.Campus;
            var group = new ResolvedGroup
            {
                Plan = plan,
                CampusId = _campusIds[campusKey],
                ParentId = _setup.CampusParentIds[campusKey],
                Started = _dates.Resolve( plan.Lifecycle.Started ).Value,
                Time = TimeSpan.Parse( plan.Schedule.Time, CultureInfo.InvariantCulture )
            };

            switch ( plan.Lifecycle )
            {
                case InactiveLifecycle inactive:
                    group.Ended = _dates.Resolve( inactive.Ended );
                    group.InactiveReasonId = _setup.InactiveReasonIds[inactive.Reason];
                    break;
                case ArchivedLifecycle archived:
                    group.Ended = _dates.Resolve( archived.Ended );
                    group.Archived = _dates.Resolve( archived.Archived );
                    break;
            }

            if ( plan.MeetingStyle is InPersonMeeting inPerson )
            {
                var host = households[inPerson.HostHousehold];
                group.LocationId = host.HomeLocationId ?? throw new InvalidOperationException( $"{plan.Name} meets at {inPerson.HostHousehold}, which has no home address." );
                var address = host.Cast.Address;
                group.LocationName = $"{address.Street1} {address.City}, {address.State} {address.PostalCode}";
            }

            foreach ( var member in plan.Members )
            {
                var left = member switch
                {
                    InactiveMember inactive => _dates.Resolve( inactive.Left ),
                    ArchivedMember archived => _dates.Resolve( archived.Left ),
                    _ => null
                };
                group.Members.Add( new ResolvedMember( member, people.People[member.Guid], _dates.Resolve( member.Joined ).Value, left ) );
            }

            return group;
        }

        /// <summary>
        /// Adds the group's schedule, group, meeting location, attribute values, and group and location history.
        /// </summary>
        private void AddGroup( Tables tables, ResolvedGroup group )
        {
            var plan = group.Plan;
            group.ScheduleId = tables.NextScheduleId++;
            group.GroupId = tables.NextGroupId++;
            var modified = group.Archived ?? group.Ended ?? group.Started;
            var scheduleName = $"{plan.Schedule.Day} at {DateTime.Today.Add( group.Time ).ToString( "h:mm tt", CultureInfo.GetCultureInfo( "en-US" ) )}";

            tables.Schedules.Rows.Add( group.ScheduleId, string.Empty, ( int ) plan.Schedule.Day, group.Time, true, 0, false, true, Guid.NewGuid(), _foreignKey, group.Started, group.Started );
            tables.Groups.Rows.Add( group.GroupId, false, group.ParentId, _setup.GroupTypeId, group.CampusId, plan.Name, plan.Description, false, !group.Ended.HasValue, 0, plan.Guid, _foreignKey, group.Started, modified,
                group.ScheduleId, plan.IsPublic, plan.Capacity, Value( group.Ended ), Value( group.InactiveReasonId ), group.Archived.HasValue, Value( group.Archived ), group.Archived.HasValue ? _archiverAliasId : DBNull.Value );

            var styleValue = plan.MeetingStyle is InPersonMeeting ? _rules.InPersonStyle : _rules.OnlineStyle;
            AddValue( tables, _setup.TopicAttributeId, group, _setup.TopicGuids[plan.Topic].ToString().ToLowerInvariant(), null );
            AddValue( tables, _setup.ChildCareAttributeId, group, plan.ChildCareProvided ? "True" : "False", plan.ChildCareProvided );
            AddValue( tables, _setup.MeetingStyleAttributeId, group, styleValue, null );
            if ( plan.MeetingStyle is OnlineMeeting online )
            {
                AddValue( tables, _setup.MeetingLinkAttributeId, group, online.Link, null );
            }

            // One history row per state: active, then inactive when it ended, then archived.
            var states = new List<(DateTime From, bool IsActive, bool IsArchived)> { (group.Started, true, false) };
            if ( group.Ended.HasValue )
            {
                states.Add( (group.Ended.Value, false, false) );
            }

            if ( group.Archived.HasValue )
            {
                states.Add( (group.Archived.Value, false, true) );
            }

            for ( var i = 0; i < states.Count; i++ )
            {
                var state = states[i];
                var isCurrent = i == states.Count - 1;
                tables.GroupHistory.Rows.Add( group.GroupId, plan.Name, _setup.GroupTypeId, _setup.GroupTypeName, group.CampusId, group.ParentId, plan.Description, group.ScheduleId, scheduleName, group.Started,
                    state.IsArchived, state.IsArchived ? group.Archived.Value : DBNull.Value, state.IsArchived ? _archiverAliasId : DBNull.Value,
                    state.IsActive, state.IsActive ? DBNull.Value : group.Ended.Value,
                    state.From, isCurrent ? MaxExpireDateTime : states[i + 1].From, isCurrent, group.Started, state.From, Guid.NewGuid(), _foreignKey );
            }

            if ( group.LocationId.HasValue )
            {
                var groupLocationId = tables.NextGroupLocationId++;
                tables.GroupLocations.Rows.Add( groupLocationId, group.GroupId, group.LocationId.Value, _setup.MeetingLocationTypeId, false, true, 0, false, Guid.NewGuid(), _foreignKey, group.Started, group.Started );
                tables.LocationHistory.Rows.Add( groupLocationId, group.GroupId, _setup.MeetingLocationTypeId, _setup.MeetingLocationTypeName, group.LocationId.Value, group.LocationName, group.Started,
                    group.Started, MaxExpireDateTime, true, group.Started, group.Started, Guid.NewGuid(), _foreignKey );
            }
        }

        /// <summary>
        /// Adds each membership and its history: an active row from joining, then an inactive or
        /// archived row from the day the member left. A pending request has one pending row.
        /// </summary>
        private void AddMembers( Tables tables, ResolvedGroup group )
        {
            foreach ( var member in group.Members )
            {
                var memberId = tables.NextMemberId++;
                var role = _setup.Roles[member.Plan.Role];
                var isArchived = member.Plan is ArchivedMember;
                var status = member.Plan switch
                {
                    PendingMember => 2,
                    InactiveMember or ArchivedMember => 0,
                    _ => 1
                };

                tables.Members.Rows.Add( memberId, false, group.GroupId, _setup.GroupTypeId, member.Person.PersonId, role.Id, status, member.Joined, Value( member.Left ), isArchived,
                    isArchived ? member.Left.Value : DBNull.Value, isArchived ? _archiverAliasId : DBNull.Value, Guid.NewGuid(), _foreignKey, member.Joined, member.Left ?? member.Joined );

                var states = new List<(DateTime From, int Status, bool IsArchived)> { (member.Joined, status == 2 ? 2 : 1, false) };
                if ( member.Left.HasValue )
                {
                    states.Add( (member.Left.Value, 0, isArchived) );
                }

                for ( var i = 0; i < states.Count; i++ )
                {
                    var state = states[i];
                    var isCurrent = i == states.Count - 1;
                    tables.MemberHistory.Rows.Add( memberId, group.GroupId, role.Id, role.Name, role.IsLeader, state.Status, state.IsArchived,
                        state.IsArchived ? member.Left.Value : DBNull.Value, state.IsArchived ? _archiverAliasId : DBNull.Value,
                        state.Status == 0 ? member.Left.Value : DBNull.Value,
                        state.From, isCurrent ? MaxExpireDateTime : states[i + 1].From, isCurrent, member.Joined, state.From, Guid.NewGuid(), _foreignKey );
                }
            }
        }

        /// <summary>
        /// Adds weekly attendance on the group's day and time, from its start (or the start of the history
        /// window) until it ended (or the anchor). Groups meet in the meeting months, stop for December after
        /// the configured day, and meet in summer only when they do. Skipped holiday weeks are marked
        /// "did not meet". A leader with a spotty habit records only some weeks.
        /// </summary>
        private void AddAttendance( Tables tables, ResolvedGroup group )
        {
            var plan = group.Plan;
            var calendar = _rules.Calendar;
            var attendance = _rules.Attendance;
            var random = new SeededRandom( $"{_seed}:small-groups:attendance:{plan.Guid}" );
            var from = group.Started > _calendar.HistoryStart ? group.Started : _calendar.HistoryStart;
            var until = group.Ended ?? _calendar.HistoryEnd.AddDays( 1 );
            var attenders = group.Members.Where( m => m.Plan is not PendingMember ).ToList();

            for ( var date = from.Date.AddDays( ( ( int ) plan.Schedule.Day - ( int ) from.DayOfWeek + 7 ) % 7 ); date < until; date = date.AddDays( 7 ) )
            {
                var sunday = SundayCalendar.SundayOnOrAfter( date );
                var holiday = _calendar.Holiday( sunday );
                var isSkipped = holiday != null && calendar.SkippedWeeks.Contains( holiday );
                var inSeason = calendar.MeetingMonths.Contains( date.Month )
                    ? date.Month != 12 || date.Day <= calendar.LastDecemberMeetingDay
                    : plan.MeetsInSummer;
                var isRecorded = plan.AttendanceHabit == AttendanceHabit.EveryWeek || random.Chance( attendance.SpottyRecordsPercent / 100.0 );
                if ( !( isSkipped || inSeason ) || !isRecorded )
                {
                    continue;
                }

                var occurrenceId = tables.NextOccurrenceId++;
                var start = date.Add( group.Time );
                tables.Occurrences.Rows.Add( occurrenceId, group.GroupId, Value( group.LocationId ), group.ScheduleId, date, sunday, int.Parse( date.ToString( "yyyyMMdd" ) ), isSkipped, Guid.NewGuid(), _foreignKey, start, start );
                if ( isSkipped )
                {
                    continue;
                }

                // Spouses usually come together: the first one in the family decides, the other usually follows.
                var familyAttended = new Dictionary<int, bool>();
                foreach ( var member in attenders.Where( m => m.Joined.Date <= date && ( m.Left == null || date < m.Left.Value.Date ) ) )
                {
                    var familyId = member.Person.FamilyId;
                    var didAttend = familyAttended.TryGetValue( familyId, out var partner ) && random.Chance( attendance.SpouseTogetherPercent / 100.0 )
                        ? partner
                        : random.Chance( member.Plan.AttendancePercent / 100.0 );
                    familyAttended.TryAdd( familyId, didAttend );
                    tables.Attendance.Rows.Add( occurrenceId, member.Person.AliasId, group.CampusId, start, didAttend, Guid.NewGuid(), _foreignKey, start, start );
                }
            }
        }

        private void AddValue( Tables tables, int attributeId, ResolvedGroup group, string value, bool? asBoolean )
        {
            tables.Values.Rows.Add( false, attributeId, group.GroupId, value, asBoolean.HasValue ? asBoolean.Value : DBNull.Value, true, Guid.NewGuid(), _foreignKey, group.Started, group.Started );
        }

        private static object Value<T>( T? value ) where T : struct
        {
            return value.HasValue ? value.Value : DBNull.Value;
        }

        /// <summary>
        /// One plan, resolved against this load's anchor and loaded people.
        /// </summary>
        private sealed class ResolvedGroup
        {
            public SmallGroupPlan Plan { get; set; }

            public int GroupId { get; set; }

            public int ScheduleId { get; set; }

            public int CampusId { get; set; }

            public int ParentId { get; set; }

            public DateTime Started { get; set; }

            public DateTime? Ended { get; set; }

            public DateTime? Archived { get; set; }

            public int? InactiveReasonId { get; set; }

            public TimeSpan Time { get; set; }

            public int? LocationId { get; set; }

            public string LocationName { get; set; }

            public List<ResolvedMember> Members { get; } = new List<ResolvedMember>();
        }

        private sealed record ResolvedMember( PlannedMember Plan, LoadedPerson Person, DateTime Joined, DateTime? Left );

        /// <summary>
        /// The in-memory tables and the next Id for each table whose Ids other rows point to.
        /// </summary>
        private sealed class Tables
        {
            public Tables( RockDatabase db )
            {
                NextScheduleId = NextId( db, "Schedule" );
                NextGroupId = NextId( db, "Group" );
                NextGroupLocationId = NextId( db, "GroupLocation" );
                NextMemberId = NextId( db, "GroupMember" );
                NextOccurrenceId = NextId( db, "AttendanceOccurrence" );
            }

            public int NextScheduleId { get; set; }

            public int NextGroupId { get; set; }

            public int NextGroupLocationId { get; set; }

            public int NextMemberId { get; set; }

            public int NextOccurrenceId { get; set; }

            public DataTable Schedules { get; } = TableFactory.Create( "Id:int", "iCalendarContent:string", "WeeklyDayOfWeek:int", "WeeklyTimeOfDay:time", "IsActive:bool", "Order:int", "AutoInactivateWhenComplete:bool", "IsPublic:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Groups { get; } = TableFactory.Create( "Id:int", "IsSystem:bool", "ParentGroupId:int", "GroupTypeId:int", "CampusId:int", "Name:string", "Description:string", "IsSecurityRole:bool", "IsActive:bool", "Order:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date",
                "ScheduleId:int", "IsPublic:bool", "GroupCapacity:int", "InactiveDateTime:date", "InactiveReasonValueId:int", "IsArchived:bool", "ArchivedDateTime:date", "ArchivedByPersonAliasId:int" );

            public DataTable GroupLocations { get; } = TableFactory.Create( "Id:int", "GroupId:int", "LocationId:int", "GroupLocationTypeValueId:int", "IsMailingLocation:bool", "IsMappedLocation:bool", "Order:int", "IsOverflowLocation:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Members { get; } = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date", "InactiveDateTime:date", "IsArchived:bool", "ArchivedDateTime:date", "ArchivedByPersonAliasId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Values { get; } = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsBoolean:bool", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable GroupHistory { get; } = TableFactory.Create( "GroupId:int", "GroupName:string", "GroupTypeId:int", "GroupTypeName:string", "CampusId:int", "ParentGroupId:int", "Description:string", "ScheduleId:int", "ScheduleName:string", "ScheduleModifiedDateTime:date",
                "IsArchived:bool", "ArchivedDateTime:date", "ArchivedByPersonAliasId:int", "IsActive:bool", "InactiveDateTime:date", "EffectiveDateTime:date", "ExpireDateTime:date", "CurrentRowIndicator:bool", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );

            public DataTable MemberHistory { get; } = TableFactory.Create( "GroupMemberId:int", "GroupId:int", "GroupRoleId:int", "GroupRoleName:string", "IsLeader:bool", "GroupMemberStatus:int", "IsArchived:bool", "ArchivedDateTime:date", "ArchivedByPersonAliasId:int", "InactiveDateTime:date",
                "EffectiveDateTime:date", "ExpireDateTime:date", "CurrentRowIndicator:bool", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );

            public DataTable LocationHistory { get; } = TableFactory.Create( "GroupLocationId:int", "GroupId:int", "GroupLocationTypeValueId:int", "GroupLocationTypeName:string", "LocationId:int", "LocationName:string", "LocationModifiedDateTime:date",
                "EffectiveDateTime:date", "ExpireDateTime:date", "CurrentRowIndicator:bool", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );

            public DataTable Occurrences { get; } = TableFactory.Create( "Id:int", "GroupId:int", "LocationId:int", "ScheduleId:int", "OccurrenceDate:date", "SundayDate:date", "OccurrenceDateKey:int", "DidNotOccur:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Attendance { get; } = TableFactory.Create( "OccurrenceId:int", "PersonAliasId:int", "CampusId:int", "StartDateTime:date", "DidAttend:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            /// <summary>
            /// Bulk-copies every table, parents before the rows that point to them.
            /// </summary>
            public void Write( RockDatabase db )
            {
                db.BulkInsert( "Schedule", Schedules, keepIdentity: true );
                db.BulkInsert( "Group", Groups, keepIdentity: true );
                db.BulkInsert( "GroupLocation", GroupLocations, keepIdentity: true );
                db.BulkInsert( "GroupMember", Members, keepIdentity: true );
                db.BulkInsert( "AttributeValue", Values, keepIdentity: false );
                db.BulkInsert( "GroupHistorical", GroupHistory, keepIdentity: false );
                db.BulkInsert( "GroupMemberHistorical", MemberHistory, keepIdentity: false );
                db.BulkInsert( "GroupLocationHistorical", LocationHistory, keepIdentity: false );
                db.BulkInsert( "AttendanceOccurrence", Occurrences, keepIdentity: true );
                db.BulkInsert( "Attendance", Attendance, keepIdentity: false );
            }

            private static int NextId( RockDatabase db, string table )
            {
                return Convert.ToInt32( db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
            }
        }
    }
}
