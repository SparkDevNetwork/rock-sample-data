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

using static RockSampleData.Loader.Loading.RockGivingAutomation;

namespace RockSampleData.Loader.Generation
{
    /*
        10/21/26 - CLAUDE

        Every step comes from a record the data already has (decision 86). Each step type in
        configuration/steps.json names its source, and this planner has one method per source kind:
        memberships (Small Group, Serve, Lead), the giving journey (Give), the Baptism Date (Baptism),
        classes (Starting Point Class, Foundations), and, from student check-ins, events (Student Camp,
        Mission Trip), a decision (Follow Jesus), and school years (Student Leader). The planner is pure:
        it turns facts that earlier load steps wrote into step records, so the same facts, seed, and
        anchor give the same steps.

        Reason: Steps and the rest of the data always agree, and the rules live in one place.
    */

    /// <summary>
    /// Plans every step program: every step, every baptism, and the program completions Rock's Update Step
    /// Program Completions job would record.
    /// </summary>
    internal sealed class StepPlanner
    {
        /// <summary>
        /// Source kinds in the order they are planned. A decision is dated from the events, a student's baptism
        /// from the decision, and classes from the group, team, and class steps, so each comes after those.
        /// </summary>
        private static readonly string[] SourceOrder = { "membership", "giving-journey", "event", "decision", "school-years", "baptism", "class" };

        private static readonly SortedSet<DateTime> NoCheckIns = new SortedSet<DateTime>();

        private readonly StepsConfiguration _config;
        private readonly StepFacts _facts;
        private readonly string _seed;
        private readonly Dictionary<int, StepPerson> _people;
        private readonly Dictionary<string, StepProgramDefinition> _programByType;
        private readonly StepPlan _plan = new StepPlan();
        private readonly Dictionary<int, List<PlannedStep>> _stepsByPerson = new Dictionary<int, List<PlannedStep>>();

        private StepPlanner( StepsConfiguration config, StepFacts facts, string seed )
        {
            _config = config;
            _facts = facts;
            _seed = seed;
            _people = facts.People.ToDictionary( p => p.PersonId );
            _programByType = config.Programs.SelectMany( p => p.StepTypes.Select( t => (Program: p, t.Key) ) ).ToDictionary( x => x.Key, x => x.Program );
        }

        /// <summary>
        /// Plans every step type in the configuration from the facts.
        /// </summary>
        public static StepPlan Plan( StepsConfiguration config, StepFacts facts, string seed )
        {
            var duplicates = config.StepTypes.GroupBy( t => t.Key ).Where( g => g.Count() > 1 ).Select( g => g.Key ).ToList();
            var unknown = config.StepTypes.Select( t => t.Source.Kind ).Except( SourceOrder ).ToList();
            if ( duplicates.Count > 0 || unknown.Count > 0 )
            {
                throw new InvalidOperationException( $"configuration/steps.json has repeated step type keys ({string.Join( ", ", duplicates )}) or unknown step sources ({string.Join( ", ", unknown )})" );
            }

            var planner = new StepPlanner( config, facts, seed );
            foreach ( var kind in SourceOrder )
            {
                foreach ( var type in config.StepTypes.Where( t => t.Source.Kind == kind ) )
                {
                    planner.PlanType( type );
                }
            }

            planner.PlanCompletions();
            return planner._plan;
        }

        private void PlanType( StepTypeDefinition type )
        {
            switch ( type.Source.Kind )
            {
                case "membership":
                    PlanMemberships( type );
                    break;
                case "giving-journey":
                    PlanGiving( type );
                    break;
                case "event":
                    PlanEvents( type );
                    break;
                case "decision":
                    PlanDecision( type );
                    break;
                case "school-years":
                    PlanSchoolYears( type );
                    break;
                case "baptism":
                    PlanBaptisms( type );
                    break;
                case "class":
                    PlanClass( type );
                    break;
            }
        }

        #region Memberships

        /// <summary>
        /// One step per membership in the source's group types: Complete from the day the person joined, ending
        /// the day they left. A pending membership is In Progress when the source says so, and otherwise no step.
        /// </summary>
        private void PlanMemberships( StepTypeDefinition type )
        {
            var source = type.Source;
            foreach ( var membership in _facts.Memberships.Where( m => source.GroupTypes.Contains( m.GroupType ) && ( !source.LeadersOnly || m.IsLeader ) ) )
            {
                if ( !_people.TryGetValue( membership.PersonId, out var person ) )
                {
                    continue;
                }

                if ( membership.IsPending )
                {
                    if ( source.PendingIsInProgress )
                    {
                        Add( person, type.Key, StepStatus.InProgress, membership.Joined, null, null, membership.GroupId );
                    }

                    continue;
                }

                Add( person, type.Key, StepStatus.Complete, membership.Joined, membership.Left, membership.Joined, membership.GroupId );
            }
        }

        #endregion

        #region Giving journey

