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

namespace RockSampleData.Loader.Loading
{
    /*
        10/15/26 - CLAUDE

        Attendance is planned per household, then written per person, so the whole family
        agrees: kids only check in on Sundays the family came, at the family's service time,
        in the room for their age or grade on that date (kids move up a grade each June 1).
        Students check in at the student service. The Sundays a family attended are kept
        for the giving step, so onsite checks and cash only happen when the family was there.

        Reason: Records from different features must tell the same story.
    */

    /*
        10/23/26 - CLAUDE

        Students (grades 7 to 12) now have attendance of their own, planned in a second pass after
        every family's Sundays. An engaged student can come to the student service on a Sunday their
        family stayed home, some keep coming for a while after their family stops, and a recent
        graduate checks in as a student until they graduate, then attends as an adult (some only when
        home from college). The first pass makes exactly the random draws it made before, and the
        second pass draws from each student's own seed and issues its security codes after every
        family's, so adults and young kids get the same Sundays as before. A student on their own
        never adds a family Sunday, so giving still follows the family.

        Reason: A teen can come more often than their parents without changing anyone else's
        records (decisions D-student-attendance-1 to D-student-attendance-3).
    */

    /// <summary>
    /// Generates Sunday worship attendance for adults and check-in attendance for kids and students.
    /// </summary>
    internal sealed class AttendanceLoader
    {
        internal static readonly Guid PhoneSearchTypeGuid = new Guid( "F3F66040-C50F-4D13-9652-780305FFFE23" );
        private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly RockDatabase _db;
        private readonly SundayCalendar _calendar;
        private readonly AnchorDates _dates;
        private readonly ConfigurationLoader _configuration;
        private readonly string _foreignKey;
        private readonly string _seed;

