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

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Turns relative Cast dates into real dates for a given anchor (normally install day).
    /// </summary>
    internal sealed class AnchorDates
    {
        /// <summary>
        /// Rock's default grade transition date (the GradeTransitionDate global attribute): June 1.
        /// </summary>
        internal const int GradeTransitionMonth = 6;
        internal const int GradeTransitionDay = 1;

        /// <summary>
        /// The day every relative date counts back from.
        /// </summary>
        public DateTime Anchor { get; }

        public AnchorDates( DateTime anchor )
        {
            Anchor = anchor.Date;
        }

        /// <summary>
        /// Resolves a relative date. A daysAgo date counts back from the anchor. Otherwise, if the
        /// result falls after the anchor, it moves back one year.
        /// </summary>
        public DateTime? Resolve( RelativeDate relative )
        {
            if ( relative == null )
            {
                return null;
            }

            // Recent events ("first visit two weeks ago") must stay recent whatever the anchor's month.
            var date = relative.DaysAgo.HasValue
                ? Anchor.AddDays( -relative.DaysAgo.Value )
                : SafeDate( Anchor.Year - relative.YearsAgo, relative.Month, relative.Day );
            if ( date > Anchor )
            {
                date = SafeDate( date.Year - 1, relative.Month, relative.Day );
            }

            if ( !string.IsNullOrEmpty( relative.Time ) )
            {
                date = date.Add( TimeSpan.Parse( relative.Time ) );
            }

            return date;
        }

        /// <summary>
        /// Picks the birth date that gives this age on the anchor date. A February 29 birthday
        /// uses the nearest leap year, so the age may be off by at most one.
        /// </summary>
        public DateTime BirthDate( CastBirth birth )
        {
            var year = Anchor.Year - birth.Age;
            var birthdayThisYear = SafeDate( Anchor.Year, birth.Month, birth.Day );
            if ( birthdayThisYear > Anchor )
            {
                year--;
            }

            if ( birth.Month == 2 && birth.Day == 29 )
            {
                // Search outward from the target year for the leap year whose age is closest.
                var best = Enumerable.Range( year - 4, 9 )
                    .Where( DateTime.IsLeapYear )
                    .OrderBy( y => Math.Abs( AgeOn( new DateTime( y, 2, 29 ) ) - birth.Age ) )
                    .ThenBy( y => Math.Abs( y - year ) )
                    .First();
                return new DateTime( best, 2, 29 );
            }

            return new DateTime( year, birth.Month, birth.Day );
        }

        /// <summary>
        /// The age in whole years on the anchor date.
        /// </summary>
        public int AgeOn( DateTime birthDate )
        {
            var age = Anchor.Year - birthDate.Year;
            if ( birthDate.Date > Anchor.AddYears( -age ) )
            {
                age--;
            }

            return age;
        }

        /// <summary>
        /// The graduation year for a school grade on the anchor date (0 = kindergarten, 12 = senior),
        /// using Rock's grade transition date.
        /// </summary>
        public int GraduationYear( int grade )
        {
            var transition = new DateTime( Anchor.Year, GradeTransitionMonth, GradeTransitionDay );
            var schoolYearEnds = Anchor >= transition ? Anchor.Year + 1 : Anchor.Year;
            return schoolYearEnds + ( 12 - grade );
        }

        /// <summary>
        /// The school grade on a day for a graduation year (12 = senior, 13 = graduated), moving up each grade
        /// transition date as Rock's check-in does.
        /// </summary>
        public static int GradeOn( int graduationYear, DateTime day )
        {
            var schoolYearEnds = day.Date >= new DateTime( day.Year, GradeTransitionMonth, GradeTransitionDay ) ? day.Year + 1 : day.Year;
            return 12 - ( graduationYear - schoolYearEnds );
        }

        /// <summary>
        /// The day a person may first join an adult group or team: their birthday at the minimum age, or, when
        /// their graduation is known, the grade transition date after it, whichever is later. Grade decides,
        /// so an 18-year-old senior waits until they are out of high school (decision 169).
        /// </summary>
        public DateTime AdultSince( CastPerson person, int minimumAge )
        {
            // A few Cast adults have no birth date on file; their age is unknown, so only a known graduation limits them.
            var byAge = person.Birth == null ? DateTime.MinValue : BirthDate( person.Birth ).AddYears( minimumAge );
            var graduated = Resolve( person.Graduation );
            if ( !graduated.HasValue )
            {
                return byAge;
            }

            var outOfHighSchool = new DateTime( graduated.Value.Year, GradeTransitionMonth, GradeTransitionDay );
            return outOfHighSchool > byAge ? outOfHighSchool : byAge;
        }

        /// <summary>
        /// Rock's AgeBracket value for an age.
        /// </summary>
        public static int AgeBracket( int? age )
        {
            if ( age == null )
            {
                return 0;
            }

            return age switch
            {
                < 6 => 1,
                < 13 => 2,
                < 18 => 3,
                < 25 => 4,
                < 35 => 5,
                < 45 => 6,
                < 55 => 7,
                < 65 => 8,
                _ => 9
            };
        }

        /// <summary>
        /// Builds a date, clamping the day to the end of the month (for example February 29 in a common year).
        /// </summary>
        private static DateTime SafeDate( int year, int month, int day )
        {
            return new DateTime( year, month, Math.Min( day, DateTime.DaysInMonth( year, month ) ) );
        }
    }
}