        /// <summary>
        /// Replays Rock's weekly journey stage runs (Tuesdays at 10pm) from the start of the history window to its
        /// last run. A step starts on the run that makes a giver the starting stage and ends on the run that makes
        /// them an ending stage. Each person in the giving unit gets the steps, as each carries the unit's stage.
        /// </summary>
        private void PlanGiving( StepTypeDefinition type )
        {
            var startsAt = ( JourneyStage ) Enum.Parse( typeof( JourneyStage ), type.Source.StartsAt, true );
            var endsAt = type.Source.EndsAt.Select( s => ( JourneyStage ) Enum.Parse( typeof( JourneyStage ), s, true ) ).ToHashSet();
            var runs = new List<DateTime>();
            for ( var run = _facts.FinalJourneyRun; run >= _facts.HistoryStart; run = run.AddDays( -7 ) )
            {
                runs.Insert( 0, run );
            }

            foreach ( var unit in _facts.People.Where( p => p.GivingId != null ).GroupBy( p => p.GivingId ) )
            {
                if ( !_facts.GiftDatesByGivingId.TryGetValue( unit.Key, out var dates ) )
                {
                    continue;
                }

                DateTime? open = null;
                var spans = new List<(DateTime Start, DateTime? End)>();
                foreach ( var run in runs )
                {
                    var stage = GetJourneyStage( dates, run );
                    if ( open == null && stage == startsAt )
                    {
                        open = run.Date;
                    }
                    else if ( open != null && endsAt.Contains( stage ) )
                    {
                        spans.Add( (open.Value, run.Date) );
                        open = null;
                    }
                }

                if ( open != null )
                {
                    spans.Add( (open.Value, null) );
                }

                foreach ( var person in unit )
                {
                    foreach ( var (start, end) in spans )
                    {
                        Add( person, type.Key, StepStatus.Complete, start, end, start, null );
                    }
                }
            }
        }

        #endregion

        #region Students

