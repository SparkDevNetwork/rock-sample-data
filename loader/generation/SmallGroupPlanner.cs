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

    /// <summary>
    /// Plans the small groups for one campus's households.
    /// </summary>
    internal sealed class SmallGroupPlanner
    {
        /// <summary>
        /// Children this age or younger make a family want child care.
        /// </summary>
        private const int YoungChildMaxAge = 12;

        private readonly SmallGroupRules _rules;
        private readonly PlanningClock _clock;
        private readonly string _seed;

        public SmallGroupPlanner( SmallGroupRules rules, PlanningClock clock, string seed )
        {
            _rules = rules;
            _clock = clock;
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
            private readonly Dictionary<Guid, Unit> _unitByPerson = new Dictionary<Guid, Unit>();
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

                foreach ( var draft in active.Where( d => d.TestCase == SmallGroupTestCase.Pending ) )
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
            /// </summary>
            private void BuildUnits()
            {
                foreach ( var household in _request.Households )
                {
                    if ( household.DuplicateOf != null || household.Kind == "business" )
                    {
                        continue;
                    }

                    var hasYoungKids = household.Members.Any( p => p.FamilyRole == "child" && p.Birth != null && p.Birth.Age <= YoungChildMaxAge );
                    var hasChildren = household.Members.Any( p => p.FamilyRole == "child" && !p.IsDeceased && p.Birth != null && p.Birth.Age < 18 );
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
                            var unit = new Unit
                            {
                                Household = household,
                                People = unitPeople,
                                HasYoungKids = hasYoungKids,
                                HasChildren = hasChildren,
                                HasHome = household.Address?.Type == "home",
                                RecordEnded = isInactive ? _clock.Dates.Resolve( household.Attendance?.Ended ) : null
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

                var adultSince = _clock.Dates.BirthDate( person.Birth ).AddYears( _rules.Members.MinimumAge );
                _adultSince[person.Guid] = adultSince;
                return adultSince <= _clock.LatestChange;
            }

            /// <summary>
            /// The units in an active group now: pinned leaders and hosts always, others by their join chance.
            /// </summary>
            private List<Unit> PickJoiners()
            {
                var forced = new HashSet<Unit>();
                foreach ( var legacy in _request.Legacy )
                {
                    if ( legacy.LeaderGuid.HasValue )
                    {
                        forced.Add( _unitByPerson.GetValueOrDefault( legacy.LeaderGuid.Value ) ?? throw new InvalidOperationException( $"Pinned leader of {legacy.Key} is not an eligible adult." ) );
                    }

                    if ( legacy.HostHousehold != null )
                    {
                        forced.Add( _current.FirstOrDefault( u => u.Household.Key == legacy.HostHousehold && u.HasHome ) ?? throw new InvalidOperationException( $"Pinned host household of {legacy.Key} has no eligible adults at home." ) );
                    }
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

                        if ( forced.Contains( unit ) || roll < percent )
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
                var activePool = _couldJoin.SelectMany( u => u.People ).ToList();
                var formerPool = _formerOnly.SelectMany( u => u.People ).ToList();
                var formerNeeded = _request.TestCases.Inactive + _request.TestCases.Archived;                foreach ( var status in _rules.Leaders.FallbackStatuses )
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
            private int MinimumActiveGroups => Math.Max( _request.TestCases.ActiveCount > 0 ? _request.TestCases.ActiveCount + 1 : 0, _request.Legacy.Count );

            #endregion Units

            #region Leaders and drafts

            /// <summary>
            /// Picks one leader per active group and creates the group drafts, legacy groups first.
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
                foreach ( var legacy in _request.Legacy.Where( l => l.LeaderGuid.HasValue ) )
                {
                    var leader = candidates.FirstOrDefault( c => c.Guid == legacy.LeaderGuid ) ?? throw new InvalidOperationException( $"Pinned leader of {legacy.Key} cannot lead a group." );
                    drafts.Add( new Draft { Legacy = legacy, Leader = leader, LeaderUnit = _unitByPerson[leader.Guid] } );
                }

                var pinnedHosts = _request.Legacy.Where( l => l.HostHousehold != null ).Select( l => _current.First( u => u.Household.Key == l.HostHousehold && u.HasHome ) );
                var usedUnits = drafts.Select( d => d.LeaderUnit ).Concat( pinnedHosts ).ToHashSet();
                var names = new FullNames( _request.TakenLeaderNames );
                drafts.ForEach( d => names.Add( d.Leader ) );
                var picked = new List<CastPerson>();
                foreach ( var candidate in candidates )
                {
                    if ( drafts.Count + picked.Count >= count )
                    {
                        break;
                    }

                    var unit = _unitByPerson[candidate.Guid];
                    if ( !usedUnits.Contains( unit ) && names.Add( candidate ) )
                    {
                        usedUnits.Add( unit );
                        picked.Add( candidate );
                    }
                }

                drafts.AddRange( picked.OrderBy( p => p.Guid ).Select( p => new Draft { Leader = p, LeaderUnit = _unitByPerson[p.Guid] } ) );

                foreach ( var legacy in _request.Legacy.Where( l => !l.LeaderGuid.HasValue ) )
                {
                    var draft = drafts.FirstOrDefault( d => d.Legacy == null ) ?? throw new InvalidOperationException( $"No group is left for {legacy.Key}." );
                    draft.Legacy = legacy;
                }

                foreach ( var draft in drafts.Where( d => d.Legacy?.HostHousehold != null ) )
                {
                    draft.PinnedHost = _current.First( u => u.Household.Key == draft.Legacy.HostHousehold && u.HasHome );
                }

                return drafts;
            }

            /// <summary>
            /// People allowed to lead, in a stable order: pinned leaders first, then by a seeded key per person.
            /// </summary>
            private List<CastPerson> LeaderCandidates( IEnumerable<CastPerson> people )
            {
                var pinned = _request.Legacy.Where( l => l.LeaderGuid.HasValue ).Select( l => l.LeaderGuid.Value ).ToList();
                return people
                    .Where( CanLead )
                    .OrderBy( p => pinned.Contains( p.Guid ) ? pinned.IndexOf( p.Guid ) : int.MaxValue )
                    .ThenBy( p => new SeededRandom( $"{_planner._seed}:small-groups:leader:{p.Guid}" ).Between( 0.0, 1.0 ) )
                    .ToList();
            }

            /// <summary>
            /// Whether the leader rules allow this person to lead a group.
            /// </summary>
            private bool CanLead( CastPerson person )
            {
                var leaders = _rules.Leaders;
                return person.Gender == leaders.Gender && person.Birth.Age >= leaders.MinimumAge && _leaderStatuses.Contains( person.ConnectionStatus );
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
                var usedUnits = active.Select( d => d.LeaderUnit ).ToHashSet();
                var candidates = LeaderCandidates( _formerOnly.SelectMany( u => u.People ) )
                    .Concat( LeaderCandidates( _couldJoin.SelectMany( u => u.People ) ) );
                var earliestStart = new DateTime( _clock.Anchor.Year - _rules.Membership.StartedYearsAgo.Max, 1, 1 );

                foreach ( var candidate in candidates )
                {
                    if ( drafts.Count == cases.Count )
                    {
                        break;
                    }

                    var unit = _unitByPerson[candidate.Guid];
                    var from = Max( earliestStart, AdultSince( unit ) );
                    var until = Min( _clock.LatestChange.AddDays( -60 ), unit.RecordEnded ?? DateTime.MaxValue, FirstReserved( unit ) );
                    if ( !usedUnits.Contains( unit ) && ( until - from ).TotalDays >= 365 && names.Add( candidate ) )
                    {
                        usedUnits.Add( unit );
                        drafts.Add( new Draft { Leader = candidate, LeaderUnit = unit, TestCase = cases[drafts.Count], WindowStart = from, WindowEnd = until } );
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

                // Groups with a pinned host household always meet in that home.
                var online = Apportion( _rules.MeetingStyle, all.Count, random ).Count( s => s == _rules.OnlineStyle );
                var free = all.Where( d => d.PinnedHost == null ).OrderBy( d => d.Legacy == null ? 0 : 1 ).ThenBy( _ => random.Between( 0.0, 1.0 ) ).ToList();
                all.ForEach( d => d.InPerson = true );
                free.Take( online ).ToList().ForEach( d => d.InPerson = false );

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
            /// spreading topics evenly across the campus. Runs after members are planned.
            /// </summary>
            private void ChooseTopics( List<Draft> drafts, string set )
            {
                var random = Stream( $"topics:{set}" );
                var used = _rules.Topics.ToDictionary( t => t.Value, _ => 0 );
                foreach ( var draft in drafts )
                {
                    var people = draft.Members.Where( m => m.State != MemberState.Pending ).ToList();
                    var parents = people.Count( m => m.Unit.HasChildren );
                    var fitting = _rules.Topics.Where( t => t.Fit == null || t.Fit.IsMetBy( people.Select( m => m.Person ).ToList(), parents ) ).ToList();
                    var fewest = fitting.Min( t => used[t.Value] );
                    var topic = random.Pick( fitting.Where( t => used[t.Value] == fewest ).ToList() );
                    used[topic.Value]++;
                    draft.Topic = topic;
                    draft.Description = random.Pick( topic.Descriptions );
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
            /// Places the joining units into active groups. Families with young children go first,
            /// into child care groups still under the target size; everyone else fills the smallest group.
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
                }

                var placed = active.SelectMany( d => d.Units ).ToHashSet();
                var rest = joiners.Where( u => !placed.Contains( u ) )
                    .OrderBy( u => _rules.ChildCareFollowsYoungKids && u.HasYoungKids ? 0 : 1 )
                    .ThenBy( _ => random.Between( 0.0, 1.0 ) )
                    .ToList();

                foreach ( var unit in rest )
                {
                    var open = active.Where( d => d.PeopleCount + unit.People.Count <= _rules.Size.Max ).ToList();
                    if ( open.Count == 0 )
                    {
                        continue;
                    }

                    var wantsCare = _rules.ChildCareFollowsYoungKids && unit.HasYoungKids;
                    var care = open.Where( d => d.ChildCare && d.PeopleCount + unit.People.Count <= _rules.Size.Target ).ToList();
                    var choice = ( wantsCare && care.Count > 0 ? care : open ).OrderBy( d => d.PeopleCount ).First();
                    choice.Units.Add( unit );
                }
            }

            /// <summary>
            /// Dates the group's start and each current member's join. The leader's family and a
            /// pinned host family were there from the start; others joined as the group turned over.
            /// </summary>
            private void PlanActiveMembers( Draft draft )
            {
                var random = DraftStream( draft, "members" );
                var started = DateInYear( _clock.Anchor.Year - random.Between( _rules.Membership.StartedYearsAgo.Min, _rules.Membership.StartedYearsAgo.Max ), random );
                draft.Started = Clamp( started, AdultSince( draft.LeaderUnit ), _clock.LatestChange );

                foreach ( var unit in draft.Units )
                {
                    var isFounder = unit == draft.LeaderUnit || unit == draft.PinnedHost;
                    var joined = isFounder ? draft.Started : Clamp( TenureStart( draft.Started, random ), Max( draft.Started, AdultSince( unit ) ), _clock.LatestChange );
                    foreach ( var person in unit.People )
                    {
                        draft.Members.Add( new MemberDraft { Person = person, Unit = unit, Joined = joined, State = MemberState.Active, Role = person == draft.Leader ? SmallGroupRole.Leader : SmallGroupRole.Member } );
                        Reserve( person, joined, DateTime.MaxValue );
                    }
                }
            }

            /// <summary>
            /// Adds people who joined and later left, at the configured yearly turnover. Spouses leave
            /// together, and nobody is in two groups at once.
            /// </summary>
            private void PlanLeavers( Draft draft )
            {
                var random = DraftStream( draft, "leavers" );
                var years = ( _clock.LatestChange - draft.Started ).TotalDays / 365.25;
                var target = ( int ) Math.Round( draft.PeopleCount * _rules.Membership.LeavePercentPerYear / 100.0 * years );
                var pool = _couldJoin.Where( u => !draft.Units.Contains( u ) ).ToList();
                var added = 0;
                var misses = 0;

                while ( added < target && pool.Count > 0 && misses < 50 )
                {
                    var unit = random.Pick( pool );
                    var joined = DateBetween( draft.Started, _clock.LatestChange.AddDays( -60 ), random );
                    var left = joined.AddDays( Math.Max( 30, TruncatedTenureDays( ( _clock.LatestChange - joined ).TotalDays, random ) ) );
                    if ( draft.Members.Any( m => m.Unit == unit ) || joined < AdultSince( unit ) || left > _clock.LatestChange || !IsFree( unit, joined, left ) )
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
            /// A leader with an inactive record (a family that moved) ends the group when they left.
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
                    var end = Min( draft.Ended.Value, unit.RecordEnded ?? DateTime.MaxValue );
                    if ( joined < AdultSince( unit ) || end < joined.AddDays( 30 ) || !IsFree( unit, joined, end ) )
                    {
                        continue;
                    }

                    draft.Units.Add( unit );
                    AddFormerMembers( draft, unit, joined );
                }
            }

            /// <summary>
            /// Adds a unit to an ended group. They stay members of the ended group, unless their
            /// record ended first (they moved away), in which case they left on that date.
            /// </summary>
            private void AddFormerMembers( Draft draft, Unit unit, DateTime joined )
            {
                var leftEarly = unit.RecordEnded.HasValue && unit.RecordEnded < draft.Ended;
                foreach ( var person in unit.People )
                {
                    draft.Members.Add( new MemberDraft
                    {
                        Person = person,
                        Unit = unit,
                        Joined = joined,
                        Left = leftEarly ? unit.RecordEnded : null,
                        State = leftEarly ? MemberState.Inactive : MemberState.Active,
                        Role = person == draft.Leader ? SmallGroupRole.Leader : SmallGroupRole.Member
                    } );
                    Reserve( person, joined, leftEarly ? unit.RecordEnded.Value : draft.Ended.Value );
                }
            }

            /// <summary>
            /// Adds recent join requests from people who are not in a group now.
            /// </summary>
            private void PlanPendingRequests( Draft draft )
            {
                var random = DraftStream( draft, "pending" );
                var wanted = random.Between( _rules.Membership.PendingRequests.Min, _rules.Membership.PendingRequests.Max );
                var pool = _couldJoin.Where( u => draft.Members.All( m => m.Unit != u ) && u.People.All( p => !_pending.Contains( p.Guid ) && !IsMemberNow( p ) ) )
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
                    foreach ( var person in unit.People )
                    {
                        draft.Members.Add( new MemberDraft { Person = person, Unit = unit, Joined = requested, State = MemberState.Pending, Role = SmallGroupRole.Member, IsRecent = true } );
                        _pending.Add( person.Guid );
                    }

                    added += unit.People.Count;
                    if ( added == wanted )
                    {
                        break;
                    }
                }
            }

            /// <summary>
            /// Picks the host for a group that meets in a home: the leader's spouse in the configured
            /// share of groups, otherwise another member family whose home the group meets in.
            /// </summary>
            private void ChooseHost( Draft draft )
            {
                if ( !draft.InPerson )
                {
                    return;
                }

                var random = DraftStream( draft, "host" );
                var spouse = draft.LeaderUnit.People.FirstOrDefault( p => p != draft.Leader );
                var current = draft.Members.Where( m => m.State == MemberState.Active ).Select( m => m.Unit ).Distinct().ToList();
                var others = current.Where( u => u != draft.LeaderUnit && u.HasHome ).ToList();

                Unit host;
                if ( draft.PinnedHost != null )
                {
                    host = draft.PinnedHost;
                }
                else if ( spouse != null && draft.LeaderUnit.HasHome && ( others.Count == 0 || random.Chance( _rules.Hosts.SpouseHostsPercent / 100.0 ) ) )
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

            private DateTime AdultSince( Unit unit )
            {
                return unit.People.Max( p => _adultSince[p.Guid] );
            }

            #endregion Members

            #region Names and plans

            /// <summary>
            /// Names each group for its leader ("Decker Group"), adding the first name when two
            /// leaders on the campus share a last name. Legacy groups keep their keys and Guids.
            /// </summary>
            private void NameGroups( List<Draft> drafts )
            {
                var lastNames = drafts.Select( d => d.Leader.LastName ).Concat( _request.TakenLeaderNames.Select( n => n.LastName ) )
                    .GroupBy( n => n, StringComparer.OrdinalIgnoreCase )
                    .ToDictionary( g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase );
                var keys = new HashSet<string>( _request.TakenKeys );

                foreach ( var draft in drafts.OrderBy( d => d.Legacy == null ? 1 : 0 ) )
                {
                    var leader = draft.Leader;
                    var format = lastNames[leader.LastName] > 1 ? _rules.Naming.DuplicateFormat : _rules.Naming.Format;
                    draft.Name = format.Replace( "{firstName}", leader.NickName ?? leader.FirstName ).Replace( "{lastName}", leader.LastName );

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
        /// Adults who join, leave, or ask to join together: a married couple, or one adult.
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
            /// For an inactive record, the day the household stopped attending (if known).
            /// Set only for units that may appear as former members.
            /// </summary>
            public DateTime? RecordEnded { get; set; }
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
    /// A legacy Cast group that keeps its key and Guid, with its pinned leader or host household.
    /// </summary>
    internal sealed record LegacyGroup( string Key, Guid Guid, Guid? LeaderGuid, string HostHousehold );

    /// <summary>
    /// A leader's first (or nick) name and last name.
    /// </summary>
    internal sealed record LeaderName( string FirstName, string LastName );

    /*
        10/16/26 - CLAUDE

        Cast plans are written with "yearsAgo" dates counted from a fixed planning anchor, and
        every changing date is at least one year back, so the files never depend on the day the
        planner ran and the dates resolve in the same order for any load anchor. Pending requests
        use "daysAgo" so they stay recent. Plans made at load time use the load anchor.

        Reason: Cast files must be stable, and their dates must stay in order at any anchor.
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
        /// The latest date a group can start, or a member can join or leave.
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
            return _writesYearsAgo
                ? new RelativeDate { YearsAgo = Anchor.Year - date.Year, Month = date.Month, Day = date.Day }
                : ToRecent( date );
        }

        public RelativeDate ToRecent( DateTime date )
        {
            return new RelativeDate { DaysAgo = ( Anchor - date.Date ).Days };
        }
    }
}
