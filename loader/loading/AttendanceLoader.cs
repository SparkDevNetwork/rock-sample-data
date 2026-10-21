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

    /// <summary>
    /// Generates Sunday worship attendance for adults and check-in attendance for kids.
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
        /// The planned rows, which <see cref="Write"/> writes.
        /// </summary>
        private (DataTable Occurrences, DataTable Codes, DataTable Attendance) _planned;

        /// <summary>
        /// Plans attendance for every household and fills the plans that later steps read. Other parts
        /// read them even when attendance is not written, so planning never depends on the parts chosen.
        /// </summary>
        public void Plan( List<LoadedHousehold> households )
        {
            var now = DateTime.Now;
            var searchTypeId = _db.IdByGuid( "DefinedValue", PhoneSearchTypeGuid );
            var occurrences = NewTable( "Id:int", "GroupId:int", "LocationId:int", "ScheduleId:int", "OccurrenceDate:date", "SundayDate:date", "OccurrenceDateKey:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var attendance = NewTable( "Id:int", "OccurrenceId:int", "PersonAliasId:int", "CampusId:int", "StartDateTime:date", "DidAttend:bool", "DeviceId:int", "SearchTypeValueId:int", "AttendanceCodeId:int", "CheckedInByPersonAliasId:int", "PresentDateTime:date", "IsFirstTime:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var codes = NewTable( "Id:int", "IssueDateTime:date", "Code:string", "Guid:guid", "ForeignKey:string" );

            var nextOccurrenceId = NextId( "AttendanceOccurrence" );
            var nextAttendanceId = NextId( "Attendance" );
            var nextCodeId = NextId( "AttendanceCode" );
            var occurrenceIds = new Dictionary<(int Group, int Location, int Schedule, DateTime Date), int>();
            var attendedBefore = new HashSet<int>();
            var checkedInThisSunday = new HashSet<(int PersonId, DateTime Sunday)>();

            // Rock keeps security codes unique within a day, and the database enforces it.
            var codesUsed = new HashSet<(DateTime Day, string Code)>();

            int Occurrence( int groupId, int locationId, int scheduleId, DateTime date )
            {
                var key = (groupId, locationId, scheduleId, date);
                if ( !occurrenceIds.TryGetValue( key, out var id ) )
                {
                    id = nextOccurrenceId++;
                    occurrenceIds[key] = id;
                    occurrences.Rows.Add( id, groupId, locationId, scheduleId, date, SundayCalendar.SundayOnOrAfter( date ), int.Parse( date.ToString( "yyyyMMdd" ) ), Guid.NewGuid(), _foreignKey, now, now );
                }

                return id;
            }

            foreach ( var household in households )
            {
                var plan = _configuration.CampusPlans[household.CampusKey];
                var random = new SeededRandom( $"{_seed}:attendance:{household.Cast.Guid}" );
                var (percent, primaryPercent) = AttendanceRate( household );
                var started = _dates.Resolve( household.Cast.Attendance?.Started ) ?? _calendar.HistoryStart;
                var ended = _dates.Resolve( household.Cast.Attendance?.Ended ) ?? _calendar.HistoryEnd;
                var primary = PickPrimaryService( plan, random );
                var adults = household.Members.Where( m => m.IsAdult && m.CanAttend && m.FamilyId == household.FamilyId ).ToList();
                var children = household.Members.Where( m => !m.IsAdult && m.CanAttend ).ToList();
                var sundays = new HashSet<DateTime>();
                FamilySundays[household.FamilyId] = sundays;

                // A child on their own (usually a student) can still come and check in without an adult.
                if ( percent <= 0 || ( adults.Count == 0 && children.Count == 0 ) )
                {
                    continue;
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

                    foreach ( var adult in present )
                    {
                        var start = sunday.Add( service.Start ).AddMinutes( -random.Between( 0, 15 ) );
                        var occurrenceId = Occurrence( _configuration.WorshipGroupIdValue, plan.LocationId, service.ScheduleId, sunday );
                        attendance.Rows.Add( nextAttendanceId++, occurrenceId, adult.AliasId, plan.CampusId, start, true, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, attendedBefore.Add( adult.PersonId ), Guid.NewGuid(), _foreignKey, start, start );
                    }

                    // Kids check in with the family. The kids multiplier is relative to the adult one,
                    // so a Sunday with no children's ministry (Christmas week) has almost no kids.
                    var kidsFactor = Math.Min( 1, _calendar.Multiplier( sunday, "kids" ) / Math.Max( 0.01, adultFactor ) );
                    int? codeId = null;
                    foreach ( var child in children )
                    {
                        var groupKey = CheckInGroupFor( child, sunday );
                        if ( groupKey == null || !checkedInThisSunday.Add( (child.PersonId, sunday) ) )
                        {
                            continue;
                        }

                        var isStudent = groupKey == "grades-7-8" || groupKey == "grades-9-12";
                        if ( !random.Chance( ( isStudent ? 0.85 : 0.95 ) * kidsFactor ) )
                        {
                            checkedInThisSunday.Remove( (child.PersonId, sunday) );
                            continue;
                        }

                        var childService = isStudent ? plan.StudentService : service;
                        var start = sunday.Add( childService.Start ).AddMinutes( -random.Between( 2, 25 ) );
                        if ( codeId == null )
                        {
                            codeId = nextCodeId++;
                            string code;
                            do
                            {
                                code = Code( random );
                            }
                            while ( !codesUsed.Add( (sunday, code) ) );

                            codes.Rows.Add( codeId, start, code, Guid.NewGuid(), _foreignKey );
                        }

                        var occurrenceId = Occurrence( _configuration.CheckInGroupIdsByKey[groupKey], plan.RoomIds[groupKey], childService.ScheduleId, sunday );
                        attendance.Rows.Add( nextAttendanceId++, occurrenceId, child.AliasId, plan.CampusId, start, true,
                            ( object ) _configuration.KioskDeviceId ?? DBNull.Value, ( object ) searchTypeId ?? DBNull.Value, codeId.Value, present.Count > 0 ? present[0].AliasId : child.AliasId, start,
                            attendedBefore.Add( child.PersonId ), Guid.NewGuid(), _foreignKey, start, start );

                        if ( !CheckInSundaysByAlias.TryGetValue( child.AliasId, out var checkedIn ) )
                        {
                            checkedIn = new SortedSet<DateTime>();
                            CheckInSundaysByAlias[child.AliasId] = checkedIn;
                        }

                        checkedIn.Add( sunday );
                        CheckInSundays.Add( sunday );
                    }
                }
            }

            _planned = (occurrences, codes, attendance);
        }

        /// <summary>
        /// Writes the attendance <see cref="Plan"/> planned.
        /// </summary>
        public void Write()
        {
            var (occurrences, codes, attendance) = _planned;
            _db.BulkInsert( "AttendanceOccurrence", occurrences, keepIdentity: true );
            _db.BulkInsert( "AttendanceCode", codes, keepIdentity: true );
            _db.BulkInsert( "Attendance", attendance, keepIdentity: true );

            var checkIns = attendance.AsEnumerable().Count( r => r["AttendanceCodeId"] != DBNull.Value );
            _db.Log.Write( $"  Sundays: {_calendar.Sundays.Count}, occurrences: {occurrences.Rows.Count:N0}, attendance: {attendance.Rows.Count:N0} ({checkIns:N0} kids check-ins)" );
        }

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
                var schoolYearEnds = sunday >= new DateTime( sunday.Year, 6, 1 ) ? sunday.Year + 1 : sunday.Year;
                var grade = 12 - ( child.GraduationYear.Value - schoolYearEnds );
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
    }
}