        /*
            10/22/26 - CLAUDE

            The Student Path reads the student check-ins the attendance step wrote. A student's grade on a day
            comes from their graduation year, moving up each June 1 as check-in does, and they have been attending
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
            var could = _facts.People.Where( p => p.GraduationYear.HasValue )
                .ToDictionary( p => p.PersonId, p => occurrences.Where( o => InGrades( p, o.Start, source.Grades ) && HasBeenAttending( p, o.Start, attending ) ).ToList() );
            var goers = ChooseShare( type.Key, _facts.People.Where( p => could.TryGetValue( p.PersonId, out var list ) && list.Count > 0 ), source.PercentOfStudents, p => Story( p, type ) != null );

            foreach ( var person in _facts.People.Where( p => goers.Contains( p.PersonId ) || Story( p, type ) != null ).OrderBy( p => p.Guid ) )
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
                    Add( person, type.Key, StepStatus.Complete, occurrence.Start, occurrence.End, occurrence.Start, null );
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
            var deciders = ChooseShare( type.Key, _facts.People.Where( IsStudent ), source.PercentOfStudents, p => Story( p, type ) != null );

            foreach ( var person in _facts.People.Where( p => deciders.Contains( p.PersonId ) || Story( p, type ) != null ).OrderBy( p => p.Guid ) )
            {
                var story = Story( person, type );
                var random = PersonRandom( type, person );
                DateTime date;
                if ( story?.Events != null )
                {
                    var occurrence = story.Events.Count == 1 ? StoryEvent( eventType, story.Events[0] ) : throw new InvalidOperationException( $"The Cast story for {person.Name} names {story.Events.Count} events for their {type.Name}; a decision is made at one." );
                    if ( !StepsOf( person.PersonId ).Any( s => s.StepType == eventType.Key && s.Start == occurrence.Start ) )
                    {
                        throw new InvalidOperationException( $"The Cast story for {person.Name} makes their {type.Name} at {eventType.Name} in the {occurrence.Season} of {occurrence.Year}, which they did not go to." );
                    }

                    date = DayOf( occurrence.Start, occurrence.End, random );
                }
                else
                {
                    date = IsStudent( person ) ? DecisionDate( source, eventType.Key, person, random )
                        : throw new InvalidOperationException( $"The Cast story for {person.Name} needs a {type.Name}, but they never checked in to the student service." );
                }

                Add( person, type.Key, StepStatus.Complete, date, null, date, null );
            }
        }

        private DateTime DecisionDate( StepSource source, string eventTypeKey, StepPerson person, SeededRandom random )
        {
            var events = StepsOf( person.PersonId ).Where( s => s.StepType == eventTypeKey ).ToList();
            if ( events.Count > 0 && random.Chance( source.AtEvent.Percent / 100 ) )
            {
                var at = random.Pick( events );
                return DayOf( at.Start, at.End.Value, random );
            }

            var sundays = CheckIns( person ).ToList();
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
            foreach ( var person in _facts.People.Where( p => p.GraduationYear.HasValue ) )
            {
                var start = Enumerable.Range( _facts.HistoryStart.Year, _facts.Anchor.Year - _facts.HistoryStart.Year + 1 )
                    .Select( year => source.StartsOn.In( year ) )
                    .Where( day => day >= _facts.HistoryStart && day <= _facts.Anchor && InGrades( person, day, source.Grades ) && HasBeenAttending( person, day, attending ) )
                    .Cast<DateTime?>()
                    .FirstOrDefault();
                if ( start.HasValue )
                {
                    starts[person.PersonId] = start.Value;
                }
            }

            var leaders = ChooseShare( type.Key, _facts.People.Where( p => starts.ContainsKey( p.PersonId ) ), source.PercentOfStudents, p => Story( p, type ) != null );
            foreach ( var person in _facts.People.Where( p => leaders.Contains( p.PersonId ) || Story( p, type ) != null ).OrderBy( p => p.Guid ) )
            {
                if ( !starts.TryGetValue( person.PersonId, out var start ) )
                {
                    throw new InvalidOperationException( $"The Cast story for {person.Name} needs a {type.Name}, but they were never in grades {source.Grades.Min} to {source.Grades.Max} and attending at a school year's start." );
                }

                var graduation = source.EndsOn.In( person.GraduationYear.Value );
                Add( person, type.Key, StepStatus.Complete, start, graduation <= _facts.Anchor ? graduation : null, start, null );
            }
        }

        /// <summary>
        /// Who takes a step: the given share of the candidates, rounded, counted separately for the Cast and for the
        /// generated households. Forced people (the Cast's stories) count toward their share. The rest are drawn
        /// by a seeded number per person.
        /// </summary>
        private HashSet<int> ChooseShare( string key, IEnumerable<StepPerson> candidates, double percent, Func<StepPerson, bool> isForced )
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
                    .OrderBy( p => new SeededRandom( $"{_seed}:steps:{key}:share:{p.Guid}" ).Between( 0.0, 1.0 ) ).ThenBy( p => p.Guid )
                    .Take( Math.Max( 0, target - forced.Count ) )
                    .Select( p => p.PersonId ) );
            }

            _plan.Shares[key] = (chosen.Count, count);
            return chosen;
        }

        private static CastStepChoice Story( StepPerson person, StepTypeDefinition type ) => person.StepChoices.TryGetValue( type.Key, out var story ) ? story : null;

        /// <summary>
        /// Every event held inside the history window that ended before the install day, oldest first.
        /// </summary>
        private List<EventOccurrence> Occurrences( StepSource source )
        {
            return Enumerable.Range( _facts.HistoryStart.Year, _facts.Anchor.Year - _facts.HistoryStart.Year + 1 )
                .SelectMany( year => source.Seasons.Select( s => Occurrence( s, year ) ) )
                .Where( o => o.Start >= _facts.HistoryStart && o.End < _facts.Anchor )
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
            var latest = Occurrence( season, _facts.Anchor.Year );
            var latestYear = latest.End < _facts.Anchor ? latest.Year : latest.Year - 1;
            return Occurrence( season, latestYear - named.YearsAgo );
        }

        private StudentRule StudentsOf( StepTypeDefinition type ) => _programByType[type.Key].Students
            ?? throw new InvalidOperationException( $"configuration/steps.json: {type.Name} reads student check-ins, but its program has no students rule." );

        /// <summary>
        /// The days the person checked in to the student service.
        /// </summary>
        private SortedSet<DateTime> CheckIns( StepPerson person ) => _facts.StudentCheckIns.TryGetValue( person.PersonId, out var days ) ? days : NoCheckIns;

        /// <summary>
        /// A person who checked in to the student service in the history window.
        /// </summary>
        private bool IsStudent( StepPerson person ) => person.GraduationYear.HasValue && CheckIns( person ).Count > 0;

        private static bool InGrades( StepPerson person, DateTime day, IntRange grades )
        {
            var grade = AnchorDates.GradeOn( person.GraduationYear.Value, day );
            return grade >= grades.Min && grade <= grades.Max;
        }

        private bool HasBeenAttending( StepPerson person, DateTime day, AttendingRule rule ) => CheckIns( person ).GetViewBetween( day.AddDays( -rule.WithinDays ), day.AddDays( -1 ) ).Count >= rule.CheckIns;

        private static DateTime DayOf( DateTime first, DateTime last, SeededRandom random ) => first.AddDays( random.Between( 0, ( last - first ).Days ) );

        #endregion

        #region Baptism

        /*
            10/21/26 - CLAUDE

            A Cast baptism date in a household file is kept unchanged. Every other adult, Cast or generated, is
            baptized at the configured rates: volunteers and leaders more often, other active adults making up
            the rest of the active-adult share, and adults on the church's edge less often. The Cast and the
            generated households each reach the share on their own, so the Cast's baptisms are the same in every
            dataset. Baptized Here is Yes only when the baptism came after the household's first visit. A
            household whose first visit is before the history window has no known first visit, so a baptism
            elsewhere is dated before the window.

            Reason: The Baptism step, the Baptism Date attribute, and Baptized Here must agree with each other
            and with when the family started coming. Only a few Cast files set a date, and Core 1 is all Cast.
        */

