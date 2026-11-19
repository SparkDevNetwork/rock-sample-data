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

using System.Text.RegularExpressions;

using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        10/16/26 - CLAUDE

        One planner turns households into small group plans, for the Cast (written to
        cast/groups by --plan-cast-groups) and for generated households (at load time).
        Every rule and number comes from configuration/small-groups.json.

        The Cast must plan the same way on every run, even though the plan sets each new
        leader's status to Leading. So nothing here depends on whether a leader is Engaged
        or Leading beyond being allowed to lead: join rolls and leader order come from
        per-person seeded streams, and a higher join percent only keeps a joined unit joined.

        Reason: A second --plan-cast-groups run must change nothing.
    */

    /*
        11/19/26 - CLAUDE

        Three rules shape every group plan (issues 35, 37, and 41).

        1. A topic with a share is planned on purpose: about one in six of a campus's groups study the Women's
           Bible Study with a woman leader and only women, and one in ten the Men's Bible Study. A married
           person joins such a group alone, so a wife can be in a women's group while her husband is in a
           men's group, a mixed group, or none.
        2. A household joins no group until four weeks after it started coming, and a household that stopped
           coming leaves its groups two to six weeks after, as an inactive member.
        3. A third of a campus's active groups started on one of its last four launch Sundays, and two thirds
           of those are led by someone who was Ready to Lead six weeks before: in another group (which half of
           them stay in), on a serving team, with an Equip step, and not leading. The plan marks those groups,
           and the serving and step plans make the team and the step true.

        Reason: The data shows women leading, every membership fits the household's story, and the Lead a
        Group flow converts (decisions D-womens-groups-1, D-group-fixes-1, and D-launch-groups-1).
    */

    /// <summary>
    /// Plans the small groups for one campus's households.
    /// </summary>
    internal sealed class SmallGroupPlanner
    {
        /// <summary>
        /// Children this age or younger make a family want child care.
        /// </summary>
        private const int YoungChildMaxAge = 12;

        /// <summary>
        /// How long a Ready to Lead leader had been coming before their launch, so their serving and steps fit before it.
        /// </summary>
        private const int ReadyToLeadSettledDays = 365;

        private readonly SmallGroupRules _rules;
        private readonly PlanningClock _clock;
        private readonly GroupLaunchCalendar _launches;
        private readonly string _seed;

        public SmallGroupPlanner( SmallGroupRules rules, PlanningClock clock, GroupLaunchCalendar launches, string seed )
        {
            _rules = rules;
            _clock = clock;
            _launches = launches;
            _seed = seed;
        }

        /// <summary>
        /// Plans every active and former small group for one campus.
        /// </summary>
        public List<SmallGroupPlan> PlanCampus( CampusPlanRequest request )
        {
            var context = new CampusContext( this, request );
            return context.Plan();
        }

        /// <summary>
        /// The working state for one campus. It lives only for one <see cref="PlanCampus"/> call.
        /// </summary>
        private sealed class CampusContext
        {
            private readonly SmallGroupPlanner _planner;
            private readonly CampusPlanRequest _request;
            private readonly SmallGroupRules _rules;
            private readonly PlanningClock _clock;
            private readonly List<Unit> _current = new List<Unit>();

            // Current units with any chance to join. Only they leave, start, or ask to join groups, so
            // group history never makes an otherwise inactive household look active.
            private readonly List<Unit> _couldJoin = new List<Unit>();
            private readonly List<Unit> _formerOnly = new List<Unit>();

            // Units that joined but whose household stopped coming: they were in a group and left it.
            private readonly List<Unit> _lapsed = new List<Unit>();
            private readonly Dictionary<Guid, Unit> _unitByPerson = new Dictionary<Guid, Unit>();
            private readonly Dictionary<Guid, Unit> _alone = new Dictionary<Guid, Unit>();
            private readonly Dictionary<Guid, DateTime> _adultSince = new Dictionary<Guid, DateTime>();
            private readonly Dictionary<Guid, List<(DateTime From, DateTime To)>> _busy = new Dictionary<Guid, List<(DateTime From, DateTime To)>>();
            private readonly HashSet<Guid> _pending = new HashSet<Guid>();
            private readonly List<string> _leaderStatuses;

            public CampusContext( SmallGroupPlanner planner, CampusPlanRequest request )
            {
                _planner = planner;
                _request = request;
                _rules = planner._rules;
                _clock = planner._clock;
                _leaderStatuses = new List<string>( _rules.Leaders.Statuses );
            }

            private SeededRandom Stream( string purpose ) => new SeededRandom( $"{_planner._seed}:small-groups:{purpose}:{_request.Campus}" );

            /// <summary>
            /// The legacy groups that are still meeting. A pin with an ended state is one of the campus's ended test groups.
            /// </summary>
            private IEnumerable<LegacyGroup> ActiveLegacy => _request.Legacy.Where( l => l.Ended == null );

            /// <summary>
            /// Runs every planning step in order and builds the plans.
            /// </summary>
            public List<SmallGroupPlan> Plan()
            {
                BuildUnits();
                var joiners = PickJoiners();
                var active = CreateActiveDrafts( joiners );
                AssignTestCases( active );
                ApportionTraits( active, "active" );
                AssignUnits( active, joiners );
                foreach ( var draft in active )
                {
                    PlanActiveMembers( draft );
                }

                foreach ( var draft in active.Where( d => d.ReadyToLead ) )
                {
                    PlanReadyToLeadHistory( draft, active );
                }

                PlanLapsedMembers( active );
                ChooseTopics( active, "active" );
                foreach ( var draft in active )
                {
                    PlanLeavers( draft );
                }

                // Former groups come last, so each fits before its people's later memberships.
                var former = CreateFormerDrafts( active );
                ApportionTraits( former, "former" );
                foreach ( var draft in former )
                {
                    PlanFormerGroup( draft );
                }

                ChooseTopics( former, "former" );

                var all = active.Concat( former ).ToList();

                // A household a pin gives a request is never picked for another group's request.
                foreach ( var person in ActiveLegacy.SelectMany( l => l.PendingHouseholds ).SelectMany( key => _current.Where( u => u.Household.Key == key ) ).SelectMany( u => u.People ) )
                {
                    _pending.Add( person.Guid );
                }

                foreach ( var draft in active.Where( d => d.TestCase == SmallGroupTestCase.Pending || d.Legacy?.PendingHouseholds.Count > 0 ) )
                {
                    PlanPendingRequests( draft );
                }

                foreach ( var draft in all )
                {
                    ChooseHost( draft );
                }

                NameGroups( all );
                return all.Select( BuildPlan ).ToList();
            }

            #region Units

            /// <summary>
            /// Groups each household's eligible adults into units that join together: a married
            /// couple, or a single adult. Inactive records form units that may only be former members.
            /// Each unit carries its household's attendance window.
            /// </summary>
            private void BuildUnits()
            {
                var window = _rules.Membership.AttendanceWindow ?? new SmallGroupAttendanceWindow { LeaveAfterEndDays = new IntRange() };
                foreach ( var household in _request.Households )
                {
                    if ( household.DuplicateOf != null || household.Kind == "business" )
                    {
                        continue;
                    }

                    var hasYoungKids = household.Members.Any( p => p.FamilyRole == "child" && p.Birth != null && p.Birth.Age <= YoungChildMaxAge );
                    var hasChildren = household.Members.Any( p => p.FamilyRole == "child" && !p.IsDeceased && p.Birth != null && p.Birth.Age < 18 );
                    var started = _clock.Dates.Resolve( household.Attendance?.Started );
                    var ended = _clock.Dates.Resolve( household.Attendance?.Ended );
                    var leaveRandom = new SeededRandom( $"{_planner._seed}:small-groups:leave:{household.Guid}" );
                    DateTime? leaveBy = ended?.AddDays( leaveRandom.Between( window.LeaveAfterEndDays.Min, window.LeaveAfterEndDays.Max ) );
                    var adults = household.Members.Where( IsEligibleAdult ).ToList();
                    foreach ( var isInactive in new[] { false, true } )
                    {
                        var people = adults.Where( p => ( p.RecordStatus == "inactive" ) == isInactive ).ToList();
                        var married = people.Where( p => string.Equals( p.MaritalStatus, "married", StringComparison.OrdinalIgnoreCase ) && p.FamilyRole == "adult" ).Take( 2 ).ToList();
                        var units = new List<List<CastPerson>>();
                        if ( married.Count == 2 )
                        {
                            units.Add( married );
                        }

                        units.AddRange( people.Except( married.Count == 2 ? married : new List<CastPerson>() ).Select( p => new List<CastPerson> { p } ) );

                        foreach ( var unitPeople in units )
                        {
                            var adultSince = unitPeople.Max( p => _adultSince[p.Guid] );
                            var unit = new Unit
                            {
                                Household = household,
                                People = unitPeople,
                                HasYoungKids = hasYoungKids,
                                HasChildren = hasChildren,
                                HasHome = household.Address?.Type == "home",
                                JoinFrom = started.HasValue && started.Value.AddDays( window.JoinAfterStartDays ) > adultSince ? started.Value.AddDays( window.JoinAfterStartDays ) : adultSince,
                                LeaveBy = leaveBy
                            };
                            ( isInactive ? _formerOnly : _current ).Add( unit );
                            unitPeople.ForEach( p => _unitByPerson[p.Guid] = unit );
                        }
                    }
                }
            }

            /// <summary>
            /// Adults who may be in a small group: old enough on the latest planned date, alive, a person (not a business).
            /// </summary>
            private bool IsEligibleAdult( CastPerson person )
            {
                if ( person.Birth == null || person.IsDeceased || person.RecordType == "business" || person.Birth.Age < _rules.Members.MinimumAge )
                {
                    return false;
                }

                var adultSince = _clock.Dates.AdultSince( person, _rules.Members.MinimumAge );
                _adultSince[person.Guid] = adultSince;
                return adultSince <= _clock.LatestChange;
            }

            /// <summary>
            /// The person on their own, for a group of one gender: their own unit when they are single, otherwise a
            /// unit of one with their household's window.
            /// </summary>
            private Unit Alone( CastPerson person )
            {
                var unit = _unitByPerson[person.Guid];
                if ( unit.People.Count == 1 )
                {
                    return unit;
                }

                if ( !_alone.TryGetValue( person.Guid, out var alone ) )
                {
                    alone = new Unit
                    {
                        Household = unit.Household,
                        People = new List<CastPerson> { person },
                        HasYoungKids = unit.HasYoungKids,
                        HasChildren = unit.HasChildren,
                        HasHome = unit.HasHome,
                        JoinFrom = unit.JoinFrom,
                        LeaveBy = unit.LeaveBy
                    };
                    _alone[person.Guid] = alone;
                }

                return alone;
            }

            /// <summary>
            /// The unit that may join the draft: the unit itself for a mixed group, or its one person of the group's
            /// gender for a group of one gender, or null when no one in it fits.
            /// </summary>
            private Unit FittingUnit( Draft draft, Unit unit )
            {
                if ( draft.Gender == null )
                {
                    return unit;
                }

                var person = unit.People.FirstOrDefault( p => p.Gender == draft.Gender );
                return person == null ? null : Alone( person );
            }

            /// <summary>
            /// Whether the household stopped coming long enough ago that its people have left their groups.
            /// </summary>
            private bool HasLapsed( Unit unit ) => unit.LeaveBy.HasValue && unit.LeaveBy.Value <= _clock.Anchor;

            /// <summary>
            /// The units in an active group now: pinned leaders and hosts always, others by their join chance.
            /// A unit whose household stopped coming was in a group and left it, and a unit that started coming too
            /// recently can only ask to join.
            /// </summary>
            private List<Unit> PickJoiners()
            {
                var forced = new HashSet<Unit>();
                foreach ( var legacy in ActiveLegacy )
                {
                    if ( legacy.LeaderGuid.HasValue )
                    {
                        forced.Add( _unitByPerson.GetValueOrDefault( legacy.LeaderGuid.Value ) ?? throw new InvalidOperationException( $"Pinned leader of {legacy.Key} is not an eligible adult." ) );
                    }

                    if ( legacy.HostHousehold != null )
                    {
                        forced.Add( _current.FirstOrDefault( u => u.Household.Key == legacy.HostHousehold && u.HasHome ) ?? throw new InvalidOperationException( $"Pinned host household of {legacy.Key} has no eligible adults at home." ) );
                    }

                    foreach ( var key in legacy.MemberHouseholds )
                    {
                        var units = _current.Where( u => u.Household.Key == key ).ToList();
                        units.ForEach( u => forced.Add( u ) );
                        if ( units.Count == 0 )
                        {
                            throw new InvalidOperationException( $"Pinned member household {key} of {legacy.Key} has no eligible adults." );
                        }
                    }
                }

                foreach ( var unit in forced.Where( u => HasLapsed( u ) || u.JoinFrom > _clock.LatestChange ) )
                {
                    throw new InvalidOperationException( $"A pinned leader or host household ({unit.Household.Key}) is outside its attendance window." );
                }

                var joiners = new List<Unit>();
                var waiting = new List<(Unit Unit, double Closeness)>();
                foreach ( var household in _current.GroupBy( u => u.Household ) )
                {
                    var random = new SeededRandom( $"{_planner._seed}:small-groups:join:{household.Key.Guid}" );
                    foreach ( var unit in household )
                    {
                        var roll = random.Between( 0.0, 100.0 );
                        var percent = _request.JoinPercent( unit.Household, unit.People );
                        if ( percent > 0 )
                        {
                            _couldJoin.Add( unit );
                        }

                        var joins = forced.Contains( unit ) || roll < percent;
                        if ( joins && HasLapsed( unit ) )
                        {
                            _lapsed.Add( unit );
                        }
                        else if ( HasLapsed( unit ) || unit.JoinFrom > _clock.LatestChange )
                        {
                            continue;
                        }
                        else if ( joins )
                        {
                            joiners.Add( unit );
                        }
                        else if ( percent > 0 )
                        {
                            waiting.Add( (unit, roll / percent) );
                        }
                    }
                }

                WidenLeaderStatuses();
                TopUpJoiners( joiners, waiting.OrderBy( w => w.Closeness ).Select( w => w.Unit ).ToList() );
                return joiners;
            }

            /*
                10/16/26 - CLAUDE

                A small campus (one of Core 3's companion campuses) can be too small to fill the groups it needs:
                one ordinary group beside each active test case, plus its inactive and archived test
                groups. It may have too few men with a leader status, and too few joiners. So it first
                takes leaders from the fallback statuses, in order, until it has enough. Then units that
                came closest to joining are added, first enough possible leaders, then enough people for
                groups of the minimum size. A campus that already has enough is left exactly as it rolled.

                Reason: Every campus gets its Group Finder test groups, however few households it has.
            */

            /// <summary>
            /// Lets people with the fallback statuses lead, one status at a time, while the campus has too few possible leaders.
            /// </summary>
            private void WidenLeaderStatuses()
            {
                var activePool = _couldJoin.Where( u => !HasLapsed( u ) && u.JoinFrom <= _clock.LatestChange ).SelectMany( u => u.People ).ToList();
                var formerPool = _formerOnly.SelectMany( u => u.People ).ToList();
                var formerNeeded = _request.TestCases.Inactive + _request.TestCases.Archived;
                foreach ( var status in _rules.Leaders.FallbackStatuses )
                {
                    var activeLeaders = activePool.Count( CanLead );
                    var hasEnough = activeLeaders >= MinimumActiveGroups && activeLeaders + formerPool.Count( CanLead ) >= MinimumActiveGroups + formerNeeded;
                    if ( hasEnough )
                    {
                        return;
                    }

                    _leaderStatuses.Add( status );
                }
            }

            /// <summary>
            /// Adds waiting units, closest to joining first, until the campus can fill its minimum active groups.
            /// </summary>
            private void TopUpJoiners( List<Unit> joiners, List<Unit> waiting )
            {
                var needed = MinimumActiveGroups;
                var peopleNeeded = needed * _rules.Size.Min;
                var leaders = joiners.Sum( u => u.People.Count( CanLead ) );
                var people = joiners.Sum( u => u.People.Count );

                foreach ( var unit in waiting.Where( u => u.People.Any( CanLead ) ).ToList() )
                {
                    if ( leaders >= needed )
                    {
                        break;
                    }

                    joiners.Add( unit );
                    waiting.Remove( unit );
                    leaders += unit.People.Count( CanLead );
                    people += unit.People.Count;
                }

                foreach ( var unit in waiting )
                {
                    if ( people >= peopleNeeded )
                    {
                        break;
                    }

                    joiners.Add( unit );
                    people += unit.People.Count;
                }
            }

            /// <summary>
            /// The fewest active groups the campus plans: one ordinary group beside its active test cases, and every legacy group.
            /// </summary>
            private int MinimumActiveGroups => Math.Max( _request.TestCases.ActiveCount > 0 ? _request.TestCases.ActiveCount + 1 : 0, ActiveLegacy.Count() );

            #endregion Units

            #region Leaders and drafts

            /// <summary>
            /// Plans the campus's active groups and picks one leader for each, legacy groups first. A share of the groups
            /// study a topic of one gender, and a share started on a recent launch Sunday, some of them led by someone
            /// who was Ready to Lead.
            /// </summary>
            private List<Draft> CreateActiveDrafts( List<Unit> joiners )
            {
                var size = _rules.Size;
                var people = joiners.Sum( u => u.People.Count );
                var candidates = LeaderCandidates( joiners.SelectMany( u => u.People ) );

                var count = Math.Max( ( int ) Math.Round( ( double ) people / size.Target ), MinimumActiveGroups );
                count = Math.Min( count, people / size.Min );
                count = Math.Min( count, candidates.Count );

                var drafts = new List<Draft>();
                var usedPeople = new HashSet<Guid>();
                var names = new FullNames( _request.TakenLeaderNames );
                foreach ( var legacy in ActiveLegacy.Where( l => l.LeaderGuid.HasValue ) )
                {
                    var leader = candidates.FirstOrDefault( c => c.Guid == legacy.LeaderGuid ) ?? throw new InvalidOperationException( $"Pinned leader of {legacy.Key} cannot lead a group." );
                    var topic = legacy.Topic == null ? null : _rules.Topics.FirstOrDefault( t => t.Value == legacy.Topic ) ?? throw new InvalidOperationException( $"The pin for {legacy.Key} names topic {legacy.Topic}, which configuration/small-groups.json does not have." );
                    var draft = new Draft { Legacy = legacy, Leader = leader, Topic = topic, Gender = topic?.LeaderGender };
                    if ( draft.Gender != null && leader.Gender != draft.Gender )
                    {
                        throw new InvalidOperationException( $"Pinned leader of {legacy.Key} is not {draft.Gender}, as its topic needs." );
                    }

                    // A pinned leader may also be in their spouse's group (the Cast's one exception to one group a person).
                    draft.LeaderUnit = draft.Gender == null ? _unitByPerson[leader.Guid] : Alone( leader );
                    draft.LeaderUnit.People.ForEach( p => usedPeople.Add( p.Guid ) );
                    names.Add( leader );
                    drafts.Add( draft );

                    if ( legacy.LaunchesAgo is int launchesAgo )
                    {
                        draft.Launch = _planner._launches.Recent( _clock.LatestChange, launchesAgo ).ElementAtOrDefault( launchesAgo - 1 );
                        draft.ReadyToLead = legacy.ReadyToLead;
                        var settled = draft.LeaderUnit.JoinFrom <= draft.Launch?.AddDays( legacy.ReadyToLead ? -ReadyToLeadSettledDays : 0 );
                        if ( draft.Launch == default( DateTime ) || !settled || ( legacy.ReadyToLead && !_request.ReadyToLeadCandidate( leader ) ) )
                        {
                            throw new InvalidOperationException( $"The pin for {legacy.Key} launches it from Ready to Lead, but its leader cannot launch it then." );
                        }
                    }
                }

                foreach ( var member in ActiveLegacy.SelectMany( l => l.MemberHouseholds ).SelectMany( key => _current.Where( u => u.Household.Key == key ) ) )
                {
                    member.People.ForEach( p => usedPeople.Add( p.Guid ) );
                }

                // The pinned leader of an ended group leads no active group.
                foreach ( var leader in _request.Legacy.Where( l => l.Ended != null && l.LeaderGuid.HasValue ) )
                {
                    usedPeople.Add( leader.LeaderGuid.Value );
                }

                foreach ( var host in ActiveLegacy.Where( l => l.HostHousehold != null ).Select( l => _current.First( u => u.Household.Key == l.HostHousehold && u.HasHome ) ) )
                {
                    host.People.ForEach( p => usedPeople.Add( p.Guid ) );
                }

                var slots = PlanSlots( count - drafts.Count, count, drafts );
                foreach ( var slot in slots.OrderBy( s => s.ReadyToLead ? 0 : s.Launch.HasValue ? 1 : s.Gender != null ? 2 : 3 ) )
                {
                    var leader = PickLeader( slot, candidates, usedPeople, names );
                    if ( leader == null && slot.ReadyToLead )
                    {
                        slot.ReadyToLead = false;
                        leader = PickLeader( slot, candidates, usedPeople, names );
                    }

                    if ( leader == null && slot.Gender != null )
                    {
                        slot.Gender = null;
                        slot.Topic = null;
                        leader = PickLeader( slot, candidates, usedPeople, names );
                    }

                    if ( leader != null )
                    {
                        slot.Leader = leader;
                        slot.LeaderUnit = slot.Gender == null ? _unitByPerson[leader.Guid] : Alone( leader );
                        slot.LeaderUnit.People.ForEach( p => usedPeople.Add( p.Guid ) );
                    }
                }

                drafts.AddRange( slots.Where( s => s.Leader != null ).OrderBy( s => s.Leader.Guid ) );

                foreach ( var legacy in ActiveLegacy.Where( l => !l.LeaderGuid.HasValue ) )
                {
                    var draft = drafts.FirstOrDefault( d => d.Legacy == null && d.Gender == null ) ?? throw new InvalidOperationException( $"No group is left for {legacy.Key}." );
                    draft.Legacy = legacy;
                }

                foreach ( var draft in drafts.Where( d => d.Legacy?.HostHousehold != null ) )
                {
                    draft.PinnedHost = _current.First( u => u.Household.Key == draft.Legacy.HostHousehold && u.HasHome );
                }

                return drafts;
            }

            /// <summary>
            /// The new groups beside the pinned ones, each with its gender, topic, launch Sunday, and whether it is led
            /// from Ready to Lead. Each designated topic gets its share of all the campus's groups, less the pinned
            /// groups that study it. The launch groups take the newest launches first, in turn, and the first of them
            /// in that order are led from Ready to Lead, so every launch has some.
            /// </summary>
            private List<Draft> PlanSlots( int newCount, int count, List<Draft> pinned )
            {
                var slots = Enumerable.Range( 0, Math.Max( 0, newCount ) ).Select( _ => new Draft() ).ToList();
                var random = Stream( "slots" );
                var byTopic = slots.OrderBy( _ => random.Between( 0.0, 1.0 ) ).ToList();
                var next = 0;
                foreach ( var topic in _rules.Topics.Where( t => t.Share != null ) )
                {
                    var wanted = topic.Share.GroupsFor( count ) - pinned.Count( d => d.Topic == topic );
                    for ( var i = 0; i < wanted && next < byTopic.Count; i++ )
                    {
                        byTopic[next].Topic = topic;
                        byTopic[next].Gender = topic.LeaderGender;
                        next++;
                    }
                }

                var launches = _rules.Launches;
                var recent = launches == null ? new List<DateTime>() : _planner._launches.Recent( _clock.LatestChange, launches.RecentLaunches );
                if ( recent.Count == 0 )
                {
                    return slots;
                }

                var totalLaunches = ( int ) Math.Round( count * launches.GroupsPercent / 100.0, MidpointRounding.AwayFromZero );
                var launchCount = Math.Min( slots.Count, Math.Max( 0, totalLaunches - pinned.Count( d => d.Launch.HasValue ) ) );
                var readyCount = Math.Max( 0, ( int ) Math.Round( totalLaunches * launches.ReadyToLeadPercent / 100.0, MidpointRounding.AwayFromZero ) - pinned.Count( d => d.ReadyToLead ) );
                var launched = slots.OrderBy( _ => random.Between( 0.0, 1.0 ) ).Take( launchCount ).ToList();
                for ( var i = 0; i < launched.Count; i++ )
                {
                    launched[i].Launch = recent[i % recent.Count];
                    launched[i].ReadyToLead = i < readyCount;
                }

                return slots;
            }

            /// <summary>
            /// The first candidate, in seeded order, who can lead the slot: the slot's gender, not used, a full name not
            /// taken on the campus, and coming since before the launch. A leader from Ready to Lead came a year before,
            /// and is someone the request lets be Ready to Lead.
            /// </summary>
            private CastPerson PickLeader( Draft slot, List<CastPerson> candidates, HashSet<Guid> usedPeople, FullNames names )
            {
                var gender = slot.Gender ?? _rules.Leaders.Gender;
                foreach ( var candidate in candidates.Where( c => c.Gender == gender ) )
                {
                    var unit = slot.Gender == null ? _unitByPerson[candidate.Guid] : Alone( candidate );
                    var settled = !slot.Launch.HasValue || unit.JoinFrom <= ( slot.ReadyToLead ? slot.Launch.Value.AddDays( -ReadyToLeadSettledDays ) : slot.Launch.Value );
                    if ( unit.People.Any( p => usedPeople.Contains( p.Guid ) ) || !settled || ( slot.ReadyToLead && !_request.ReadyToLeadCandidate( candidate ) ) )
                    {
                        continue;
                    }

                    if ( names.Add( candidate ) )
                    {
                        return candidate;
                    }
                }

                return null;
            }

            /// <summary>
            /// People allowed to lead, in a stable order: pinned leaders first, then by a seeded key per person.
            /// </summary>
            private List<CastPerson> LeaderCandidates( IEnumerable<CastPerson> people )
            {
                var pinned = ActiveLegacy.Where( l => l.LeaderGuid.HasValue ).Select( l => l.LeaderGuid.Value ).ToList();
                return people
                    .Where( p => CanLead( p, p.Gender ) )
                    .OrderBy( p => pinned.Contains( p.Guid ) ? pinned.IndexOf( p.Guid ) : int.MaxValue )
                    .ThenBy( p => new SeededRandom( $"{_planner._seed}:small-groups:leader:{p.Guid}" ).Between( 0.0, 1.0 ) )
                    .ToList();
            }

            /// <summary>
            /// Whether the leader rules allow this person to lead a group whose topic sets no leader gender.
            /// </summary>
            private bool CanLead( CastPerson person ) => CanLead( person, _rules.Leaders.Gender );

            /// <summary>
            /// Whether the leader rules allow this person to lead a group whose leader is the given gender.
            /// </summary>
            private bool CanLead( CastPerson person, string gender )
            {
                var leaders = _rules.Leaders;
                return person.Gender == gender
                    && ( gender == leaders.Gender || _rules.Topics.Any( t => t.LeaderGender == gender ) )
                    && person.Birth.Age >= leaders.MinimumAge && _leaderStatuses.Contains( person.ConnectionStatus );
            }

            /// <summary>
            /// Marks the Full, Closed, and Pending test groups. New groups take them before legacy
            /// groups, and the first pinned legacy group (the best-known) takes one last.
            /// </summary>
            private void AssignTestCases( List<Draft> active )
            {
                var order = active.Where( d => d.Legacy == null )
                    .Concat( active.Where( d => d.Legacy != null ).Reverse() )
                    .ToList();
                var cases = Enumerable.Repeat( SmallGroupTestCase.Full, _request.TestCases.Full )
                    .Concat( Enumerable.Repeat( SmallGroupTestCase.Closed, _request.TestCases.Closed ) )
                    .Concat( Enumerable.Repeat( SmallGroupTestCase.Pending, _request.TestCases.Pending ) )
                    .ToList();

                for ( var i = 0; i < cases.Count && i < order.Count; i++ )
                {
                    order[i].TestCase = cases[i];
                }
            }

            /// <summary>
            /// Creates the inactive and archived test groups, each with a leader who does not lead
            /// an active group. People with inactive records lead or join these first.
            /// </summary>
            private List<Draft> CreateFormerDrafts( List<Draft> active )
            {
                var cases = Enumerable.Repeat( SmallGroupTestCase.Inactive, _request.TestCases.Inactive )
                    .Concat( Enumerable.Repeat( SmallGroupTestCase.Archived, _request.TestCases.Archived ) )
                    .ToList();
                var drafts = new List<Draft>();
                if ( cases.Count == 0 )
                {
                    return drafts;
                }

                var names = new FullNames( _request.TakenLeaderNames );
                active.ForEach( d => names.Add( d.Leader ) );
                var usedPeople = active.SelectMany( d => d.LeaderUnit.People ).Select( p => p.Guid ).ToHashSet();
                var candidates = LeaderCandidates( _formerOnly.SelectMany( u => u.People ) )
                    .Concat( LeaderCandidates( _couldJoin.SelectMany( u => u.People ) ) )
                    .Where( CanLead );
                var earliestStart = new DateTime( _clock.Anchor.Year - _rules.Membership.StartedYearsAgo.Max, 1, 1 );

                // A pinned ended group keeps its leader and state, and takes its case first.
                foreach ( var legacy in _request.Legacy.Where( l => l.Ended != null ) )
                {
                    var unit = _unitByPerson.GetValueOrDefault( legacy.LeaderGuid ?? Guid.Empty ) ?? throw new InvalidOperationException( $"The pin for {legacy.Key} names a leader who is not an eligible adult." );
                    var leader = unit.People.First( p => p.Guid == legacy.LeaderGuid );
                    var from = Max( earliestStart, unit.JoinFrom );
                    var until = Min( _clock.LatestChange.AddDays( -60 ), unit.LeaveBy ?? DateTime.MaxValue, FirstReserved( unit ) );
                    if ( !cases.Remove( legacy.Ended.Value ) || ( until - from ).TotalDays < 365 || unit.People.Any( p => usedPeople.Contains( p.Guid ) ) || !names.Add( leader ) )
                    {
                        throw new InvalidOperationException( $"The pin for {legacy.Key} makes it an ended group its leader cannot lead." );
                    }

                    unit.People.ForEach( p => usedPeople.Add( p.Guid ) );
                    drafts.Add( new Draft { Legacy = legacy, Leader = leader, LeaderUnit = unit, TestCase = legacy.Ended.Value, WindowStart = from, WindowEnd = until } );
                }

                foreach ( var candidate in candidates )
                {
                    if ( cases.Count == 0 )
                    {
                        break;
                    }

                    var unit = _unitByPerson[candidate.Guid];
                    var from = Max( earliestStart, unit.JoinFrom );
                    var until = Min( _clock.LatestChange.AddDays( -60 ), unit.LeaveBy ?? DateTime.MaxValue, FirstReserved( unit ) );
                    if ( !unit.People.Any( p => usedPeople.Contains( p.Guid ) ) && ( until - from ).TotalDays >= 365 && names.Add( candidate ) )
                    {
                        unit.People.ForEach( p => usedPeople.Add( p.Guid ) );
                        drafts.Add( new Draft { Leader = candidate, LeaderUnit = unit, TestCase = cases[0], WindowStart = from, WindowEnd = until } );
                        cases.RemoveAt( 0 );
                    }
                }

                return drafts;
            }

            #endregion Leaders and drafts

            #region Traits

            /// <summary>
            /// Spreads days, times, meeting styles, child care, summer meetings, and attendance habits
            /// across the campus's groups in the configured proportions. Small campuses
            /// get the exact mix, which a weighted coin per group would miss.
            /// </summary>
            private void ApportionTraits( List<Draft> all, string set )
            {
                var random = Stream( $"traits:{set}" );
                var schedule = _rules.Schedule;

                Assign( all, Apportion( schedule.Days, all.Count, random ), ( d, v ) => d.Day = ( DayOfWeek ) Enum.Parse( typeof( DayOfWeek ), v ) );
                Assign( all, Apportion( schedule.Times, all.Count, random ), ( d, v ) => d.Time = v );
                Assign( all, Apportion( Percent( _rules.Calendar.SummerMeetingPercent ), all.Count, random ), ( d, v ) => d.MeetsInSummer = v == "yes" );
                Assign( all, Apportion( Percent( _rules.Attendance.RecordsEveryWeekPercent ), all.Count, random ), ( d, v ) => d.Habit = v == "yes" ? AttendanceHabit.EveryWeek : AttendanceHabit.Spotty );

                // Groups with a pinned host household always meet in that home, and a group pinned online meets online.
                var online = Apportion( _rules.MeetingStyle, all.Count, random ).Count( s => s == _rules.OnlineStyle );
                var pinnedOnline = all.Where( d => d.Legacy?.Online == true ).ToList();
                var free = all.Where( d => d.PinnedHost == null && d.Legacy?.Online != true ).OrderBy( d => d.Legacy == null ? 0 : 1 ).ThenBy( _ => random.Between( 0.0, 1.0 ) ).ToList();
                all.ForEach( d => d.InPerson = true );
                pinnedOnline.ForEach( d => d.InPerson = false );
                free.Take( Math.Max( 0, online - pinnedOnline.Count ) ).ToList().ForEach( d => d.InPerson = false );

                // Groups whose leader has young children are the first to provide child care.
                var withCare = Apportion( Percent( _rules.ChildCareProvidedPercent ), all.Count, random ).Count( v => v == "yes" );
                var order = all.OrderBy( d => _rules.ChildCareFollowsYoungKids && d.LeaderUnit.HasYoungKids ? 0 : 1 ).ThenBy( _ => random.Between( 0.0, 1.0 ) ).ToList();
                for ( var i = 0; i < order.Count; i++ )
                {
                    order[i].ChildCare = i < withCare;
                }
            }

            /// <summary>
            /// Gives each group a topic its members fit (a Marriage group is mostly married couples),
            /// spreading topics evenly across the campus. Runs after members are planned. A group planned for a
            /// designated topic keeps it, and no other group gets one.
            /// </summary>
            private void ChooseTopics( List<Draft> drafts, string set )
            {
                var random = Stream( $"topics:{set}" );
                var chosen = _rules.Topics.Where( t => t.Share == null ).ToList();
                var used = chosen.ToDictionary( t => t.Value, _ => 0 );
                foreach ( var draft in drafts )
                {
                    if ( draft.Topic == null )
                    {
                        var people = draft.Members.Where( m => m.State != MemberState.Pending ).ToList();
                        var parents = people.Count( m => m.Unit.HasChildren );
                        var fitting = chosen.Where( t => t.Fit == null || t.Fit.IsMetBy( people.Select( m => m.Person ).ToList(), parents ) ).ToList();
                        var fewest = fitting.Min( t => used[t.Value] );
                        draft.Topic = random.Pick( fitting.Where( t => used[t.Value] == fewest ).ToList() );
                        used[draft.Topic.Value]++;
                    }

                    var description = random.Pick( draft.Topic.Descriptions );
                    draft.Description = draft.Legacy?.Description ?? description;
                }
            }

            private static void Assign( List<Draft> drafts, List<string> values, Action<Draft, string> set )
            {
                for ( var i = 0; i < drafts.Count; i++ )
                {
                    set( drafts[i], values[i] );
                }
            }

            private static Dictionary<string, double> Percent( double percent )
            {
                return new Dictionary<string, double> { ["yes"] = percent, ["no"] = 100 - percent };
            }

            /// <summary>
            /// Splits a count across weighted values by largest remainder, then shuffles them.
            /// </summary>
            private static List<string> Apportion( IReadOnlyDictionary<string, double> weights, int count, SeededRandom random )
            {
                var total = weights.Values.Sum();
                var quotas = weights.Select( w => (w.Key, Exact: w.Value / total * count) ).ToList();
                var counts = quotas.ToDictionary( q => q.Key, q => ( int ) Math.Floor( q.Exact ) );
                foreach ( var q in quotas.OrderByDescending( q => q.Exact - Math.Floor( q.Exact ) ).Take( count - counts.Values.Sum() ) )
                {
                    counts[q.Key]++;
                }

                var values = counts.SelectMany( c => Enumerable.Repeat( c.Key, c.Value ) ).ToList();
                for ( var i = values.Count - 1; i > 0; i-- )
                {
                    var j = random.Between( 0, i );
                    (values[i], values[j]) = (values[j], values[i]);
                }

                return values;
            }

            #endregion Traits

            #region Members

            /// <summary>
            /// Places the joining units into active groups. Groups of one gender fill first, from single people of that
            /// gender, then from couples, each spouse to a group of their gender with room. Then families with young
            /// children go into child care groups still under the target size, and everyone else fills the smallest group.
            /// </summary>
            private void AssignUnits( List<Draft> active, List<Unit> joiners )
            {
                if ( active.Count == 0 )
                {
                    return;
                }

                var random = Stream( "assign" );
                foreach ( var draft in active )
                {
                    draft.Units.Add( draft.LeaderUnit );
                    if ( draft.PinnedHost != null && draft.PinnedHost != draft.LeaderUnit )
                    {
                        draft.Units.Add( draft.PinnedHost );
                    }

                    foreach ( var unit in ( draft.Legacy?.MemberHouseholds ?? new List<string>() ).SelectMany( key => _current.Where( u => u.Household.Key == key ) ).Select( u => FittingUnit( draft, u ) ).Where( u => u != null ) )
                    {
                        draft.Units.Add( unit );
                    }
                }

                var placed = active.SelectMany( d => d.Units ).SelectMany( u => u.People ).Select( p => p.Guid ).ToHashSet();
                var rest = new List<Unit>();
                foreach ( var unit in joiners )
                {
                    var free = unit.People.Where( p => !placed.Contains( p.Guid ) ).ToList();
                    if ( free.Count == unit.People.Count )
                    {
                        rest.Add( unit );
                    }
                    else if ( free.Count > 0 )
                    {
                        // The spouse of a leader of a group of one gender joins on their own.
                        rest.Add( Alone( free[0] ) );
                    }
                }

                rest = rest.OrderBy( u => _rules.ChildCareFollowsYoungKids && u.HasYoungKids ? 0 : 1 )
                    .ThenBy( _ => random.Between( 0.0, 1.0 ) )
                    .ToList();

                // Groups of one gender fill to the size the campus's groups will average, so they are no bigger than the rest.
                var gendered = active.Where( d => d.Gender != null ).ToList();
                var average = Math.Max( _rules.Size.Min, Math.Min( _rules.Size.Target, ( placed.Count + rest.Sum( u => u.People.Count ) ) / active.Count ) );
                Draft OpenFor( CastPerson person, int limit ) => gendered.Where( d => d.Gender == person.Gender && d.PeopleCount < limit ).OrderBy( d => d.PeopleCount ).FirstOrDefault();

                foreach ( var unit in rest.Where( u => u.People.Count == 1 ).ToList() )
                {
                    if ( OpenFor( unit.People[0], average ) is { } draft )
                    {
                        draft.Units.Add( unit );
                        rest.Remove( unit );
                    }
                }

                foreach ( var couple in rest.Where( u => u.People.Count > 1 ).ToList() )
                {
                    var homes = couple.People.Select( p => (Person: p, Draft: OpenFor( p, average )) ).ToList();
                    if ( homes.All( h => h.Draft == null ) )
                    {
                        continue;
                    }

                    rest.Remove( couple );
                    foreach ( var (person, draft) in homes )
                    {
                        if ( draft != null )
                        {
                            draft.Units.Add( Alone( person ) );
                        }
                        else
                        {
                            rest.Add( Alone( person ) );
                        }
                    }
                }

                var mixed = active.Where( d => d.Gender == null ).ToList();
                foreach ( var unit in rest )
                {
                    var open = mixed.Where( d => d.PeopleCount + unit.People.Count <= _rules.Size.Max ).ToList();
                    if ( open.Count == 0 )
                    {
                        if ( unit.People.Count == 1 && OpenFor( unit.People[0], _rules.Size.Max ) is { } single )
                        {
                            single.Units.Add( unit );
                        }

                        continue;
                    }

                    var wantsCare = _rules.ChildCareFollowsYoungKids && unit.HasYoungKids;
                    var care = open.Where( d => d.ChildCare && d.PeopleCount + unit.People.Count <= _rules.Size.Target ).ToList();
                    var choice = ( wantsCare && care.Count > 0 ? care : open ).OrderBy( d => d.PeopleCount ).First();
                    choice.Units.Add( unit );
                }
            }

            /// <summary>
            /// Dates the group's start and each current member's join. A launch group started on its launch Sunday.
            /// The leader's family and a pinned host family were there from the start; others joined as the group
            /// turned over, never before four weeks after their household started coming.
            /// </summary>
            private void PlanActiveMembers( Draft draft )
            {
                var random = DraftStream( draft, "members" );
                var started = DateInYear( _clock.Anchor.Year - random.Between( _rules.Membership.StartedYearsAgo.Min, _rules.Membership.StartedYearsAgo.Max ), random );
                var founders = Max( draft.LeaderUnit.JoinFrom, draft.PinnedHost?.JoinFrom ?? DateTime.MinValue );
                if ( draft.Launch < founders )
                {
                    // A pinned host household that started coming after the launch: the group starts on another day.
                    draft.Launch = null;
                    draft.ReadyToLead = false;
                }

                draft.Started = draft.Launch ?? Clamp( started, founders, _clock.LatestChange );

                foreach ( var unit in draft.Units )
                {
                    var isFounder = unit == draft.LeaderUnit || unit == draft.PinnedHost;
                    var joined = isFounder ? draft.Started : Clamp( TenureStart( draft.Started, random ), Max( draft.Started, unit.JoinFrom ), _clock.LatestChange );
                    foreach ( var person in unit.People )
                    {
                        draft.Members.Add( new MemberDraft { Person = person, Unit = unit, Joined = joined, State = MemberState.Active, Role = person == draft.Leader ? SmallGroupRole.Leader : SmallGroupRole.Member } );
                        Reserve( person, joined, DateTime.MaxValue );
                    }
                }
            }

            /// <summary>
            /// Gives a launch group's Ready to Lead leader their place in another group six weeks before the launch:
            /// a group of a fitting gender that started well before, joined at least a month before that day. Some stay
            /// in it, and the rest leave it the day before they launch. A leader no group fits leads as usual.
            /// </summary>
            private void PlanReadyToLeadHistory( Draft draft, List<Draft> active )
            {
                var launches = _rules.Launches;
                var random = DraftStream( draft, "ready-to-lead" );
                var readyOn = draft.Started.AddDays( -launches.ReadyDaysBefore );
                var latestJoin = readyOn.AddDays( -30 );
                var unit = draft.LeaderUnit;
                var stays = random.Chance( launches.StaysInOldGroupPercent / 100.0 );
                var old = active
                    .Where( d => d != draft && FittingUnit( d, unit ) == unit && d.Members.All( m => !unit.People.Contains( m.Person ) ) )
                    .Where( d => Max( d.Started, unit.JoinFrom ) <= latestJoin.AddDays( -60 ) && ( !stays || d.PeopleCount + unit.People.Count <= _rules.Size.Max ) )
                    .OrderBy( d => d.PeopleCount )
                    .ThenBy( _ => random.Between( 0.0, 1.0 ) )
                    .FirstOrDefault();
                if ( old == null )
                {
                    draft.ReadyToLead = false;
                    return;
                }

                var joined = DateBetween( Max( old.Started, unit.JoinFrom ), latestJoin, random );
                var left = stays ? ( DateTime? ) null : draft.Started.AddDays( -1 );
                foreach ( var person in unit.People )
                {
                    old.Members.Add( new MemberDraft { Person = person, Unit = unit, Joined = joined, Left = left, State = stays ? MemberState.Active : MemberState.Inactive, Role = SmallGroupRole.Member, LeadsAnotherGroup = true } );
                    Reserve( person, joined, left ?? DateTime.MaxValue );
                }
            }

            /// <summary>
            /// Places each joining unit whose household stopped coming into an active group that was meeting while they
            /// came, as inactive members who left two to six weeks after their household's last Sunday.
            /// </summary>
            private void PlanLapsedMembers( List<Draft> active )
            {
                var random = Stream( "lapsed" );
                foreach ( var unit in _lapsed.OrderBy( u => u.People[0].Guid ) )
                {
                    var leaveBy = unit.LeaveBy.Value;
                    var latestJoin = Min( _clock.LatestBefore( leaveBy ).AddDays( -60 ), _clock.LatestChange );
                    var group = active
                        .Where( d => FittingUnit( d, unit ) == unit && d.Members.All( m => !unit.People.Contains( m.Person ) ) && Max( d.Started, unit.JoinFrom ) <= latestJoin )
                        .OrderBy( d => d.PeopleCount )
                        .ThenBy( _ => random.Between( 0.0, 1.0 ) )
                        .FirstOrDefault();
                    if ( group == null )
                    {
                        continue;
                    }

                    var joined = DateBetween( Max( group.Started, unit.JoinFrom ), latestJoin, random );
                    if ( !IsFree( unit, joined, leaveBy ) )
                    {
                        continue;
                    }

                    foreach ( var person in unit.People )
                    {
                        group.Members.Add( new MemberDraft { Person = person, Unit = unit, Joined = joined, Left = leaveBy, State = MemberState.Inactive, Role = SmallGroupRole.Member } );
                        Reserve( person, joined, leaveBy );
                    }
                }
            }

            /// <summary>
            /// Adds people who joined and later left, at the configured yearly turnover. Spouses leave
            /// together, and nobody is in two groups at once. Nobody joins before four weeks after their household
            /// started coming, or stays past the day they left after it stopped.
            /// </summary>
            private void PlanLeavers( Draft draft )
            {
                var random = DraftStream( draft, "leavers" );
                var years = ( _clock.LatestChange - draft.Started ).TotalDays / 365.25;
                var target = ( int ) Math.Round( draft.PeopleCount * _rules.Membership.LeavePercentPerYear / 100.0 * years );
                var pool = _couldJoin.Select( u => FittingUnit( draft, u ) ).Where( u => u != null && !draft.Units.Contains( u ) ).ToList();
                var added = draft.Members.Count( m => m.State == MemberState.Inactive );
                var misses = 0;

                while ( added < target && pool.Count > 0 && misses < 50 )
                {
                    var unit = random.Pick( pool );
                    var joined = DateBetween( draft.Started, _clock.LatestChange.AddDays( -60 ), random );
                    var left = joined.AddDays( Math.Max( 30, TruncatedTenureDays( ( _clock.LatestChange - joined ).TotalDays, random ) ) );
                    if ( unit.LeaveBy.HasValue && left > unit.LeaveBy.Value )
                    {
                        left = unit.LeaveBy.Value;
                    }

                    if ( draft.Members.Any( m => unit.People.Contains( m.Person ) ) || joined < unit.JoinFrom || left < joined.AddDays( 30 ) || left > _clock.LatestChange || !IsFree( unit, joined, left ) )
                    {
                        misses++;
                        continue;
                    }

                    var archived = random.Chance( _rules.Membership.ArchivedPercentOfLeavers / 100.0 );
                    foreach ( var person in unit.People )
                    {
                        draft.Members.Add( new MemberDraft { Person = person, Unit = unit, Joined = joined, Left = left, State = archived ? MemberState.Archived : MemberState.Inactive, Role = SmallGroupRole.Member } );
                        Reserve( person, joined, left );
                    }

                    added += unit.People.Count;
                }
            }

            /// <summary>
            /// Dates an inactive or archived group and fills it with members from before it ended.
            /// A leader whose household stopped coming (a family that moved) ends the group when they left.
            /// </summary>
            private void PlanFormerGroup( Draft draft )
            {
                var random = DraftStream( draft, "former" );
                var started = DateInYear( _clock.Anchor.Year - random.Between( _rules.Membership.StartedYearsAgo.Min, _rules.Membership.StartedYearsAgo.Max ), random );
                draft.Started = Clamp( started, draft.WindowStart, draft.WindowEnd.AddDays( -180 ) );
                draft.Ended = RandomDate( draft.Started.AddDays( 180 ), draft.WindowEnd, random );
                if ( draft.TestCase == SmallGroupTestCase.Archived )
                {
                    draft.Archived = RandomDate( draft.Ended.Value.AddDays( 30 ), _clock.LatestChange, random );
                }
                else
                {
                    draft.Reason = random.Pick( _rules.InactiveReasons ).Value;
                }

                draft.Units.Add( draft.LeaderUnit );
                AddFormerMembers( draft, draft.LeaderUnit, draft.Started );

                var target = random.Between( _rules.Size.Min, _rules.Size.Target );
                var pool = _formerOnly.Concat( _couldJoin.OrderBy( _ => random.Between( 0.0, 1.0 ) ) ).Where( u => u != draft.LeaderUnit ).ToList();
                foreach ( var unit in pool )
                {
                    if ( draft.PeopleCount >= target )
                    {
                        break;
                    }

                    var joined = random.Chance( 0.5 ) ? draft.Started : DateBetween( draft.Started, draft.Ended.Value.AddDays( -90 ), random );
                    var end = Min( draft.Ended.Value, unit.LeaveBy ?? DateTime.MaxValue );
                    if ( joined < unit.JoinFrom || end < joined.AddDays( 30 ) || !IsFree( unit, joined, end ) )
                    {
                        continue;
                    }

                    draft.Units.Add( unit );
                    AddFormerMembers( draft, unit, joined );
                }
            }

            /// <summary>
            /// Adds a unit to an ended group. They stay members of the ended group, unless they left
            /// first because their household stopped coming, in which case they left on that date.
            /// </summary>
            private void AddFormerMembers( Draft draft, Unit unit, DateTime joined )
            {
                var leftEarly = unit.LeaveBy.HasValue && unit.LeaveBy < draft.Ended;
                foreach ( var person in unit.People )
                {
                    draft.Members.Add( new MemberDraft
                    {
                        Person = person,
                        Unit = unit,
                        Joined = joined,
                        Left = leftEarly ? unit.LeaveBy : null,
                        State = leftEarly ? MemberState.Inactive : MemberState.Active,
                        Role = person == draft.Leader ? SmallGroupRole.Leader : SmallGroupRole.Member
                    } );
                    Reserve( person, joined, leftEarly ? unit.LeaveBy.Value : draft.Ended.Value );
                }
            }

            /// <summary>
            /// Adds recent join requests from people who are not in a group now: first the households the group's pin
            /// names, then others for a Pending test group. A request comes at least four weeks after the household
            /// started coming, and never from a household that stopped.
            /// </summary>
            private void PlanPendingRequests( Draft draft )
            {
                var random = DraftStream( draft, "pending" );
                foreach ( var key in draft.Legacy?.PendingHouseholds ?? new List<string>() )
                {
                    var units = _current.Where( u => u.Household.Key == key ).ToList();
                    var requested = _clock.Anchor.AddDays( -random.Between( 1, 30 ) );
                    if ( units.Count == 0 || units.Any( u => HasLapsed( u ) || u.People.Any( IsMemberNow ) ) )
                    {
                        throw new InvalidOperationException( $"The pin for {draft.Legacy.Key} names pending household {key}, which has no adults free to ask to join." );
                    }

                    foreach ( var unit in units )
                    {
                        AddRequest( draft, unit, Max( requested, Min( unit.JoinFrom, _clock.Anchor.AddDays( -1 ) ) ) );
                    }
                }

                if ( draft.TestCase != SmallGroupTestCase.Pending )
                {
                    return;
                }

                var wanted = random.Between( _rules.Membership.PendingRequests.Min, _rules.Membership.PendingRequests.Max );
                var pool = _couldJoin.Select( u => FittingUnit( draft, u ) )
                    .Where( u => u != null && !HasLapsed( u ) && draft.Members.All( m => !u.People.Contains( m.Person ) ) && u.People.All( p => !_pending.Contains( p.Guid ) && !IsMemberNow( p ) ) )
                    .OrderBy( _ => random.Between( 0.0, 1.0 ) )
                    .ToList();

                var added = 0;
                foreach ( var unit in pool.Where( u => u.People.Count <= wanted ) )
                {
                    if ( added + unit.People.Count > wanted )
                    {
                        continue;
                    }

                    var requested = _clock.Anchor.AddDays( -random.Between( 1, 30 ) );
                    if ( requested < unit.JoinFrom )
                    {
                        continue;
                    }

                    AddRequest( draft, unit, requested );
                    added += unit.People.Count;
                    if ( added == wanted )
                    {
                        break;
                    }
                }
            }

            private void AddRequest( Draft draft, Unit unit, DateTime requested )
            {
                foreach ( var person in unit.People )
                {
                    draft.Members.Add( new MemberDraft { Person = person, Unit = unit, Joined = requested, State = MemberState.Pending, Role = SmallGroupRole.Member, IsRecent = true } );
                    _pending.Add( person.Guid );
                }
            }

            /// <summary>
            /// Picks the host for a group that meets in a home: the leader's own home (with their spouse, or a leader of
            /// a group of one gender) in the configured share of groups, otherwise another member family whose home the
            /// group meets in. A member who leads another group never hosts this one.
            /// </summary>
            private void ChooseHost( Draft draft )
            {
                if ( !draft.InPerson )
                {
                    return;
                }

                var random = DraftStream( draft, "host" );
                var spouse = draft.LeaderUnit.People.FirstOrDefault( p => p != draft.Leader );
                var current = draft.Members.Where( m => m.State == MemberState.Active && !m.LeadsAnotherGroup ).Select( m => m.Unit ).Distinct().ToList();
                var others = current.Where( u => u != draft.LeaderUnit && u.HasHome ).ToList();
                var leaderHome = draft.LeaderUnit.HasHome && ( spouse != null || draft.Gender != null );

                Unit host;
                if ( draft.PinnedHost != null )
                {
                    host = draft.PinnedHost;
                }
                else if ( leaderHome && ( others.Count == 0 || random.Chance( _rules.Hosts.SpouseHostsPercent / 100.0 ) ) )
                {
                    host = draft.LeaderUnit;
                }
                else if ( others.Count > 0 )
                {
                    host = random.Pick( others );
                }
                else if ( draft.LeaderUnit.HasHome )
                {
                    host = draft.LeaderUnit;
                }
                else
                {
                    draft.InPerson = false;
                    return;
                }

                draft.HostUnit = host;
                foreach ( var member in draft.Members.Where( m => m.Unit == host && m.Person != draft.Leader && m.State == MemberState.Active ) )
                {
                    member.Role = SmallGroupRole.Host;
                }
            }

            private bool IsMemberNow( CastPerson person )
            {
                return _busy.TryGetValue( person.Guid, out var spans ) && spans.Any( s => s.To == DateTime.MaxValue );
            }

            private bool IsFree( Unit unit, DateTime from, DateTime to )
            {
                return unit.People.All( p => !_busy.TryGetValue( p.Guid, out var spans ) || spans.All( s => to <= s.From || from >= s.To ) );
            }

            private void Reserve( CastPerson person, DateTime from, DateTime to )
            {
                if ( !_busy.TryGetValue( person.Guid, out var spans ) )
                {
                    spans = new List<(DateTime From, DateTime To)>();
                    _busy[person.Guid] = spans;
                }

                spans.Add( (from, to) );
            }

            /// <summary>
            /// The first date any of the unit's people is already in a group, so an ended group can fit before it.
            /// </summary>
            private DateTime FirstReserved( Unit unit )
            {
                return unit.People.SelectMany( p => _busy.GetValueOrDefault( p.Guid ) ?? new List<(DateTime From, DateTime To)>() ).Select( s => s.From ).DefaultIfEmpty( DateTime.MaxValue ).Min();
            }

            #endregion Members

            #region Names and plans

            /// <summary>
            /// Names each group for its leader ("Decker Group"), adding the first name when two
            /// leaders on the campus share a last name. Legacy groups keep their keys and Guids, and a
            /// pinned name, which does not count toward shared last names.
            /// </summary>
            private void NameGroups( List<Draft> drafts )
            {
                var lastNames = drafts.Where( d => d.Legacy?.Name == null ).Select( d => d.Leader.LastName ).Concat( _request.TakenLeaderNames.Select( n => n.LastName ) )
                    .GroupBy( n => n, StringComparer.OrdinalIgnoreCase )
                    .ToDictionary( g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase );
                var keys = new HashSet<string>( _request.TakenKeys );

                foreach ( var draft in drafts.OrderBy( d => d.Legacy == null ? 1 : 0 ) )
                {
                    var leader = draft.Leader;
                    var format = lastNames.GetValueOrDefault( leader.LastName ) > 1 ? _rules.Naming.DuplicateFormat : _rules.Naming.Format;
                    draft.Name = draft.Legacy?.Name ?? format.Replace( "{firstName}", leader.NickName ?? leader.FirstName ).Replace( "{lastName}", leader.LastName );

                    if ( draft.Legacy != null )
                    {
                        draft.Key = draft.Legacy.Key;
                        draft.Guid = draft.Legacy.Guid;
                        keys.Add( draft.Key );
                        continue;
                    }

                    var slug = _request.KeyPrefix + Regex.Replace( draft.Name.ToLowerInvariant(), "[^a-z0-9]+", "-" ).Trim( '-' );
                    var key = slug;
                    for ( var n = 2; keys.Contains( key ); n++ )
                    {
                        key = $"{slug}-{n}";
                    }

                    keys.Add( key );
                    draft.Key = key;
                    draft.Guid = new SeededRandom( $"{_planner._seed}:small-groups:guid:{_request.Campus}:{key}" ).NextGuid();
                }
            }

            /// <summary>
            /// Turns a finished draft into the plan the loader reads.
            /// </summary>
            private SmallGroupPlan BuildPlan( Draft draft )
            {
                var random = DraftStream( draft, "attendance" );
                var attendance = _rules.Attendance;
                var members = draft.Members
                    .OrderBy( m => m.State == MemberState.Active ? 0 : m.State == MemberState.Pending ? 1 : 2 )
                    .ThenBy( m => m.Role )
                    .ThenBy( m => m.Joined )
                    .ThenBy( m => m.Person.LastName, StringComparer.Ordinal )
                    .ThenBy( m => m.Person.FirstName, StringComparer.Ordinal )
                    .Select( m => ToPlannedMember( m, m.Role switch
                    {
                        SmallGroupRole.Leader => attendance.LeaderPercent,
                        SmallGroupRole.Host => attendance.HostPercent,
                        _ => random.Between( attendance.MemberPercent.Min, attendance.MemberPercent.Max )
                    } ) )
                    .ToList();

                var activeCount = draft.Members.Count( m => m.State == MemberState.Active );
                return new SmallGroupPlan
                {
                    Key = draft.Key,
                    Guid = draft.Guid,
                    Name = draft.Name,
                    Campus = _request.Campus,
                    Topic = draft.Topic.Value,
                    Description = draft.Description,
                    Schedule = new GroupSchedule( draft.Day, draft.Time ),
                    MeetingStyle = draft.InPerson ? new InPersonMeeting( draft.HostUnit.Household.Key ) : new OnlineMeeting( _rules.Attributes.MeetingLink.Format.Replace( "{groupKey}", draft.Key ) ),
                    ChildCareProvided = draft.ChildCare,
                    Capacity = draft.TestCase == SmallGroupTestCase.Full ? activeCount : _rules.Size.Capacity,
                    IsPublic = draft.TestCase != SmallGroupTestCase.Closed,
                    MeetsInSummer = draft.MeetsInSummer,
                    AttendanceHabit = draft.Habit,
                    Lifecycle = draft.TestCase switch
                    {
                        SmallGroupTestCase.Inactive => new InactiveLifecycle( _clock.ToRelative( draft.Started ), _clock.ToRelative( draft.Ended.Value ), draft.Reason ),
                        SmallGroupTestCase.Archived => new ArchivedLifecycle( _clock.ToRelative( draft.Started ), _clock.ToRelative( draft.Ended.Value ), _clock.ToRelative( draft.Archived.Value ) ),
                        _ => new ActiveLifecycle( _clock.ToRelative( draft.Started ) )
                    },
                    TestCase = draft.TestCase,
                    LeaderWasReadyToLead = draft.ReadyToLead,
                    Members = members
                };
            }

            private PlannedMember ToPlannedMember( MemberDraft member, int percent )
            {
                var person = member.Person;
                var name = $"{person.NickName ?? person.FirstName} {person.LastName}";
                var joined = member.IsRecent ? _clock.ToRecent( member.Joined ) : _clock.ToRelative( member.Joined );
                return member.State switch
                {
                    MemberState.Inactive => new InactiveMember( person.Guid, name, member.Role, joined, _clock.ToRelative( member.Left.Value ), percent ),
                    MemberState.Archived => new ArchivedMember( person.Guid, name, member.Role, joined, _clock.ToRelative( member.Left.Value ), percent ),
                    MemberState.Pending => new PendingMember( person.Guid, name, member.Role, joined, percent ),
                    _ => new ActiveMember( person.Guid, name, member.Role, joined, percent )
                };
            }

            #endregion Names and plans

            #region Dates

            private SeededRandom DraftStream( Draft draft, string purpose )
            {
                return new SeededRandom( $"{_planner._seed}:small-groups:{purpose}:{_request.Campus}:{draft.Leader.Guid}" );
            }

            /// <summary>
            /// A date in the given year whose month follows the configured join month weights.
            /// </summary>
            private DateTime DateInYear( int year, SeededRandom random )
            {
                var weights = _rules.Calendar.JoinMonthWeights;
                var pick = random.Pick( weights );
                var month = pick == "other"
                    ? random.Pick( Enumerable.Range( 1, 12 ).Where( m => !weights.ContainsKey( m.ToString() ) ).ToList() )
                    : int.Parse( pick );
                return new DateTime( year, month, random.Between( 1, 28 ) );
            }

            /// <summary>
            /// When a current member joined: a time back from the latest date drawn from the yearly
            /// leave rate, re-dated into a join month. Long tenures reach back to the group's start.
            /// </summary>
            private DateTime TenureStart( DateTime started, SeededRandom random )
            {
                var stay = Math.Log( 1 - random.Between( 0.0, 0.999 ) ) / Math.Log( 1 - _rules.Membership.LeavePercentPerYear / 100.0 );
                var date = _clock.LatestChange.AddDays( -stay * 365.25 );
                return date <= started ? started : DateBetween( Max( started, new DateTime( date.Year, 1, 1 ) ), Min( _clock.LatestChange, new DateTime( date.Year, 12, 31 ) ), random );
            }

            /// <summary>
            /// A date in a span whose month follows the join month weights, when the span allows it.
            /// </summary>
            private DateTime DateBetween( DateTime from, DateTime to, SeededRandom random )
            {
                for ( var tries = 0; tries < 10; tries++ )
                {
                    var date = DateInYear( random.Between( from.Year, to.Year ), random );
                    if ( date >= from && date <= to )
                    {
                        return date;
                    }
                }

                return RandomDate( from, to, random );
            }

            /// <summary>
            /// Days a leaver stayed, from the yearly leave rate, given they left before the latest date.
            /// </summary>
            private double TruncatedTenureDays( double maxDays, SeededRandom random )
            {
                var rate = -Math.Log( 1 - _rules.Membership.LeavePercentPerYear / 100.0 ) / 365.25;
                var cap = 1 - Math.Exp( -rate * maxDays );
                return -Math.Log( 1 - random.Between( 0.0, cap ) ) / rate;
            }

            private static DateTime RandomDate( DateTime from, DateTime to, SeededRandom random )
            {
                return to <= from ? from : from.AddDays( random.Between( 0, ( int ) ( to - from ).TotalDays ) );
            }

            private static DateTime Clamp( DateTime date, DateTime min, DateTime max ) => date < min ? min : date > max ? max : date;

            private static DateTime Max( DateTime a, DateTime b ) => a > b ? a : b;

            private static DateTime Min( params DateTime[] dates ) => dates.Min();

            #endregion Dates
        }

        /// <summary>
        /// Adults who join, leave, or ask to join together: a married couple, or one adult. A married person
        /// in a group of one gender is a unit of one.
        /// </summary>
        private sealed class Unit
        {
            public CastHousehold Household { get; set; }

            public List<CastPerson> People { get; set; }

            public bool HasYoungKids { get; set; }

            /// <summary>
            /// The household has children under 18, so its adults count as parents for topic fit.
            /// </summary>
            public bool HasChildren { get; set; }

            public bool HasHome { get; set; }

            /// <summary>
            /// The first day the unit may join a group: four weeks after its household started coming, once its
            /// people are adults out of high school.
            /// </summary>
            public DateTime JoinFrom { get; set; }

            /// <summary>
            /// The day the unit left its groups, two to six weeks after its household stopped coming, or null while it comes.
            /// </summary>
            public DateTime? LeaveBy { get; set; }
        }

        /// <summary>
        /// One group while it is being planned.
        /// </summary>
        private sealed class Draft
        {
            public LegacyGroup Legacy { get; set; }

            public CastPerson Leader { get; set; }

            public Unit LeaderUnit { get; set; }

            public Unit PinnedHost { get; set; }

            public Unit HostUnit { get; set; }

            public SmallGroupTestCase TestCase { get; set; }

            /// <summary>
            /// The gender of every member, for a group whose topic sets one, or null for a mixed group.
            /// </summary>
            public string Gender { get; set; }

            /// <summary>
            /// The launch Sunday the group started on, or null for a group that started on another day.
            /// </summary>
            public DateTime? Launch { get; set; }

            /// <summary>
            /// The leader was Ready to Lead six weeks before the launch.
            /// </summary>
            public bool ReadyToLead { get; set; }

            public List<Unit> Units { get; } = new List<Unit>();

            public List<MemberDraft> Members { get; } = new List<MemberDraft>();

            public int PeopleCount => Members.Count > 0 ? Members.Count( m => m.State == MemberState.Active ) : Units.Sum( u => u.People.Count );

            public DayOfWeek Day { get; set; }

            public string Time { get; set; }

            public bool InPerson { get; set; }

            public bool ChildCare { get; set; }

            public bool MeetsInSummer { get; set; }

            public AttendanceHabit Habit { get; set; }

            public SmallGroupTopic Topic { get; set; }

            public string Description { get; set; }

            public DateTime Started { get; set; }

            public DateTime? Ended { get; set; }

            public DateTime? Archived { get; set; }

            public string Reason { get; set; }

            /// <summary>
            /// For a former group, the span its leader's family was free to lead it.
            /// </summary>
            public DateTime WindowStart { get; set; }

            public DateTime WindowEnd { get; set; }

            public string Name { get; set; }

            public string Key { get; set; }

            public Guid Guid { get; set; }
        }

        private enum MemberState
        {
            Active,
            Inactive,
            Archived,
            Pending
        }

        private sealed class MemberDraft
        {
            public CastPerson Person { get; set; }

            public Unit Unit { get; set; }

            public SmallGroupRole Role { get; set; }

            public DateTime Joined { get; set; }

            public DateTime? Left { get; set; }

            public MemberState State { get; set; }

            /// <summary>
            /// A recent date (a pending request), written as days ago so it stays recent.
            /// </summary>
            public bool IsRecent { get; set; }

            /// <summary>
            /// A Ready to Lead leader's old group, which they may stay in beside the group they lead.
            /// </summary>
            public bool LeadsAnotherGroup { get; set; }
        }

        /// <summary>
        /// Leader full names already used on a campus, so no two leaders share one.
        /// </summary>
        private sealed class FullNames
        {
            private readonly HashSet<string> _names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

            public FullNames( IEnumerable<LeaderName> taken )
            {
                foreach ( var name in taken )
                {
                    _names.Add( $"{name.FirstName} {name.LastName}" );
                }
            }

            public bool Add( CastPerson person ) => _names.Add( $"{person.NickName ?? person.FirstName} {person.LastName}" );
        }
    }

    /// <summary>
    /// What the planner needs to plan one campus.
    /// </summary>
    internal sealed class CampusPlanRequest
    {
        /// <summary>
        /// The plans' campus value: a campus key, or "main" for the Cast.
        /// </summary>
        public string Campus { get; set; }

        /// <summary>
        /// Put before new group keys, so keys stay unique across campuses (empty for the Cast).
        /// </summary>
        public string KeyPrefix { get; set; } = string.Empty;

        public IReadOnlyList<CastHousehold> Households { get; set; }

        /// <summary>
        /// The percent chance (0 to 100) that a unit of these people from this household joins an active group.
        /// </summary>
        public Func<CastHousehold, IReadOnlyList<CastPerson>, double> JoinPercent { get; set; }

        /// <summary>
        /// Whether a person may lead a launch group from Ready to Lead. Generated people always may, since the serving and
        /// step plans give them their team and Equip step. A Cast person must already be on a serving team, not as its leader.
        /// </summary>
        public Func<CastPerson, bool> ReadyToLeadCandidate { get; set; } = _ => true;

        public TestCaseCounts TestCases { get; set; } = TestCaseCounts.None;

        public IReadOnlyList<LegacyGroup> Legacy { get; set; } = new List<LegacyGroup>();

        /// <summary>
        /// Leaders of groups planned elsewhere on this campus (the Cast's, for generated households on the main campus).
        /// </summary>
        public IReadOnlyList<LeaderName> TakenLeaderNames { get; set; } = new List<LeaderName>();

        /// <summary>
        /// Group keys planned elsewhere on this campus.
        /// </summary>
        public IReadOnlyCollection<string> TakenKeys { get; set; } = new List<string>();
    }

    /// <summary>
    /// A legacy Cast group that keeps its key and Guid, with what its pin fixes (see <see cref="SmallGroupPin"/>).
    /// </summary>
    internal sealed record LegacyGroup( string Key, Guid Guid, Guid? LeaderGuid, string HostHousehold )
    {
        public string Topic { get; init; }

        public string Name { get; init; }

        public string Description { get; init; }

        public bool Online { get; init; }

        public List<string> MemberHouseholds { get; init; } = new List<string>();

        public List<string> PendingHouseholds { get; init; } = new List<string>();

        public int? LaunchesAgo { get; init; }

        public bool ReadyToLead { get; init; }

        /// <summary>
        /// Inactive or Archived for one of the campus's ended test groups, or null for a group still meeting.
        /// </summary>
        public SmallGroupTestCase? Ended { get; init; }
    }

    /// <summary>
    /// A leader's first (or nick) name and last name.
    /// </summary>
    internal sealed record LeaderName( string FirstName, string LastName );

    /// <summary>
    /// The fall and spring small group launch Sundays from configuration/communications.json.
    /// </summary>
    internal sealed class GroupLaunchCalendar
    {
        private readonly List<MonthWeek> _launches;

        public GroupLaunchCalendar( CommunicationEvents events )
        {
            _launches = new[] { events?.FallGroupLaunch, events?.SpringGroupLaunch }.Where( l => l != null ).ToList();
        }

        /// <summary>
        /// The launch Sundays on or before a date, newest first.
        /// </summary>
        public List<DateTime> Recent( DateTime onOrBefore, int count )
        {
            var dates = new List<DateTime>();
            for ( var year = onOrBefore.Year; _launches.Count > 0 && dates.Count < count; year-- )
            {
                dates.AddRange( _launches.Select( l => l.In( year ) ).Where( d => d <= onOrBefore ).OrderByDescending( d => d ) );
            }

            return dates.Take( count ).ToList();
        }

        /// <summary>
        /// The launch Sunday nearest a date, or null when there are no launches.
        /// </summary>
        public DateTime? Nearest( DateTime date )
        {
            return _launches.Count == 0 ? null : Enumerable.Range( date.Year - 1, 3 ).SelectMany( y => _launches.Select( l => l.In( y ) ) ).OrderBy( d => Math.Abs( ( d - date ).TotalDays ) ).First();
        }

        /// <summary>
        /// Each leader of a group the plans mark as led from Ready to Lead, and the day they were Ready to Lead: the
        /// configured days before the real launch Sunday nearest the group's start. A Cast group's start is a month
        /// and day, so it falls within a few days of that year's launch Sunday.
        /// </summary>
        public Dictionary<Guid, DateTime> ReadyToLeadLeaders( IEnumerable<SmallGroupPlan> plans, SmallGroupRules rules, AnchorDates dates )
        {
            var leaders = new Dictionary<Guid, DateTime>();
            foreach ( var plan in plans.Where( p => p.LeaderWasReadyToLead ) )
            {
                var leader = plan.Members.First( m => m.Role == SmallGroupRole.Leader );
                var launch = Nearest( dates.Resolve( plan.Lifecycle.Started ).Value ).Value;
                leaders[leader.Guid] = launch.AddDays( -rules.Launches.ReadyDaysBefore );
            }

            return leaders;
        }
    }

    /*
        10/16/26 - CLAUDE

        Cast plans are written with "yearsAgo" dates counted from a fixed planning anchor, and
        every changing date is at least one year back, so the files never depend on the day the
        planner ran and the dates resolve in the same order for any load anchor. Pending requests
        use "daysAgo" so they stay recent. Plans made at load time use the load anchor.

        Reason: Cast files must be stable, and their dates must stay in order at any anchor.
    */

    /*
        11/19/26 - CLAUDE

        A Cast household that stopped coming in the last year leaves its groups a few weeks after its last
        Sunday, which its file writes as days ago. So a date after the latest change is written as days ago
        too. A "yearsAgo" date can land up to a year later at another anchor, so a date that must come before
        a days ago date is held to two or more years back.

        Reason: The Ortizes leave their group a few weeks after they stopped, at any anchor (decision D-group-fixes-1).
    */

    /// <summary>
    /// The dates a plan is made against, and how plan dates are written.
    /// </summary>
    internal sealed class PlanningClock
    {
        private static readonly DateTime CastAnchor = new DateTime( 2000, 12, 31 );

        private readonly bool _writesYearsAgo;

        private PlanningClock( DateTime anchor, DateTime latestChange, bool writesYearsAgo )
        {
            Anchor = anchor.Date;
            LatestChange = latestChange.Date;
            Dates = new AnchorDates( Anchor );
            _writesYearsAgo = writesYearsAgo;
        }

        /// <summary>
        /// The anchor ages and relative dates count from.
        /// </summary>
        public DateTime Anchor { get; }

        /// <summary>
        /// The latest date a group can start, or a member can join or leave, except a household's leaving after it stopped coming.
        /// </summary>
        public DateTime LatestChange { get; }

        public AnchorDates Dates { get; }

        /// <summary>
        /// Plans for the Cast files.
        /// </summary>
        public static PlanningClock ForCast() => new PlanningClock( CastAnchor, new DateTime( CastAnchor.Year - 1, 12, 31 ), true );

        /// <summary>
        /// Plans made during a load, on the load's anchor.
        /// </summary>
        public static PlanningClock ForLoad( DateTime anchor ) => new PlanningClock( anchor, anchor, false );

        public RelativeDate ToRelative( DateTime date )
        {
            return _writesYearsAgo && date <= LatestChange
                ? new RelativeDate { YearsAgo = Anchor.Year - date.Year, Month = date.Month, Day = date.Day }
                : ToRecent( date );
        }

        public RelativeDate ToRecent( DateTime date )
        {
            return new RelativeDate { DaysAgo = ( Anchor - date.Date ).Days };
        }

        /// <summary>
        /// The latest date that comes before the given date at any load anchor, once both are written into a plan.
        /// </summary>
        public DateTime LatestBefore( DateTime date )
        {
            var yearsAgoTwo = new DateTime( Anchor.Year - 2, 12, 31 );
            return _writesYearsAgo && date > LatestChange && date > yearsAgoTwo ? yearsAgoTwo : date;
        }
    }
}
