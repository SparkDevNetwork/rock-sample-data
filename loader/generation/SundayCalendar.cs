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

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// The Sundays in the history window, the holiday each one falls on, and how much each
    /// Sunday's attendance rises or falls compared with a normal week.
    /// </summary>
    internal sealed class SundayCalendar
    {
        private readonly ProfileTargets _profile;
        private readonly Dictionary<DateTime, string> _holidays = new Dictionary<DateTime, string>();

        /// <summary>
        /// Every Sunday from the start of history through the anchor, oldest first.
        /// </summary>
        public List<DateTime> Sundays { get; } = new List<DateTime>();

        /// <summary>
        /// The first day of the history window.
        /// </summary>
        public DateTime HistoryStart { get; }

        /// <summary>
        /// The last day of the history window (the anchor).
        /// </summary>
        public DateTime HistoryEnd { get; }

        public SundayCalendar( DateTime anchor, int historyYears, ProfileTargets profile )
        {
            _profile = profile;
            HistoryEnd = anchor.Date;
            HistoryStart = anchor.Date.AddYears( -historyYears );

            for ( var sunday = SundayOnOrAfter( HistoryStart ); sunday <= HistoryEnd; sunday = sunday.AddDays( 7 ) )
            {
                Sundays.Add( sunday );
            }

            for ( var year = HistoryStart.Year - 1; year <= HistoryEnd.Year; year++ )
            {
                foreach ( var holiday in HolidayWeekends( year ) )
                {
                    _holidays[holiday.Value] = holiday.Key;
                }
            }
        }

        /// <summary>
        /// The holiday a Sunday falls on (for example "easter"), or null.
        /// </summary>
        public string Holiday( DateTime sunday )
        {
            return _holidays.TryGetValue( sunday.Date, out var name ) ? name : null;
        }

        /// <summary>
        /// How a Sunday compares with a normal week for one measure ("kids", "adults", or "firstTime").
        /// Holidays use the profile's holiday multipliers; other weeks use the month index.
        /// </summary>
        public double Multiplier( DateTime sunday, string measure )
        {
            var holiday = Holiday( sunday );
            if ( holiday != null )
            {
                return _profile.Number( $"attendance.holidayMultipliers.{holiday}.{measure}", 1 );
            }

            return measure == "firstTime" ? 1 : _profile.Number( $"attendance.kidsMonthIndex.{sunday:MM}", 1 );
        }

        /// <summary>
        /// Rock's SundayDate: the Sunday on or after a date.
        /// </summary>
        public static DateTime SundayOnOrAfter( DateTime date )
        {
            return date.Date.AddDays( ( 7 - ( int ) date.DayOfWeek ) % 7 );
        }

        /// <summary>
        /// The Sunday each holiday affects in a year. Matches calibration/normalize.js.
        /// </summary>
        private static Dictionary<string, DateTime> HolidayWeekends( int year )
        {
            var july4 = new DateTime( year, 7, 4 );
            var july4Weekend = july4.DayOfWeek == DayOfWeek.Sunday ? july4
                : ( int ) july4.DayOfWeek <= 3 ? july4.AddDays( -( int ) july4.DayOfWeek )
                : SundayOnOrAfter( july4 );

            return new Dictionary<string, DateTime>
            {
                ["easter"] = Easter( year ),
                ["mothersDay"] = NthWeekday( year, 5, DayOfWeek.Sunday, 2 ),
                ["memorialDay"] = LastWeekday( year, 5, DayOfWeek.Monday ).AddDays( -1 ),
                ["july4"] = july4Weekend,
                ["laborDay"] = NthWeekday( year, 9, DayOfWeek.Monday, 1 ).AddDays( -1 ),
                ["fallKickoff"] = LastWeekday( year, 8, DayOfWeek.Sunday ),
                ["thanksgiving"] = NthWeekday( year, 11, DayOfWeek.Thursday, 4 ).AddDays( 3 ),
                ["sundayBeforeChristmas"] = SundayOnOrAfter( new DateTime( year, 12, 24 ) ).AddDays( -7 ),
                ["christmasWeek"] = SundayOnOrAfter( new DateTime( year, 12, 24 ) ),
                ["newYear"] = SundayOnOrAfter( new DateTime( year + 1, 1, 1 ) )
            };
        }

        private static DateTime NthWeekday( int year, int month, DayOfWeek day, int n )
        {
            var first = new DateTime( year, month, 1 );
            return first.AddDays( ( ( int ) day - ( int ) first.DayOfWeek + 7 ) % 7 + ( n - 1 ) * 7 );
        }

        private static DateTime LastWeekday( int year, int month, DayOfWeek day )
        {
            var last = new DateTime( year, month, DateTime.DaysInMonth( year, month ) );
            return last.AddDays( -( ( ( int ) last.DayOfWeek - ( int ) day + 7 ) % 7 ) );
        }

        /// <summary>
        /// Western Easter (anonymous Gregorian algorithm).
        /// </summary>
        private static DateTime Easter( int year )
        {
            int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
            int f = ( b + 8 ) / 25, g = ( b - f + 1 ) / 3, h = ( 19 * a + b - d - g + 15 ) % 30;
            int i = c / 4, k = c % 4, l = ( 32 + 2 * e + 2 * i - h - k ) % 7, m = ( a + 11 * h + 22 * l ) / 451;
            var month = ( h + l - 7 * m + 114 ) / 31;
            var day = ( ( h + l - 7 * m + 114 ) % 31 ) + 1;
            return new DateTime( year, month, day );
        }
    }
}