        private void PlanBaptisms( StepTypeDefinition type )
        {
            var source = type.Source;
            var serving = PeopleWithSteps( "serve" );
            var leading = PeopleWithSteps( "lead" );
            bool IsVolunteerOrLeader( StepPerson p ) => serving.Contains( p.PersonId ) || leading.Contains( p.PersonId );

            var restPercent = new[] { true, false }.ToDictionary( isCast => isCast, isCast => RestPercent( source, _facts.People.Where( p => p.IsCast == isCast && p.IsActiveAdult ).ToList(), IsVolunteerOrLeader ) );
            var studentDates = StudentBaptismDates( source );

            foreach ( var person in _facts.People.OrderBy( p => p.Guid ) )
            {
                DateTime? date;
                bool isHere;
                if ( person.CastBaptismDate.HasValue )
                {
                    date = person.CastBaptismDate;
                    isHere = person.FirstVisit.HasValue && date.Value.Date > person.FirstVisit.Value.Date;
                }
                else if ( !person.IsAdult )
                {
                    date = studentDates.TryGetValue( person.PersonId, out var studentDate ) ? studentDate : null;
                    isHere = true;
                }
                else
                {
                    var random = new SeededRandom( $"{_seed}:steps:baptism:{person.Guid}" );
                    var percent = !person.IsActiveAdult ? source.PercentOfOtherAdults : IsVolunteerOrLeader( person ) ? source.VolunteersAndLeadersPercent : restPercent[person.IsCast];
                    if ( !random.Chance( percent / 100 ) )
                    {
                        continue;
                    }

                    // When no Sunday fits the first choice (a newcomer has had no baptism Sunday yet), the other one is used.
                    isHere = random.Chance( source.BaptizedHerePercent / 100 );
                    date = BaptismDate( source, person, random, isHere );
                    if ( !date.HasValue )
                    {
                        isHere = !isHere;
                        date = BaptismDate( source, person, random, isHere );
                    }
                }

                if ( !date.HasValue )
                {
                    continue;
                }

                _plan.Baptisms.Add( new PlannedBaptism( person.PersonId, date.Value.Date, isHere ) );
                Add( person, type.Key, StepStatus.Complete, date.Value.Date, null, date.Value.Date, null );
            }
        }

        /*
            10/22/26 - CLAUDE

            A student is baptized here on a baptism Sunday (decision 87). Most follow a decision to follow Jesus:
            the first baptism Sunday at least two weeks after it. A Sunday that has not come yet is no baptism, so
            a recent decision waits, as it does in a real church. A few students with no decision on record are
            baptized on a baptism Sunday after they started coming to the student service. Each is a share of the
            students who have such a Sunday, counted for the Cast and the generated households on their own, like
            the Student Path's steps. Children who never checked in to the student service are not baptized here.

            Reason: The Baptism step, its attribute, and the Student Path tell one story, and the adult rates stay
            as they were (decision 117).
        */
        private Dictionary<int, DateTime> StudentBaptismDates( StepSource source )
        {
            var rule = source.Students;
            if ( rule == null )
            {
                return new Dictionary<int, DateTime>();
            }

            var lastSunday = _facts.Anchor.AddDays( -1 );
            var sundays = new Dictionary<int, List<DateTime>>();
            var decided = new HashSet<int>();
            foreach ( var person in _facts.People.Where( p => !p.IsAdult && !p.CastBaptismDate.HasValue && IsStudent( p ) ) )
            {
                var oldEnough = person.BirthDate?.AddYears( source.HereMinimumAge ) ?? DateTime.MinValue;
                var decision = CompletedOn( person.PersonId, rule.AfterStepType );
                var from = decision?.AddDays( rule.MinimumDaysAfter ) ?? CheckIns( person ).Min;
                var options = BaptismSundays( source, Max( from, oldEnough ), lastSunday );
                if ( options.Count > 0 )
                {
                    sundays[person.PersonId] = options;
                    if ( decision.HasValue )
                    {
                        decided.Add( person.PersonId );
                    }
                }
            }

            var candidates = _facts.People.Where( p => sundays.ContainsKey( p.PersonId ) ).ToList();
            var chosen = ChooseShare( $"baptism-after-{rule.AfterStepType}", candidates.Where( p => decided.Contains( p.PersonId ) ), rule.AfterPercent, _ => false );
            chosen.UnionWith( ChooseShare( "baptism-other-students", candidates.Where( p => !decided.Contains( p.PersonId ) ), rule.PercentOfOtherStudents, _ => false ) );

            return candidates.Where( p => chosen.Contains( p.PersonId ) ).ToDictionary( p => p.PersonId, p => decided.Contains( p.PersonId )
                ? sundays[p.PersonId].Min()
                : new SeededRandom( $"{_seed}:steps:baptism:{p.Guid}" ).Pick( sundays[p.PersonId] ) );
        }

        /// <summary>
        /// The baptism chance for active adults who are neither volunteers nor leaders, so the active adults reach
        /// the configured share. Dates already set in Cast files count toward it.
        /// </summary>
        private static double RestPercent( StepSource source, List<StepPerson> activeAdults, Func<StepPerson, bool> isVolunteerOrLeader )
        {
            var undecided = activeAdults.Where( p => !p.CastBaptismDate.HasValue ).ToList();
            var boosted = undecided.Count( isVolunteerOrLeader );
            if ( undecided.Count == boosted )
            {
                return source.PercentOfActiveAdults;
            }

            var needed = ( source.PercentOfActiveAdults * activeAdults.Count ) - ( 100.0 * ( activeAdults.Count - undecided.Count ) ) - ( source.VolunteersAndLeadersPercent * boosted );
            return Math.Min( Math.Max( needed / ( undecided.Count - boosted ), 0 ), 100 );
        }