        public AttendanceLoader( RockDatabase db, SundayCalendar calendar, AnchorDates dates, ConfigurationLoader configuration, string foreignKey, string seed )
        {
            _db = db;
            _calendar = calendar;
            _dates = dates;
            _configuration = configuration;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// The Sundays each family attended, keyed by family group Id. Used by the giving step.
        /// A student who came on their own does not make a Sunday a family Sunday.
        /// </summary>
        public Dictionary<int, HashSet<DateTime>> FamilySundays { get; } = new Dictionary<int, HashSet<DateTime>>();

        /// <summary>
        /// The Sundays each child checked in, keyed by the child's person alias. Used by the achievements step.
        /// </summary>
        public Dictionary<int, SortedSet<DateTime>> CheckInSundaysByAlias { get; } = new Dictionary<int, SortedSet<DateTime>>();

        /// <summary>
        /// The Sundays with at least one kids check-in, in any area of the check-in template.
        /// </summary>
        public SortedSet<DateTime> CheckInSundays { get; } = new SortedSet<DateTime>();

        /// <summary>
        /// The Sundays each person checked in, by check-in group key, then by person Id.
        /// </summary>
        private readonly Dictionary<string, Dictionary<int, SortedSet<DateTime>>> _checkInDaysByGroup = new Dictionary<string, Dictionary<int, SortedSet<DateTime>>>();

        /// <summary>
        /// The rows being planned, which <see cref="Write"/> writes.
        /// </summary>
        private DataTable _occurrences;
        private DataTable _codes;
        private DataTable _attendance;

        private int _nextOccurrenceId;
        private int _nextAttendanceId;
        private int _nextCodeId;
        private DateTime _now;
        private int? _searchTypeId;
        private readonly Dictionary<(int Group, int Location, int Schedule, DateTime Date), int> _occurrenceIds = new Dictionary<(int, int, int, DateTime), int>();
        private readonly HashSet<int> _attendedBefore = new HashSet<int>();
        private readonly HashSet<(int PersonId, DateTime Sunday)> _checkedInThisSunday = new HashSet<(int, DateTime)>();

        /// <summary>
        /// Rock keeps security codes unique within a day, and the database enforces it.
        /// </summary>
        private readonly HashSet<(DateTime Day, string Code)> _codesUsed = new HashSet<(DateTime, string)>();

        /// <summary>
        /// The security code each family used on each Sunday its kids checked in.
        /// </summary>
        private readonly Dictionary<(int FamilyId, DateTime Sunday), int> _familyCodes = new Dictionary<(int, DateTime), int>();

        /// <summary>
        /// People with check-ins from the student pass, whose first-time flag is set from all of their rows at the end.
        /// </summary>
        private readonly HashSet<int> _studentAliases = new HashSet<int>();

        /// <summary>
        /// Each recent graduate's college plan: whether they are away, and the stream for their weekends home.
        /// </summary>
        private readonly Dictionary<int, (bool IsAway, SeededRandom Random)> _college = new Dictionary<int, (bool, SeededRandom)>();

        /// <summary>
        /// A family Sunday a recent graduate came with their family while still in school. The student pass
        /// decides whether they checked in to the student service or sat in the service.
        /// </summary>
        private sealed record StudentFamilySunday( LoadedHousehold Household, LoadedPerson Student, DateTime Sunday, ServicePlan Service, DateTime ServiceStart, int CheckedInByAliasId );

        /// <summary>
        /// Plans attendance for every household and fills the plans that later steps read. Other parts
        /// read them even when attendance is not written, so planning never depends on the parts chosen.
        /// </summary>
        public void Plan( List<LoadedHousehold> households )
        {
            _now = DateTime.Now;
            _searchTypeId = _db.IdByGuid( "DefinedValue", PhoneSearchTypeGuid );
            _occurrences = NewTable( "Id:int", "GroupId:int", "LocationId:int", "ScheduleId:int", "OccurrenceDate:date", "SundayDate:date", "OccurrenceDateKey:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            _attendance = NewTable( "Id:int", "OccurrenceId:int", "PersonAliasId:int", "CampusId:int", "StartDateTime:date", "DidAttend:bool", "DeviceId:int", "SearchTypeValueId:int", "AttendanceCodeId:int", "CheckedInByPersonAliasId:int", "PresentDateTime:date", "IsFirstTime:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            _codes = NewTable( "Id:int", "IssueDateTime:date", "Code:string", "Guid:guid", "ForeignKey:string" );
            _nextOccurrenceId = NextId( "AttendanceOccurrence" );
            _nextAttendanceId = NextId( "Attendance" );
            _nextCodeId = NextId( "AttendanceCode" );

            var studentFamilySundays = new List<StudentFamilySunday>();
            foreach ( var household in households )
            {
                PlanFamily( household, studentFamilySundays );
            }

            // Students only check in on Sundays the check-in ran for the families, so the kids' streaks keep their Sundays.
            var checkInRan = new HashSet<DateTime>( CheckInSundays );
            foreach ( var sunday in studentFamilySundays )
            {
                PlanStudentFamilySunday( sunday, checkInRan );
            }

            foreach ( var household in households )
            {
                PlanStudentsOnTheirOwn( household, checkInRan );
            }

            SetFirstTimes();
        }

        /// <summary>
        /// The family pass: the Sundays a household came, its adults in the service, and its kids and
        /// students checked in with it. A recent graduate still in school is set aside for the student pass,
        /// and one away at college is left out when someone else from the family came.
        /// </summary>
        private void PlanFamily( LoadedHousehold household, List<StudentFamilySunday> studentFamilySundays )
        {
            var plan = _configuration.CampusPlans[household.CampusKey];
            var random = new SeededRandom( $"{_seed}:attendance:{household.Cast.Guid}" );
            var (percent, primaryPercent) = AttendanceRate( household );
            var (started, ended) = Window( household );
            var primary = PickPrimaryService( plan, random );
            var adults = household.Members.Where( m => m.IsAdult && m.CanAttend && m.FamilyId == household.FamilyId ).ToList();
            var children = household.Members.Where( m => !m.IsAdult && m.CanAttend ).ToList();
            var sundays = new HashSet<DateTime>();
            FamilySundays[household.FamilyId] = sundays;

            // A child on their own (usually a student) can still come and check in without an adult.
            if ( percent <= 0 || ( adults.Count == 0 && children.Count == 0 ) )
            {
                return;
            }

            foreach ( var sunday in _calendar.Sundays.Where( s => s >= started && s <= ended ) )
            {
                var adultFactor = _calendar.Multiplier( sunday, "adults" );
                if ( !random.Chance( Math.Min( 0.98, percent / 100.0 * adultFactor ) ) )
                {
                    continue;
                }

                sundays.Add( sunday );
                var service = random.Chance( primaryPercent / 100.0 ) ? primary : random.Pick( plan.Services );
                var present = adults.Where( _ => random.Chance( 0.9 ) ).ToList();
                if ( present.Count == 0 && adults.Count > 0 )
                {
                    present.Add( random.Pick( adults ) );
                }

                var inService = new List<LoadedPerson>();
                var students = new List<(LoadedPerson Person, DateTime Start)>();
                var away = new List<(LoadedPerson Person, DateTime Start)>();
                foreach ( var adult in present )
                {
                    var start = sunday.Add( service.Start ).AddMinutes( -random.Between( 0, 15 ) );
                    if ( IsStillInSchool( adult, sunday ) )
                    {
                        students.Add( (adult, start) );
                    }
                    else if ( IsAwayAtCollege( adult, sunday ) )
                    {
                        away.Add( (adult, start) );
                    }
                    else
                    {
                        inService.Add( adult );
                        AddWorship( adult, plan, service, sunday, start );
                    }
                }

                foreach ( var (student, start) in students )
                {
                    var checkedInBy = inService.FirstOrDefault()?.AliasId ?? student.AliasId;
                    studentFamilySundays.Add( new StudentFamilySunday( household, student, sunday, service, start, checkedInBy ) );
                }

                // Kids check in with the family. The kids multiplier is relative to the adult one,
                // so a Sunday with no children's ministry (Christmas week) has almost no kids.
                var kidsFactor = KidsFactor( sunday );
                int? codeId = null;
                foreach ( var child in children )
                {
                    var groupKey = CheckInGroupFor( child, sunday );
                    if ( groupKey == null || !_checkedInThisSunday.Add( (child.PersonId, sunday) ) )
                    {
                        continue;
                    }

                    var isStudent = IsStudentGroup( groupKey );
                    if ( !random.Chance( ( isStudent ? StudentRules.FamilyCheckIn : StudentRules.YoungKidCheckIn ) * kidsFactor ) )
                    {
                        _checkedInThisSunday.Remove( (child.PersonId, sunday) );
                        continue;
                    }

                    var childService = isStudent ? plan.StudentService : service;
                    var start = sunday.Add( childService.Start ).AddMinutes( -random.Between( 2, 25 ) );
                    codeId ??= NewCode( random, sunday, start );
                    AddCheckIn( child, plan, groupKey, childService, sunday, start, codeId.Value, present.Count > 0 ? present[0].AliasId : child.AliasId );
                }

                if ( codeId.HasValue )
                {
                    _familyCodes[(household.FamilyId, sunday)] = codeId.Value;
                }

                // A family Sunday stays one, because giving and serving follow it. When a graduate away at college
                // was the only one of the family there, they were home that weekend.
                if ( inService.Count == 0 && students.Count == 0 && codeId == null )
                {
                    foreach ( var (adult, start) in away )
                    {
                        AddWorship( adult, plan, service, sunday, start );
                    }
                }
            }
        }

        /// <summary>
        /// A recent graduate who came with their family while still in school: they check in to the student
        /// service as a student with their family would, or else sit in the service with the family.
        /// </summary>
        private void PlanStudentFamilySunday( StudentFamilySunday familySunday, HashSet<DateTime> checkInRan )
        {
            var (household, student, sunday, service, serviceStart, checkedInBy) = familySunday;
            var plan = _configuration.CampusPlans[household.CampusKey];
            var random = StudentRandom( "family", student, sunday );
            var groupKey = CheckInGroupFor( student, sunday );
            var isCheckedIn = IsStudentGroup( groupKey ) && checkInRan.Contains( sunday ) && random.Chance( StudentRules.FamilyCheckIn * KidsFactor( sunday ) )
                && _checkedInThisSunday.Add( (student.PersonId, sunday) );

            _studentAliases.Add( student.AliasId );
            if ( !isCheckedIn )
            {
                AddWorship( student, plan, service, sunday, serviceStart );
                return;
            }

            var start = sunday.Add( plan.StudentService.Start ).AddMinutes( -random.Between( 2, 25 ) );
            var codeId = _familyCodes.TryGetValue( (household.FamilyId, sunday), out var familyCode ) ? familyCode : NewCode( random, sunday, start );
            AddCheckIn( student, plan, groupKey, plan.StudentService, sunday, start, codeId, checkedInBy );
        }

        /// <summary>
        /// The student pass for one household: an engaged student comes to the student service on some
        /// Sundays the family stayed home, and some keep coming for a while after the family stops.
        /// They check themselves in.
        /// </summary>
        private void PlanStudentsOnTheirOwn( LoadedHousehold household, HashSet<DateTime> checkInRan )
        {
            var (percent, _) = AttendanceRate( household );
            if ( percent <= 0 )
            {
                return;
            }

            var plan = _configuration.CampusPlans[household.CampusKey];
            var (started, ended) = Window( household );
            var familySundays = FamilySundays[household.FamilyId];
            var students = household.Members
                .Where( m => m.CanAttend && m.FamilyId == household.FamilyId && ( !m.IsAdult || IsRecentGraduate( m ) ) && StudentRules.EngagedStatuses.Contains( m.Cast.ConnectionStatus ?? string.Empty ) )
                .ToList();

            foreach ( var student in students )
            {
                var random = new SeededRandom( $"{_seed}:student-attendance:{student.Cast.Guid}" );
                foreach ( var sunday in _calendar.Sundays.Where( s => s >= started && s <= ended && !familySundays.Contains( s ) ) )
                {
                    var groupKey = CheckInGroupFor( student, sunday );
                    if ( IsStudentGroup( groupKey ) && random.Chance( Math.Min( 0.95, percent / 100.0 * StudentRules.OnTheirOwn[groupKey] * _calendar.Multiplier( sunday, "kids" ) ) ) )
                    {
                        AddCheckInOnTheirOwn( student, plan, groupKey, sunday, checkInRan );
                    }
                }

                // The family stopped coming inside the history window while this student was in grades 7 to 12.
                var isFamilyStopped = household.Cast.Attendance?.Ended != null && ended < _calendar.HistoryEnd;
                if ( !isFamilyStopped || !IsStudentGroup( CheckInGroupFor( student, ended ) ) || !random.Chance( StudentRules.KeepComingShare ) )
                {
                    continue;
                }

                var keepsComingUntil = ended.AddDays( 7 * random.Between( StudentRules.KeepComingWeeks.Min, StudentRules.KeepComingWeeks.Max ) );
                foreach ( var sunday in _calendar.Sundays.Where( s => s > ended && s <= keepsComingUntil ) )
                {
                    var groupKey = CheckInGroupFor( student, sunday );
                    if ( IsStudentGroup( groupKey ) && random.Chance( Math.Min( 0.95, percent / 100.0 * StudentRules.FamilyCheckIn * _calendar.Multiplier( sunday, "kids" ) ) ) )
                    {
                        AddCheckInOnTheirOwn( student, plan, groupKey, sunday, checkInRan );
                    }
                }
            }
        }

        /// <summary>
        /// A student's check-in on their own, with their own security code, on a Sunday the check-in ran. Its time and
        /// code come from a stream for that Sunday alone, because a code that collides with another family's draws again,
        /// and the other families differ by dataset. The Cast's Sundays stay the same in every dataset.
        /// </summary>
        private void AddCheckInOnTheirOwn( LoadedPerson student, CampusPlan plan, string groupKey, DateTime sunday, HashSet<DateTime> checkInRan )
        {
            if ( !checkInRan.Contains( sunday ) || !_checkedInThisSunday.Add( (student.PersonId, sunday) ) )
            {
                return;
            }

            var random = StudentRandom( "own", student, sunday );
            var start = sunday.Add( plan.StudentService.Start ).AddMinutes( -random.Between( 2, 25 ) );
            _studentAliases.Add( student.AliasId );
            AddCheckIn( student, plan, groupKey, plan.StudentService, sunday, start, NewCode( random, sunday, start ), student.AliasId );
        }

        /// <summary>
        /// Sets the first-time flag of everyone the student pass touched on their earliest row only.
        /// </summary>
        private void SetFirstTimes()
        {
            var rows = _attendance.AsEnumerable()
                .Where( r => _studentAliases.Contains( ( int ) r["PersonAliasId"] ) )
                .GroupBy( r => ( int ) r["PersonAliasId"] );
            foreach ( var person in rows )
            {
                var first = person.OrderBy( r => ( DateTime ) r["StartDateTime"] ).First();
                foreach ( var row in person )
                {
                    row["IsFirstTime"] = row == first;
                }
            }
        }

        /// <summary>
        /// The Sundays each person checked in to any of these check-in groups, keyed by person Id. Read from the plan,
        /// so it is the same whether or not attendance is written.
        /// </summary>
        public Dictionary<int, SortedSet<DateTime>> CheckInDays( IEnumerable<string> groupKeys )
        {
            var days = new Dictionary<int, SortedSet<DateTime>>();
            foreach ( var key in groupKeys.Distinct() )
            {
                if ( !_configuration.CheckInGroupIdsByKey.ContainsKey( key ) )
                {
                    throw new InvalidOperationException( $"Check-in group \"{key}\" is not in configuration/checkin.json." );
                }

                if ( !_checkInDaysByGroup.TryGetValue( key, out var groupDays ) )
                {
                    continue;
                }

                foreach ( var person in groupDays )
                {
                    if ( !days.TryGetValue( person.Key, out var personDays ) )
                    {
                        personDays = new SortedSet<DateTime>();
                        days[person.Key] = personDays;
                    }

                    personDays.UnionWith( person.Value );
                }
            }

            return days;
        }

        /// <summary>
        /// Writes the attendance <see cref="Plan"/> planned.
        /// </summary>
        public void Write()
        {
            _db.BulkInsert( "AttendanceOccurrence", _occurrences, keepIdentity: true );
            _db.BulkInsert( "AttendanceCode", _codes, keepIdentity: true );
            _db.BulkInsert( "Attendance", _attendance, keepIdentity: true );

            var checkIns = _attendance.AsEnumerable().Count( r => r["AttendanceCodeId"] != DBNull.Value );
            var onTheirOwn = _attendance.AsEnumerable().Count( r => r["AttendanceCodeId"] != DBNull.Value && Equals( r["CheckedInByPersonAliasId"], r["PersonAliasId"] ) );
            _db.Log.Write( $"  Sundays: {_calendar.Sundays.Count}, occurrences: {_occurrences.Rows.Count:N0}, attendance: {_attendance.Rows.Count:N0} ({checkIns:N0} kids check-ins, {onTheirOwn:N0} on their own)" );
        }

        /// <summary>
        /// One adult in the worship service.
        /// </summary>
        private void AddWorship( LoadedPerson adult, CampusPlan plan, ServicePlan service, DateTime sunday, DateTime start )
        {
            var occurrenceId = Occurrence( _configuration.WorshipGroupIdValue, plan.LocationId, service.ScheduleId, sunday );
            _attendance.Rows.Add( _nextAttendanceId++, occurrenceId, adult.AliasId, plan.CampusId, start, true, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, _attendedBefore.Add( adult.PersonId ), Guid.NewGuid(), _foreignKey, start, start );
        }

        /// <summary>
        /// One check-in to a kids or student room, and the plans later steps read.
        /// </summary>
        private void AddCheckIn( LoadedPerson person, CampusPlan plan, string groupKey, ServicePlan service, DateTime sunday, DateTime start, int codeId, int checkedInByAliasId )
        {
            var occurrenceId = Occurrence( _configuration.CheckInGroupIdsByKey[groupKey], plan.RoomIds[groupKey], service.ScheduleId, sunday );
            _attendance.Rows.Add( _nextAttendanceId++, occurrenceId, person.AliasId, plan.CampusId, start, true,
                ( object ) _configuration.KioskDeviceId ?? DBNull.Value, ( object ) _searchTypeId ?? DBNull.Value, codeId, checkedInByAliasId, start,
                _attendedBefore.Add( person.PersonId ), Guid.NewGuid(), _foreignKey, start, start );

            if ( !CheckInSundaysByAlias.TryGetValue( person.AliasId, out var checkedIn ) )
            {
                checkedIn = new SortedSet<DateTime>();
                CheckInSundaysByAlias[person.AliasId] = checkedIn;
            }

            checkedIn.Add( sunday );
            CheckInSundays.Add( sunday );

            if ( !_checkInDaysByGroup.TryGetValue( groupKey, out var groupDays ) )
            {
                groupDays = new Dictionary<int, SortedSet<DateTime>>();
                _checkInDaysByGroup[groupKey] = groupDays;
            }

            if ( !groupDays.TryGetValue( person.PersonId, out var personDays ) )
            {
                personDays = new SortedSet<DateTime>();
                groupDays[person.PersonId] = personDays;
            }

            personDays.Add( sunday );
        }

        private int Occurrence( int groupId, int locationId, int scheduleId, DateTime date )
        {
            var key = (groupId, locationId, scheduleId, date);
            if ( !_occurrenceIds.TryGetValue( key, out var id ) )
            {
                id = _nextOccurrenceId++;
                _occurrenceIds[key] = id;
                _occurrences.Rows.Add( id, groupId, locationId, scheduleId, date, SundayCalendar.SundayOnOrAfter( date ), int.Parse( date.ToString( "yyyyMMdd" ) ), Guid.NewGuid(), _foreignKey, _now, _now );
            }

            return id;
        }

        /// <summary>
        /// A new security code, unique for the day.
        /// </summary>
        private int NewCode( SeededRandom random, DateTime sunday, DateTime start )
        {
            var codeId = _nextCodeId++;
            string code;
            do
            {
                code = Code( random );
            }
            while ( !_codesUsed.Add( (sunday, code) ) );

            _codes.Rows.Add( codeId, start, code, Guid.NewGuid(), _foreignKey );
            return codeId;
        }

        /// <summary>
        /// The kids multiplier relative to the adult one, so a Sunday with no children's ministry (Christmas week) has almost no kids.
        /// </summary>
        private double KidsFactor( DateTime sunday ) => Math.Min( 1, _calendar.Multiplier( sunday, "kids" ) / Math.Max( 0.01, _calendar.Multiplier( sunday, "adults" ) ) );

        /// <summary>
        /// The first and last Sundays a household could come.
        /// </summary>
        private (DateTime Started, DateTime Ended) Window( LoadedHousehold household )
        {
            return (_dates.Resolve( household.Cast.Attendance?.Started ) ?? _calendar.HistoryStart, _dates.Resolve( household.Cast.Attendance?.Ended ) ?? _calendar.HistoryEnd);
        }

        /// <summary>
        /// An adult whose graduation falls inside the history window, so they were a student in it.
        /// </summary>
        private bool IsRecentGraduate( LoadedPerson person ) => person.IsAdult && person.GraduationYear.HasValue && AnchorDates.GradeOn( person.GraduationYear.Value, _calendar.HistoryStart ) <= 12;

        /// <summary>
        /// A recent graduate who had not yet graduated on this Sunday.
        /// </summary>
        private bool IsStillInSchool( LoadedPerson adult, DateTime sunday ) => IsRecentGraduate( adult ) && AnchorDates.GradeOn( adult.GraduationYear.Value, sunday ) <= 12;

        /// <summary>
        /// A recent graduate away at college on this Sunday. They are home at Christmas, at Easter, and for the
        /// summer, and now and then for a weekend. The family pass still counts them home when they were the
        /// only one of the family there, so the family's Sundays never change.
        /// </summary>
        private bool IsAwayAtCollege( LoadedPerson adult, DateTime sunday )
        {
            if ( !IsRecentGraduate( adult ) )
            {
                return false;
            }

            if ( !_college.TryGetValue( adult.PersonId, out var college ) )
            {
                var random = new SeededRandom( $"{_seed}:college:{adult.Cast.Guid}" );
                college = (random.Chance( StudentRules.AwayAtCollegeShare ), random);
                _college[adult.PersonId] = college;
            }

            return college.IsAway && !IsHomeFromCollege( sunday ) && !college.Random.Chance( StudentRules.WeekendHomeChance );
        }

        /// <summary>
        /// Christmas, Easter, and summer Sundays, when college students are home.
        /// </summary>
        private bool IsHomeFromCollege( DateTime sunday )
        {
            var holiday = _calendar.Holiday( sunday );
            var isSummer = sunday.Month is 6 or 7 || ( sunday.Month == 5 && sunday.Day > 15 ) || ( sunday.Month == 8 && sunday.Day <= 15 );
            return isSummer || holiday is "easter" or "sundayBeforeChristmas" or "christmasWeek" or "newYear";
        }

        /// <summary>
        /// A student's own random stream for one family Sunday, so the student pass never shifts anyone else's draws.
        /// </summary>
        private SeededRandom StudentRandom( string purpose, LoadedPerson student, DateTime sunday ) => new SeededRandom( $"{_seed}:student-{purpose}:{student.Cast.Guid}:{sunday:yyyy-MM-dd}" );

        private static bool IsStudentGroup( string groupKey ) => groupKey is "grades-7-8" or "grades-9-12";

        /// <summary>
        /// A household's chance of attending on a normal Sunday, and of using its usual service.
        /// Households without an attendance pattern get one from their most engaged adult's status.
        /// </summary>
        private static (int Percent, int PrimaryPercent) AttendanceRate( LoadedHousehold household )
        {
            var attendance = household.Cast.Attendance;
            if ( attendance != null )
            {
                return (attendance.PercentAttended, attendance.PercentPrimaryService ?? 80);
            }

            var statuses = household.Members.Where( m => m.IsAdult ).Select( m => m.Cast.ConnectionStatus ).ToList();
            var percent = statuses.Contains( "Leading" ) ? 85
                : statuses.Contains( "Engaged" ) ? 72
                : statuses.Contains( "Connected" ) ? 55
                : statuses.Contains( "Known" ) ? 18
                : statuses.Contains( "Participant" ) ? 8
                : 4;
            return (percent, 80);
        }

        /// <summary>
        /// Picks a household's usual service. Mid-morning services are the most popular.
        /// </summary>
        private static ServicePlan PickPrimaryService( CampusPlan plan, SeededRandom random )
        {
            var weights = plan.Services.ToDictionary( s => s.ScheduleId.ToString(), s => s.Start.Hours switch
            {
                < 10 => 0.40,
                < 12 => 0.45,
                _ => 0.15
            } );
            var pick = int.Parse( random.Pick( weights ) );
            return plan.Services.First( s => s.ScheduleId == pick );
        }

        /// <summary>
        /// The check-in group for a child on a given Sunday, or null when none fits.
        /// Grade wins when known; otherwise age picks the nursery and preschool rooms.
        /// </summary>
        private static string CheckInGroupFor( LoadedPerson child, DateTime sunday )
        {
            if ( child.GraduationYear.HasValue )
            {
                var grade = AnchorDates.GradeOn( child.GraduationYear.Value, sunday );
                if ( grade >= 0 )
                {
                    return grade switch
                    {
                        <= 1 => "grades-k-1",
                        <= 3 => "grades-2-3",
                        <= 6 => "grades-4-6",
                        <= 8 => "grades-7-8",
                        <= 12 => "grades-9-12",
                        _ => null
                    };
                }
            }

            if ( child.BirthDate == null || child.BirthDate > sunday )
            {
                return null;
            }

            var months = ( ( sunday.Year - child.BirthDate.Value.Year ) * 12 ) + sunday.Month - child.BirthDate.Value.Month;
            return months switch
            {
                < 12 => "nursery",
                < 36 => "crawlers-walkers",
                < 66 => "preschool",
                _ => null
            };
        }

        /// <summary>
        /// A three-character security code, avoiding look-alike characters.
        /// </summary>
        private static string Code( SeededRandom random )
        {
            return new string( Enumerable.Range( 0, 3 ).Select( _ => CodeCharacters[random.Between( 0, CodeCharacters.Length - 1 )] ).ToArray() );
        }

        private int NextId( string table )
        {
            return Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
        }

        private static DataTable NewTable( params string[] columns )
        {
            return TableFactory.Create( columns );
        }

        /// <summary>
        /// How often kids and students come (decisions D-student-attendance-1 to D-student-attendance-3).
        /// </summary>
        private static class StudentRules
        {
            /// <summary>
            /// A student (grades 7 to 12) with their family checks in to the student service this often.
            /// </summary>
            public const double FamilyCheckIn = 0.85;

            /// <summary>
            /// A young kid (nursery to 6th grade) with their family checks in this often. They never come without the family.
            /// </summary>
            public const double YoungKidCheckIn = 0.95;

            /// <summary>
            /// Only students with these connection statuses come on their own or keep coming after their family stops.
            /// </summary>
            public static readonly HashSet<string> EngagedStatuses = new HashSet<string> { "Engaged", "Leading" };

            /// <summary>
            /// On a normal Sunday their family stayed home, an engaged student comes on their own with this share of the family's
            /// own chance of coming, so a student from a family that rarely comes rarely comes alone. High schoolers drive.
            /// </summary>
            public static readonly Dictionary<string, double> OnTheirOwn = new Dictionary<string, double>
            {
                ["grades-7-8"] = 0.25,
                ["grades-9-12"] = 0.50
            };

            /// <summary>
            /// The share of engaged students who keep coming after their family stops, at the rate they came with the family.
            /// </summary>
            public const double KeepComingShare = 0.4;

            /// <summary>
            /// How many weeks a student keeps coming after their family stops.
            /// </summary>
            public static readonly (int Min, int Max) KeepComingWeeks = (13, 52);

            /// <summary>
            /// The share of recent graduates away at college.
            /// </summary>
            public const double AwayAtCollegeShare = 0.5;

            /// <summary>
            /// The chance a college student is home for a weekend outside Christmas, Easter, and summer.
            /// </summary>
            public const double WeekendHomeChance = 0.1;
        }
    }
}
