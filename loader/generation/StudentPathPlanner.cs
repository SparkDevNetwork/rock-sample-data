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

using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        10/22/26 - CLAUDE

        The Student Path reads the student check-ins the attendance step wrote. A student's grade on a day
        comes from their graduation year, moving up the day after each June 1, as Rock does, and they have been attending
        when they checked in to the student service enough times in the weeks before. Camps and trips are
        church-wide events on fixed days of the year, so everyone at one camp shares its dates.

        Who takes each step is a share of the students who could, counted separately for the Cast and for the
        generated households, as baptisms are. A Cast story counts toward its share, a story that names events
        gets exactly those, and the load fails when a story cannot hold. A dataset has only a few dozen
        students, so a share drawn person by person would wander far from the designed rate. The Cast's share
        depends only on the Cast, so the Cast's steps are the same in every dataset.

        Reason: Student steps agree with the check-ins and grades Rock shows, without waiting for attendance
        per person, and every dataset shows the designed rates (decisions 115 and 116).
    */
    /*
        10/28/26 - CLAUDE

        The Student Path is planned once in every load, from the people, the attendance plan, and the seed,
        before any part writes. The steps part copies its camps, trips, decisions, and Student Leader steps,
        and the registrations part reads the same plan, so last summer's campers are exactly the people with
        last summer's Student Camp step without registrations needing the steps part. The rules moved here
        from StepPlanner unchanged, so the steps are the same as before.

        Reason: Registrations agree with the Student Path without depending on the Steps part (decision 206).
    */

    /// <summary>
    /// Plans the Student Path's check-in steps: events (Student Camp, Mission Trip), the Follow Jesus decision, and
    /// Student Leader. Reads no database.
    /// </summary>
    internal sealed class StudentPathPlanner
    {
        /// <summary>
        /// Step source kinds this planner owns, in the order they are planned. A decision is dated from the events.
        /// </summary>
        public static readonly string[] SourceKinds = { "event", "decision", "school-years" };

        private readonly StepsConfiguration _config;
        private readonly string _seed;
        private readonly StudentPath _path;
        private readonly Dictionary<string, StepProgramDefinition> _programByType;

        private StudentPathPlanner( StepsConfiguration config, StudentPath path, string seed )
        {
            _config = config;
            _path = path;
            _seed = seed;
            _programByType = config.Programs.SelectMany( p => p.StepTypes.Select( t => (Program: p, t.Key) ) ).ToDictionary( x => x.Key, x => x.Program );
        }

        /// <summary>
        /// Plans every event, decision, and school-years step type in the configuration.
        /// </summary>
        /// <param name="people">Everyone who can have steps, in <see cref="StepPerson.Of"/> order.</param>
        /// <param name="checkIns">The days each person checked in to the student service, from the attendance plan.</param>
        public static StudentPath Plan( StepsConfiguration config, IReadOnlyList<StepPerson> people, Dictionary<int, SortedSet<DateTime>> checkIns, DateTime historyStart, DateTime anchor, string seed )
        {
            var students = config.Programs.Where( p => p.Students != null ).ToList();
            var path = new StudentPath( people, checkIns, historyStart, anchor, students.Count == 1 ? students[0].Students.Attending : null );
            var planner = new StudentPathPlanner( config, path, seed );
            foreach ( var kind in SourceKinds )
            {
                foreach ( var type in config.StepTypes.Where( t => t.Source.Kind == kind ) )
                {
                    switch ( kind )
                    {
                        case "event":
                            planner.PlanEvents( type );
                            break;
                        case "decision":
                            planner.PlanDecision( type );
                            break;
                        default:
                            planner.PlanSchoolYears( type );
                            break;
                    }
                }
            }

            return path;
        }

        private sealed record EventOccurrence( string Season, int Year, DateTime Start, DateTime End );

        /// <summary>
        /// Events (camps and trips): a share of the students who could go go once, in a year they could, and then
        /// sometimes again in later years. Each is a Complete step from the event's first day to its last.
        /// </summary>
        private void PlanEvents( StepTypeDefinition type )
        {
            var source = type.Source;
            var attending = StudentsOf( type ).Attending;
            var occurrences = Occurrences( source );
            var could = _path.People.Where( p => p.GraduationYear.HasValue )
                .ToDictionary( p => p.PersonId, p => occurrences.Where( o => StudentPath.InGrades( p, o.Start, source.Grades ) && _path.HasBeenAttending( p.PersonId, o.Start, attending ) ).ToList() );
            var goers = ChooseShare( type.Key, _path.People.Where( p => could.TryGetValue( p.PersonId, out var list ) && list.Count > 0 ), source.PercentOfStudents, p => Story( p, type ) != null );

            foreach ( var person in _path.People.Where( p => goers.Contains( p.PersonId ) || Story( p, type ) != null ).OrderBy( p => p.Guid ) )
            {
                var story = Story( person, type );
                var eligible = could.TryGetValue( person.PersonId, out var list ) ? list : new List<EventOccurrence>();
                List<EventOccurrence> went;
                if ( story?.Events != null )
                {
                    went = story.Events.Select( e => StoryEvent( type, e ) ).ToList();
                    var impossible = went.FirstOrDefault( o => !eligible.Contains( o ) );
                    if ( impossible != null )
                    {
                        throw new InvalidOperationException( $"The Cast story for {person.Name} puts them at {type.Name} in the {impossible.Season} of {impossible.Year}, when they were not a student in grades {source.Grades.Min} to {source.Grades.Max} who had been attending." );
                    }
                }
                else
                {
                    went = eligible.Count > 0 ? PickEvents( source, eligible, PersonRandom( type, person ) )
                        : throw new InvalidOperationException( $"The Cast story for {person.Name} needs a {type.Name}, but they were never a student who could go." );
                }

                foreach ( var occurrence in went )
                {
                    _path.Add( person, type.Key, StepStatus.Complete, occurrence.Start, occurrence.End, occurrence.Start );
                }
            }
        }

        /// <summary>
        /// The events one student who goes goes to: one in a random year they could go, and then, in each later
        /// such year, another one some of the time. A year's events are the ones held in that calendar year.
        /// </summary>
        private static List<EventOccurrence> PickEvents( StepSource source, List<EventOccurrence> could, SeededRandom random )
        {
            var years = could.GroupBy( o => o.Year ).Select( g => g.ToList() ).ToList();
            var first = random.Between( 0, years.Count - 1 );
            var went = new List<EventOccurrence> { random.Pick( years[first] ) };
            foreach ( var year in years.Skip( first + 1 ) )
            {
                if ( random.Chance( source.AgainPercent / 100 ) )
                {
                    went.Add( random.Pick( year ) );
                }
            }

            return went;
        }

        /// <summary>
        /// A decision, made once by a share of the students: often at one of their events, otherwise on a Sunday
        /// they checked in to the student service, sometimes Easter.
        /// </summary>
        private void PlanDecision( StepTypeDefinition type )
        {
            var source = type.Source;
            var eventType = _config.StepTypes.Single( t => t.Key == source.AtEvent.StepType );
            var deciders = ChooseShare( type.Key, _path.People.Where( _path.IsStudent ), source.PercentOfStudents, p => Story( p, type ) != null );

            foreach ( var person in _path.People.Where( p => deciders.Contains( p.PersonId ) || Story( p, type ) != null ).OrderBy( p => p.Guid ) )
            {
                var story = Story( person, type );
                var random = PersonRandom( type, person );
                DateTime date;
                if ( story?.Events != null )
                {
                    var occurrence = story.Events.Count == 1 ? StoryEvent( eventType, story.Events[0] ) : throw new InvalidOperationException( $"The Cast story for {person.Name} names {story.Events.Count} events for their {type.Name}; a decision is made at one." );
                    if ( !_path.StepsOf( person.PersonId ).Any( s => s.StepType == eventType.Key && s.Start == occurrence.Start ) )
                    {
                        throw new InvalidOperationException( $"The Cast story for {person.Name} makes their {type.Name} at {eventType.Name} in the {occurrence.Season} of {occurrence.Year}, which they did not go to." );
                    }

                    date = DayOf( occurrence.Start, occurrence.End, random );
                }
                else
                {
                    date = _path.IsStudent( person ) ? DecisionDate( source, eventType.Key, person, random )
                        : throw new InvalidOperationException( $"The Cast story for {person.Name} needs a {type.Name}, but they never checked in to the student service." );
                }

                _path.Add( person, type.Key, StepStatus.Complete, date, null, date );
            }
        }

        private DateTime DecisionDate( StepSource source, string eventTypeKey, StepPerson person, SeededRandom random )
        {
            var events = _path.StepsOf( person.PersonId ).Where( s => s.StepType == eventTypeKey ).ToList();
            if ( events.Count > 0 && random.Chance( source.AtEvent.Percent / 100 ) )
            {
                var at = random.Pick( events );
                return DayOf( at.Start, at.End.Value, random );
            }

            var sundays = _path.CheckIns( person.PersonId ).ToList();
            var easters = sundays.Where( d => d == SundayCalendar.Easter( d.Year ) ).ToList();
            return easters.Count > 0 && random.Chance( source.EasterPercent / 100 ) ? random.Pick( easters ) : random.Pick( sundays );
        }

        /// <summary>
        /// School years: a share of the students who could take the step at the first school year start in its
        /// grades when they had been attending, and keep it until graduation.
        /// </summary>
        private void PlanSchoolYears( StepTypeDefinition type )
        {
            var source = type.Source;
            var attending = StudentsOf( type ).Attending;
            var starts = new Dictionary<int, DateTime>();
            foreach ( var person in _path.People.Where( p => p.GraduationYear.HasValue ) )
            {
                var start = Enumerable.Range( _path.HistoryStart.Year, _path.Anchor.Year - _path.HistoryStart.Year + 1 )
                    .Select( year => source.StartsOn.In( year ) )
                    .Where( day => day >= _path.HistoryStart && day <= _path.Anchor && StudentPath.InGrades( person, day, source.Grades ) && _path.HasBeenAttending( person.PersonId, day, attending ) )
                    .Cast<DateTime?>()
                    .FirstOrDefault();
                if ( start.HasValue )
                {
                    starts[person.PersonId] = start.Value;
                }
            }

            var leaders = ChooseShare( type.Key, _path.People.Where( p => starts.ContainsKey( p.PersonId ) ), source.PercentOfStudents, p => Story( p, type ) != null );
            foreach ( var person in _path.People.Where( p => leaders.Contains( p.PersonId ) || Story( p, type ) != null ).OrderBy( p => p.Guid ) )
            {
                if ( !starts.TryGetValue( person.PersonId, out var start ) )
                {
                    throw new InvalidOperationException( $"The Cast story for {person.Name} needs a {type.Name}, but they were never in grades {source.Grades.Min} to {source.Grades.Max} and attending at a school year's start." );
                }

                var graduation = source.EndsOn.In( person.GraduationYear.Value );
                _path.Add( person, type.Key, StepStatus.Complete, start, graduation <= _path.Anchor ? graduation : null, start );
            }
        }

        private HashSet<int> ChooseShare( string key, IEnumerable<StepPerson> candidates, double percent, Func<StepPerson, bool> isForced )
        {
            var (chosen, count) = StepShares.Choose( $"{_seed}:steps:{key}", candidates, percent, isForced );
            _path.Shares[key] = (chosen.Count, count);
            return chosen;
        }

        private static CastStepChoice Story( StepPerson person, StepTypeDefinition type ) => person.StepChoices.TryGetValue( type.Key, out var story ) ? story : null;

        /// <summary>
        /// Every event held inside the history window that ended before the install day, oldest first.
        /// </summary>
        private List<EventOccurrence> Occurrences( StepSource source )
        {
            return Enumerable.Range( _path.HistoryStart.Year, _path.Anchor.Year - _path.HistoryStart.Year + 1 )
                .SelectMany( year => source.Seasons.Select( s => Occurrence( s, year ) ) )
                .Where( o => o.Start >= _path.HistoryStart && o.End < _path.Anchor )
                .OrderBy( o => o.Start )
                .ToList();
        }

        private static EventOccurrence Occurrence( EventSeason season, int year )
        {
            var start = season.In( year );
            return new EventOccurrence( season.Key, year, start, start.AddDays( season.Days - 1 ) );
        }

        /// <summary>
        /// The event a Cast story names: 0 is the latest one that ended before the install day, 1 the one a year
        /// before it, and so on. So "last summer" holds on any install day.
        /// </summary>
        private EventOccurrence StoryEvent( StepTypeDefinition type, CastStepEvent named )
        {
            var season = type.Source.Seasons.SingleOrDefault( s => s.Key == named.Season )
                ?? throw new InvalidOperationException( $"A Cast story names a {type.Name} in season \"{named.Season}\", which configuration/steps.json does not hold." );
            var latest = Occurrence( season, _path.Anchor.Year );
            var latestYear = latest.End < _path.Anchor ? latest.Year : latest.Year - 1;
            return Occurrence( season, latestYear - named.YearsAgo );
        }

        private StudentRule StudentsOf( StepTypeDefinition type ) => _programByType[type.Key].Students
            ?? throw new InvalidOperationException( $"configuration/steps.json: {type.Name} reads student check-ins, but its program has no students rule." );

        private SeededRandom PersonRandom( StepTypeDefinition type, StepPerson person ) => new SeededRandom( $"{_seed}:steps:{type.Key}:{person.Guid}" );

        private static DateTime DayOf( DateTime first, DateTime last, SeededRandom random ) => first.AddDays( random.Between( 0, ( last - first ).Days ) );
    }

    /// <summary>
    /// The planned Student Path: who took each camp, trip, decision, and Student Leader step, and the check-ins
    /// they came from. The steps part copies <see cref="Steps"/>, and the registrations part reads them.
    /// </summary>
    internal sealed class StudentPath
    {
        private static readonly SortedSet<DateTime> NoCheckIns = new SortedSet<DateTime>();

        private readonly Dictionary<int, SortedSet<DateTime>> _checkIns;
        private readonly Dictionary<int, List<PlannedStep>> _stepsByPerson = new Dictionary<int, List<PlannedStep>>();
        private readonly List<PlannedStep> _steps = new List<PlannedStep>();

        public StudentPath( IReadOnlyList<StepPerson> people, Dictionary<int, SortedSet<DateTime>> checkIns, DateTime historyStart, DateTime anchor, AttendingRule attending )
        {
            People = people;
            _checkIns = checkIns;
            HistoryStart = historyStart;
            Anchor = anchor;
            Attending = attending;
        }

        /// <summary>
        /// Everyone who can have steps, in <see cref="StepPerson.Of"/> order.
        /// </summary>
        public IReadOnlyList<StepPerson> People { get; }

        public DateTime HistoryStart { get; }

        public DateTime Anchor { get; }

        /// <summary>
        /// The student program's rule for who has been attending, or null when no single program has students.
        /// </summary>
        public AttendingRule Attending { get; }

        /// <summary>
        /// Every planned step, in the order the step types were planned.
        /// </summary>
        public IReadOnlyList<PlannedStep> Steps => _steps;

        /// <summary>
        /// For each step type, how many people took it of how many could, for the load summary.
        /// </summary>
        public Dictionary<string, (int Chosen, int Candidates)> Shares { get; } = new Dictionary<string, (int Chosen, int Candidates)>();

        /// <summary>
        /// The days the person checked in to the student service.
        /// </summary>
        public SortedSet<DateTime> CheckIns( int personId ) => _checkIns.TryGetValue( personId, out var days ) ? days : NoCheckIns;

        /// <summary>
        /// True when the attendance plan has student check-ins for the person.
        /// </summary>
        public bool HasCheckedIn( int personId ) => _checkIns.ContainsKey( personId );

        /// <summary>
        /// A person who checked in to the student service in the history window.
        /// </summary>
        public bool IsStudent( StepPerson person ) => person.GraduationYear.HasValue && CheckIns( person.PersonId ).Count > 0;

        /// <summary>
        /// True when the person checked in to the student service enough times in the days before a day.
        /// </summary>
        public bool HasBeenAttending( int personId, DateTime day, AttendingRule rule ) => CheckIns( personId ).GetViewBetween( day.AddDays( -rule.WithinDays ), day.AddDays( -1 ) ).Count >= rule.CheckIns;

        public IEnumerable<PlannedStep> StepsOf( int personId ) => _stepsByPerson.TryGetValue( personId, out var steps ) ? steps : Enumerable.Empty<PlannedStep>();

        public static bool InGrades( StepPerson person, DateTime day, IntRange grades )
        {
            var grade = AnchorDates.GradeOn( person.GraduationYear.Value, day );
            return grade >= grades.Min && grade <= grades.Max;
        }

        internal void Add( StepPerson person, string stepType, StepStatus status, DateTime start, DateTime? end, DateTime? completed )
        {
            var step = new PlannedStep( person.PersonId, person.AliasId, stepType, status, start, end, completed, person.CampusId, null );
            _steps.Add( step );
            if ( !_stepsByPerson.TryGetValue( person.PersonId, out var steps ) )
            {
                steps = new List<PlannedStep>();
                _stepsByPerson[person.PersonId] = steps;
            }

            steps.Add( step );
        }
    }

    /// <summary>
    /// Who takes a step chosen by share.
    /// </summary>
    internal static class StepShares
    {
        /// <summary>
        /// The given share of the candidates, rounded, counted separately for the Cast and for the generated
        /// households. Forced people (the Cast's stories) count toward their share. The rest are drawn by a
        /// seeded number per person from the stream. Returns the chosen people and how many could.
        /// </summary>
        public static (HashSet<int> Chosen, int Candidates) Choose( string stream, IEnumerable<StepPerson> candidates, double percent, Func<StepPerson, bool> isForced )
        {
            var chosen = new HashSet<int>();
            var count = 0;
            foreach ( var group in candidates.GroupBy( p => p.IsCast ) )
            {
                var people = group.ToList();
                var forced = people.Where( isForced ).ToList();
                var target = ( int ) Math.Round( people.Count * percent / 100, MidpointRounding.AwayFromZero );
                count += people.Count;
                chosen.UnionWith( forced.Select( p => p.PersonId ) );
                chosen.UnionWith( people.Except( forced )
                    .OrderBy( p => new SeededRandom( $"{stream}:share:{p.Guid}" ).Between( 0.0, 1.0 ) ).ThenBy( p => p.Guid )
                    .Take( Math.Max( 0, target - forced.Count ) )
                    .Select( p => p.PersonId ) );
            }

            return (chosen, count);
        }
    }
}