        /// <summary>
        /// A Sunday for a baptism here (after the first visit, usually a baptism Sunday) or elsewhere (before the
        /// first visit, or before the history window when it is unknown). Null when no Sunday fits.
        /// </summary>
        private DateTime? BaptismDate( StepSource source, StepPerson person, SeededRandom random, bool isHere )
        {
            var anchor = _facts.Anchor;
            var birth = person.BirthDate ?? anchor.AddYears( -40 );
            DateTime from, until;
            if ( isHere )
            {
                from = Max( birth.AddYears( source.HereMinimumAge ), anchor.AddYears( -source.YearsBack ), person.FirstVisit?.Date.AddDays( 1 ) ?? DateTime.MinValue );
                until = anchor.AddDays( -1 );
            }
            else
            {
                from = Max( birth.AddYears( source.MinimumAge ), anchor.AddYears( -60 ) );
                until = ( person.FirstVisit?.Date ?? _facts.HistoryStart ).AddDays( -1 );
            }

            var sundays = Sundays( from, until );
            if ( sundays.Count == 0 )
            {
                return null;
            }

            if ( isHere && random.Chance( source.BaptismSundayPercent / 100 ) )
            {
                var baptismSundays = BaptismSundays( source, from, until );
                if ( baptismSundays.Count > 0 )
                {
                    return random.Pick( baptismSundays );
                }
            }

            return random.Pick( sundays );
        }

        /// <summary>
        /// Easter and the summer baptism Sunday of each year, inside the range.
        /// </summary>
        private static List<DateTime> BaptismSundays( StepSource source, DateTime from, DateTime until )
        {
            var sundays = new List<DateTime>();
            for ( var year = from.Year; year <= until.Year; year++ )
            {
                sundays.AddRange( new[] { SundayCalendar.Easter( year ), source.SummerSunday.In( year ) }.Where( d => d >= from && d <= until ) );
            }

            return sundays;
        }

        private static List<DateTime> Sundays( DateTime from, DateTime until )
        {
            var sundays = new List<DateTime>();
            for ( var day = from.Date.AddDays( ( 7 - ( int ) from.DayOfWeek ) % 7 ); day <= until; day = day.AddDays( 7 ) )
            {
                sundays.Add( day );
            }

            return sundays;
        }

        #endregion

        #region Classes

        /*
            10/21/26 - CLAUDE

            Classes run on a fixed rhythm that counts back from the install day: the current class started a set
            number of weeks ago and is still running, and earlier classes started every few weeks before it. So
            every campus has a class In Progress on any install day, and a person's class always falls on a real
            class date. A class is complete on its last session.

            Who takes a class, in order: the Cast people whose stories name a class; everyone who needs it before
            another step (every volunteer takes Starting Point before serving); a few people per campus in the
            current class; then active adults, those in a group first, until the campus reaches its share.

            Reason: Serve's prerequisite always holds, the Cast's stories hold, and the rates are designed targets
            (decision 89).
        */

        private void PlanClass( StepTypeDefinition type )
        {
            var source = type.Source;
            var schedule = new ClassSchedule( source, _facts.Anchor, type.Key );
            var holders = new HashSet<int>();

            void Take( StepPerson person, int classesAgo )
            {
                holders.Add( person.PersonId );
                if ( classesAgo == 0 )
                {
                    Add( person, type.Key, StepStatus.InProgress, schedule.Start( 0 ), null, null, null );
                }
                else
                {
                    var completed = schedule.Completion( classesAgo );
                    Add( person, type.Key, StepStatus.Complete, schedule.Start( classesAgo ), completed, completed, null );
                }
            }

            // The Cast's stories come first, exactly as written.
            var forced = new List<StepPerson>();
            foreach ( var person in _facts.People.Where( p => p.StepChoices.ContainsKey( type.Key ) ).OrderBy( p => p.Guid ) )
            {
                var choice = person.StepChoices[type.Key];
                if ( choice.ClassesAgo is int classesAgo )
                {
                    CheckStory( person, type, schedule, classesAgo );
                    Take( person, classesAgo );
                }
                else if ( choice.Taken )
                {
                    forced.Add( person );
                }
            }

            foreach ( var person in forced )
            {
                var classesAgo = PickClass( person, source, schedule, PersonRandom( type, person ) )
                    ?? throw new InvalidOperationException( $"The Cast story for {person.Name} needs a {type.Name}, but no class fits their other records." );
                Take( person, classesAgo );
            }

            // Everyone who needs this class before another step has completed it before their first such step.
            if ( source.RequiredBefore != null )
            {
                foreach ( var (personId, deadline) in FirstStarts( new[] { source.RequiredBefore } ).OrderBy( p => _people[p.Key].Guid ) )
                {
                    var person = _people[personId];
                    if ( !holders.Contains( personId ) )
                    {
                        Take( person, PickClass( person, source, schedule, PersonRandom( type, person ) ) ?? schedule.LastCompletedBefore( deadline ) );
                    }
                }
            }

            foreach ( var campus in _facts.People.Where( p => p.IsActiveAdult ).GroupBy( p => p.CampusKey ).OrderBy( g => g.Key ) )
            {
                var random = new SeededRandom( $"{_seed}:steps:{type.Key}:{campus.Key}" );
                var candidates = campus.Where( p => !holders.Contains( p.PersonId ) && IsEligible( p, source, schedule ) ).OrderBy( p => p.Guid ).ToList();

                // A few people at every campus are in the class running now.
                var inProgress = Math.Max( 0, random.Between( source.InProgressPerCampus.Min, source.InProgressPerCampus.Max )
                    - _facts.People.Count( p => p.CampusKey == campus.Key && p.StepChoices.TryGetValue( type.Key, out var c ) && c.ClassesAgo == 0 ) );
                foreach ( var person in candidates
                    .Where( p => source.RequiredBefore == null || !HasStep( p.PersonId, source.RequiredBefore ) )
                    .Where( p => p.BirthDate == null || p.BirthDate.Value.AddYears( source.MinimumAge ) <= schedule.Start( 0 ) )
                    .Select( p => (Person: p, Order: InProgressOrder( p, source ), Tie: random.Between( 0.0, 1.0 )) )
                    .OrderBy( c => c.Order ).ThenBy( c => c.Tie )
                    .Take( inProgress )
                    .Select( c => c.Person )
                    .ToList() )
                {
                    Take( person, 0 );
                }

                // Then active adults until the campus reaches its share: those with a preferred step first, then those near a group or team.
                var target = ( int ) Math.Round( campus.Count() * source.PercentOfActiveAdults / 100 );
                var count = campus.Count( p => holders.Contains( p.PersonId ) );
                foreach ( var person in candidates
                    .Where( p => !holders.Contains( p.PersonId ) )
                    .Select( p => (Person: p, Tier: FillTier( p, source ), Tie: random.Between( 0.0, 1.0 )) )
                    .OrderBy( c => c.Tier ).ThenBy( c => c.Tie )
                    .Select( c => c.Person ) )
                {
                    if ( count >= target )
                    {
                        break;
                    }

                    if ( PickClass( person, source, schedule, random ) is int classesAgo )
                    {
                        Take( person, classesAgo );
                        count++;
                    }
                }
            }
        }

