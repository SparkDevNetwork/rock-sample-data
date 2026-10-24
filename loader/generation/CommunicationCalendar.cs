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
        10/23/26 - CLAUDE

        Every message names an occasion, and this dates each occasion from the install day, so a
        message always matches when it was sent (docs/communications.md). The dates come from the
        same rules the rest of the data uses: baptism Sundays, Student Camp, the mission trips, and
        the Starting Point and Foundations classes from configuration/steps.json, Commitment Sunday
        from configuration/campaign.json, and VBS, the group launches, and Serve Sunday from
        configuration/communications.json. Sermon series fill three slots a year, and the Built on
        the Rock series takes the four Sundays that end on Commitment Sunday.

        Reason: Messages tie to the church's real programs on the dates the data already has.
    */

    /// <summary>
    /// The dates of every message occasion over the communication history window.
    /// </summary>
    internal sealed class CommunicationCalendar
    {
        private readonly DateTime _anchor;
        private readonly DateTime _from;
        private readonly Dictionary<string, List<DateTime>> _dates = new Dictionary<string, List<DateTime>>();
        private readonly Dictionary<DateTime, (SermonSeries Series, int Week)> _series = new Dictionary<DateTime, (SermonSeries, int)>();

        /// <param name="anchor">The install day.</param>
        /// <param name="from">The first day of the history window.</param>
        public CommunicationCalendar( DateTime anchor, DateTime from, StepsConfiguration steps, CampaignConfiguration campaign, CommunicationEvents events, SeriesLibrary series, string seed )
        {
            _anchor = anchor.Date;
            _from = from.Date;

            // A little room on both sides, so a message sent before or after its occasion still finds it.
            var years = Enumerable.Range( _from.Year - 1, _anchor.Year - _from.Year + 3 ).ToList();

            Add( "sunday", Weekdays( DayOfWeek.Sunday ) );
            Add( "newsletter", Weekdays( DayOfWeek.Thursday ) );
            Add( "easter", years.Select( SundayCalendar.Easter ) );
            Add( "christmas", years.Select( y => new DateTime( y, 12, 24 ) ) );
            Add( "thanksgiving", years.Select( y => new MonthWeek { Month = 11, Week = 4, Weekday = nameof( DayOfWeek.Thursday ) }.In( y ) ) );
            Add( "year-end", years.Select( y => new DateTime( y, 12, 31 ) ) );
            Add( "giving-statement", years.Select( y => new DateTime( y, 1, 31 ) ) );

            if ( campaign != null )
            {
                var commitment = campaign.CommitmentSunday( _anchor );
                Add( "commitment-sunday", new[] { commitment } );
                var end = campaign.End( commitment );
                Add( "campaign-update", Enumerable.Range( 1, 12 ).Select( q => commitment.AddDays( 91 * q ) ).Where( d => d <= end ) );
            }

            var types = steps?.StepTypes.ToDictionary( t => t.Key ) ?? new Dictionary<string, StepTypeDefinition>();
            if ( types.TryGetValue( "baptism", out var baptism ) )
            {
                Add( "baptism-sunday", years.SelectMany( y => new[] { SundayCalendar.Easter( y ), baptism.Source.SummerSunday.In( y ) } ) );
            }

            AddSeasons( "student-camp", types.GetValueOrDefault( "student-camp" ), years );
            AddSeasons( "mission-trip", types.GetValueOrDefault( "mission-trip" ), years );
            AddClasses( "starting-point", types.GetValueOrDefault( "starting-point-class" ) );
            AddClasses( "foundations", types.GetValueOrDefault( "foundations" ) );

            if ( events?.Vbs != null )
            {
                Add( "vbs", years.Select( y => events.Vbs.In( y ) ) );
            }

            Add( "group-launch", years.SelectMany( y => new[] { events?.FallGroupLaunch?.In( y ), events?.SpringGroupLaunch?.In( y ) } ).Where( d => d.HasValue ).Select( d => d.Value ) );
            if ( events?.ServeSunday != null )
            {
                Add( "serve-sunday", years.Select( y => events.ServeSunday.In( y ) ) );
            }

            // A couple of monsoon storms each summer close a campus or move a service online.
            var random = new SeededRandom( $"{seed}:communications:weather" );
            Add( "weather", years.Select( y => new DateTime( y, 7, 1 ).AddDays( 7 * random.Between( 0, 8 ) ) ).Select( d => d.AddDays( -( int ) d.DayOfWeek ) ) );

            PlanSeries( series, campaign, years );
        }

        /// <summary>
        /// The occasion's dates, oldest first. An unknown occasion has none.
        /// </summary>
        public IReadOnlyList<DateTime> Dates( string occasion ) => _dates.TryGetValue( occasion, out var dates ) ? dates : new List<DateTime>();

        /// <summary>
        /// The sermon series running on a Sunday, and which week of it, or null.
        /// </summary>
        public (SermonSeries Series, int Week)? SeriesOn( DateTime sunday ) => _series.TryGetValue( sunday.Date, out var value ) ? value : null;

        /// <summary>
        /// The next occurrence of an occasion on or after a day, or null.
        /// </summary>
        public DateTime? Next( string occasion, DateTime day ) => Dates( occasion ).Where( d => d >= day.Date ).Cast<DateTime?>().FirstOrDefault();

        private void Add( string occasion, IEnumerable<DateTime> dates )
        {
            if ( !_dates.TryGetValue( occasion, out var list ) )
            {
                list = _dates[occasion] = new List<DateTime>();
            }

            list.AddRange( dates.Select( d => d.Date ).Where( d => d >= _from.AddDays( -60 ) && d <= _anchor.AddDays( 120 ) ) );
            list.Sort();
            _dates[occasion] = list.Distinct().ToList();
        }

        private IEnumerable<DateTime> Weekdays( DayOfWeek weekday )
        {
            var day = _from.AddDays( -60 );
            day = day.AddDays( ( ( int ) weekday - ( int ) day.DayOfWeek + 7 ) % 7 );
            for ( ; day <= _anchor.AddDays( 120 ); day = day.AddDays( 7 ) )
            {
                yield return day;
            }
        }

        private void AddSeasons( string occasion, StepTypeDefinition type, List<int> years )
        {
            if ( type?.Source?.Seasons != null )
            {
                Add( occasion, years.SelectMany( y => type.Source.Seasons.Select( s => s.In( y ) ) ) );
            }
        }

        /// <summary>
        /// Every class's first night, the same at every campus, back from the one running on install day.
        /// </summary>
        private void AddClasses( string occasion, StepTypeDefinition type )
        {
            if ( type?.Source == null )
            {
                return;
            }

            var schedule = new ClassSchedule( type.Source, _anchor, type.Key );
            var starts = new List<DateTime>();
            for ( var classesAgo = -1; schedule.Start( classesAgo ) >= _from.AddDays( -60 ); classesAgo++ )
            {
                starts.Add( schedule.Start( classesAgo ) );
            }

            Add( occasion, starts );
        }

        /// <summary>
        /// Fills each year's three slots with its series (the first of a slot's two in even years), then lays the
        /// campaign series over the four Sundays that end on Commitment Sunday.
        /// </summary>
        private void PlanSeries( SeriesLibrary library, CampaignConfiguration campaign, List<int> years )
        {
            if ( library == null )
            {
                return;
            }

            var starts = new List<(DateTime Start, SermonSeries Series, int Weeks)>();
            foreach ( var year in years )
            {
                foreach ( var (slotKey, slot) in library.Slots )
                {
                    var choices = library.Series.Where( s => s.Slot == slotKey ).ToList();
                    if ( choices.Count == 0 )
                    {
                        continue;
                    }

                    var start = slotKey switch
                    {
                        "new-year" => new MonthWeek { Month = 1, Week = 1 }.In( year ),
                        "after-easter" => SundayCalendar.Easter( year ).AddDays( 7 ),
                        "fall" => new MonthWeek { Month = 9, Week = 2 }.In( year ),
                        _ => throw new InvalidDataException( $"content/communications/series.json has slot {slotKey}, which the loader does not know." )
                    };
                    starts.Add( (start, choices[year % 2 == 0 ? 0 : Math.Min( 1, choices.Count - 1 )], slot.Weeks) );
                }
            }

            foreach ( var (start, series, weeks) in starts )
            {
                for ( var week = 1; week <= weeks; week++ )
                {
                    _series[start.AddDays( 7 * ( week - 1 ) )] = (series, week);
                }
            }

            if ( library.Campaign != null && campaign != null )
            {
                var commitment = campaign.CommitmentSunday( _anchor );
                var weeks = Math.Max( 1, library.Campaign.Weeks );
                for ( var week = 1; week <= weeks; week++ )
                {
                    _series[commitment.AddDays( -7 * ( weeks - week ) )] = (library.Campaign, week);
                }
            }

            // A series starts on its week 1, or on the first Sunday after the campaign series cut into it.
            Add( "series-start", _series.Where( s => s.Value.Week == 1 || !_series.ContainsKey( s.Key.AddDays( -7 ) ) ).Select( s => s.Key ) );
        }
    }
}
