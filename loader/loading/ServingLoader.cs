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

        public ServingLoader( RockDatabase db, ServingConfiguration config, ServingSetup setup, Dictionary<string, CampusPlan> campuses, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _config = config;
            _setup = setup;
            _campuses = campuses;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Builds every row for every team in memory, sets each position's staffing, then bulk-copies.
        /// </summary>
        public void Load( IReadOnlyList<ServingTeamPlan> plans, PeopleLoader people, Dictionary<int, HashSet<DateTime>> familySundays )
        {
            var templateIds = _db.Query( "SELECT [Id], [Name] FROM [GroupMemberScheduleTemplate]" ).ToDictionary( r => ( string ) r["Name"], r => Convert.ToInt32( r["Id"] ) );
            var declineReasonIds = _db.Query( "SELECT [dv].[Id] FROM [DefinedValue] AS [dv] INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId] WHERE [dt].[Guid] = @Guid",
                P( "@Guid", DeclineReasonTypeGuid ) ).Select( r => Convert.ToInt32( r["Id"] ) ).ToList();
            var roles = _db.Query( "SELECT [Id], [Name], [IsLeader] FROM [GroupTypeRole] WHERE [Id] IN ( @Leader, @Member )", P( "@Leader", _setup.LeaderRoleId ), P( "@Member", _setup.MemberRoleId ) )
                .ToDictionary( r => Convert.ToInt32( r["Id"] ), r => (Name: ( string ) r["Name"], IsLeader: Convert.ToBoolean( r["IsLeader"] )) );

            var backgroundCheckAttributeIds = _db.Query( @"
SELECT [a].[Key], [a].[Id]
FROM [Attribute] AS [a]
INNER JOIN [EntityType] AS [e] ON [e].[Id] = [a].[EntityTypeId]
WHERE [e].[Name] = 'Rock.Model.Person' AND [a].[Key] IN ( 'BackgroundChecked', 'BackgroundCheckDate', 'BackgroundCheckResult' )" ).ToDictionary( r => ( string ) r["Key"], r => Convert.ToInt32( r["Id"] ) );
            var requirementIds = _db.Query( "SELECT [GroupId], [Id] FROM [GroupRequirement] WHERE [GroupId] IS NOT NULL" ).ToDictionary( r => Convert.ToInt32( r["GroupId"] ), r => Convert.ToInt32( r["Id"] ) );
            var checkedPeople = new Dictionary<int, DateTime>();

            var tables = new Tables( _db );
            var today = _dates.Anchor.Date;
            var firstSunday = SundayCalendar.SundayOnOrAfter( today ).AddDays( -7 * _config.Scheduling.PastWeeks );
            var sundays = Enumerable.Range( 0, _config.Scheduling.PastWeeks + _config.Scheduling.FutureWeeks + 1 ).Select( i => firstSunday.AddDays( 7 * i ) ).ToList();

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

                    if ( team.RequiresBackgroundCheck && requirementIds.TryGetValue( groupId, out var requirementId ) )
                    {
                        var checkedOn = AddBackgroundCheck( tables, random, person, today, backgroundCheckAttributeIds, checkedPeople );
                        tables.Requirements.Rows.Add( memberId, requirementId, checkedOn, today, 0, false, false, Guid.NewGuid(), _foreignKey, checkedOn, today );
                    }

                    if ( member.PreferredService.HasValue || member.PreferredPosition.HasValue )
                    {
                        tables.Assignments.Rows.Add( memberId, groupId,
                            member.PreferredPosition.HasValue ? positions[member.PreferredPosition.Value].LocationId : ( object ) DBNull.Value,
                            member.PreferredService.HasValue ? services[member.PreferredService.Value].ScheduleId : ( object ) DBNull.Value,
                            Guid.NewGuid(), _foreignKey, member.Joined, member.Joined );
                    }

                    var blackout = AddBlackout( tables, random, person, today );

                    foreach ( var sunday in sundays.Where( s => s >= startSunday && IsServingWeek( member.Rotation, startSunday, s ) ) )
                    {
                        if ( sunday > today && blackout.HasValue && sunday >= blackout.Value.Start && sunday <= blackout.Value.End )
                        {
                            continue;
                        }

                        var service = member.PreferredService ?? random.Between( 0, services.Count - 1 );
                        var position = member.PreferredPosition ?? Enumerable.Range( 0, positions.Count ).OrderBy( p => load.GetValueOrDefault( (sunday, service, p) ) ).ThenBy( _ => random.Between( 0, 1000 ) ).First();
                        load[(sunday, service, position)] = load.GetValueOrDefault( (sunday, service, position) ) + 1;

                        var occurrenceId = tables.Occurrence( groupId, positions[position].LocationId, services[service].ScheduleId, sunday, _foreignKey );
                        var start = sunday.Add( services[service].Start );
                        var outcome = sunday <= today
                            ? familySundays.TryGetValue( person.FamilyId, out var attended ) && attended.Contains( sunday ) ? Outcome.Attended : Enum.Parse<Outcome>( random.Pick( _config.Scheduling.WhenAbsent ), true )
                            : Enum.Parse<Outcome>( random.Pick( _config.Scheduling.Future ), true );
                        AddRequest( tables, random, occurrenceId, person, campus.CampusId, start, outcome, declineReasonIds, member.IsLeader ? null : leaderAliasId );
                    }
                }
            }

            tables.Write( _db );
            Console.WriteLine( $"  Serving: {plans.Sum( p => p.Members.Count ):N0} volunteers on {plans.Count:N0} teams, {tables.Assignments.Rows.Count:N0} preferences, {tables.Exclusions.Rows.Count:N0} blackout dates; "
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

        /*
            10/19/26 - CLAUDE

            Rock's Background Check Required requirement is met by the "Background check is still valid"
            data view: Background Checked is True, the Background Check Date is within the last 1,095
            days, and the Background Check Result is Pass. The scheduler reads the stored
            GroupMemberRequirement rows that Rock's Calculate Group Requirements job writes, not the
            data view, so both are written here, agreeing with each other.

            Reason: without them every kids and student volunteer shows as not meeting the requirement.
        */

        /// <summary>
        /// Gives a volunteer on a team that needs one a passing background check from the last few
        /// years, once per person. Returns the check date.
        /// </summary>
        private DateTime AddBackgroundCheck( Tables tables, SeededRandom random, LoadedPerson person, DateTime today, Dictionary<string, int> attributeIds, Dictionary<int, DateTime> checkedPeople )
        {
            if ( checkedPeople.TryGetValue( person.PersonId, out var existing ) )
            {
                return existing;
            }

            var checkedOn = today.AddDays( -random.Between( 30, 900 ) );
            checkedPeople[person.PersonId] = checkedOn;
            tables.PersonValues.Rows.Add( false, attributeIds["BackgroundChecked"], person.PersonId, "True", DBNull.Value, true, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
            tables.PersonValues.Rows.Add( false, attributeIds["BackgroundCheckDate"], person.PersonId, checkedOn.ToString( "o" ), checkedOn, DBNull.Value, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
            tables.PersonValues.Rows.Add( false, attributeIds["BackgroundCheckResult"], person.PersonId, "Pass", DBNull.Value, DBNull.Value, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
            return checkedOn;
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

        /// <summary>
        /// Adds one request to serve, as it stands for its outcome.
        /// </summary>
        private void AddRequest( Tables tables, SeededRandom random, int occurrenceId, LoadedPerson person, int campusId, DateTime start, Outcome outcome, List<int> declineReasonIds, int? scheduledByAliasId )
        {
            var (rsvp, scheduled, didAttend) = outcome switch
            {
                Outcome.Attended => (RsvpYes, true, true),
                Outcome.Confirmed or Outcome.ConfirmedNoShow => (RsvpYes, true, false),
                Outcome.Declined => (RsvpNo, false, false),
                _ => (RsvpUnknown, false, false)
            };
            var requested = start.AddDays( -random.Between( 8, 20 ) );
            var answered = rsvp == RsvpUnknown ? DBNull.Value : ( object ) requested.AddDays( random.Between( 1, 5 ) );
            var declineReason = outcome == Outcome.Declined && declineReasonIds.Count > 0 ? random.Pick( declineReasonIds ) : ( object ) DBNull.Value;

            tables.Attendance.Rows.Add( occurrenceId, person.AliasId, campusId, start, didAttend, rsvp, true, scheduled, true, true, answered, declineReason,
                scheduledByAliasId.HasValue ? scheduledByAliasId.Value : ( object ) DBNull.Value, Guid.NewGuid(), _foreignKey, requested, start );
        }

        private enum Outcome
        {
            Attended,
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

            public DataTable PersonValues { get; } = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsDateTime:date", "ValueAsBoolean:bool", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Requirements { get; } = TableFactory.Create( "GroupMemberId:int", "GroupRequirementId:int", "RequirementMetDateTime:date", "LastRequirementCheckDateTime:date", "GroupMemberRequirementState:int",
                "WasManuallyCompleted:bool", "WasOverridden:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Exclusions { get; } = TableFactory.Create( "PersonAliasId:int", "Title:string", "StartDate:date", "EndDate:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Occurrences { get; } = TableFactory.Create( "Id:int", "GroupId:int", "LocationId:int", "ScheduleId:int", "OccurrenceDate:date", "SundayDate:date", "OccurrenceDateKey:int", "DidNotOccur:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Attendance { get; } = TableFactory.Create( "OccurrenceId:int", "PersonAliasId:int", "CampusId:int", "StartDateTime:date", "DidAttend:bool", "RSVP:int", "RequestedToAttend:bool", "ScheduledToAttend:bool",
                "ScheduleConfirmationSent:bool", "ScheduleReminderSent:bool", "RSVPDateTime:date", "DeclineReasonValueId:int", "ScheduledByPersonAliasId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            /// <summary>
            /// The team's occurrence for one room, service, and Sunday, added the first time it is needed.
            /// </summary>
            public int Occurrence( int groupId, int locationId, int scheduleId, DateTime sunday, string foreignKey )
            {
                var key = (groupId, locationId, scheduleId, sunday);
                if ( !_occurrenceIds.TryGetValue( key, out var id ) )
                {
                    id = NextOccurrenceId++;
                    _occurrenceIds[key] = id;
                    Occurrences.Rows.Add( id, groupId, locationId, scheduleId, sunday, sunday, int.Parse( sunday.ToString( "yyyyMMdd" ) ), false, Guid.NewGuid(), foreignKey, sunday.AddDays( -14 ), sunday.AddDays( -14 ) );
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
                db.BulkInsert( "GroupMemberRequirement", Requirements, keepIdentity: false );
                db.BulkInsert( "AttributeValue", PersonValues, keepIdentity: false );
                db.BulkInsert( "PersonScheduleExclusion", Exclusions, keepIdentity: false );
                db.BulkInsert( "AttendanceOccurrence", Occurrences, keepIdentity: true );
                db.BulkInsert( "Attendance", Attendance, keepIdentity: false );
            }
        }
    }
}