        /// <summary>
        /// Fails the load when a Cast story's class cannot hold against the person's other records: the class
        /// must come after the step it follows and be complete before the step that needs it.
        /// </summary>
        private void CheckStory( StepPerson person, StepTypeDefinition type, ClassSchedule schedule, int classesAgo )
        {
            var source = type.Source;
            var start = schedule.Start( classesAgo );
            if ( source.RequiredBefore != null && FirstStart( person.PersonId, new[] { source.RequiredBefore } ) is DateTime needed
                && ( classesAgo == 0 || schedule.Completion( classesAgo ) >= needed ) )
            {
                throw new InvalidOperationException( $"The Cast story for {person.Name} puts their {type.Name} after their first {source.RequiredBefore} step on {needed:yyyy-MM-dd}, which needs it first." );
            }

            if ( source.After != null && !( CompletedOn( person.PersonId, source.After ) is DateTime after && after < start ) )
            {
                throw new InvalidOperationException( $"The Cast story for {person.Name} puts their {type.Name} on {start:yyyy-MM-dd}, before a completed {source.After} step." );
            }
        }

        /// <summary>
        /// Whether a person can take the class at all: the step it follows is complete.
        /// </summary>
        private bool IsEligible( StepPerson person, StepSource source, ClassSchedule schedule )
        {
            return source.After == null || CompletedOn( person.PersonId, source.After ) is DateTime after && after < schedule.Start( 0 );
        }

        /// <summary>
        /// Who goes in the current class first: people waiting to serve when the class comes before serving, then
        /// people who just joined a group or just finished the class before, then everyone else.
        /// </summary>
        private int InProgressOrder( StepPerson person, StepSource source )
        {
            if ( source.RequiredBefore != null && _facts.OpenServeRequests.Contains( person.PersonId ) )
            {
                return 0;
            }

            var recent = NearStart( person, source ) ?? ( source.After == null ? person.FirstVisit : CompletedOn( person.PersonId, source.After ) );
            return recent.HasValue && recent.Value >= _facts.Anchor.AddDays( -365 ) ? 1 : 2;
        }

        /// <summary>
        /// Picks a completed class for a person (counted back from the current one), or null when none fits: near
        /// their first group when they have one, otherwise soon after the step it follows or their first visit.
        /// </summary>
        private int? PickClass( StepPerson person, StepSource source, ClassSchedule schedule, SeededRandom random )
        {
            var after = source.After == null ? ( DateTime? ) null : CompletedOn( person.PersonId, source.After );
            var deadline = source.RequiredBefore == null ? ( DateTime? ) null : FirstStart( person.PersonId, new[] { source.RequiredBefore } );
            var oldest = person.BirthDate?.AddYears( source.MinimumAge ) ?? DateTime.MinValue;

            bool Fits( int k ) =>
                schedule.Start( k ) >= oldest
                && ( after == null || schedule.Start( k ) > after.Value )
                && ( deadline == null || schedule.Completion( k ) < deadline.Value );

            var near = NearStart( person, source );
            if ( near.HasValue )
            {
                var options = source.Near.DaysBefore > 0
                    ? schedule.CompletedBetween( near.Value.AddDays( -source.Near.DaysBefore ), near.Value.AddDays( -1 ) )
                    : schedule.StartedBetween( near.Value, near.Value.AddDays( source.Near.DaysAfter ) );
                var fitting = options.Where( Fits ).ToList();
                if ( fitting.Count > 0 )
                {
                    return random.Pick( fitting );
                }
            }

            var from = after ?? person.FirstVisit;
            var others = from.HasValue
                ? schedule.StartedBetween( from.Value, from.Value.AddDays( source.Otherwise.DaysAfter ) )
                : schedule.StartedBetween( _facts.HistoryStart, _facts.Anchor );
            var fits = others.Where( Fits ).ToList();
            return fits.Count > 0 ? random.Pick( fits ) : null;
        }

