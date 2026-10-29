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

        Kids, couples, adults, and volunteers register from the people and the small group and serving plans every
        load makes, never from another part's rows (SampleDataPart). Each takes an exact share by connection status,
        counted apart for the Cast and the generated households, so the Cast's numbers never depend on the Crowd.
        A Cast person less involved than Connected registers only through a Cast story: their story is often about
        their one touch, such as a diaper drive, and a registration the story does not name would contradict it.

        Reason: Registrations follow the Student Path's rules and the people's plans on any install day (decisions D-registrations-8, D-registrations-9, and D-registrations-11).
    */

    /// <summary>
    /// Chooses who registers for an instance, by the audience its registrants rule names, and groups them into
    /// registrations. Only the choosing differs between audiences; <see cref="RegistrantPlanner"/> dates, prices,
    /// pays, places, and answers every registration the same way. Reads no database.
    /// </summary>
    internal sealed class RegistrantChooser
    {
        /// <summary>
        /// The least involved connection status whose Cast people register without a Cast story.
        /// </summary>
        private const string CastShareStatus = "Connected";

        private readonly StepsConfiguration _steps;
        private readonly StudentPath _path;
        private readonly RegistrantPlans _plans;
        private readonly DateTime _anchor;
        private readonly string _seed;
        private readonly IReadOnlyDictionary<int, LoadedPerson> _people;
        private readonly IReadOnlyDictionary<int, LoadedHousehold> _families;
        private readonly Dictionary<int, StepPerson> _students;
        private readonly Dictionary<Guid, LoadedPerson> _byGuid;

        /// <param name="path">The Student Path, or null when the package has no steps, so no students register.</param>
        /// <param name="people">Every person by Id, in their primary family.</param>
        /// <param name="families">Every household by family Id.</param>
        public RegistrantChooser( StepsConfiguration steps, StudentPath path, RegistrantPlans plans, IReadOnlyDictionary<int, LoadedPerson> people, IReadOnlyDictionary<int, LoadedHousehold> families, DateTime anchor, string seed )
        {
            _steps = steps;
            _path = path;
            _plans = plans;
            _people = people;
            _families = families;
            _anchor = anchor.Date;
            _seed = seed;
            _students = path?.People.ToDictionary( p => p.PersonId ) ?? new Dictionary<int, StepPerson>();
            _byGuid = people.Values.ToDictionary( p => p.Cast.Guid );
        }

        /// <summary>
        /// The registrations of everyone who registers other than the Cast's pinned people, and whether the
        /// instance's event already happened in a plan that fixes exactly who went.
        /// </summary>
        public RegistrantChoice Choose( PlannedRegistrationInstance instance, ISet<int> pinned )
        {
            var eligibility = RegistrantEligibility.Of( instance.Template, instance.Rule.Registrants.Students?.Gender );
            return instance.Rule.Registrants.Audience switch
            {
                RegistrantAudience.Students => _path == null ? new RegistrantChoice( new List<RegistrationDraft>(), false ) : ChooseStudents( instance, pinned ),
                RegistrantAudience.Kids => new RegistrantChoice( Kids( instance, eligibility, pinned ), false ),
                RegistrantAudience.Couples => new RegistrantChoice( Couples( instance, eligibility, pinned ), false ),
                RegistrantAudience.Adults => new RegistrantChoice( Adults( instance, eligibility, pinned ), false ),
                _ => new RegistrantChoice( Volunteers( instance, eligibility, pinned ), false )
            };
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

        #region Kids, couples, adults, and volunteers

        /// <summary>
        /// A share of the families at the instance's campus with kids who may go, by the family's connection status.
        /// A parent registers the siblings together.
        /// </summary>
        private List<RegistrationDraft> Kids( PlannedRegistrationInstance instance, RegistrantEligibility eligibility, ISet<int> pinned )
        {
            var rule = instance.Rule.Registrants.Kids;
            var families = new List<(LoadedHousehold Household, List<LoadedPerson> Kids)>();
            foreach ( var household in Households( instance ) )
            {
                var kids = Members( household )
                    .Where( m => !m.IsAdult && eligibility.Fits( m, instance ) && ( rule.MaxGrade == null || !m.GraduationYear.HasValue || AnchorDates.GradeOn( m.GraduationYear.Value, instance.EventDay ) <= rule.MaxGrade ) )
                    .ToList();
                var isFamily = kids.Count > 0 && !( rule.MarriedParents && Couple( household ) == null );

                // A Cast story registers its own family's kids.
                if ( isFamily && !kids.Any( k => pinned.Contains( k.PersonId ) ) )
                {
                    families.Add( (household, kids) );
                }
            }

            var chosen = Share( instance, Stream( instance, "kids" ), families, f => f.Household, f => Status( Members( f.Household ).Where( m => m.IsAdult ) ), rule.Statuses );
            return Families( chosen.SelectMany( f => f.Kids ), instance.Template ).ToList();
        }

        /// <summary>
        /// A share of the married couples at the instance's campus, by the more involved spouse's connection status.
        /// Both register on one registration, made by the first.
        /// </summary>
        private List<RegistrationDraft> Couples( PlannedRegistrationInstance instance, RegistrantEligibility eligibility, ISet<int> pinned )
        {
            var couples = Households( instance )
                .Select( h => (Household: h, Couple: Couple( h )) )
                .Where( c => c.Couple != null && c.Couple.All( p => eligibility.Fits( p, instance ) && !pinned.Contains( p.PersonId ) ) )
                .ToList();
            var chosen = Share( instance, Stream( instance, "couples" ), couples, c => c.Household, c => Status( c.Couple ), instance.Rule.Registrants.Couples.Statuses );
            return chosen.Select( c => new RegistrationDraft( c.Couple, null, null, false ) { Registrar = c.Couple[0] } ).ToList();
        }

        /*
            10/28/26 - CLAUDE

            Friends who go to a retreat together usually come from one small group, so some active small groups send a
            few of their members on one registration before anyone else is chosen. Each friend is someone the adults
            rule would let register on their own. The rest come one at a time by connection status, and a share of the
            married ones bring their spouse on the same registration.

            Reason: The Women's Retreat shows clusters of friends from one group, and Money Matters shows couples (decision D-registrations-11).
        */

        /// <summary>
        /// Adults at the instance's campus in the rule's ages: friends from one small group together, then a share by
        /// connection status, some with their spouse. The first person on each registration makes it.
        /// </summary>
        private List<RegistrationDraft> Adults( PlannedRegistrationInstance instance, RegistrantEligibility eligibility, ISet<int> pinned )
        {
            var rule = instance.Rule.Registrants.Adults;
            var stream = Stream( instance, "adults" );
            bool IsCandidate( LoadedPerson p ) => p.IsAdult && !pinned.Contains( p.PersonId ) && eligibility.Fits( p, instance ) && IsInAges( p, rule.Ages, instance.EventDay );
            var candidates = Households( instance ).SelectMany( Members ).Where( IsCandidate ).ToList();
            var taken = new HashSet<int>();
            var drafts = new List<RegistrationDraft>();

            if ( rule.SmallGroupFriends is { } friends )
            {
                var bySmallGroup = candidates.Where( p => IsShareable( p ) ).ToDictionary( p => p.Cast.Guid );
                foreach ( var group in _plans.SmallGroups.Where( g => g.Lifecycle is ActiveLifecycle && AtCampus( instance, g.Campus == "main" ? _plans.MainCampusKey : g.Campus ) ).OrderBy( g => g.Guid ) )
                {
                    var members = group.Members.OfType<ActiveMember>()
                        .Select( m => bySmallGroup.TryGetValue( m.Guid, out var p ) ? p : null )
                        .Where( p => p != null && !taken.Contains( p.PersonId ) )
                        .OrderBy( p => p.Cast.Guid )
                        .ToList();
                    var random = new SeededRandom( $"{stream}:small-group:{group.Guid}" );
                    if ( members.Count < friends.Size[0] || !random.Chance( friends.GroupPercent * Scale( instance ) / 100 ) )
                    {
                        continue;
                    }

                    var size = random.Between( friends.Size[0], Math.Min( Math.Min( friends.Size[1], members.Count ), instance.Template.MaxRegistrants ) );
                    var cluster = members.OrderBy( p => new SeededRandom( $"{stream}:small-group:{group.Guid}:{p.Cast.Guid}" ).Between( 0.0, 1.0 ) ).Take( size ).ToList();
                    taken.UnionWith( cluster.Select( p => p.PersonId ) );
                    drafts.Add( new RegistrationDraft( cluster, null, null, false ) { Registrar = cluster[0], Stream = cluster[0].Cast.Guid } );
                }
            }

            var rest = candidates.Where( p => !taken.Contains( p.PersonId ) ).ToList();
            var chosen = Share( instance, stream, rest, p => _families[p.FamilyId], p => Status( new[] { p } ), rule.Statuses ).ToHashSet();
            foreach ( var person in rest.Where( chosen.Contains ) )
            {
                if ( taken.Contains( person.PersonId ) )
                {
                    continue;
                }

                // A spouse who was chosen too always comes on the same registration; otherwise, a share of spouses do.
                var spouse = Couple( _families[person.FamilyId] )?.FirstOrDefault( p => p != person );
                var isSpouseComing = spouse != null && !taken.Contains( spouse.PersonId ) && !pinned.Contains( spouse.PersonId ) && eligibility.Fits( spouse, instance )
                    && ( chosen.Contains( spouse ) || new SeededRandom( $"{stream}:spouse:{person.Cast.Guid}" ).Chance( rule.CouplePercent / 100 ) );
                var people = isSpouseComing ? new[] { person, spouse }.OrderBy( p => p.Cast.Guid ).ToList() : new List<LoadedPerson> { person };
                taken.UnionWith( people.Select( p => p.PersonId ) );
                drafts.Add( new RegistrationDraft( people, null, null, false ) { Registrar = people[0], Stream = people[0].Cast.Guid } );
            }

            return drafts;
        }

        /// <summary>
        /// Volunteers at the instance's campus: shares of the people serving on a team and of the small group members
        /// on the event day, and a share of other adults by connection status. Teens in a volunteer's family sometimes
        /// come along. A family signs up together, with an adult first.
        /// </summary>
        private List<RegistrationDraft> Volunteers( PlannedRegistrationInstance instance, RegistrantEligibility eligibility, ISet<int> pinned )
        {
            var rule = instance.Rule.Registrants.Volunteers;
            var stream = Stream( instance, "volunteers" );
            var day = instance.EventDay;
            var atCampus = Households( instance ).SelectMany( Members )
                .Where( p => !pinned.Contains( p.PersonId ) && eligibility.Fits( p, instance ) && ( p.IsAdult || IsInAges( p, rule.TeenAges, day ) ) )
                .ToDictionary( p => p.Cast.Guid );

            var servers = _plans.Serving.Where( t => t.Campus == instance.CampusKey || instance.CampusKey == null )
                .SelectMany( t => t.Members.Where( m => m.Joined.Date <= day ).Select( m => m.Person ) )
                .Distinct().Where( atCampus.ContainsKey ).Select( g => atCampus[g] ).OrderBy( p => p.Cast.Guid ).ToList();
            var groupMembers = _plans.SmallGroups.Where( g => AtCampus( instance, g.Campus == "main" ? _plans.MainCampusKey : g.Campus ) && IsMeeting( g, day ) )
                .SelectMany( g => g.Members.Where( m => IsMember( m, day ) ).Select( m => m.Guid ) )
                .Distinct().Where( atCampus.ContainsKey ).Select( g => atCampus[g] ).OrderBy( p => p.Cast.Guid ).ToList();
            var others = atCampus.Values.Where( p => p.IsAdult ).OrderBy( p => p.Cast.Guid ).ToList();

            var chosen = Share( instance, $"{stream}:servers", servers, p => _families[p.FamilyId], _ => "serving", new Dictionary<string, double> { ["serving"] = rule.Servers } )
                .Union( Share( instance, $"{stream}:small-groups", groupMembers, p => _families[p.FamilyId], _ => "small-group", new Dictionary<string, double> { ["small-group"] = rule.GroupMembers } ) )
                .Union( Share( instance, $"{stream}:others", others, p => _families[p.FamilyId], p => Status( new[] { p } ), rule.Statuses ) )
                .ToHashSet();

            // Teens come along with a parent who signed up.
            foreach ( var family in chosen.Where( p => p.IsAdult ).Select( p => p.FamilyId ).Distinct().ToList() )
            {
                foreach ( var teen in atCampus.Values.Where( p => p.FamilyId == family && !p.IsAdult && !chosen.Contains( p ) ).OrderBy( p => p.Cast.Guid ).ToList() )
                {
                    if ( new SeededRandom( $"{stream}:teen:{teen.Cast.Guid}" ).Chance( rule.TeenPercent / 100 ) )
                    {
                        chosen.Add( teen );
                    }
                }
            }

            var drafts = new List<RegistrationDraft>();
            foreach ( var family in chosen.GroupBy( p => p.FamilyId ).OrderBy( f => _families[f.Key].Cast.Guid ) )
            {
                var members = family.OrderBy( p => !p.IsAdult ).ThenBy( p => p.Cast.Guid ).ToList();
                for ( var i = 0; i < members.Count; i += instance.Template.MaxRegistrants )
                {
                    var people = members.Skip( i ).Take( instance.Template.MaxRegistrants ).ToList();
                    drafts.Add( new RegistrationDraft( people, null, null, false ) { Registrar = people[0] } );
                }
            }

            return drafts;
        }

        /// <summary>
        /// The share of the units by each one's connection status, scaled for the dataset and rounded, counted apart
        /// for the Cast and the generated households. A Cast unit less involved than Connected is never chosen. The
        /// rest are drawn by a seeded number per unit. Returns the chosen units in their given order.
        /// </summary>
        private List<T> Share<T>( PlannedRegistrationInstance instance, string stream, List<T> units, Func<T, LoadedHousehold> householdOf, Func<T, string> statusOf, IReadOnlyDictionary<string, double> percents )
        {
            var scale = Scale( instance );
            var picked = new HashSet<int>();
            var indexed = units.Select( ( u, i ) => (Unit: u, Index: i, Household: householdOf( u ), Status: statusOf( u )) ).ToList();
            foreach ( var group in indexed.GroupBy( u => (IsCast: _plans.CastHouseholdKeys.Contains( u.Household.Cast.Key ), u.Status) ) )
            {
                var percent = ( percents.TryGetValue( group.Key.Status ?? string.Empty, out var p ) ? p : 0 ) * scale;
                if ( group.Key.IsCast && Rank( group.Key.Status ) < Rank( CastShareStatus ) && group.Key.Status is not "serving" and not "small-group" )
                {
                    percent = 0;
                }

                var members = group.ToList();
                var target = ( int ) Math.Round( members.Count * percent / 100, MidpointRounding.AwayFromZero );
                picked.UnionWith( members
                    .OrderBy( u => new SeededRandom( $"{stream}:share:{u.Household.Cast.Guid}:{u.Index}" ).Between( 0.0, 1.0 ) ).ThenBy( u => u.Index )
                    .Take( target )
                    .Select( u => u.Index ) );
            }

            return indexed.Where( u => picked.Contains( u.Index ) ).Select( u => u.Unit ).ToList();
        }

        private double Scale( PlannedRegistrationInstance instance ) => instance.Rule.Registrants.Scale.TryGetValue( _plans.Dataset, out var scale ) ? scale : 1;

        /// <summary>
        /// True when a Cast person may be chosen without a Cast story: Connected or more involved. Anyone generated may.
        /// </summary>
        private bool IsShareable( LoadedPerson person ) => !_plans.CastHouseholdKeys.Contains( _families[person.FamilyId].Cast.Key ) || Rank( person.Cast.ConnectionStatus ) >= Rank( CastShareStatus );

        /// <summary>
        /// The most involved connection status among the people, or the family's when they have none.
        /// </summary>
        private string Status( IEnumerable<LoadedPerson> people ) => people.Select( p => p.Cast.ConnectionStatus ).OrderByDescending( Rank ).FirstOrDefault();

        private int Rank( string status ) => _plans.StatusOrder.IndexOf( status ?? string.Empty );

        /// <summary>
        /// The households the instance is for: every family at its campus, or at every campus for a church-wide one.
        /// </summary>
        private IEnumerable<LoadedHousehold> Households( PlannedRegistrationInstance instance ) => _families.Values
            .Where( h => h.Cast.Kind != "business" && AtCampus( instance, h.CampusKey ) )
            .OrderBy( h => h.Cast.Guid );

        private static bool AtCampus( PlannedRegistrationInstance instance, string campusKey ) => instance.CampusKey == null || instance.CampusKey == campusKey;

        /// <summary>
        /// The household's own members who can attend, in Guid order.
        /// </summary>
        private static IEnumerable<LoadedPerson> Members( LoadedHousehold household ) => household.Members
            .Where( m => m.FamilyId == household.FamilyId && m.CanAttend && m.Cast.RecordType != "business" )
            .OrderBy( m => m.Cast.Guid );

        /// <summary>
        /// The household's married couple, in Guid order, or null when it has none.
        /// </summary>
        private static List<LoadedPerson> Couple( LoadedHousehold household )
        {
            var couple = Members( household ).Where( m => m.IsAdult && string.Equals( m.Cast.MaritalStatus, "married", StringComparison.OrdinalIgnoreCase ) ).ToList();
            return couple.Count == 2 ? couple : null;
        }

        private static bool IsInAges( LoadedPerson person, List<int> ages, DateTime day ) => ages == null
            || person.BirthDate.HasValue && RegistrantEligibility.AgeOn( person.BirthDate.Value, day ) is var age && age >= ages[0] && age <= ages[1];

        /// <summary>
        /// True when the small group had started and had not ended on the day.
        /// </summary>
        private bool IsMeeting( SmallGroupPlan group, DateTime day )
        {
            var started = _plans.Dates.Resolve( group.Lifecycle.Started );
            var ended = group.Lifecycle switch
            {
                InactiveLifecycle inactive => _plans.Dates.Resolve( inactive.Ended ),
                ArchivedLifecycle archived => _plans.Dates.Resolve( archived.Ended ),
                _ => null
            };
            return started <= day && ( ended == null || ended > day );
        }

        /// <summary>
        /// True when the person was an active member of the small group on the day.
        /// </summary>
        private bool IsMember( PlannedMember member, DateTime day )
        {
            var left = member switch
            {
                InactiveMember inactive => _plans.Dates.Resolve( inactive.Left ),
                ArchivedMember archived => _plans.Dates.Resolve( archived.Left ),
                PendingMember _ => DateTime.MinValue,
                _ => null
            };
            return _plans.Dates.Resolve( member.Joined ) <= day && ( left == null || left > day );
        }

        private string Stream( PlannedRegistrationInstance instance, string audience ) => $"{_seed}:registrations:{instance.Rule.Key}:{instance.CampusKey}:{instance.EventDay:yyyy-MM-dd}:{audience}";

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
    /// What besides the Student Path says who registers: the dataset, the small group and serving plans, the
    /// connection statuses in order of involvement, and which households are the Cast's.
    /// </summary>
    internal sealed record RegistrantPlans( string Dataset, IReadOnlyList<SmallGroupPlan> SmallGroups, IReadOnlyList<ServingTeamPlan> Serving, List<string> StatusOrder, ISet<string> CastHouseholdKeys, string MainCampusKey, AnchorDates Dates );

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

        /// <summary>
        /// Who registers them, or null for a parent or guardian in the family.
        /// </summary>
        public LoadedPerson Registrar { get; init; }

        /// <summary>
        /// The Guid whose seeded stream dates the registration, or null for the family's, so siblings in one family
        /// register the same day.
        /// </summary>
        public Guid? Stream { get; init; }

        public DateTime When { get; set; }
    }

    /// <summary>
    /// Who Rock lets register for a template, in the terms of its eligibility settings, plus the gender an instance
    /// is for. Rock checks a registrant on the day they register (RegistrantEligibilityEvaluator), and the event is
    /// for the same people, so a registrant meets every rule on both days.
    /// </summary>
    internal sealed class RegistrantEligibility
    {
        private RegistrantEligibility( IntRange grades, IntRange ages, RegistrationGender? gender )
        {
            Grades = grades;
            Ages = ages;
            Gender = gender;
        }

        /// <param name="instanceGender">The gender an instance is for, such as camp's girls' instance, or null for the template's.</param>
        public static RegistrantEligibility Of( RegistrationTemplateDefinition template, RegistrationGender? instanceGender )
        {
            var grades = template.Eligibility?.Grades is { Count: 2 } g ? new IntRange { Min = g[0], Max = g[1] } : null;
            var ages = template.Eligibility?.Ages is { Count: 2 } a ? new IntRange { Min = ( int ) a[0], Max = ( int ) a[1] } : null;
            return new RegistrantEligibility( grades, ages, instanceGender ?? template.Eligibility?.Gender );
        }

        /// <summary>
        /// The template's lowest and highest grade, or null when it has no grade rule.
        /// </summary>
        public IntRange Grades { get; }

        /// <summary>
        /// The template's youngest and oldest age in whole years, or null when it has no age rule.
        /// </summary>
        public IntRange Ages { get; }

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

            if ( Ages != null )
            {
                if ( !person.BirthDate.HasValue )
                {
                    return false;
                }

                var age = AgeOn( person.BirthDate.Value, day );
                if ( age < Ages.Min || age > Ages.Max )
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The first and last day in a window when the person meets every rule, or null. Grades change only on June 1,
        /// and ages on birthdays.
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

                var birthday = person.BirthDate?.AddYears( year - person.BirthDate.Value.Year );
                if ( Ages != null && birthday > from && birthday <= until )
                {
                    starts.Add( birthday.Value );
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

        /// <summary>
        /// The person's age in whole years on the day, as Rock's Person.Age counts it.
        /// </summary>
        public static int AgeOn( DateTime birthDate, DateTime day )
        {
            var age = day.Year - birthDate.Year;
            return birthDate.Date > day.Date.AddYears( -age ) ? age - 1 : age;
        }

        public static RegistrationGender? GenderOf( LoadedPerson person ) => person.Cast.Gender switch
        {
            "female" => RegistrationGender.Female,
            "male" => RegistrationGender.Male,
            _ => null
        };
    }
}
