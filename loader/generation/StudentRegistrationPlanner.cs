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

using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        10/28/26 - CLAUDE

        Students register from the Student Path plan, so registrations agree with grades, check-ins, and steps.
        An instance whose event the Student Path planned (last summer's camp) has exactly the students with that
        step at that event. Any other instance takes a share of the students who could go, with the step's own
        rate, or a share of the students who attend. Each registration is dated by a seeded draw across the whole
        window, with bursts the configuration names, and only those dated before the install day are written, so
        how many have registered follows how far into the window the install day is.

        Rock checks a registrant's grade on the day they register, so a registration falls on a day when every
        registrant is in the template's grades, and every registrant is also in them on the event day.

        Reason: Registrations follow Rock's rules and the Student Path's, on any install day (decisions D-registrations-8 and D-registrations-9).
    */

    /// <summary>
    /// Plans the student registrations: who registered for each instance with a students rule, when, who
    /// registered them, what they chose, what they paid, and where they were placed. Reads no database.
    /// </summary>
    internal sealed class StudentRegistrationPlanner
    {
        private static readonly string[] Currencies = { "Credit Card", "ACH" };

        /// <summary>
        /// Most registrations are paid by card, some by bank transfer.
        /// </summary>
        private const double CardShare = 0.8;

        /// <summary>
        /// Rock's Send Registration Payment Reminders job: weekdays at 9 AM, every PaymentReminderTimeSpan days,
        /// until 30 days after registration closes.
        /// </summary>
        private const int ReminderHour = 9;
        private const int ReminderCutoffDays = 30;

        /// <summary>
        /// Rock's Download Payments job runs at 5 AM, so a plan's payment is recorded early on its day.
        /// </summary>
        private const int ScheduledPaymentHour = 5;

        private readonly RegistrationsConfiguration _config;
        private readonly StepsConfiguration _steps;
        private readonly StudentPath _path;
        private readonly DateTime _anchor;
        private readonly string _seed;
        private readonly int _reminderDays;
        private readonly Dictionary<int, LoadedPerson> _people = new Dictionary<int, LoadedPerson>();
        private readonly Dictionary<int, LoadedHousehold> _families = new Dictionary<int, LoadedHousehold>();
        private readonly Dictionary<int, StepPerson> _students;
        private readonly List<(LoadedHousehold Household, CastRegistration Pin)> _pins;
        private readonly Dictionary<string, int> _placed = new Dictionary<string, int>();

        /// <param name="households">Every loaded household. A person's family is the household whose family is their primary one.</param>
        /// <param name="castHouseholdKeys">The Cast's household keys, whose registrations lists pin stories.</param>
        /// <param name="reminderDays">The days between payment reminders on a paid template.</param>
        public StudentRegistrationPlanner( RegistrationsConfiguration config, StepsConfiguration steps, StudentPath path, IReadOnlyList<LoadedHousehold> households, ISet<string> castHouseholdKeys, DateTime anchor, string seed, int reminderDays )
        {
            _config = config;
            _steps = steps;
            _path = path;
            _anchor = anchor.Date;
            _seed = seed;
            _reminderDays = reminderDays;
            _students = path.People.ToDictionary( p => p.PersonId );
            foreach ( var household in households )
            {
                _families[household.FamilyId] = household;
                foreach ( var member in household.Members.Where( m => m.FamilyId == household.FamilyId ) )
                {
                    _people[member.PersonId] = member;
                }
            }

            _pins = households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) )
                .SelectMany( h => h.Cast.Registrations.Select( r => (h, r) ) )
                .ToList();
        }

        /// <summary>
        /// Plans every instance whose rule has a students rule, in plan order. Returns the registrations by date.
        /// </summary>
        public List<PlannedRegistration> Plan( RegistrationPlan plan )
        {
            var registrations = new List<PlannedRegistration>();
            foreach ( var instance in plan.Instances.Where( i => i.Rule.Registrants?.Students != null ) )
            {
                registrations.AddRange( PlanInstance( instance, plan ) );
            }

            var unknown = _pins.FirstOrDefault( p => !plan.Instances.Any( i => i.Rule.Key == p.Pin.Instance && i.Pick == p.Pin.Occurrence && i.Rule.Registrants?.Students != null ) );
            if ( unknown.Pin != null )
            {
                throw new InvalidOperationException( $"cast/households/{unknown.Household.Cast.Key}.json registers people for the {unknown.Pin.Occurrence.ToString().ToLowerInvariant()} {unknown.Pin.Instance}, which configuration/registrations.json does not plan with a students rule." );
            }

            return registrations.OrderBy( r => r.When ).ThenBy( r => r.Guid ).ToList();
        }

        private List<PlannedRegistration> PlanInstance( PlannedRegistrationInstance instance, RegistrationPlan plan )
        {
            var rule = instance.Rule.Registrants;
            var template = instance.Template;
            var grades = template.Eligibility?.Grades is { Count: 2 } g ? new IntRange { Min = g[0], Max = g[1] }
                : throw new InvalidDataException( $"configuration/registrations.json: instance {instance.Rule.Key} registers students, but template {template.Key} has no grades." );
            var lastDay = Min( instance.Closes, _anchor.AddDays( -1 ) );
            if ( lastDay < instance.Opens )
            {
                return new List<PlannedRegistration>();
            }

            var pins = _pins.Where( p => p.Pin.Instance == instance.Rule.Key && p.Pin.Occurrence == instance.Pick ).ToList();
            var pinned = pins.SelectMany( p => p.Pin.Registrants.Select( key => PinPerson( p.Household, p.Pin, key ) ) ).Select( p => p.PersonId ).ToHashSet();
            var drafts = new List<Draft>();
            foreach ( var (household, pin) in pins )
            {
                var people = pin.Registrants.Select( key => StepPersonOf( PinPerson( household, pin, key ) ) ).ToList();
                drafts.Add( new Draft( people, pin, household, false ) );
            }

            // Current Student Leaders go on their own with the leader code.
            var leaders = new HashSet<int>();
            if ( rule.StudentLeaders is { } leaderRule )
            {
                var attendingOn = Min( _anchor, instance.Closes.AddDays( 1 ) );
                foreach ( var step in _path.Steps.Where( s => s.StepType == leaderRule.StepType && s.Start <= instance.EventDay && ( s.End == null || s.End >= instance.EventDay ) ).OrderBy( s => StepPersonOf( s.PersonId ).Guid ) )
                {
                    var person = StepPersonOf( step.PersonId );
                    if ( !pinned.Contains( person.PersonId ) && Fits( person, instance, grades, rule.Students.Gender ) && _path.HasBeenAttending( person.PersonId, attendingOn, _path.Attending ) && leaders.Add( person.PersonId ) )
                    {
                        drafts.Add( new Draft( new List<StepPerson> { person }, null, null, true ) );
                    }
                }
            }

            var stepType = rule.Students.StepType == null ? null : _steps.StepTypes.SingleOrDefault( t => t.Key == rule.Students.StepType )
                ?? throw new InvalidDataException( $"configuration/registrations.json: instance {instance.Rule.Key} names step type {rule.Students.StepType}, which configuration/steps.json does not hold." );
            var isStepHeld = stepType != null && IsPlannedEvent( stepType, instance.EventDay );
            var chosen = Choose( instance, rule, stepType, grades, pinned, leaders );
            foreach ( var family in chosen.GroupBy( p => _people[p.PersonId].FamilyId ).OrderBy( f => _families[f.Key].Cast.Guid ) )
            {
                var siblings = family.OrderBy( p => p.Guid ).ToList();
                for ( var i = 0; i < siblings.Count; i += template.MaxRegistrants )
                {
                    drafts.Add( new Draft( siblings.Skip( i ).Take( template.MaxRegistrants ).ToList(), null, null, false ) );
                }
            }

            var dated = drafts.Where( d => Date( d, instance, rule, grades, lastDay ) ).ToList();
            var registrations = FillSpots( dated.Select( d => Registration( d, instance ) ).ToList(), instance, rule, isStepHeld );
            foreach ( var registration in registrations.OrderBy( r => r.When ).ThenBy( r => r.Guid ) )
            {
                Price( registration, instance, rule, grades );
                Pay( registration, instance, rule );
            }

            Place( registrations, instance, rule, grades, plan );
            Answer( registrations, instance, grades );
            return registrations;
        }

        #region Who

        /// <summary>
        /// The students who register, other than the Cast's pinned ones and the leaders: exactly the people with the
        /// event step when the Student Path planned this event, otherwise a share of those who could go.
        /// </summary>
        private List<StepPerson> Choose( PlannedRegistrationInstance instance, RegistrantRule rule, StepTypeDefinition stepType, IntRange grades, HashSet<int> pinned, HashSet<int> leaders )
        {
            var gender = rule.Students.Gender;
            if ( stepType != null && IsPlannedEvent( stepType, instance.EventDay ) )
            {
                // The event has happened, so its campers are the Student Path's: all of them, whatever the share.
                var holders = _path.Steps.Where( s => s.StepType == stepType.Key && s.Start == instance.EventDay ).Select( s => StepPersonOf( s.PersonId ) ).Distinct()
                    .Where( p => gender == null || GenderOf( p ) == gender ).ToList();
                var unfit = holders.FirstOrDefault( p => !Fits( p, instance, grades, gender ) );
                if ( unfit != null )
                {
                    throw new InvalidOperationException( $"{unfit.Name} has a {stepType.Name} step at the {instance.EventDay:yyyy-MM-dd} event, but Rock would not let them register for {instance.Name}." );
                }

                var unheld = pinned.FirstOrDefault( id => !holders.Any( h => h.PersonId == id ) );
                if ( unheld != 0 )
                {
                    throw new InvalidOperationException( $"A Cast registration puts {StepPersonOf( unheld ).Name} at {instance.Name}, but they have no {stepType.Name} step at that event." );
                }

                return holders.Where( p => !pinned.Contains( p.PersonId ) && !leaders.Contains( p.PersonId ) ).ToList();
            }

            var attendingOn = Min( _anchor, instance.Closes.AddDays( 1 ) );
            var candidates = _path.People
                .Where( p => !leaders.Contains( p.PersonId ) && Fits( p, instance, grades, gender ) && _path.HasBeenAttending( p.PersonId, attendingOn, _path.Attending ) )
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

        /// <summary>
        /// True when Rock would let the person register: in the template's grades on some day of the window, and on the event day.
        /// </summary>
        private bool Fits( StepPerson person, PlannedRegistrationInstance instance, IntRange grades, RegistrationGender? gender )
        {
            return person.GraduationYear.HasValue && ( gender == null || GenderOf( person ) == gender )
                && StudentPath.InGrades( person, instance.EventDay, grades )
                && EligibleDays( person, instance.Opens, instance.Closes, grades ) != null;
        }

        /// <summary>
        /// The first and last day in a window when the person is in the grades, or null. Grades change only on June 1.
        /// </summary>
        private static (DateTime First, DateTime Last)? EligibleDays( StepPerson person, DateTime from, DateTime until, IntRange grades )
        {
            if ( !person.GraduationYear.HasValue || until < from )
            {
                return null;
            }

            var starts = new List<DateTime> { from };
            for ( var year = from.Year; year <= until.Year; year++ )
            {
                var transition = new DateTime( year, AnchorDates.GradeTransitionMonth, AnchorDates.GradeTransitionDay );
                if ( transition > from && transition <= until )
                {
                    starts.Add( transition );
                }
            }

            DateTime? first = null;
            DateTime? last = null;
            for ( var i = 0; i < starts.Count; i++ )
            {
                var end = i + 1 < starts.Count ? starts[i + 1].AddDays( -1 ) : until;
                if ( StudentPath.InGrades( person, starts[i], grades ) )
                {
                    first ??= starts[i];
                    last = end;
                }
            }

            return first.HasValue ? (first.Value, last.Value) : null;
        }

        private RegistrationGender? GenderOf( StepPerson person ) => _people[person.PersonId].Cast.Gender switch
        {
            "female" => RegistrationGender.Female,
            "male" => RegistrationGender.Male,
            _ => null
        };

        private StepPerson StepPersonOf( LoadedPerson person ) => StepPersonOf( person.PersonId );

        private StepPerson StepPersonOf( int personId ) => _students.TryGetValue( personId, out var person ) ? person
            : throw new InvalidOperationException( $"Person {personId} is not in the Student Path's people." );

        private static LoadedPerson PinPerson( LoadedHousehold household, CastRegistration pin, string key ) => household.Members.FirstOrDefault( m => m.Cast.Key == key )
            ?? throw new InvalidDataException( $"cast/households/{household.Cast.Key}.json registers \"{key}\" for {pin.Instance}, who is not a member." );

        #endregion

        #region When

        /// <summary>
        /// Dates a registration by a seeded draw across the whole window, moved to a day Rock lets every registrant
        /// register. Returns false when the day is not before the install day, so the registration is not written. A
        /// Cast registration is always early in the window, so it is made before the install day and a payment plan fits.
        /// </summary>
        private bool Date( Draft draft, PlannedRegistrationInstance instance, RegistrantRule rule, IntRange grades, DateTime lastDay )
        {
            var first = draft.People[0];
            var stream = draft.IsLeader ? first.Guid : _families[_people[first.PersonId].FamilyId].Cast.Guid;
            var random = new SeededRandom( $"{_seed}:registrations:{instance.Template.Key}:{instance.EventDay:yyyy-MM-dd}:{stream}" );
            var day = Draw( rule.Timing, instance, random );
            var eligible = draft.People.Select( p => EligibleDays( p, instance.Opens, instance.Closes, grades ) ).ToList();
            if ( eligible.Any( e => e == null ) )
            {
                throw new InvalidOperationException( $"{draft.People.First( p => EligibleDays( p, instance.Opens, instance.Closes, grades ) == null ).Name} cannot register for {instance.Name}: they are never in grades {grades.Min} to {grades.Max} while it is open." );
            }

            var from = eligible.Max( e => e.Value.First );
            var until = eligible.Min( e => e.Value.Last );
            if ( day < from )
            {
                day = Min( from.AddDays( random.Between( 0, 6 ) ), until );
            }

            day = Min( day, until );
            if ( draft.Pin != null )
            {
                day = Max( from, Min( lastDay, instance.Opens.AddDays( random.Between( 0, 13 ) ) ) );
                if ( day > lastDay || day > until )
                {
                    throw new InvalidOperationException( $"The Cast registration in cast/households/{draft.PinHousehold.Cast.Key}.json for {instance.Name} cannot be dated before the install day: its registrants are not all in grades {grades.Min} to {grades.Max} yet." );
                }
            }

            if ( day > lastDay )
            {
                return false;
            }

            draft.When = day.AddMinutes( random.Between( 7 * 60, 22 * 60 ) );
            return true;
        }

        /// <summary>
        /// A day in the window: each burst's share falls in its days, and the rest anywhere in the window.
        /// </summary>
        private static DateTime Draw( List<RegistrationBurst> timing, PlannedRegistrationInstance instance, SeededRandom random )
        {
            var roll = random.Between( 0.0, 100.0 );
            foreach ( var burst in timing )
            {
                roll -= burst.Percent;
                if ( roll >= 0 )
                {
                    continue;
                }

                var (from, until) = burst.After is { } after
                    ? (DayOf( after, instance ), DayOf( after, instance ).AddDays( burst.Days - 1 ))
                    : (DayOf( burst.Before.Value, instance ).AddDays( -burst.Days ), DayOf( burst.Before.Value, instance ).AddDays( -1 ));
                from = Max( from, instance.Opens );
                until = Min( until, instance.Closes );
                if ( from <= until )
                {
                    return from.AddDays( random.Between( 0, ( until - from ).Days ) );
                }

                break;
            }

            return instance.Opens.AddDays( random.Between( 0, ( instance.Closes - instance.Opens ).Days ) );
        }

        private static DateTime DayOf( RegistrationDay day, PlannedRegistrationInstance instance )
        {
            return day switch
            {
                RegistrationDay.Opens => instance.Opens,
                RegistrationDay.Closes => instance.Closes.AddDays( 1 ),
                RegistrationDay.EarlyBird => EarlyBirdEnds( instance )?.AddDays( 1 ) ?? instance.Closes.AddDays( 1 ),
                _ => instance.PaymentDeadline ?? instance.Closes.AddDays( 1 )
            };
        }

        /// <summary>
        /// The last day of the template's early bird for this instance's event, or null when it has none.
        /// </summary>
        private static DateTime? EarlyBirdEnds( PlannedRegistrationInstance instance )
        {
            var earlyBird = instance.Template.Discounts.FirstOrDefault( d => d.AutoApply && d.EndsBefore != null );
            return earlyBird == null ? null : RegistrationPlanner.LastOnOrBefore( earlyBird.EndsBefore, instance.EventDay );
        }

        #endregion

        #region Registration

        private PlannedRegistration Registration( Draft draft, PlannedRegistrationInstance instance )
        {
            var first = _people[draft.People[0].PersonId];
            var registration = new PlannedRegistration
            {
                Instance = instance,
                When = draft.When,
                Pin = draft.Pin,
                IsLeader = draft.IsLeader,
                Guid = SeededRandom.StableGuid( $"registration:{instance.Guid}:{first.Cast.Guid}" ),
                Random = new SeededRandom( $"{_seed}:registrations:{instance.Guid}:{first.Cast.Guid}" )
            };
            registration.Registrar = Registrar( draft, registration.Random );
            foreach ( var person in draft.People )
            {
                var loaded = _people[person.PersonId];
                registration.Registrants.Add( new PlannedRegistrant
                {
                    Person = loaded,
                    Student = person,
                    Guid = SeededRandom.StableGuid( $"registration-registrant:{instance.Guid}:{loaded.Cast.Guid}" )
                } );
            }

            return registration;
        }

        /// <summary>
        /// Who registered: the Cast story's registrar, or a parent or guardian in the family (one with an email when
        /// there is one), or the student when no adult lives with them.
        /// </summary>
        private LoadedPerson Registrar( Draft draft, SeededRandom random )
        {
            if ( draft.Pin?.Registrar is { } key )
            {
                return PinPerson( draft.PinHousehold, draft.Pin, key );
            }

            var first = _people[draft.People[0].PersonId];
            var registrants = draft.People.Select( p => p.PersonId ).ToHashSet();
            var adults = _families[first.FamilyId].Members
                .Where( m => m.FamilyId == first.FamilyId && m.IsAdult && m.CanAttend && !registrants.Contains( m.PersonId ) )
                .OrderBy( m => m.Cast.Guid )
                .ToList();
            var withEmail = adults.Where( m => !string.IsNullOrEmpty( m.Cast.Email ) ).ToList();
            var pool = withEmail.Count > 0 ? withEmail : adults;
            return pool.Count > 0 ? random.Pick( pool ) : first;
        }

        /// <summary>
        /// Fills the limit in order: the Cast's registrations first, then by date. Past the limit, the rule's share of
        /// the rest join the wait list and the others do not register. A template with no wait list stops taking
        /// registrations once full, except at an event the Student Path planned, whose campers must all fit. Returns
        /// the registrations made.
        /// </summary>
        private List<PlannedRegistration> FillSpots( List<PlannedRegistration> registrations, PlannedRegistrationInstance instance, RegistrantRule rule, bool isStepHeld )
        {
            var taken = 0;
            var made = new List<PlannedRegistration>();
            foreach ( var registration in registrations.OrderBy( r => r.Pin == null ).ThenBy( r => r.When ).ThenBy( r => r.Guid ) )
            {
                var spotsLeft = instance.MaxAttendees.HasValue ? instance.MaxAttendees.Value - taken : int.MaxValue;
                if ( !instance.Template.WaitList && registration.Registrants.Count > spotsLeft )
                {
                    if ( isStepHeld )
                    {
                        throw new InvalidDataException( $"configuration/registrations.json: {instance.Name} has more campers with its step than its maxAttendees for this dataset ({instance.MaxAttendees}), and its template has no wait list. Raise maxAttendees." );
                    }

                    continue;
                }

                if ( spotsLeft <= 0 && !new SeededRandom( $"{_seed}:registrations:{instance.Guid}:wait-list:{registration.Guid}" ).Chance( ( rule.WaitListPercent ?? 100 ) / 100 ) )
                {
                    continue;
                }

                foreach ( var registrant in registration.Registrants )
                {
                    registrant.OnWaitList = instance.MaxAttendees.HasValue && taken >= instance.MaxAttendees.Value;
                    taken += registrant.OnWaitList ? 0 : 1;
                }

                made.Add( registration );
            }

            return made;
        }

        #endregion

        #region Cost

        /// <summary>
        /// Sets each registrant's cost and fees and the discount Rock gives the registration: a code the person used,
        /// or the first automatic discount, in the template's order, valid on the day.
        /// </summary>
        private void Price( PlannedRegistration registration, PlannedRegistrationInstance instance, RegistrantRule rule, IntRange grades )
        {
            var template = instance.Template;
            var cost = template.SetCostOnInstance ? instance.Rule.Cost ?? 0 : template.Cost;
            foreach ( var registrant in registration.Registrants )
            {
                registrant.Cost = cost;
                registrant.DiscountApplies = true;
                if ( registrant.OnWaitList )
                {
                    continue;
                }

                foreach ( var (feeKey, percent) in rule.Fees )
                {
                    var fee = template.Fees.FirstOrDefault( f => f.Key == feeKey )
                        ?? throw new InvalidDataException( $"configuration/registrations.json: instance {instance.Rule.Key} has a share for fee {feeKey}, which template {template.Key} does not hold." );
                    var random = new SeededRandom( $"{_seed}:registrations:{instance.Guid}:fee:{fee.Key}:{registrant.Person.Cast.Guid}" );
                    if ( random.Chance( percent / 100 ) )
                    {
                        // Sizes run small to large, and older students take larger ones.
                        var grade = AnchorDates.GradeOn( registrant.Student.GraduationYear.Value, instance.EventDay );
                        var scale = grades.Max == grades.Min ? 0.5 : ( grade - grades.Min ) / ( double ) ( grades.Max - grades.Min );
                        var index = Math.Min( Math.Max( ( int ) Math.Round( scale * ( fee.Items.Count - 1 ) ) + random.Between( -1, 1 ), 0 ), fee.Items.Count - 1 );
                        registrant.Fees.Add( new PlannedRegistrantFee( fee, index, 1, fee.Items[index].Cost ) );
                    }
                }
            }

            registration.Discount = Discount( registration, instance );
        }

        private RegistrationDiscountDefinition Discount( PlannedRegistration registration, PlannedRegistrationInstance instance )
        {
            var template = instance.Template;
            if ( registration.IsLeader && instance.Rule.Registrants.StudentLeaders is { } leaders )
            {
                var code = template.Discounts.FirstOrDefault( d => d.Key == leaders.Discount )
                    ?? throw new InvalidDataException( $"configuration/registrations.json: instance {instance.Rule.Key} gives leaders discount {leaders.Discount}, which template {template.Key} does not hold." );
                var limit = ( code.MaxUsage ?? int.MaxValue ) - leaders.KeepUnused;
                var used = _leaderCodesUsed.GetValueOrDefault( instance.Guid );
                if ( used < limit )
                {
                    _leaderCodesUsed[instance.Guid] = used + 1;
                    return code;
                }

                return null;
            }

            var day = registration.When.Date;
            foreach ( var discount in template.Discounts.Where( d => d.AutoApply ) )
            {
                var isInDates = discount.EndsBefore == null || day <= RegistrationPlanner.LastOnOrBefore( discount.EndsBefore, instance.EventDay );
                var hasRegistrants = discount.MinRegistrants == null || registration.Registrants.Count >= discount.MinRegistrants.Value;
                if ( isInDates && hasRegistrants )
                {
                    return discount;
                }
            }

            return null;
        }

        private readonly Dictionary<Guid, int> _leaderCodesUsed = new Dictionary<Guid, int>();

        #endregion

        #region Payments

        /// <summary>
        /// How the registration pays and every payment made before the install day: in full, a deposit with the
        /// balance paid by the deadline once it has passed, or a deposit and a payment plan that ends by the deadline.
        /// </summary>
        private void Pay( PlannedRegistration registration, PlannedRegistrationInstance instance, RegistrantRule rule )
        {
            var template = instance.Template;
            var total = registration.DiscountedCost;
            if ( total <= 0 )
            {
                return;
            }

            var random = registration.Random;
            registration.Currency = random.Chance( CardShare ) ? Currencies[0] : Currencies[1];
            var registrantCount = registration.Registrants.Count( r => !r.OnWaitList );
            var minimumEach = template.SetCostOnInstance ? instance.Rule.MinimumInitialPayment : template.MinimumInitialPayment;
            var minimum = Math.Min( total, ( minimumEach ?? 0 ) * registrantCount );
            var deadline = instance.PaymentDeadline;
            var day = registration.When.Date;

            var choice = registration.Pin?.Payment ?? Pick( rule.Payments, random );
            var isFullRequired = template.FullPaymentOrPlanRequired != null;
            if ( minimum >= total || ( deadline.HasValue && day >= deadline.Value ) )
            {
                choice = RegistrationPayment.PaidInFull;
            }

            PlannedPaymentPlan paymentPlan = null;
            if ( choice == RegistrationPayment.Plan )
            {
                paymentPlan = PaymentPlan( registration, template, rule, total - minimum, deadline, random );
                choice = paymentPlan != null ? RegistrationPayment.Plan : isFullRequired ? RegistrationPayment.PaidInFull : RegistrationPayment.Deposit;
            }

            if ( choice == RegistrationPayment.Deposit && isFullRequired )
            {
                choice = RegistrationPayment.PaidInFull;
            }

            registration.Payment = choice;
            var paidAt = registration.When.AddMinutes( 1 );
            switch ( choice )
            {
                case RegistrationPayment.PaidInFull:
                    registration.Payments.Add( new PlannedPayment( paidAt, total, false ) );
                    break;

                case RegistrationPayment.Deposit:
                    registration.Payments.Add( new PlannedPayment( paidAt, minimum, false ) );
                    if ( deadline.HasValue && deadline.Value <= _anchor )
                    {
                        // Once the deadline has passed, every balance has been paid, most in the last weeks before it.
                        var latest = deadline.Value.AddDays( -1 );
                        var earliest = Max( day, deadline.Value.AddDays( -21 ) );
                        var balanceDay = earliest >= latest ? latest : earliest.AddDays( random.Between( 0, ( latest - earliest ).Days ) );
                        var balanceAt = balanceDay <= day ? paidAt.AddHours( 1 ) : balanceDay.AddMinutes( random.Between( 17 * 60, 21 * 60 ) );
                        registration.Payments.Add( new PlannedPayment( balanceAt, total - minimum, false ) );
                    }

                    break;

                default:
                    var planned = paymentPlan.AmountPerPayment * paymentPlan.Dates.Count;
                    registration.Payments.Add( new PlannedPayment( paidAt, total - planned, false ) );
                    foreach ( var date in paymentPlan.Dates.Where( d => d < _anchor ) )
                    {
                        registration.Payments.Add( new PlannedPayment( date.AddHours( ScheduledPaymentHour ).AddMinutes( random.Between( 0, 59 ) ), paymentPlan.AmountPerPayment, true ) );
                    }

                    registration.Plan = paymentPlan;
                    break;
            }

            registration.LastPaymentReminder = LastReminder( registration, instance );
        }

        /*
            10/28/26 - CLAUDE

            A payment plan is made the way Rock's registration entry makes one: the plan covers what is left after the
            first payment, split into as many payments at its frequency as fit from its start to the payment deadline
            (PaymentPlanConfigurationService), at least two. Each payment is the plan amount divided by the number of
            payments, rounded down to the cent, and the cents left over are added to the first payment.

            Reason: The registration's plan shows the same amounts and dates in Rock's screens as one a person made.
        */

        /// <summary>
        /// A payment plan for the amount, or null when fewer than two payments fit before the deadline.
        /// </summary>
        private static PlannedPaymentPlan PaymentPlan( PlannedRegistration registration, RegistrationTemplateDefinition template, RegistrantRule rule, decimal amount, DateTime? deadline, SeededRandom random )
        {
            if ( !deadline.HasValue || template.PaymentPlan.Count == 0 || amount <= 0 )
            {
                return null;
            }

            var allowed = rule.PlanFrequencies.Where( f => template.PaymentPlan.Contains( f.Key ) ).ToDictionary( f => f.Key, f => f.Value );
            var frequency = registration.Pin?.Frequency ?? ( allowed.Count > 0 ? Pick( allowed, random ) : template.PaymentPlan[0] );
            var day = registration.When.Date;
            // A monthly plan starts a month out, on a day every month has, so its payments keep one day of the month.
            var monthOut = day.AddMonths( 1 );
            var start = frequency == PaymentPlanFrequency.Monthly ? new DateTime( monthOut.Year, monthOut.Month, Math.Min( monthOut.Day, 28 ) ) : NextFirstOrFifteenth( day.AddDays( 7 ) );
            var dates = PaymentDates( frequency, start, deadline.Value );
            if ( dates.Count < 2 )
            {
                return null;
            }

            var cents = ( int ) ( amount * 100 );
            return new PlannedPaymentPlan
            {
                Frequency = frequency,
                Start = start,
                Dates = dates,
                AmountPerPayment = Math.Floor( ( decimal ) cents / dates.Count ) / 100
            };
        }

        /// <summary>
        /// The payment dates from the start to the end, as Rock counts them: monthly on the start's day of the month,
        /// or twice a month on the 1st and 15th. A monthly start is never after the 28th.
        /// </summary>
        internal static List<DateTime> PaymentDates( PaymentPlanFrequency frequency, DateTime start, DateTime end )
        {
            var dates = new List<DateTime>();
            if ( frequency == PaymentPlanFrequency.Monthly )
            {
                for ( var date = start; date <= end; date = date.AddMonths( 1 ) )
                {
                    dates.Add( date );
                }
            }
            else
            {
                for ( var date = NextFirstOrFifteenth( start ); date <= end; date = date.Day == 1 ? date.AddDays( 14 ) : date.AddDays( -14 ).AddMonths( 1 ) )
                {
                    dates.Add( date );
                }
            }

            return dates;
        }

        private static DateTime NextFirstOrFifteenth( DateTime day ) => day.Day == 1 || day.Day == 15 ? day : day.Day < 15 ? new DateTime( day.Year, day.Month, 15 ) : new DateTime( day.Year, day.Month, 1 ).AddMonths( 1 );

        /// <summary>
        /// When Rock's payment reminder job last emailed the registrar: each weekday at 9 AM while a balance is due,
        /// at least the template's reminder days after the last one (or after registering), until 30 days after
        /// registration closed. Null when it never did.
        /// </summary>
        private DateTime? LastReminder( PlannedRegistration registration, PlannedRegistrationInstance instance )
        {
            DateTime? last = null;
            var since = registration.When;
            var cost = registration.DiscountedCost;
            var cutoff = instance.Closes.AddDays( ReminderCutoffDays );
            for ( var day = registration.When.Date.AddDays( 1 ); day < _anchor && day <= cutoff; day = day.AddDays( 1 ) )
            {
                if ( day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday )
                {
                    continue;
                }

                var run = day.AddHours( ReminderHour );
                var paid = registration.Payments.Where( p => p.When <= run ).Sum( p => p.Amount );
                if ( cost > paid && since < run.AddDays( -_reminderDays ) )
                {
                    last = run;
                    since = run;
                }
            }

            return last;
        }

        #endregion

        #region Placements

        /// <summary>
        /// Places registrants once registration has closed (each placement as many times as the rule says), or, while
        /// it is open, the people who registered in its first days. Cabins and other groups only this instance has go
        /// by grade bands; groups shared with another instance, such as buses, and groups a person joins more than one
        /// of, such as activities, fill evenly.
        /// </summary>
        private void Place( List<PlannedRegistration> registrations, PlannedRegistrationInstance instance, RegistrantRule rule, IntRange grades, RegistrationPlan plan )
        {
            if ( rule.Placements == null )
            {
                return;
            }

            var isClosed = instance.Closes < _anchor;
            var keys = isClosed ? rule.Placements.Each.Keys.ToList() : rule.Placements.Early?.Placements ?? new List<string>();
            var placedRegistrants = new List<(PlannedRegistrant Registrant, DateTime On)>();
            foreach ( var registration in registrations )
            {
                var random = new SeededRandom( $"{_seed}:registrations:{instance.Guid}:placed:{registration.Guid}" );
                DateTime on;
                if ( isClosed )
                {
                    // Everyone is placed in the last two weeks before the event, or right after a late registration.
                    on = instance.EventDay.AddDays( -random.Between( 2, 14 ) ).AddHours( random.Between( 9, 16 ) );
                    on = on > registration.When ? on : registration.When.AddHours( 1 );
                }
                else if ( registration.When.Date < instance.Opens.AddDays( rule.Placements.Early.Days ) )
                {
                    on = instance.Opens.AddDays( rule.Placements.Early.Days + random.Between( 0, 13 ) ).AddHours( random.Between( 9, 16 ) );
                    if ( on.Date >= _anchor )
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                placedRegistrants.AddRange( registration.Registrants.Where( r => !r.OnWaitList ).Select( r => (r, on) ) );
            }

            foreach ( var key in keys )
            {
                var groups = instance.PlacementGroups.Where( g => g.Placement.Key == key ).OrderBy( g => g.Order ).ToList();
                var count = rule.Placements.Each.TryGetValue( key, out var each ) ? each : 1;
                if ( groups.Count == 0 )
                {
                    continue;
                }

                var isShared = groups.Any( g => plan.Instances.Count( i => i.PlacementGroups.Any( p => p.Group == g.Group ) ) > 1 );
                var people = placedRegistrants.OrderBy( p => p.Registrant.Person.Cast.Guid ).ToList();
                if ( count == 1 && !isShared )
                {
                    // Grade bands: the youngest fill the first groups.
                    var byGrade = people.OrderBy( p => AnchorDates.GradeOn( p.Registrant.Student.GraduationYear.Value, instance.EventDay ) ).ThenBy( p => p.Registrant.Person.Cast.Guid ).ToList();
                    for ( var i = 0; i < byGrade.Count; i++ )
                    {
                        Join( byGrade[i].Registrant, groups[i * groups.Count / byGrade.Count], byGrade[i].On );
                    }

                    continue;
                }

                foreach ( var (registrant, on) in people )
                {
                    var random = new SeededRandom( $"{_seed}:registrations:{instance.Guid}:placement:{key}:{registrant.Person.Cast.Guid}" );
                    foreach ( var group in groups.OrderBy( g => _placed.GetValueOrDefault( g.Group.Key ) ).ThenBy( _ => random.Between( 0.0, 1.0 ) ).Take( count ).ToList() )
                    {
                        Join( registrant, group, on );
                    }
                }
            }
        }

        private void Join( PlannedRegistrant registrant, PlannedPlacementGroup group, DateTime on )
        {
            registrant.Placements.Add( new PlannedPlacement( group, on ) );
            _placed[group.Group.Key] = _placed.GetValueOrDefault( group.Group.Key ) + 1;
        }

        #endregion

        #region Answers

        /// <summary>
        /// Answers the registration attributes and each registrant's attributes that have a fill rule.
        /// </summary>
        private void Answer( List<PlannedRegistration> registrations, PlannedRegistrationInstance instance, IntRange grades )
        {
            var everyone = registrations.SelectMany( r => r.Registrants.Where( p => !p.OnWaitList ) ).OrderBy( p => p.Person.Cast.Guid ).ToList();
            foreach ( var registration in registrations )
            {
                var random = new SeededRandom( $"{_seed}:registrations:{instance.Guid}:answers:{registration.Guid}" );
                foreach ( var attribute in _config.RegistrationAttributes.Where( a => a.Fill != null ) )
                {
                    var value = Fill( attribute, registration, null, everyone, instance, random );
                    if ( value != null )
                    {
                        registration.Answers[attribute.Key] = value;
                    }
                }

                foreach ( var registrant in registration.Registrants )
                {
                    foreach ( var attribute in instance.Template.Attributes.Where( a => a.Fill != null ) )
                    {
                        var value = Fill( attribute, registration, registrant, everyone, instance, random );
                        if ( value != null )
                        {
                            registrant.Answers[attribute.Key] = value;
                        }
                    }
                }
            }
        }

        private string Fill( RegistrationAttributeDefinition attribute, PlannedRegistration registration, PlannedRegistrant registrant, List<PlannedRegistrant> everyone, PlannedRegistrationInstance instance, SeededRandom random )
        {
            var fill = attribute.Fill;
            switch ( fill.Kind )
            {
                case RegistrationAttributeFillKind.Choice:
                    var weights = attribute.Values.Select( ( v, i ) => (v, i) ).ToDictionary( x => x.v, x => x.i < fill.Weights.Count ? fill.Weights[x.i] : 0 );
                    return random.Pick( weights );

                case RegistrationAttributeFillKind.Friend:
                    if ( registrant == null || registrant.OnWaitList || !random.Chance( fill.Percent / 100 ) )
                    {
                        return null;
                    }

                    var grade = AnchorDates.GradeOn( registrant.Student.GraduationYear.Value, instance.EventDay );
                    var friends = everyone.Where( p => p.Person.FamilyId != registrant.Person.FamilyId
                        && Math.Abs( AnchorDates.GradeOn( p.Student.GraduationYear.Value, instance.EventDay ) - grade ) <= 1 ).ToList();
                    return friends.Count == 0 ? null : NameOf( random.Pick( friends ).Person );

                default:
                    if ( registrant == null )
                    {
                        return null;
                    }

                    var (name, phone) = EmergencyContact( registration, registrant );
                    return fill.Kind == RegistrationAttributeFillKind.ContactName ? name : phone;
            }
        }

        /*
            10/28/26 - CLAUDE

            Camp's emergency contact questions are required, so Rock's form never saves a registration without them.
            A student who registers themselves has no parent in Rock, and some registrars have no phone. Their
            answers name a parent who is not in Rock, drawn from the Crowd's name lists with the student's last name,
            and a phone in the reserved 555-0100 to 555-0199 range. The draw is seeded by the student, so the name
            and the phone answer agree.

            Reason: Every required answer is filled, as Rock's form requires.
        */

        /// <summary>
        /// The registrant's emergency contact name and phone: a parent or guardian in Rock when one has a phone,
        /// otherwise a parent who is not in Rock.
        /// </summary>
        private (string Name, string Phone) EmergencyContact( PlannedRegistration registration, PlannedRegistrant registrant )
        {
            var contact = Contact( registration, registrant );
            var phone = contact == null ? null : Phone( contact );
            if ( contact != null && phone != null )
            {
                return (NameOf( contact ), phone);
            }

            var random = new SeededRandom( $"{_seed}:emergency-contact:{registrant.Person.Cast.Guid}" );
            var first = random.Pick( random.Chance( 0.6 ) ? CrowdNames.FemaleFirstNames : CrowdNames.MaleFirstNames );
            var name = contact == null ? $"{first} {registrant.Person.Cast.LastName}" : NameOf( contact );
            return (name, $"({random.Pick( new[] { "602", "480", "623" } )}) 555-0{random.Between( 100, 199 )}");
        }

        /// <summary>
        /// The registrant's emergency contact in Rock: a parent or guardian other than the registrar when the family
        /// has one, otherwise the registrar. Null for a student who registered themselves, whose parents are not in Rock.
        /// </summary>
        private LoadedPerson Contact( PlannedRegistration registration, PlannedRegistrant registrant )
        {
            var family = _families[registrant.Person.FamilyId];
            var contact = family.Members
                .Where( m => m.FamilyId == family.FamilyId && m.IsAdult && m.CanAttend && m != registration.Registrar && m != registrant.Person && Phone( m ) != null )
                .OrderBy( m => m.Cast.Guid )
                .FirstOrDefault() ?? registration.Registrar;
            return contact == registrant.Person ? null : contact;
        }

        private static string NameOf( LoadedPerson person ) => $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}";

        /// <summary>
        /// The person's mobile phone, or home phone, as (602) 555-0127. Null when they have neither.
        /// </summary>
        private static string Phone( LoadedPerson person )
        {
            var digits = new string( ( person.Cast.Phones?.Mobile ?? person.Cast.Phones?.Home ?? string.Empty ).Where( char.IsDigit ).ToArray() );
            return digits.Length == 10 ? string.Format( CultureInfo.InvariantCulture, "({0}) {1}-{2}", digits.Substring( 0, 3 ), digits.Substring( 3, 3 ), digits.Substring( 6 ) ) : null;
        }

        #endregion

        private static T Pick<T>( IReadOnlyDictionary<T, double> weights, SeededRandom random )
        {
            var key = random.Pick( weights.ToDictionary( w => w.Key.ToString(), w => w.Value ) );
            return weights.Keys.First( k => k.ToString() == key );
        }

        private static DateTime Min( DateTime a, DateTime b ) => a < b ? a : b;

        private static DateTime Max( DateTime a, DateTime b ) => a > b ? a : b;

        /// <summary>
        /// A registration before it is dated: who is on it, and the Cast story that pins it.
        /// </summary>
        private sealed class Draft
        {
            public Draft( List<StepPerson> people, CastRegistration pin, LoadedHousehold pinHousehold, bool isLeader )
            {
                People = people;
                Pin = pin;
                PinHousehold = pinHousehold;
                IsLeader = isLeader;
            }

            public List<StepPerson> People { get; }

            public CastRegistration Pin { get; }

            public LoadedHousehold PinHousehold { get; }

            public bool IsLeader { get; }

            public DateTime When { get; set; }
        }
    }

    /// <summary>
    /// One planned registration: who registered whom, when, what it costs, and how it was paid.
    /// </summary>
    internal sealed class PlannedRegistration
    {
        public PlannedRegistrationInstance Instance { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// When the registrar registered.
        /// </summary>
        public DateTime When { get; set; }

        public LoadedPerson Registrar { get; set; }

        public List<PlannedRegistrant> Registrants { get; } = new List<PlannedRegistrant>();

        /// <summary>
        /// The Cast story that pins the registration, or null.
        /// </summary>
        public CastRegistration Pin { get; set; }

        /// <summary>
        /// True for a current Student Leader who registered with the leader code.
        /// </summary>
        public bool IsLeader { get; set; }

        public RegistrationDiscountDefinition Discount { get; set; }

        public RegistrationPayment? Payment { get; set; }

        /// <summary>
        /// Credit Card or ACH, for every payment and the plan.
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// Every payment made before the install day, in order.
        /// </summary>
        public List<PlannedPayment> Payments { get; } = new List<PlannedPayment>();

        public PlannedPaymentPlan Plan { get; set; }

        /// <summary>
        /// When Rock's payment reminder job last emailed the registrar, or null.
        /// </summary>
        public DateTime? LastPaymentReminder { get; set; }

        /// <summary>
        /// Registration attribute values by key.
        /// </summary>
        public Dictionary<string, string> Answers { get; } = new Dictionary<string, string>();

        /// <summary>
        /// The seeded stream for the registration's own choices.
        /// </summary>
        public SeededRandom Random { get; set; }

        /// <summary>
        /// What the registration costs after its discount, the way Rock's Registration.DiscountedCost adds it up.
        /// </summary>
        public decimal DiscountedCost => Registrants.Sum( r => r.DiscountedCost( Discount ) );

        public decimal Paid => Payments.Sum( p => p.Amount );
    }

    internal sealed class PlannedRegistrant
    {
        public LoadedPerson Person { get; set; }

        public StepPerson Student { get; set; }

        public Guid Guid { get; set; }

        public decimal Cost { get; set; }

        public bool DiscountApplies { get; set; }

        public bool OnWaitList { get; set; }

        public List<PlannedRegistrantFee> Fees { get; } = new List<PlannedRegistrantFee>();

        public List<PlannedPlacement> Placements { get; } = new List<PlannedPlacement>();

        /// <summary>
        /// Registrant attribute values by key.
        /// </summary>
        public Dictionary<string, string> Answers { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Rock's RegistrationRegistrant.DiscountedCost: nothing on the wait list; otherwise the cost and fees less the
        /// discount's percent where it applies, less its amount once, and never below zero.
        /// </summary>
        public decimal DiscountedCost( RegistrationDiscountDefinition discount )
        {
            if ( OnWaitList )
            {
                return 0;
            }

            var percent = ( discount?.Percent ?? 0 ) / 100;
            var cost = Cost - ( DiscountApplies ? Cost * percent : 0 );
            foreach ( var fee in Fees )
            {
                var total = fee.Quantity * fee.Cost;
                cost += fee.Fee.DiscountApplies && DiscountApplies ? total - ( total * percent ) : total;
            }

            cost -= DiscountApplies ? discount?.Amount ?? 0 : 0;
            return cost > 0 ? cost : 0;
        }
    }

    internal sealed record PlannedRegistrantFee( RegistrationFeeDefinition Fee, int ItemIndex, int Quantity, decimal Cost );

    internal sealed record PlannedPlacement( PlannedPlacementGroup Group, DateTime On );

    /// <summary>
    /// One payment: a website payment the registrar made, or one of the payment plan's.
    /// </summary>
    internal sealed record PlannedPayment( DateTime When, decimal Amount, bool IsScheduled );

    internal sealed class PlannedPaymentPlan
    {
        public PaymentPlanFrequency Frequency { get; set; }

        public DateTime Start { get; set; }

        /// <summary>
        /// Every payment date, the last on or before the payment deadline.
        /// </summary>
        public List<DateTime> Dates { get; set; }

        public decimal AmountPerPayment { get; set; }
    }
}