        /// <summary>
        /// Who fills a class's share first: people with a step the class prefers, then people near a group, then everyone else.
        /// </summary>
        private int FillTier( StepPerson person, StepSource source )
        {
            if ( source.Prefer.Any( t => HasStep( person.PersonId, t ) ) )
            {
                return 0;
            }

            return NearStart( person, source ) == null ? 2 : 1;
        }

        private SeededRandom PersonRandom( StepTypeDefinition type, StepPerson person ) => new SeededRandom( $"{_seed}:steps:{type.Key}:{person.Guid}" );

        /// <summary>
        /// The start of the person's first step of the class's near step types, or null.
        /// </summary>
        private DateTime? NearStart( StepPerson person, StepSource source ) => source.Near == null ? null : FirstStart( person.PersonId, source.Near.StepTypes );

        #endregion

        #region Program completions

        /*
            10/21/26 - CLAUDE

            A port of Rock's Update Step Program Completions job (Rock/Jobs/UpdateStepProgramCompletions.cs,
            AddMissingCompletions), run for each program. A person completes a program when their Complete steps
            cover every active step type in it. The completion starts at their earliest Complete step's start, ends at their latest Complete
            step's completion (or end), and takes the largest campus Id among those steps. Then every step of the
            person in the program, In Progress ones too, points at it. validation/invariants.sql reruns the job's
            rule against the loaded data.

            Reason: Rock's job finds nothing to add after install, and the program's completion counts are Rock's.
        */

        private void PlanCompletions()
        {
            foreach ( var program in _config.Programs )
            {
                var types = program.StepTypes.Select( t => t.Key ).ToHashSet();
                foreach ( var person in _plan.Steps.Where( s => types.Contains( s.StepType ) ).GroupBy( s => s.AliasId ) )
                {
                    var complete = person.Where( s => s.Status == StepStatus.Complete ).ToList();
                    if ( complete.Select( s => s.StepType ).Distinct().Count() != types.Count )
                    {
                        continue;
                    }

                    _plan.Completions.Add( new PlannedCompletion( program.Key, person.Key, complete.Max( s => s.CampusId ), complete.Min( s => s.Start ), complete.Max( s => s.Completed ?? s.End ) ) );
                }
            }
        }

        #endregion

        #region Helpers

        private void Add( StepPerson person, string stepType, StepStatus status, DateTime start, DateTime? end, DateTime? completed, int? relatedGroupId )
        {
            var step = new PlannedStep( person.PersonId, person.AliasId, stepType, status, start, end, completed, person.CampusId, relatedGroupId );
            _plan.Steps.Add( step );
            if ( !_stepsByPerson.TryGetValue( person.PersonId, out var steps ) )
            {
                steps = new List<PlannedStep>();
                _stepsByPerson[person.PersonId] = steps;
            }

            steps.Add( step );
        }

        private IEnumerable<PlannedStep> StepsOf( int personId ) => _stepsByPerson.TryGetValue( personId, out var steps ) ? steps : Enumerable.Empty<PlannedStep>();

        private HashSet<int> PeopleWithSteps( string stepType ) => _plan.Steps.Where( s => s.StepType == stepType ).Select( s => s.PersonId ).ToHashSet();

        private bool HasStep( int personId, string stepType ) => StepsOf( personId ).Any( s => s.StepType == stepType );

        /// <summary>
        /// The start of each person's first step of the given types, by person.
        /// </summary>
        private Dictionary<int, DateTime> FirstStarts( IEnumerable<string> stepTypes )
        {
            var types = stepTypes.ToHashSet();
            return _plan.Steps.Where( s => types.Contains( s.StepType ) ).GroupBy( s => s.PersonId ).ToDictionary( g => g.Key, g => g.Min( s => s.Start ) );
        }

        private DateTime? FirstStart( int personId, IEnumerable<string> stepTypes )
        {
            var starts = StepsOf( personId ).Where( s => stepTypes.Contains( s.StepType ) ).Select( s => s.Start ).ToList();
            return starts.Count == 0 ? null : starts.Min();
        }

        /// <summary>
        /// When the person's first Complete step of the type was completed, or null.
        /// </summary>
        private DateTime? CompletedOn( int personId, string stepType )
        {
            var dates = StepsOf( personId ).Where( s => s.StepType == stepType && s.Status == StepStatus.Complete ).Select( s => s.Completed.Value ).ToList();
            return dates.Count == 0 ? null : dates.Min();
        }

        private static DateTime Max( params DateTime[] dates ) => dates.Max();

        #endregion
    }

    /// <summary>
    /// A class's dates, counted back from the install day: the current class is number 0, the one before it 1, and so on.
    /// </summary>
    internal sealed class ClassSchedule
    {
        private readonly DateTime _currentStart;
        private readonly int _everyDays;
        private readonly int _lengthDays;

        public ClassSchedule( StepSource source, DateTime anchor, string key )
        {
            var meetsOn = ( DayOfWeek ) Enum.Parse( typeof( DayOfWeek ), source.MeetsOn, true );
            var day = anchor.Date.AddDays( -7 * source.CurrentStartedWeeksAgo );
            _currentStart = day.AddDays( -( ( ( int ) day.DayOfWeek - ( int ) meetsOn + 7 ) % 7 ) );
            _everyDays = 7 * source.EveryWeeks;
            _lengthDays = 7 * ( source.Sessions - 1 );

            if ( Completion( 0 ) <= anchor.Date || Completion( 1 ) >= anchor.Date )
            {
                throw new InvalidOperationException( $"configuration/steps.json: the {key} schedule must have exactly one class running on the install day." );
            }
        }

