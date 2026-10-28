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

using System.Net;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/27/26 - CLAUDE

        Rock ships four sample events (Staff Meeting, Customs & Classics Car Show, Rock Solid Finances Class, and
        Warrior Youth Event) with schedules in 2015 and 2018 and placeholder text. Each becomes one of the church's
        events, updated in place under Rock's Guid the way the Discipleship Path is, and its old occurrences,
        schedules, calendars, and audiences are replaced. Every other event is new.

        Every event is approved by Ted Decker, who holds the Calendar Administration role: Rock's public calendar
        blocks and the app show only active, approved events.

        Reason: The calendar has no placeholder events, and every event shows where Rock lists events (decision 174).
    */

    /// <summary>
    /// Loads the church's calendar: event items on Rock's Public and Internal calendars, their audiences, and their
    /// occurrences at each campus with schedules, contacts, and places. Event photos come from photos/events.
    /// </summary>
    internal sealed class CalendarLoader
    {
        private const string CampusPastor = "campus-pastor";

        /// <summary>
        /// The Cast person who approves events: Ted Decker, in RSR - Calendar Administration.
        /// </summary>
        private static readonly Guid ApproverGuid = new Guid( "8FEDC6EE-8630-41ED-9FC5-C7157FD1EAA4" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly DateTime _historyStart;

        public CalendarLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed, DateTime historyStart )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _historyStart = historyStart;
        }

        /// <summary>
        /// Plans and writes every event. Run after people load.
        /// </summary>
        /// <param name="people">The loaded people: contacts and the approver are Cast people.</param>
        /// <param name="castHouseholdKeys">The Cast's households. Campus pastors come from the rest.</param>
        /// <param name="mainCampusKey">The main campus, whose stand-in campus pastor is a Cast person.</param>
        /// <param name="campusPlans">The dataset's campuses with their Sunday services.</param>
        public void Load( PeopleLoader people, HashSet<string> castHouseholdKeys, string mainCampusKey, IReadOnlyDictionary<string, CampusPlan> campusPlans )
        {
            var calendar = _source.Calendar;
            if ( calendar == null )
            {
                _db.Log.Write( "  Calendar skipped (this package has no configuration/calendar.json)" );
                return;
            }

            var campusServices = _source.Campuses.Campuses
                .Where( c => campusPlans.ContainsKey( c.Key ) )
                .Select( c => (c.Key, ( IReadOnlyList<TimeSpan> ) campusPlans[c.Key].Services.Select( s => s.Start ).ToList()) )
                .ToList();
            var planned = new CalendarPlanner( calendar, _source.Steps, _source.Communications?.Events, _source.Campaign, _dates.Anchor, _historyStart ).Plan( campusServices );

            var contacts = Contacts( calendar, people, castHouseholdKeys, mainCampusKey );
            var approverAliasId = people.People.TryGetValue( ApproverGuid, out var approver ) ? approver.AliasId : ( int? ) null;
            var calendarIds = calendar.Calendars.ToDictionary( c => c.Key, c => _db.RequireIdByGuid( "EventCalendar", c.Value ) );
            var audienceIds = calendar.Audiences.ToDictionary( a => a.Key, a => _db.RequireIdByGuid( "DefinedValue", a.Value ) );

            /*
                10/27/26 - CLAUDE

                Every stamp and the ScheduleDate window count from the install day at midnight, so the same seed and
                anchor give the same rows. Rock Cleanup rebuilds ScheduleDate from the moment it runs, and the verify
                tool leaves out a day at each edge, so the window may start at midnight. Only the next start uses the
                clock: Rock sets it from RockDateTime.Now, so an event earlier today already counts as past.

                Reason: Repeatable loads that Rock Cleanup still finds nothing to fix in.
            */
            var today = _dates.Anchor.Date;
            var now = today.Add( DateTime.Now.TimeOfDay );
            var eventIds = WriteEvents( planned, approverAliasId, today );
            WriteCalendarsAndAudiences( planned, eventIds, calendarIds, audienceIds, today );
            var occurrenceCount = WriteOccurrences( planned, eventIds, contacts, campusPlans, today, now );

            var photoCount = new PhotoLoader( _db, _source, _foreignKey, _seed ).LoadEventPhotos( eventIds );
            _db.Log.Write( $"  Calendar: {planned.Count} events ({planned.Count( p => p.Event.Calendars.Contains( "internal" ) && !p.Event.Calendars.Contains( "public" ) )} internal), {occurrenceCount:N0} occurrences, {photoCount} photos" );
        }

        /// <summary>
        /// Each contact key's person and details, and each campus's campus pastor under "campus-pastor:{campus}".
        /// </summary>
        private static Dictionary<string, (int AliasId, string Email, string Phone)> Contacts( CalendarConfiguration calendar, PeopleLoader people, HashSet<string> castHouseholdKeys, string mainCampusKey )
        {
            var contacts = new Dictionary<string, (int, string, string)>( StringComparer.Ordinal );
            foreach ( var (key, contact) in calendar.Contacts )
            {
                var person = people.People.TryGetValue( contact.Person, out var found ) ? found
                    : throw new InvalidDataException( $"configuration/calendar.json: contact {key} is person {contact.Person}, who is not in the Cast." );
                contacts[key] = (person.AliasId, contact.Email, contact.Phone);
            }

            var generated = people.Households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) );
            foreach ( var (campus, pastor) in CampusPastors.Pick( generated, mainCampusKey ) )
            {
                contacts[$"{CampusPastor}:{campus}"] = (pastor.AliasId, $"{campus}@rocksolidchurchdemo.com", calendar.CampusPastorPhone);
            }

            return contacts;
        }

        /// <summary>
        /// Updates Rock's own sample events in place and adds the rest. Returns event item Ids by key.
        /// </summary>
        private Dictionary<string, int> WriteEvents( List<PlannedEvent> planned, int? approverAliasId, DateTime today )
        {
            var ids = new Dictionary<string, int>( StringComparer.Ordinal );
            var nextId = NextId( "EventItem" );
            var items = TableFactory.Create( "Id:int", "Name:string", "Summary:string", "Description:string", "DetailsUrl:string", "IsActive:bool", "IsApproved:bool", "ApprovedByPersonAliasId:int", "ApprovedOnDateTime:date",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var plan in planned )
            {
                var item = plan.Event;
                var created = Created( plan.Occurrences.Min( o => o.Schedule.FirstStart ), today );
                var approved = Min( created.AddDays( 1 ), today );
                var description = string.Join( string.Empty, item.Description.Select( p => $"<p>{WebUtility.HtmlEncode( p )}</p>" ) );
                var existingId = item.RockEvent ? _db.IdByGuid( "EventItem", item.Guid ) : null;
                if ( existingId != null )
                {
                    ClearRockEvent( existingId.Value );
                    _db.Execute( @"
UPDATE [EventItem] SET [Name] = @Name, [Summary] = @Summary, [Description] = @Description, [DetailsUrl] = '', [PhotoId] = NULL, [IsActive] = 1,
    [IsApproved] = 1, [ApprovedByPersonAliasId] = @Approver, [ApprovedOnDateTime] = @Approved, [ModifiedDateTime] = @Now
WHERE [Id] = @Id",
                        P( "@Name", item.Name ), P( "@Summary", item.Summary ), P( "@Description", description ), P( "@Approver", ( object ) approverAliasId ?? DBNull.Value ),
                        P( "@Approved", approved ), P( "@Now", today ), P( "@Id", existingId.Value ) );
                    ids[item.Key] = existingId.Value;
                    continue;
                }

                var id = nextId++;
                items.Rows.Add( id, item.Name, item.Summary, description, string.Empty, true, true, ( object ) approverAliasId ?? DBNull.Value, approved, item.Guid, _foreignKey, created, approved );
                ids[item.Key] = id;
            }

            _db.BulkInsert( "EventItem", items, keepIdentity: true );
            return ids;
        }

        /// <summary>
        /// Removes a Rock sample event's calendars, audiences, occurrences, and the schedules only those occurrences used.
        /// </summary>
        private void ClearRockEvent( int eventItemId )
        {
            var scheduleIds = _db.Query( "SELECT DISTINCT [ScheduleId] FROM [EventItemOccurrence] WHERE [EventItemId] = @Id AND [ScheduleId] IS NOT NULL", P( "@Id", eventItemId ) )
                .Select( r => Convert.ToInt32( r["ScheduleId"] ) )
                .ToList();

            _db.Execute( "DELETE FROM [EventItemOccurrence] WHERE [EventItemId] = @Id", P( "@Id", eventItemId ) );
            _db.Execute( "DELETE FROM [EventCalendarItem] WHERE [EventItemId] = @Id", P( "@Id", eventItemId ) );
            _db.Execute( "DELETE FROM [EventItemAudience] WHERE [EventItemId] = @Id", P( "@Id", eventItemId ) );

            foreach ( var scheduleId in scheduleIds )
            {
                var isUsed = Convert.ToInt32( _db.Scalar( @"
SELECT CASE WHEN EXISTS ( SELECT 1 FROM [EventItemOccurrence] WHERE [ScheduleId] = @Id )
    OR EXISTS ( SELECT 1 FROM [Group] WHERE [ScheduleId] = @Id )
    OR EXISTS ( SELECT 1 FROM [AttendanceOccurrence] WHERE [ScheduleId] = @Id )
    OR EXISTS ( SELECT 1 FROM [GroupLocationSchedule] WHERE [ScheduleId] = @Id ) THEN 1 ELSE 0 END", P( "@Id", scheduleId ) ) ) == 1;
                if ( !isUsed )
                {
                    _db.Execute( "DELETE FROM [ScheduleDate] WHERE [ScheduleId] = @Id", P( "@Id", scheduleId ) );
                    _db.Execute( "DELETE FROM [Schedule] WHERE [Id] = @Id", P( "@Id", scheduleId ) );
                }
            }
        }

        private void WriteCalendarsAndAudiences( List<PlannedEvent> planned, Dictionary<string, int> eventIds, Dictionary<string, int> calendarIds, Dictionary<string, int> audienceIds, DateTime today )
        {
            var calendarItems = TableFactory.Create( "EventCalendarId:int", "EventItemId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var audiences = TableFactory.Create( "EventItemId:int", "DefinedValueId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var plan in planned )
            {
                var item = plan.Event;
                foreach ( var key in item.Calendars )
                {
                    var calendarId = calendarIds.TryGetValue( key, out var id ) ? id : throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} is on calendar \"{key}\", which 'calendars' does not hold." );
                    calendarItems.Rows.Add( calendarId, eventIds[item.Key], SeededRandom.StableGuid( $"calendar-item:{item.Guid}:{key}" ), _foreignKey, today, today );
                }

                foreach ( var key in item.Audiences )
                {
                    var audienceId = audienceIds.TryGetValue( key, out var id ) ? id : throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} has audience \"{key}\", which 'audiences' does not hold." );
                    audiences.Rows.Add( eventIds[item.Key], audienceId, SeededRandom.StableGuid( $"calendar-audience:{item.Guid}:{key}" ), _foreignKey, today, today );
                }
            }

            _db.BulkInsert( "EventCalendarItem", calendarItems, keepIdentity: false );
            _db.BulkInsert( "EventItemAudience", audiences, keepIdentity: false );
        }

        /// <summary>
        /// Writes each occurrence with its unnamed schedule, the way Rock's occurrence screen saves one, and the
        /// schedule's dates and next start as Rock's save hooks would set them. Returns how many occurrences.
        /// </summary>
        private int WriteOccurrences( List<PlannedEvent> planned, Dictionary<string, int> eventIds, Dictionary<string, (int AliasId, string Email, string Phone)> contacts, IReadOnlyDictionary<string, CampusPlan> campusPlans, DateTime today, DateTime now )
        {
            var nextScheduleId = NextId( "Schedule" );
            var schedules = TableFactory.Create( "Id:int", "iCalendarContent:string", "EffectiveStartDate:date", "EffectiveEndDate:date", "IsActive:bool", "Order:int", "AutoInactivateWhenComplete:bool", "IsPublic:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var scheduleDates = TableFactory.Create( "ScheduleId:int", "StartDateTime:date", "EndDateTime:date", "StartDateKey:int" );
            var occurrences = TableFactory.Create( "EventItemId:int", "CampusId:int", "Location:string", "ScheduleId:int", "ContactPersonAliasId:int", "ContactPhone:string", "ContactEmail:string", "Note:string", "NextStartDateTime:date",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var mainCampus = _source.Campuses.Campuses.Single( c => c.RockDefault ).Key;
            var calendar = _source.Calendar;
            var count = 0;

            foreach ( var plan in planned )
            {
                foreach ( var occurrence in plan.Occurrences )
                {
                    var schedule = occurrence.Schedule;
                    var key = $"{plan.Event.Key}:{occurrence.CampusKey ?? "church-wide"}:{schedule.FirstStart:s}";
                    var created = Created( schedule.FirstStart, today );
                    var scheduleId = nextScheduleId++;
                    schedules.Rows.Add( scheduleId, schedule.ICalendar( SeededRandom.StableGuid( $"calendar-uid:{key}" ), created ), schedule.EffectiveStartDate, ( object ) schedule.EffectiveEndDate ?? DBNull.Value,
                        true, 0, false, true, SeededRandom.StableGuid( $"calendar-schedule:{key}" ), _foreignKey, created, created );

                    foreach ( var start in schedule.StartsOverlapping( today.AddYears( -2 ), today.AddYears( 2 ) ) )
                    {
                        scheduleDates.Rows.Add( scheduleId, start, start.AddMinutes( schedule.Minutes ), int.Parse( start.ToString( "yyyyMMdd" ) ) );
                    }

                    var contact = Contact( plan.Event, occurrence.CampusKey, mainCampus, calendar, contacts );
                    var campusId = occurrence.CampusKey == null ? DBNull.Value : ( object ) campusPlans[occurrence.CampusKey].CampusId;
                    occurrences.Rows.Add( eventIds[plan.Event.Key], campusId, occurrence.Location, scheduleId, contact.AliasId, contact.Phone, contact.Email, string.Empty,
                        ( object ) schedule.NextStart( now ) ?? DBNull.Value, SeededRandom.StableGuid( $"calendar-occurrence:{key}" ), _foreignKey, created, created );
                    count++;
                }
            }

            _db.BulkInsert( "Schedule", schedules, keepIdentity: true );
            _db.BulkInsert( "ScheduleDate", scheduleDates, keepIdentity: false );
            _db.BulkInsert( "EventItemOccurrence", occurrences, keepIdentity: false );
            return count;
        }

        /// <summary>
        /// The contact for an occurrence: a named contact, or the campus's campus pastor. The main campus, a church-wide
        /// event, and a campus with no campus pastor use the main campus's stand-in.
        /// </summary>
        private static (int AliasId, string Email, string Phone) Contact( CalendarEvent item, string campus, string mainCampus, CalendarConfiguration calendar, Dictionary<string, (int AliasId, string Email, string Phone)> contacts )
        {
            if ( item.Contact != CampusPastor )
            {
                return contacts.TryGetValue( item.Contact, out var named ) ? named : throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} has contact \"{item.Contact}\", which 'contacts' does not hold." );
            }

            return campus != null && campus != mainCampus && contacts.TryGetValue( $"{CampusPastor}:{campus}", out var pastor ) ? pastor : contacts[calendar.MainCampusPastor];
        }

        /// <summary>
        /// When an occurrence was added: six weeks before its first start, and never after the install day began.
        /// </summary>
        private static DateTime Created( DateTime firstStart, DateTime today ) => Min( firstStart.Date.AddDays( -42 ).AddHours( 10 ), today );

        private static DateTime Min( DateTime a, DateTime b ) => a < b ? a : b;

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
    }
}
