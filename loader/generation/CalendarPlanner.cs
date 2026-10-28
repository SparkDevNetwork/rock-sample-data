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

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        10/27/26 - CLAUDE

        Every event date is a rule counted from the install day, so the calendar is always current. Dates that
        other features use come from the same rules they use: Easter, the baptism Sundays, the Student Camp and
        mission trip seasons, the Starting Point and Foundations classes (ClassSchedule), VBS, the group launches,
        Serve Sunday, and Commitment Sunday. So a newsletter, a step, and the calendar always name the same day.

        One occurrence is one campus at one start time, the way a person adds them on Rock's occurrence screen:
        a weekly or monthly rule with no end, one date, or a list of dates. A yearly event gets an occurrence
        each year, so the calendar keeps its history the way a church's calendar does.

        Reason: The calendar tells the same story as steps and messages on any install day (decision D-calendar-2).
    */

    /// <summary>
    /// Plans every event's occurrences for one load: which campus, where, who to contact, and the schedule.
    /// Reads no database.
    /// </summary>
    internal sealed class CalendarPlanner
    {
        private readonly CalendarConfiguration _calendar;
        private readonly StepsConfiguration _steps;
        private readonly CommunicationEvents _events;
        private readonly CampaignConfiguration _campaign;
        private readonly DateTime _anchor;
        private readonly DateTime _historyStart;
        private readonly DateTime _windowEnd;

        public CalendarPlanner( CalendarConfiguration calendar, StepsConfiguration steps, CommunicationEvents events, CampaignConfiguration campaign, DateTime anchor, DateTime historyStart )
        {
            _calendar = calendar;
            _steps = steps;
            _events = events;
            _campaign = campaign;
            _anchor = anchor.Date;
            _historyStart = historyStart.Date;
            _windowEnd = _anchor.AddMonths( calendar.MonthsAhead );
        }

        /// <summary>
        /// The occurrences of every event at the dataset's campuses, in calendar.json order. An event with no
        /// occurrence in this dataset (for example, one only at a campus the dataset does not have) is left out.
        /// </summary>
        /// <param name="campusServices">Each of the dataset's campuses, in order, with its Sunday service start times.</param>
        public List<PlannedEvent> Plan( IReadOnlyList<(string Key, IReadOnlyList<TimeSpan> Services)> campusServices )
        {
            var services = campusServices.ToDictionary( c => c.Key, c => c.Services );
            var planned = new List<PlannedEvent>();
            foreach ( var item in _calendar.Events )
            {
                var occurrences = new List<PlannedOccurrence>();
                var campuses = item.IsChurchWide ? new List<string> { null } : item.CampusKeys( campusServices.Select( c => c.Key ) ).Cast<string>().ToList();
                foreach ( var campus in campuses )
                {
                    var times = item.IsAtServices ? services[campus].ToList() : item.StartTimes( campus );
                    foreach ( var dates in DateSets( item ) )
                    {
                        foreach ( var time in times )
                        {
                            occurrences.Add( new PlannedOccurrence
                            {
                                EventKey = item.Key,
                                CampusKey = campus,
                                Location = Location( item, campus, dates.Season ),
                                Schedule = Schedule( item, dates, time )
                            } );
                        }
                    }
                }

                if ( occurrences.Count > 0 )
                {
                    planned.Add( new PlannedEvent { Event = item, Occurrences = occurrences } );
                }
            }

            return planned;
        }

        #region Dates

        /// <summary>
        /// One set of dates an occurrence covers: a rule with no end, or a list of dates.
        /// </summary>
        private sealed class DateSet
        {
            /// <summary>
            /// The dates, for a set with an end.
            /// </summary>
            public List<DateTime> Dates { get; set; }

            /// <summary>
            /// The first date and the rule after it, for a set with no end.
            /// </summary>
            public DateTime First { get; set; }

            public RepeatRule Repeat { get; set; }

            /// <summary>
            /// For a set of weekly dates, an RRULE with a COUNT instead of a list of dates.
            /// </summary>
            public bool IsWeeklyCount { get; set; }

            public string Season { get; set; }
        }

        private IEnumerable<DateSet> DateSets( CalendarEvent item )
        {
            var when = item.When ?? throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} has no 'when'." );
            switch ( when.Kind )
            {
                case "weekly":
                    yield return Ongoing( RepeatRule.Weekly( Weekday( item, when.Weekday ), 1 ), FirstWeekday( _historyStart, Weekday( item, when.Weekday ) ) );
                    break;

                case "monthly":
                    {
                        var weekday = Weekday( item, when.Weekday );
                        yield return Ongoing( RepeatRule.Monthly( weekday, when.Week ), RepeatRule.FirstMonthly( _historyStart, weekday, when.Week ) );
                        break;
                    }

                case "pinned" when when.Repeat == "weeks":
                    {
                        var weekday = Weekday( item, when.Weekday );
                        var phase = Pinned( when, weekday, 0 );
                        var first = phase.AddDays( -7 * when.EveryWeeks * Math.Floor( ( phase - _historyStart ).TotalDays / ( 7 * when.EveryWeeks ) ) );
                        yield return Ongoing( RepeatRule.Weekly( weekday, when.EveryWeeks ), first );
                        break;
                    }

                case "pinned" when when.Repeat == "yearly":
                    for ( var yearsAgo = -1; yearsAgo <= _anchor.Year - _historyStart.Year + 1; yearsAgo++ )
                    {
                        foreach ( var set in InWindow( Days( Pinned( when, Weekday( item, when.Weekday ), yearsAgo ), item ) ) )
                        {
                            yield return set;
                        }
                    }

                    break;

                case "seasons":
                    foreach ( var set in Seasons( item, when ) )
                    {
                        yield return set;
                    }

                    break;

                case "class":
                    foreach ( var set in Classes( item, when ) )
                    {
                        yield return set;
                    }

                    break;

                case "step-season":
                    foreach ( var set in StepSeasons( item, when ) )
                    {
                        yield return set;
                    }

                    break;

                default:
                    foreach ( var day in YearlyDays( item, when ).Distinct().OrderBy( d => d ) )
                    {
                        foreach ( var set in InWindow( Repeated( day, item, when ) ) )
                        {
                            yield return set;
                        }
                    }

                    break;
            }
        }

        private static DateSet Ongoing( RepeatRule rule, DateTime first ) => new DateSet { First = first, Repeat = rule };

        /// <summary>
        /// The first day of each yearly instance, for the kinds that fall once or a few times a year.
        /// </summary>
        private IEnumerable<DateTime> YearlyDays( CalendarEvent item, EventWhen when )
        {
            var years = Enumerable.Range( _historyStart.Year - 1, _windowEnd.Year - _historyStart.Year + 2 ).ToList();
            switch ( when.Kind )
            {
                case "yearly":
                    var dates = when.Dates ?? new List<MonthWeek> { new MonthWeek { Month = when.Month, Week = when.Week, Weekday = when.Weekday } };
                    return years.SelectMany( y => dates.Select( d => d.In( y ) ) );

                case "date":
                    return years.Select( y => new DateTime( y, when.Month, when.Day ) );

                case "easter":
                    return years.Select( y => SundayCalendar.Easter( y ).AddDays( when.OffsetDays ) );

                case "occasion":
                    return OccasionDays( item, when.Occasion, years ).Select( d => d.AddDays( when.OffsetDays ) );

                default:
                    throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} has 'when' kind \"{when.Kind}\", which the loader does not know." );
            }
        }

        /// <summary>
        /// The days of an occasion other files define, the same days the message library uses.
        /// </summary>
        private IEnumerable<DateTime> OccasionDays( CalendarEvent item, string occasion, List<int> years )
        {
            switch ( occasion )
            {
                case "baptism-sunday":
                    var summer = StepType( item, "baptism" ).Source.SummerSunday;
                    return years.SelectMany( y => new[] { SundayCalendar.Easter( y ), summer.In( y ) } );

                case "serve-sunday":
                    return years.Select( y => Required( item, _events?.ServeSunday ).In( y ) );

                case "group-launch":
                    return years.SelectMany( y => new[] { Required( item, _events?.FallGroupLaunch ).In( y ), Required( item, _events?.SpringGroupLaunch ).In( y ) } );

                case "vbs":
                    return years.Select( y => Required( item, _events?.Vbs ).In( y ) );

                case "commitment-sunday":
                    return _campaign == null ? Enumerable.Empty<DateTime>() : new[] { _campaign.CommitmentSunday( _anchor ) };

                default:
                    throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} names occasion \"{occasion}\", which the loader does not know." );
            }
        }

        private static T Required<T>( CalendarEvent item, T value ) where T : class
        {
            return value ?? throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} needs a date that configuration/communications.json does not hold." );
        }

        /// <summary>
        /// A yearly day as a set: that day, the days after it (days), or the same weekday in the weeks after it (weeks).
        /// A span covers its days as one block, so its set is its first day.
        /// </summary>
        private DateSet Repeated( DateTime day, CalendarEvent item, EventWhen when )
        {
            if ( when.Weeks > 1 )
            {
                return new DateSet { Dates = Enumerable.Range( 0, when.Weeks ).Select( w => day.AddDays( 7 * w ) ).ToList() };
            }

            return Days( day, item );
        }

        private static DateSet Days( DateTime day, CalendarEvent item )
        {
            var count = item.Span ? 1 : Math.Max( 1, item.Days );
            return new DateSet { Dates = Enumerable.Range( 0, count ).Select( d => day.AddDays( d ) ).ToList() };
        }

        /// <summary>
        /// The set, when its first day is inside the window: from the history start to the window's end.
        /// </summary>
        private IEnumerable<DateSet> InWindow( DateSet set )
        {
            if ( set.Dates[0] >= _historyStart && set.Dates[0] <= _windowEnd )
            {
                yield return set;
            }
        }

        /// <summary>
        /// A pinned day: the install day minus daysAgo, moved to the weekday, in the year yearsAgo before.
        /// </summary>
        private DateTime Pinned( EventWhen when, DayOfWeek weekday, int yearsAgo )
        {
            var day = _anchor.AddDays( -when.DaysAgo ).AddYears( -yearsAgo );
            var back = ( ( int ) day.DayOfWeek - ( int ) weekday + 7 ) % 7;
            return when.Direction == "after" ? day.AddDays( ( 7 - back ) % 7 ) : day.AddDays( -back );
        }

        /// <summary>
        /// A weekly season each year, as an RRULE with a COUNT: every week from its first day to its last.
        /// </summary>
        private IEnumerable<DateSet> Seasons( CalendarEvent item, EventWhen when )
        {
            for ( var year = _historyStart.Year - 1; year <= _windowEnd.Year; year++ )
            {
                foreach ( var season in when.Seasons )
                {
                    var from = season.From.In( year );
                    var until = season.Until.In( year );
                    var weeks = ( int ) ( ( until - from ).TotalDays / 7 ) + 1;
                    var set = new DateSet { Dates = Enumerable.Range( 0, weeks ).Select( w => from.AddDays( 7 * w ) ).ToList(), IsWeeklyCount = true };
                    foreach ( var inWindow in InWindow( set ) )
                    {
                        yield return inWindow;
                    }
                }
            }
        }

        /// <summary>
        /// Every class of a steps class schedule that started in the window: its weekly session dates.
        /// </summary>
        private IEnumerable<DateSet> Classes( CalendarEvent item, EventWhen when )
        {
            var type = StepType( item, when.StepType );
            var schedule = new ClassSchedule( type.Source, _anchor, type.Key );
            var classesAgo = 0;
            while ( schedule.Start( classesAgo - 1 ) <= _windowEnd )
            {
                classesAgo--;
            }

            for ( ; schedule.Start( classesAgo ) >= _historyStart; classesAgo++ )
            {
                var start = schedule.Start( classesAgo );
                yield return new DateSet { Dates = Enumerable.Range( 0, type.Source.Sessions ).Select( s => start.AddDays( 7 * s ) ).ToList() };
            }
        }

        /// <summary>
        /// Every camp or trip of a steps event type in the window, each one block from its first day to its last.
        /// </summary>
        private IEnumerable<DateSet> StepSeasons( CalendarEvent item, EventWhen when )
        {
            var type = StepType( item, when.StepType );
            var seasons = type.Source.Seasons.Where( s => when.Season == null || s.Key == when.Season ).ToList();
            if ( seasons.Count == 0 )
            {
                throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} names season \"{when.Season}\", which {type.Key} in configuration/steps.json does not hold." );
            }

            for ( var year = _historyStart.Year - 1; year <= _windowEnd.Year; year++ )
            {
                foreach ( var season in seasons )
                {
                    var set = new DateSet { Dates = new List<DateTime> { season.In( year ) }, Season = season.Key };
                    foreach ( var inWindow in InWindow( set ) )
                    {
                        yield return inWindow;
                    }
                }
            }
        }

        private StepTypeDefinition StepType( CalendarEvent item, string key )
        {
            return _steps?.StepTypes.FirstOrDefault( t => t.Key == key )
                ?? throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} names step type \"{key}\", which configuration/steps.json does not hold." );
        }

        /// <summary>
        /// The number of days a step-season event lasts, from its season.
        /// </summary>
        private int SeasonDays( CalendarEvent item, string season )
        {
            return StepType( item, item.When.StepType ).Source.Seasons.Single( s => s.Key == season ).Days;
        }

        private static DayOfWeek Weekday( CalendarEvent item, string weekday )
        {
            return Enum.TryParse<DayOfWeek>( weekday, true, out var day ) ? day : throw new InvalidDataException( $"configuration/calendar.json: event {item.Key} needs a weekday." );
        }

        private static DateTime FirstWeekday( DateTime from, DayOfWeek weekday ) => from.AddDays( ( ( int ) weekday - ( int ) from.DayOfWeek + 7 ) % 7 );

        #endregion

        #region Places and schedules

        private string Location( CalendarEvent item, string campus, string season )
        {
            var room = season != null && item.LocationBySeason.TryGetValue( season, out var seasonal ) ? seasonal : item.Location;
            return campus != null && _calendar.LocationNames.TryGetValue( campus, out var names ) && names.TryGetValue( room, out var renamed ) ? renamed : room;
        }

        private PlannedSchedule Schedule( CalendarEvent item, DateSet dates, TimeSpan time )
        {
            if ( dates.Repeat != null )
            {
                return new PlannedSchedule( dates.First.Add( time ), item.Minutes, dates.Repeat, null, false );
            }

            var starts = dates.Dates.Select( d => d.Add( time ) ).ToList();
            if ( !item.Span )
            {
                return new PlannedSchedule( starts[0], item.Minutes, null, starts, dates.IsWeeklyCount );
            }

            var days = item.When.Kind == "step-season" ? SeasonDays( item, dates.Season ) : Math.Max( 1, item.Days );
            var end = dates.Dates[0].AddDays( days - 1 ).Add( TimeSpan.Parse( item.EndTime ?? throw new InvalidDataException( $"configuration/calendar.json: span event {item.Key} needs an endTime." ) ) );
            return new PlannedSchedule( starts[0], ( int ) ( end - starts[0] ).TotalMinutes, null, starts, false );
        }

        #endregion
    }

    /// <summary>
    /// One event item and its planned occurrences.
    /// </summary>
    internal sealed class PlannedEvent
    {
        public CalendarEvent Event { get; set; }

        public List<PlannedOccurrence> Occurrences { get; set; }
    }

    /// <summary>
    /// One occurrence: a campus (null for church-wide), a place, and a schedule.
    /// </summary>
    internal sealed class PlannedOccurrence
    {
        public string EventKey { get; set; }

        public string CampusKey { get; set; }

        public string Location { get; set; }

        public PlannedSchedule Schedule { get; set; }
    }

    /// <summary>
    /// A weekly rule every few weeks, or a monthly rule on one weekday of the month, with no end.
    /// </summary>
    internal sealed class RepeatRule
    {
        private readonly Func<DateTime, DateTime> _next;

        private RepeatRule( string rrule, Func<DateTime, DateTime> next )
        {
            RRule = rrule;
            _next = next;
        }

        /// <summary>
        /// The rule in iCalendar form, for example "FREQ=WEEKLY;INTERVAL=4;BYDAY=SA".
        /// </summary>
        public string RRule { get; }

        /// <summary>
        /// The next date after a date the rule falls on.
        /// </summary>
        public DateTime Next( DateTime day ) => _next( day );

        public static RepeatRule Weekly( DayOfWeek weekday, int everyWeeks )
        {
            var interval = everyWeeks > 1 ? $"INTERVAL={everyWeeks};" : string.Empty;
            return new RepeatRule( $"FREQ=WEEKLY;{interval}BYDAY={Day( weekday )}", d => d.AddDays( 7 * everyWeeks ) );
        }

        public static RepeatRule Monthly( DayOfWeek weekday, int week )
        {
            return new RepeatRule( $"FREQ=MONTHLY;BYDAY={week}{Day( weekday )}", d => InMonth( d.AddMonths( 1 ), weekday, week ) );
        }

        /// <summary>
        /// The first day on or after a day that falls on the given weekday of its month.
        /// </summary>
        public static DateTime FirstMonthly( DateTime day, DayOfWeek weekday, int week )
        {
            var date = InMonth( day, weekday, week );
            return date >= day.Date ? date : InMonth( day.AddMonths( 1 ), weekday, week );
        }

        private static DateTime InMonth( DateTime day, DayOfWeek weekday, int week ) => new MonthWeek { Month = day.Month, Week = week, Weekday = weekday.ToString() }.In( day.Year );

        private static string Day( DayOfWeek weekday ) => weekday.ToString().Substring( 0, 2 ).ToUpperInvariant();
    }

    /// <summary>
    /// An occurrence's schedule: its start times, how long each lasts, and the iCalendar text and dates Rock's
    /// save hooks would store for it.
    /// </summary>
    internal sealed class PlannedSchedule
    {
        private readonly RepeatRule _repeat;
        private readonly List<DateTime> _starts;
        private readonly bool _isWeeklyCount;

        public PlannedSchedule( DateTime firstStart, int minutes, RepeatRule repeat, List<DateTime> starts, bool isWeeklyCount )
        {
            FirstStart = firstStart;
            Minutes = minutes;
            _repeat = repeat;
            _starts = starts;
            _isWeeklyCount = isWeeklyCount;
        }

        public DateTime FirstStart { get; }

        public int Minutes { get; }

        /// <summary>
        /// True when the schedule has no end.
        /// </summary>
        public bool IsOngoing => _repeat != null;

        /// <summary>
        /// Every start up to a day, oldest first.
        /// </summary>
        public IEnumerable<DateTime> Starts( DateTime until )
        {
            if ( _repeat == null )
            {
                foreach ( var start in _starts.Where( s => s <= until ) )
                {
                    yield return start;
                }

                yield break;
            }

            for ( var start = FirstStart; start <= until; start = _repeat.Next( start.Date ).Add( FirstStart.TimeOfDay ) )
            {
                yield return start;
            }
        }

        /*
            10/27/26 - CLAUDE

            Rock's Schedule save hook sets EffectiveStartDate and EffectiveEndDate (Schedule.EnsureEffectiveStartEndDates)
            and writes a ScheduleDate row for each start from now minus two years to now plus two years
            (ScheduleService.UpdateScheduleDates). Rock Cleanup does both again every night, and its next-start task
            sets EventItemOccurrence.NextStartDateTime. The loader writes the same values so Rock finds nothing to fix.
            iCal counts an occurrence in a range when it overlaps the range, not only when it starts in it.

            Reason: Bulk-copied schedules look the same as schedules saved in Rock.
        */

        /// <summary>
        /// The first day, as Rock's save hook sets it.
        /// </summary>
        public DateTime EffectiveStartDate => FirstStart.Date;

        /// <summary>
        /// The day the last start ends, or null when the schedule has no end.
        /// </summary>
        public DateTime? EffectiveEndDate => IsOngoing ? null : _starts[_starts.Count - 1].AddMinutes( Minutes ).Date;

        /// <summary>
        /// The starts that overlap a range, as iCal and Rock count them.
        /// </summary>
        public IEnumerable<DateTime> StartsOverlapping( DateTime from, DateTime until )
        {
            return Starts( until ).Where( s => s < until && s.AddMinutes( Minutes ) > from );
        }

        /// <summary>
        /// The next start at or after now, as Rock's Schedule.GetNextStartDateTime finds it within two years.
        /// </summary>
        public DateTime? NextStart( DateTime now ) => StartsOverlapping( now, now.AddYears( 2 ) ).Cast<DateTime?>().FirstOrDefault();

        /// <summary>
        /// The iCalendar text, in the shape Rock's schedule builder writes.
        /// </summary>
        public string ICalendar( Guid uid, DateTime stamp )
        {
            var lines = new List<string>
            {
                "BEGIN:VCALENDAR",
                "PRODID:-//github.com/SparkDevNetwork/Rock//NONSGML Rock//EN",
                "VERSION:2.0",
                "BEGIN:VEVENT",
                $"DTEND:{Stamp( FirstStart.AddMinutes( Minutes ) )}",
                $"DTSTAMP:{Stamp( stamp )}",
                $"DTSTART:{Stamp( FirstStart )}"
            };

            if ( _repeat != null )
            {
                lines.Add( $"RRULE:{_repeat.RRule}" );
            }
            else if ( _isWeeklyCount )
            {
                lines.Add( $"RRULE:FREQ=WEEKLY;COUNT={_starts.Count};BYDAY={FirstStart.DayOfWeek.ToString().Substring( 0, 2 ).ToUpperInvariant()}" );
            }
            else if ( _starts.Count > 1 )
            {
                lines.Add( $"RDATE:{string.Join( ",", _starts.Select( Stamp ) )}" );
            }

            lines.Add( "SEQUENCE:0" );
            lines.Add( $"UID:{uid.ToString().ToLowerInvariant()}" );
            lines.Add( "END:VEVENT" );
            lines.Add( "END:VCALENDAR" );
            return string.Join( "\r\n", lines );
        }

        private static string Stamp( DateTime value ) => value.ToString( "yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture );
    }
}