        public DateTime Start( int classesAgo ) => _currentStart.AddDays( -_everyDays * classesAgo );

        public DateTime Completion( int classesAgo ) => Start( classesAgo ).AddDays( _lengthDays );

        /// <summary>
        /// The completed classes (1 and up) that started in the range.
        /// </summary>
        public List<int> StartedBetween( DateTime from, DateTime until )
        {
            var newest = Math.Max( 1, ( int ) Math.Ceiling( ( _currentStart - until.Date ).TotalDays / _everyDays ) );
            var oldest = ( int ) Math.Floor( ( _currentStart - from.Date ).TotalDays / _everyDays );
            return newest > oldest ? new List<int>() : Enumerable.Range( newest, oldest - newest + 1 ).ToList();
        }

        /// <summary>
        /// The completed classes (1 and up) that were completed in the range.
        /// </summary>
        public List<int> CompletedBetween( DateTime from, DateTime until ) => StartedBetween( from.AddDays( -_lengthDays ), until.AddDays( -_lengthDays ) );

        /// <summary>
        /// The newest completed class finished before the date.
        /// </summary>
        public int LastCompletedBefore( DateTime date )
        {
            var classesAgo = 1;
            while ( Completion( classesAgo ) >= date )
            {
                classesAgo++;
            }

            return classesAgo;
        }
    }

    /// <summary>
    /// What the earlier load steps wrote, as the step planner needs it.
    /// </summary>
    internal sealed class StepFacts
    {
        public DateTime Anchor { get; set; }

        public DateTime HistoryStart { get; set; }

        /// <summary>
        /// Rock's last weekly journey stage run, whose stages Rock shows today.
        /// </summary>
        public DateTime FinalJourneyRun { get; set; }

        public List<StepPerson> People { get; set; } = new List<StepPerson>();

        public List<MembershipFact> Memberships { get; set; } = new List<MembershipFact>();

        /// <summary>
        /// Each giving unit's qualifying gift dates, in order.
        /// </summary>
        public Dictionary<string, List<DateTime>> GiftDatesByGivingId { get; set; } = new Dictionary<string, List<DateTime>>();

        /// <summary>
        /// People with an open request to serve.
        /// </summary>
        public HashSet<int> OpenServeRequests { get; set; } = new HashSet<int>();

        /// <summary>
        /// The days each person checked in to the student service, by person Id.
        /// </summary>
        public Dictionary<int, SortedSet<DateTime>> StudentCheckIns { get; set; } = new Dictionary<int, SortedSet<DateTime>>();
    }

    /// <summary>
    /// One person who can have steps (businesses cannot).
    /// </summary>
    internal sealed class StepPerson
    {
        public int PersonId { get; set; }

        public int AliasId { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        public int CampusId { get; set; }

        public string CampusKey { get; set; }

        public string GivingId { get; set; }

        public DateTime? BirthDate { get; set; }

        public bool IsAdult { get; set; }

        /// <summary>
        /// An adult on an active record who attended, gave, or was in a small group in the last year.
        /// </summary>
        public bool IsActiveAdult { get; set; }

        public bool IsCast { get; set; }

        /// <summary>
        /// The household's first visit, or null when it came before the history window.
        /// </summary>
        public DateTime? FirstVisit { get; set; }

        /// <summary>
        /// A Cast person's Baptism Date from their household file.
        /// </summary>
        public DateTime? CastBaptismDate { get; set; }

        /// <summary>
        /// A graduation year, which sets the school grade on any day. Null for young children and for adults whose file sets none.
        /// </summary>
        public int? GraduationYear { get; set; }

        /// <summary>
        /// Steps a Cast story needs, by step type key.
        /// </summary>
        public Dictionary<string, CastStepChoice> StepChoices { get; set; } = new Dictionary<string, CastStepChoice>();
    }

    /// <summary>
    /// One small group or serving team membership. <see cref="Left"/> is the day the person left, or the day the
    /// group ended while they were still in it.
    /// </summary>
    internal sealed record MembershipFact( int PersonId, int GroupId, string GroupType, bool IsLeader, bool IsPending, DateTime Joined, DateTime? Left );

    internal enum StepStatus
    {
        InProgress,
        Complete
    }

    internal sealed record PlannedStep( int PersonId, int AliasId, string StepType, StepStatus Status, DateTime Start, DateTime? End, DateTime? Completed, int CampusId, int? RelatedGroupId );

    internal sealed record PlannedBaptism( int PersonId, DateTime Date, bool IsHere );

    internal sealed record PlannedCompletion( string ProgramKey, int AliasId, int CampusId, DateTime Start, DateTime? End );

    internal sealed class StepPlan
    {
        public List<PlannedStep> Steps { get; } = new List<PlannedStep>();

        public List<PlannedBaptism> Baptisms { get; } = new List<PlannedBaptism>();

        public List<PlannedCompletion> Completions { get; } = new List<PlannedCompletion>();

        /// <summary>
        /// For each step chosen by share, how many people took it of how many could, for the load summary.
        /// </summary>
        public Dictionary<string, (int Chosen, int Candidates)> Shares { get; } = new Dictionary<string, (int Chosen, int Candidates)>();
    }
}
