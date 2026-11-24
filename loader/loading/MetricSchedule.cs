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

using System.Globalization;
using System.Text.RegularExpressions;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// A metric schedule as Rock stores it, read from its iCalendar content: a start, a time of day, and a daily, weekly,
    /// or monthly rule. It reads only the rules Rock's metric schedules use, and refuses any other, so a Rock that
    /// changes one is caught at load time instead of getting runs on the wrong days.
    /// </summary>
    internal sealed class MetricSchedule
    {
        private static readonly Regex Start = new Regex( @"^DTSTART(?:;[^:]*)?:(\d{8}T\d{6})", RegexOptions.Multiline );
        private static readonly Regex Rule = new Regex( @"^RRULE:(.+)$", RegexOptions.Multiline );

        private MetricSchedule( DateTime start, DayOfWeek? weekday, int? dayOfMonth )
        {
            StartsAt = start;
            Weekday = weekday;
            DayOfMonth = dayOfMonth;
        }

        /// <summary>
        /// The schedule's first start. Its time of day is the time of every run.
        /// </summary>
        public DateTime StartsAt { get; }

        /// <summary>
        /// The day of a weekly schedule.
        /// </summary>
        public DayOfWeek? Weekday { get; }

        /// <summary>
        /// The day of the month of a monthly schedule.
        /// </summary>
        public int? DayOfMonth { get; }

        /// <summary>
        /// Reads a schedule's iCalendar content. Throws for a rule this reader does not know.
        /// </summary>
        public static MetricSchedule Parse( string name, string iCalendar )
        {
            var start = Start.Match( iCalendar ?? string.Empty );
            var rule = Rule.Match( iCalendar ?? string.Empty );
            if ( !start.Success || !rule.Success )
            {
                throw new InvalidDataException( $"Rock's metric schedule {name} has no start or no repeat rule." );
            }

            var parts = rule.Groups[1].Value.Trim().Split( ';' ).Select( p => p.Split( '=' ) ).ToDictionary( p => p[0], p => p.Length > 1 ? p[1] : string.Empty );
            var startsAt = DateTime.ParseExact( start.Groups[1].Value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture );
            var known = parts.Keys.All( k => k == "FREQ" || k == "BYDAY" || k == "BYMONTHDAY" );
            parts.TryGetValue( "FREQ", out var frequency );
            if ( known && frequency == "DAILY" && parts.Count == 1 )
            {
                return new MetricSchedule( startsAt, null, null );
            }

            if ( known && frequency == "WEEKLY" && parts.TryGetValue( "BYDAY", out var day ) && parts.Count == 2 && Weekdays.TryGetValue( day, out var weekday ) )
            {
                return new MetricSchedule( startsAt, weekday, null );
            }

            if ( known && frequency == "MONTHLY" && parts.TryGetValue( "BYMONTHDAY", out var monthDay ) && parts.Count == 2 && int.TryParse( monthDay, out var dayOfMonth ) && dayOfMonth >= 1 && dayOfMonth <= 28 )
            {
                return new MetricSchedule( startsAt, null, dayOfMonth );
            }

            throw new InvalidDataException( $"Rock's metric schedule {name} repeats by \"{rule.Groups[1].Value.Trim()}\", which the loader cannot follow." );
        }

        /// <summary>
        /// Each run after <paramref name="after"/> and up to <paramref name="through"/>, oldest first, never before the
        /// schedule's start.
        /// </summary>
        public List<DateTime> Runs( DateTime after, DateTime through )
        {
            var runs = new List<DateTime>();
            for ( var day = after.Date; day <= through.Date; day = day.AddDays( 1 ) )
            {
                var run = day.Add( StartsAt.TimeOfDay );
                var isDay = Weekday == null && DayOfMonth == null || day.DayOfWeek == Weekday || day.Day == DayOfMonth;
                if ( isDay && run > after && run <= through && run >= StartsAt )
                {
                    runs.Add( run );
                }
            }

            return runs;
        }

        private static readonly Dictionary<string, DayOfWeek> Weekdays = new Dictionary<string, DayOfWeek>
        {
            ["SU"] = DayOfWeek.Sunday,
            ["MO"] = DayOfWeek.Monday,
            ["TU"] = DayOfWeek.Tuesday,
            ["WE"] = DayOfWeek.Wednesday,
            ["TH"] = DayOfWeek.Thursday,
            ["FR"] = DayOfWeek.Friday,
            ["SA"] = DayOfWeek.Saturday
        };
    }
}
