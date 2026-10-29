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
        10/28/26 - CLAUDE

        Students register from the Student Path plan, so registrations agree with grades, check-ins, and steps.
        An instance whose event the Student Path planned (last summer's camp) has exactly the students with that
        step at that event. Any other instance takes a share of the students who could go, with the step's own
        rate, or a share of the students who attend.

        Reason: Registrations follow the Student Path's rules on any install day (decisions D-registrations-8 and D-registrations-9).
    */

    /// <summary>
    /// Chooses who registers for an instance, by the audience its registrants rule names, and groups them into
    /// registrations. Only the choosing differs between audiences; <see cref="RegistrantPlanner"/> dates, prices,
    /// pays, places, and answers every registration the same way. Reads no database.
    /// </summary>
    internal sealed class RegistrantChooser
    {
        private readonly StepsConfiguration _steps;
        private readonly StudentPath _path;
        private readonly DateTime _anchor;
        private readonly string _seed;
        private readonly IReadOnlyDictionary<int, LoadedPerson> _people;
        private readonly IReadOnlyDictionary<int, LoadedHousehold> _families;
        private readonly Dictionary<int, StepPerson> _students;

        /// <param name="people">Every person by Id, in their primary family.</param>
        /// <param name="families">Every household by family Id.</param>
        public RegistrantChooser( StepsConfiguration steps, StudentPath path, IReadOnlyDictionary<int, LoadedPerson> people, IReadOnlyDictionary<int, LoadedHousehold> families, DateTime anchor, string seed )
        {
            _steps = steps;
            _path = path;
            _people = people;
            _families = families;
            _anchor = anchor.Date;
            _seed = seed;
            _students = path?.People.ToDictionary( p => p.PersonId ) ?? new Dictionary<int, StepPerson>();
        }

        /// <summary>
        /// The registrations of everyone who registers other than the Cast's pinned people, and whether the
        /// instance's event already happened in a plan that fixes exactly who went.
        /// </summary>
        public RegistrantChoice Choose( PlannedRegistrationInstance instance, ISet<int> pinned )
        {
            return ChooseStudents( instance, pinned );
        }

        #region Students

        private RegistrantChoice ChooseStudents( PlannedRegistrationInstance instance, ISet<int> pinned )
        {
            var rule = instance.Rule.Registrants;
            var template = instance.Template;
            var eligibility = RegistrantEligibility.Of( template, rule.Students.Gender );
            if ( eligibility.Grades == null )
            {
                throw new InvalidDataException( $"configuration/registrations.json: instance {instance.Rule.Key} registers students, but template {template.Key} has no grades." );
            }

            var drafts = new List<RegistrationDraft>();

            // Current Student Leaders go on their own with the leader code.
            var leaders = new HashSet<int>();
            if ( rule.StudentLeaders is { } leaderRule )
            {
                var attendingOn = Min( _anchor, instance.Closes.AddDays( 1 ) );
                foreach ( var step in _path.Steps.Where( s => s.StepType == leaderRule.StepType && s.Start <= instance.EventDay && ( s.End == null || s.End >= instance.EventDay ) ).OrderBy( s => StudentOf( s.PersonId ).Guid ) )
                {
                    var person = _people[step.PersonId];
                    if ( !pinned.Contains( person.PersonId ) && eligibility.Fits( person, instance ) && _path.HasBeenAttending( person.PersonId, attendingOn, _path.Attending ) && leaders.Add( person.PersonId ) )
                    {
                        drafts.Add( new RegistrationDraft( new List<LoadedPerson> { person }, null, null, true ) );
                    }
                }
            }

            var stepType = rule.Students.StepType == null ? null : _steps.StepTypes.SingleOrDefault( t => t.Key == rule.Students.StepType )
                ?? throw new InvalidDataException( $"configuration/registrations.json: instance {instance.Rule.Key} names step type {rule.Students.StepType}, which configuration/steps.json does not hold." );
            var isStepHeld = stepType != null && IsPlannedEvent( stepType, instance.EventDay );
            var chosen = Students( instance, stepType, eligibility, pinned, leaders );
            drafts.AddRange( Families( chosen.Select( p => _people[p.PersonId] ), template ) );
            return new RegistrantChoice( drafts, isStepHeld );
        }

        /// <summary>
        /// The students who register, other than the Cast's pinned ones and the leaders: exactly the people with the
        /// event step when the Student Path planned this event, otherwise a share of those who could go.
        /// </summary>
        private List<StepPerson> Students( PlannedRegistrationInstance instance, StepTypeDefinition stepType, RegistrantEligibility eligibility, ISet<int> pinned, HashSet<int> leaders )
        {
            var rule = instance.Rule.Registrants;
            if ( stepType != null && IsPlannedEvent( stepType, instance.EventDay ) )
            {
                // The event has happened, so its campers are the Student Path's: all of them, whatever the share.
                var holders = _path.Steps.Where( s => s.StepType == stepType.Key && s.Start == instance.EventDay ).Select( s => StudentOf( s.PersonId ) ).Distinct()
                    .Where( p => eligibility.IsGender( _people[p.PersonId] ) ).ToList();
                var unfit = holders.FirstOrDefault( p => !eligibility.Fits( _people[p.PersonId], instance ) );
                if ( unfit != null )
                {
                    throw new InvalidOperationException( $"{unfit.Name} has a {stepType.Name} step at the {instance.EventDay:yyyy-MM-dd} event, but Rock would not let them register for {instance.Name}." );
                }

                var unheld = pinned.FirstOrDefault( id => !holders.Any( h => h.PersonId == id ) );
                if ( unheld != 0 )
                {
                    throw new InvalidOperationException( $"A Cast registration puts {StudentOf( unheld ).Name} at {instance.Name}, but they have no {stepType.Name} step at that event." );
                }

                return holders.Where( p => !pinned.Contains( p.PersonId ) && !leaders.Contains( p.PersonId ) ).ToList();
            }

            var attendingOn = Min( _anchor, instance.Closes.AddDays( 1 ) );
            var candidates = _path.People
                .Where( p => !leaders.Contains( p.PersonId ) && eligibility.Fits( _people[p.PersonId], instance ) && _path.HasBeenAttending( p.PersonId, attendingOn, _path.Attending ) )
                .ToList();
            var percent = stepType?.Source.PercentOfStudents ?? rule.Students.Percent
                ?? throw new InvalidDataException( $"configuration/registrations.json: instance {instance.Rule.Key} needs students.stepType or students.percent." );
            var (picked, _) = StepShares.Choose( $"{_seed}:registrations:{instance.Rule.Key}:{instance.EventDay:yyyy-MM-dd}", candidates, percent, p => pinned.Contains( p.PersonId ) );
            return candidates.Where( p => picked.Contains( p.PersonId ) && !pinned.Contains( p.PersonId ) ).ToList();
        }

        /// <summary>
        /// True when the Student Path planned this event: it started in the history window and ended before the install day.
        /// </summary>
        private bool IsPlannedEvent( StepTypeDefinition stepType, DateTime eventDay )
        {
            var season = stepType.Source.Seasons.FirstOrDefault( s => s.In( eventDay.Year ) == eventDay );
            return season != null && eventDay >= _path.HistoryStart && eventDay.AddDays( season.Days - 1 ) < _anchor;
        }

        private StepPerson StudentOf( int personId ) => _students.TryGetValue( personId, out var person ) ? person
            : throw new InvalidOperationException( $"Person {personId} is not in the Student Path's people." );

        #endregion

        /// <summary>
        /// One registration per family, up to the template's limit per registration, families in Guid order.
        /// </summary>
        private IEnumerable<RegistrationDraft> Families( IEnumerable<LoadedPerson> chosen, RegistrationTemplateDefinition template )
        {
            foreach ( var family in chosen.GroupBy( p => p.FamilyId ).OrderBy( f => _families[f.Key].Cast.Guid ) )
            {
                var members = family.OrderBy( p => p.Cast.Guid ).ToList();
                for ( var i = 0; i < members.Count; i += template.MaxRegistrants )
                {
                    yield return new RegistrationDraft( members.Skip( i ).Take( template.MaxRegistrants ).ToList(), null, null, false );
                }
            }
        }

        private static DateTime Min( DateTime a, DateTime b ) => a < b ? a : b;
    }

    /// <summary>
    /// Who registers for an instance, and whether its event already happened in a plan that fixes exactly who went.
    /// </summary>
    internal sealed record RegistrantChoice( List<RegistrationDraft> Drafts, bool IsStepHeld );

    /// <summary>
    /// A registration before it is dated: who is on it, and the Cast story that pins it.
    /// </summary>
    internal sealed class RegistrationDraft
    {
        public RegistrationDraft( List<LoadedPerson> people, CastRegistration pin, LoadedHousehold pinHousehold, bool isLeader )
        {
            People = people;
            Pin = pin;
            PinHousehold = pinHousehold;
            IsLeader = isLeader;
        }

        public List<LoadedPerson> People { get; }

        public CastRegistration Pin { get; }

        public LoadedHousehold PinHousehold { get; }

        /// <summary>
        /// True for a current Student Leader who registers with the leader code.
        /// </summary>
        public bool IsLeader { get; }

        public DateTime When { get; set; }
    }

    /// <summary>
    /// Who Rock lets register for a template, in the terms of its eligibility settings, plus the gender an instance
    /// is for. Rock checks a registrant on the day they register (RegistrantEligibilityEvaluator), and the event is
    /// for the same people, so a registrant meets every rule on both days.
    /// </summary>
    internal sealed class RegistrantEligibility
    {
        private RegistrantEligibility( IntRange grades, RegistrationGender? gender )
        {
            Grades = grades;
            Gender = gender;
        }

        /// <param name="instanceGender">The gender an instance is for, such as camp's girls' instance, or null.</param>
        public static RegistrantEligibility Of( RegistrationTemplateDefinition template, RegistrationGender? instanceGender )
        {
            var grades = template.Eligibility?.Grades is { Count: 2 } g ? new IntRange { Min = g[0], Max = g[1] } : null;
            return new RegistrantEligibility( grades, instanceGender );
        }

        /// <summary>
        /// The template's lowest and highest grade, or null when it has no grade rule.
        /// </summary>
        public IntRange Grades { get; }

        public RegistrationGender? Gender { get; }

        /// <summary>
        /// True when Rock would let the person register for the instance: on some day of its window, and on its event day.
        /// </summary>
        public bool Fits( LoadedPerson person, PlannedRegistrationInstance instance )
        {
            return IsGender( person ) && IsMet( person, instance.EventDay ) && Days( person, instance.Opens, instance.Closes ) != null;
        }

        public bool IsGender( LoadedPerson person ) => Gender == null || GenderOf( person ) == Gender;

        /// <summary>
        /// True when the person meets every rule on the day.
        /// </summary>
        public bool IsMet( LoadedPerson person, DateTime day )
        {
            if ( Grades != null )
            {
                if ( !person.GraduationYear.HasValue )
                {
                    return false;
                }

                var grade = AnchorDates.GradeOn( person.GraduationYear.Value, day );
                if ( grade < Grades.Min || grade > Grades.Max )
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The first and last day in a window when the person meets every rule, or null. Grades change only on June 1.
        /// </summary>
        public (DateTime First, DateTime Last)? Days( LoadedPerson person, DateTime from, DateTime until )
        {
            if ( until < from || !IsGender( person ) )
            {
                return null;
            }

            var starts = new SortedSet<DateTime> { from };
            for ( var year = from.Year; year <= until.Year; year++ )
            {
                var transition = new DateTime( year, AnchorDates.GradeTransitionMonth, AnchorDates.GradeTransitionDay );
                if ( Grades != null && transition > from && transition <= until )
                {
                    starts.Add( transition );
                }
            }

            var list = starts.ToList();
            DateTime? first = null;
            DateTime? last = null;
            for ( var i = 0; i < list.Count; i++ )
            {
                var end = i + 1 < list.Count ? list[i + 1].AddDays( -1 ) : until;
                if ( IsMet( person, list[i] ) )
                {
                    first ??= list[i];
                    last = end;
                }
            }

            return first.HasValue ? (first.Value, last.Value) : null;
        }

        public static RegistrationGender? GenderOf( LoadedPerson person ) => person.Cast.Gender switch
        {
            "female" => RegistrationGender.Female,
            "male" => RegistrationGender.Male,
            _ => null
        };
    }
}
