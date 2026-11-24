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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/19/26 - CLAUDE

        Group scheduling keeps a volunteer's rotation on the group member (a schedule template and
        start date) and their preferred service and room as a GroupMemberAssignment row. Each time
        they are asked to serve is an Attendance row on the team's occurrence for that room, service,
        and Sunday, the same row Rock's scheduler writes: RequestedToAttend, then RSVP and
        ScheduledToAttend as they answer, then DidAttend. See AttendanceService.ScheduledPersonAddPending
        and ScheduledPersonConfirm in Rock.

        Reason: every scheduling screen (scheduler, toolbox, status board, analytics) reads these rows.
    */

    /// <summary>
    /// Loads serving team plans: members with their rotations and preferences, staffing that fits
    /// each campus's volunteers, blackout dates, and serving history and the weeks ahead.
    /// </summary>
    internal sealed class ServingLoader
    {
        private static readonly Guid DeclineReasonTypeGuid = new Guid( "70C9F9C4-20CC-43DD-888D-9243853A0E52" );
        private static readonly DateTime MaxExpireDateTime = new DateTime( 9999, 1, 1 );

        private const int RsvpNo = 0;
        private const int RsvpYes = 1;
        private const int RsvpUnknown = 3;

        private readonly RockDatabase _db;
        private readonly ServingConfiguration _config;
        private readonly ServingSetup _setup;
        private readonly Dictionary<string, CampusPlan> _campuses;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly CheckInDevices _devices;
        private readonly DateTime _historyStart;
        private int _reminderOffsetDays = 2;
        private int? _searchTypeId;
        private CheckInSessions _sessions;

        /// <summary>
        /// Whether the student services meet in the week that ends on a Sunday, from the attendance plan, so Student Night's
        /// volunteers serve the nights its students come.
        /// </summary>
        public Func<DateTime, bool> StudentServiceRuns { get; init; } = _ => true;
        private Dictionary<int, List<LoadedPerson>> _membersByFamily;

        public ServingLoader( RockDatabase db, ServingConfiguration config, ServingSetup setup, Dictionary<string, CampusPlan> campuses, AnchorDates dates, string foreignKey, string seed, CheckInDevices devices, DateTime historyStart )
        {
            _db = db;
            _config = config;
            _setup = setup;
            _campuses = campuses;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _devices = devices;
            _historyStart = historyStart;
        }

        /// <summary>
        /// Builds every row for every team in memory, sets each position's staffing, then bulk-copies.
        /// </summary>
        public void Load( IReadOnlyList<ServingTeamPlan> plans, PeopleLoader people, Dictionary<int, HashSet<DateTime>> familySundays )
        {
            _searchTypeId = _db.IdByGuid( "DefinedValue", AttendanceLoader.PhoneSearchTypeGuid );
            _sessions = new CheckInSessions( _db, _foreignKey );
            _membersByFamily = people.Households.GroupBy( h => h.FamilyId ).ToDictionary( g => g.Key, g => g.First().Members );
            var templateIds = _db.Query( "SELECT [Id], [Name] FROM [GroupMemberScheduleTemplate]" ).ToDictionary( r => ( string ) r["Name"], r => Convert.ToInt32( r["Id"] ) );
            var declineReasonIds = _db.Query( "SELECT [dv].[Id] FROM [DefinedValue] AS [dv] INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId] WHERE [dt].[Guid] = @Guid",
                P( "@Guid", DeclineReasonTypeGuid ) ).Select( r => Convert.ToInt32( r["Id"] ) ).ToList();
            var roles = _db.Query( "SELECT [Id], [Name], [IsLeader] FROM [GroupTypeRole] WHERE [Id] IN ( @Leader, @Member )", P( "@Leader", _setup.LeaderRoleId ), P( "@Member", _setup.MemberRoleId ) )
                .ToDictionary( r => Convert.ToInt32( r["Id"] ), r => (Name: ( string ) r["Name"], IsLeader: Convert.ToBoolean( r["IsLeader"] )) );

            var tables = new Tables( _db );
            var today = _dates.Anchor.Date;
            _reminderOffsetDays = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( [ScheduleReminderEmailOffsetDays], 2 ) FROM [GroupType] WHERE [Id] = @Id", P( "@Id", _setup.GroupTypeId ) ) );
            // The whole history window, like Sunday attendance, then the weeks ahead.
            var firstSunday = SundayCalendar.SundayOnOrAfter( _historyStart );
            var lastSunday = SundayCalendar.SundayOnOrAfter( today ).AddDays( 7 * _config.Scheduling.FutureWeeks );
            var sundays = Enumerable.Range( 0, ( ( lastSunday - firstSunday ).Days / 7 ) + 1 ).Select( i => firstSunday.AddDays( 7 * i ) ).ToList();

            foreach ( var plan in plans )
            {
                var team = _config.Teams.Single( t => t.Key == plan.Team );
                var campus = _campuses[plan.Campus];
                var groupId = _setup.TeamGroupIds[(plan.Campus, plan.Team)];
                var positions = _setup.Positions.Where( p => p.Campus == plan.Campus && p.Team == plan.Team ).ToList();
                var services = ServingConfigurationLoader.TeamServices( team, campus );
                var leader = plan.Members.FirstOrDefault( m => m.IsLeader );
                var leaderAliasId = leader == null ? ( int? ) null : people.People[leader.Person].AliasId;

                SetStaffing( plan, positions, services );

                var load = new Dictionary<(DateTime Sunday, int Service, int Position), int>();
                foreach ( var member in plan.Members )
                {
                    var person = people.People[member.Person];
                    var random = new SeededRandom( $"{_seed}:serving:schedule:{plan.Campus}:{plan.Team}:{member.Person}" );
                    var role = member.IsLeader ? _setup.LeaderRoleId : _setup.MemberRoleId;
                    var memberId = tables.NextMemberId++;
                    var startSunday = SundayCalendar.SundayOnOrAfter( member.Joined );

                    tables.Members.Rows.Add( memberId, false, groupId, _setup.GroupTypeId, person.PersonId, role, 1, member.Joined, templateIds[member.Rotation], startSunday, Guid.NewGuid(), _foreignKey, member.Joined, member.Joined );
                    tables.MemberHistory.Rows.Add( memberId, groupId, role, roles[role].Name, roles[role].IsLeader, 1, false, member.Joined, MaxExpireDateTime, true, member.Joined, member.Joined, Guid.NewGuid(), _foreignKey );

                    if ( member.PreferredService.HasValue || member.PreferredPosition.HasValue )
                    {
                        tables.Assignments.Rows.Add( memberId, groupId,
                            member.PreferredPosition.HasValue ? positions[member.PreferredPosition.Value].LocationId : ( object ) DBNull.Value,
                            member.PreferredService.HasValue ? services[member.PreferredService.Value].ScheduleId : ( object ) DBNull.Value,
                            Guid.NewGuid(), _foreignKey, member.Joined, member.Joined );
                    }

                    var blackout = AddBlackout( tables, random, person, today );

                    var fifthSundayChance = _config.Volunteers.FifthSundayPercent / 100.0;
                    foreach ( var sunday in sundays.Where( s => s >= startSunday && ( IsServingWeek( member.Rotation, startSunday, s ) || IsFifthSundayFillIn( member.Rotation, s, random, fifthSundayChance ) ) ) )
                    {
                        if ( sunday > today && blackout.HasValue && sunday >= blackout.Value.Start && sunday <= blackout.Value.End )
                        {
                            continue;
                        }

                        var outcome = sunday <= today
                            ? familySundays.TryGetValue( person.FamilyId, out var attended ) && attended.Contains( sunday ) ? Outcome.Attended : ( Outcome ) Enum.Parse( typeof( Outcome ), random.Pick( _config.Scheduling.WhenAbsent ), true )
                            : ( Outcome ) Enum.Parse( typeof( Outcome ), random.Pick( _config.Scheduling.Future ), true );
                        if ( outcome == Outcome.NotScheduled )
                        {
                            continue;
                        }

                        var service = member.PreferredService ?? random.Between( 0, services.Count - 1 );
                        var position = member.PreferredPosition ?? Enumerable.Range( 0, positions.Count ).OrderBy( p => load.GetValueOrDefault( (sunday, service, p) ) ).ThenBy( _ => random.Between( 0, 1000 ) ).First();

                        // A weekday service, Student Night, meets on its day in the week that ends on this Sunday, and not in a
                        // week the student services do not meet. Its week ends by install day or is still ahead, as Student Night's
                        // check-ins do, and never before the volunteer joined (decision D-student-volunteers-1).
                        var date = services[service].DateInWeek( sunday );
                        if ( date != sunday && ( !StudentServiceRuns( sunday ) || ( date <= today && sunday > today ) || date < member.Joined ) )
                        {
                            continue;
                        }

                        load[(sunday, service, position)] = load.GetValueOrDefault( (sunday, service, position) ) + 1;

                        var occurrenceId = tables.Occurrence( groupId, positions[position].LocationId, services[service].ScheduleId, date, _foreignKey );
                        var start = date.Add( services[service].Start );
                        AddRequest( tables, random, occurrenceId, person, campus.CampusId, _devices.Volunteers( campus.Key ), start, outcome, declineReasonIds, member.IsLeader ? null : leaderAliasId );
                    }
                }
            }

            _sessions.Write();
            tables.Write( _db );
            _db.Log.Write( $"  Serving: {plans.Sum( p => p.Members.Count ):N0} volunteers on {plans.Count:N0} teams, {tables.Assignments.Rows.Count:N0} preferences, {tables.Exclusions.Rows.Count:N0} blackout dates; "
                + $"occurrences: {tables.Occurrences.Rows.Count:N0}, schedule requests: {tables.Attendance.Rows.Count:N0}" );
        }

        /// <summary>
        /// Keeps a position's configured staffing when the team can fill it. A team too small for it
        /// asks for about the people it usually has, so the scheduler shows a team that is mostly
        /// staffed with some gaps, rather than every room short.
        /// </summary>
        private void SetStaffing( ServingTeamPlan plan, List<ServingPositionInfo> positions, List<ServicePlan> services )
        {
            for ( var p = 0; p < positions.Count; p++ )
            {
                for ( var s = 0; s < services.Count; s++ )
                {
                    var expected = plan.Members.Sum( m =>
                        Frequency( m.Rotation )
                        * ( m.PreferredService.HasValue ? ( m.PreferredService == s ? 1.0 : 0.0 ) : 1.0 / services.Count )
                        * ( m.PreferredPosition.HasValue ? ( m.PreferredPosition == p ? 1.0 : 0.0 ) : 1.0 / positions.Count ) );
                    var capacity = positions[p].Capacity;
                    var (minimum, desired, maximum) = expected >= capacity[1]
                        ? (capacity[0], capacity[1], capacity[2])
                        : ScaledStaffing( expected, capacity );
                    _db.Execute( "UPDATE [GroupLocationScheduleConfig] SET [MinimumCapacity] = @Min, [DesiredCapacity] = @Desired, [MaximumCapacity] = @Max WHERE [GroupLocationId] = @GroupLocationId AND [ScheduleId] = @ScheduleId",
                        P( "@Min", minimum ), P( "@Desired", desired ), P( "@Max", maximum ), P( "@GroupLocationId", positions[p].GroupLocationId ), P( "@ScheduleId", services[s].ScheduleId ) );
                }
            }
        }

        private static (int Minimum, int Desired, int Maximum) ScaledStaffing( double expected, int[] capacity )
        {
            var desired = Math.Max( 1, ( int ) Math.Round( expected ) );
            return (Math.Min( capacity[0], desired - 1 ), desired, Math.Min( capacity[2], desired + 1 ));
        }

        /// <summary>
        /// The share of Sundays a rotation serves.
        /// </summary>
        private static double Frequency( string rotation ) => rotation switch
        {
            "Every Week" => 1.0,
            "Every Other Week" => 0.5,
            _ => 2.0 / 4.33
        };

        /// <summary>
        /// Whether a volunteer whose rotation skips fifth Sundays fills in on this one.
        /// </summary>
        private static bool IsFifthSundayFillIn( string rotation, DateTime sunday, SeededRandom random, double chance ) =>
            sunday.Day > 28 && rotation is "1st and 3rd Week" or "2nd and 4th Week" && random.Chance( chance );

        /// <summary>
        /// Whether a rotation serves on a Sunday, the way Rock's schedule templates repeat from the
        /// member's schedule start date.
        /// </summary>
        private static bool IsServingWeek( string rotation, DateTime startSunday, DateTime sunday )
        {
            var weekOfMonth = ( ( sunday.Day - 1 ) / 7 ) + 1;
            return rotation switch
            {
                "Every Week" => true,
                "Every Other Week" => ( ( sunday - startSunday ).Days / 7 ) % 2 == 0,
                "1st and 3rd Week" => weekOfMonth == 1 || weekOfMonth == 3,
                "2nd and 4th Week" => weekOfMonth == 2 || weekOfMonth == 4,
                _ => throw new InvalidOperationException( $"serving.json rotation '{rotation}' is not one of Rock's schedule templates." )
            };
        }

        /// <summary>
        /// Gives some volunteers a stretch of days away in the weeks ahead. Returns it, so no request
        /// falls inside it.
        /// </summary>
        private (DateTime Start, DateTime End)? AddBlackout( Tables tables, SeededRandom random, LoadedPerson person, DateTime today )
        {
            if ( !random.Chance( _config.Scheduling.BlackoutPercent / 100.0 ) )
            {
                return null;
            }

            var start = today.AddDays( random.Between( 3, Math.Max( 4, ( _config.Scheduling.FutureWeeks * 7 ) - 10 ) ) );
            var end = start.AddDays( random.Between( 2, 9 ) );
            tables.Exclusions.Rows.Add( person.AliasId, random.Pick( _config.Scheduling.BlackoutTitles ), start, end, Guid.NewGuid(), _foreignKey, today.AddDays( -random.Between( 3, 30 ) ), today );
            return (start, end);
        }

        /*
            10/25/26 - CLAUDE

            Rock's Send Group Schedule Notifications job marks a request's confirmation sent, and its
            reminder sent, only when it emailed the volunteer: the serving team type's system
            communications have no text message, so a volunteer Rock cannot email gets neither, and the
            job never picks them. It reminded only volunteers who confirmed. For a Sunday still ahead,
            both flags stay set for every volunteer Rock can email, so the job has nothing left to send
            after install. A request for a Sunday weeks ahead was made in the last few days, so nothing
            is dated after install day. The communications part clears a past flag again when the
            volunteer's email bounced before the job would have sent it.

            Reason: the flags agree with each volunteer's Communication History, and nothing a load
            writes may ever send (decisions 95 and 165).
        */

        /// <summary>
        /// Adds one request to serve, as it stands for its outcome.
        /// </summary>
        private void AddRequest( Tables tables, SeededRandom random, int occurrenceId, LoadedPerson person, int campusId, Kiosk kiosk, DateTime start, Outcome outcome, List<int> declineReasonIds, int? scheduledByAliasId )
        {
            var (rsvp, scheduled, didAttend) = outcome switch
            {
                Outcome.Attended => (RsvpYes, true, true),
                Outcome.Confirmed or Outcome.ConfirmedNoShow => (RsvpYes, true, false),
                Outcome.Declined => (RsvpNo, false, false),
                _ => (RsvpUnknown, false, false)
            };
            var now = _dates.Anchor;
            var requested = start.AddDays( -random.Between( 8, 20 ) );
            var answeredAt = rsvp == RsvpUnknown ? ( DateTime? ) null : requested.AddDays( random.Between( 1, 5 ) );
            if ( requested > now )
            {
                requested = now.Date.AddDays( -1 - ( start.DayOfYear % 6 ) ).AddHours( 9 + ( start.Day % 8 ) );
            }

            if ( answeredAt > now )
            {
                answeredAt = requested.AddTicks( ( now - requested ).Ticks / 2 );
            }

            var answered = answeredAt.HasValue ? answeredAt.Value : ( object ) DBNull.Value;
            var modified = start <= now ? start : answeredAt ?? requested;
            var cast = person.Cast;
            var canEmail = !string.IsNullOrWhiteSpace( cast.Email ) && cast.EmailIsActive && !cast.EmailDoNotEmail;
            var confirmationSent = canEmail;
            var reminderSent = canEmail && ( rsvp == RsvpYes || start.Date.AddDays( -_reminderOffsetDays ) > now.Date );
            var declineReason = outcome == Outcome.Declined && declineReasonIds.Count > 0 ? random.Pick( declineReasonIds ) : ( object ) DBNull.Value;

            // A volunteer who served usually checked in at their campus's volunteer kiosk a few minutes before the
            // service, in a session of their own, by their phone (decisions 360 and 361).
            var checkIn = _config.CheckIn;
            var checkedIn = outcome == Outcome.Attended && kiosk != null && random.Chance( checkIn.CheckInPercent / 100.0 );
            var present = checkedIn ? start.AddMinutes( -random.Between( ( int ) checkIn.ArrivesMinutesEarly.Min, ( int ) checkIn.ArrivesMinutesEarly.Max ) ) : ( object ) DBNull.Value;
            var sessionId = checkedIn ? _sessions.Open( kiosk ) : null;
            var search = checkedIn ? CheckInSessions.SearchValue( person, _membersByFamily.TryGetValue( person.FamilyId, out var family ) ? family : new List<LoadedPerson> { person } ) : null;

            tables.Attendance.Rows.Add( occurrenceId, person.AliasId, campusId, start, didAttend, rsvp, true, scheduled, confirmationSent, reminderSent, answered, declineReason,
                scheduledByAliasId.HasValue ? scheduledByAliasId.Value : ( object ) DBNull.Value,
                checkedIn ? kiosk.Id : ( object ) DBNull.Value, checkedIn && _searchTypeId.HasValue ? _searchTypeId.Value : ( object ) DBNull.Value, checkedIn ? person.AliasId : ( object ) DBNull.Value, present, RockCheckInStatus.For( checkedIn ),
                Guid.NewGuid(), _foreignKey, requested, modified,
                ( object ) sessionId ?? DBNull.Value, ( object ) search ?? DBNull.Value, checkedIn ? person.FamilyId : ( object ) DBNull.Value );
        }

        private enum Outcome
        {
            Attended,
            NotScheduled,
            Declined,
            NoResponse,
            ConfirmedNoShow,
            Confirmed,
            Pending
        }

        /// <summary>
        /// The in-memory tables and the next Id for each table whose Ids other rows point to.
        /// </summary>
        private sealed class Tables
        {
            private readonly Dictionary<(int, int, int, DateTime), int> _occurrenceIds = new Dictionary<(int, int, int, DateTime), int>();

            public Tables( RockDatabase db )
            {
                NextMemberId = Convert.ToInt32( db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [GroupMember]" ) );
                NextOccurrenceId = Convert.ToInt32( db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [AttendanceOccurrence]" ) );
            }

            public int NextMemberId { get; set; }

            public int NextOccurrenceId { get; set; }

            public DataTable Members { get; } = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date",
                "ScheduleTemplateId:int", "ScheduleStartDate:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable MemberHistory { get; } = TableFactory.Create( "GroupMemberId:int", "GroupId:int", "GroupRoleId:int", "GroupRoleName:string", "IsLeader:bool", "GroupMemberStatus:int", "IsArchived:bool",
                "EffectiveDateTime:date", "ExpireDateTime:date", "CurrentRowIndicator:bool", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );

            public DataTable Assignments { get; } = TableFactory.Create( "GroupMemberId:int", "GroupId:int", "LocationId:int", "ScheduleId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Exclusions { get; } = TableFactory.Create( "PersonAliasId:int", "Title:string", "StartDate:date", "EndDate:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Occurrences { get; } = TableFactory.Create( "Id:int", "GroupId:int", "LocationId:int", "ScheduleId:int", "OccurrenceDate:date", "SundayDate:date", "OccurrenceDateKey:int", "DidNotOccur:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Attendance { get; } = TableFactory.Create( "OccurrenceId:int", "PersonAliasId:int", "CampusId:int", "StartDateTime:date", "DidAttend:bool", "RSVP:int", "RequestedToAttend:bool", "ScheduledToAttend:bool",
                "ScheduleConfirmationSent:bool", "ScheduleReminderSent:bool", "RSVPDateTime:date", "DeclineReasonValueId:int", "ScheduledByPersonAliasId:int", "DeviceId:int", "SearchTypeValueId:int", "CheckedInByPersonAliasId:int", "PresentDateTime:date", "CheckInStatus:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date",
                "AttendanceCheckInSessionId:int", "SearchValue:string", "SearchResultGroupId:int" );

            /// <summary>
            /// The team's occurrence for one room, service, and day, added the first time it is needed.
            /// </summary>
            public int Occurrence( int groupId, int locationId, int scheduleId, DateTime date, string foreignKey )
            {
                var key = (groupId, locationId, scheduleId, date);
                if ( !_occurrenceIds.TryGetValue( key, out var id ) )
                {
                    id = NextOccurrenceId++;
                    _occurrenceIds[key] = id;
                    Occurrences.Rows.Add( id, groupId, locationId, scheduleId, date, SundayCalendar.SundayOnOrAfter( date ), int.Parse( date.ToString( "yyyyMMdd" ) ), false, Guid.NewGuid(), foreignKey, date.AddDays( -14 ), date.AddDays( -14 ) );
                }

                return id;
            }

            /// <summary>
            /// Bulk-copies every table, parents before the rows that point to them.
            /// </summary>
            public void Write( RockDatabase db )
            {
                db.BulkInsert( "GroupMember", Members, keepIdentity: true );
                db.BulkInsert( "GroupMemberHistorical", MemberHistory, keepIdentity: false );
                db.BulkInsert( "GroupMemberAssignment", Assignments, keepIdentity: false );
                db.BulkInsert( "PersonScheduleExclusion", Exclusions, keepIdentity: false );
                db.BulkInsert( "AttendanceOccurrence", Occurrences, keepIdentity: true );
                db.BulkInsert( "Attendance", Attendance, keepIdentity: false );
            }
        }
    }
}
