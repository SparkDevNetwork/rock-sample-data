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
    /// <summary>
    /// Someone who can take an assessment, with what the rules read about them.
    /// </summary>
    internal sealed class AssessmentPerson
    {
        public int PersonId { get; set; }

        public int AliasId { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        public bool IsCast { get; set; }

        public string CampusKey { get; set; }

        /// <summary>
        /// The first day the person is an adult and out of high school. Nobody younger takes an assessment.
        /// </summary>
        public DateTime AdultSince { get; set; }

        public bool IsActiveAdult { get; set; }

        /// <summary>
        /// The household's first visit, or null when it came before the history window.
        /// </summary>
        public DateTime? FirstVisit { get; set; }

        /// <summary>
        /// A Cast person's assessment stories, by type key.
        /// </summary>
        public Dictionary<string, CastAssessment> Cast { get; set; } = new Dictionary<string, CastAssessment>();
    }

    /// <summary>
    /// A class someone took, from their step: the step type and the class's first night.
    /// </summary>
    internal sealed record AssessmentClass( int PersonId, string StepType, DateTime FirstNight );

    /// <summary>
    /// A current small group or serving team leader: the group kind ("small-group" or "serving") and when they started leading.
    /// </summary>
    internal sealed record AssessmentLeader( int PersonId, string GroupKind, DateTime Since );

    /// <summary>
    /// A loaded staff member: when they started, and their supervisor's alias, or null when they have none loaded.
    /// </summary>
    internal sealed record AssessmentStaff( int PersonId, DateTime Started, int? SupervisorAliasId );

    /// <summary>
    /// What the earlier load steps wrote, as the assessment planner needs it.
    /// </summary>
    internal sealed class AssessmentFacts
    {
        public DateTime Anchor { get; set; }

        public DateTime HistoryStart { get; set; }

        public List<AssessmentPerson> People { get; set; } = new List<AssessmentPerson>();

        public List<AssessmentClass> Classes { get; set; } = new List<AssessmentClass>();

        public List<AssessmentLeader> Leaders { get; set; } = new List<AssessmentLeader>();

        public List<AssessmentStaff> Staff { get; set; } = new List<AssessmentStaff>();

        /// <summary>
        /// The alias of each loaded staff member, by staff key.
        /// </summary>
        public Dictionary<string, int> StaffAliases { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// The alias of each campus's campus pastor, by campus key.
        /// </summary>
        public Dictionary<string, int> CampusPastorAliases { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// One Assessment row. A row with no completion is pending. A row with a score and no answers is a legacy result.
    /// </summary>
    internal sealed class PlannedAssessment
    {
        public int PersonId { get; set; }

        public int AliasId { get; set; }

        public string Type { get; set; }

        /// <summary>
        /// Which rule made it, for the load summary: staff, a class step type, leader, self, or cast.
        /// </summary>
        public string Source { get; set; }

        public int? RequesterAliasId { get; set; }

        public DateTime? Requested { get; set; }

        public DateTime? Due { get; set; }

        public DateTime? Completed { get; set; }

        public DateTime? LastReminder { get; set; }

        public AssessmentAnswers Answers { get; set; }

        public AssessmentScore Score { get; set; }

        public double SecondsToTake { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// When Rock created the row: when it was requested, or when it was taken without a request.
        /// </summary>
        public DateTime Created => Requested ?? Completed.Value;

        public bool IsPending => !Completed.HasValue;
    }

    /// <summary>
    /// One person attribute value an assessment saved: the value from the person's latest result.
    /// </summary>
    internal sealed record PlannedAssessmentValue( int PersonId, string AttributeKey, string Value, DateTime Created, DateTime Modified );

    internal sealed class AssessmentPlan
    {
        /// <summary>
        /// Every row, in the order Rock created them.
        /// </summary>
        public List<PlannedAssessment> Assessments { get; } = new List<PlannedAssessment>();

        public List<PlannedAssessmentValue> Values { get; } = new List<PlannedAssessmentValue>();
    }

    /*
        11/16/26 - CLAUDE

        Each rule offers requests and self-started takes, and every offer for one person and type runs through one small
        state machine in date order. A request is dropped while an earlier request is still waiting inside Rock's 60-day
        reminder window, or when the person finished one in the last MinimumDaysToRetake (365) days, so completed results
        are always at least a year apart. A request still waiting after the window stays pending when a new one comes,
        as Rock's own request action leaves it. A leader is asked only if they have never taken that type. Then each
        accepted row that finished gets answers and Rock's score, and the person's attributes come from their latest
        result of each type.

        Reason: decision D-assessments-2.
    */

    /// <summary>
    /// Plans every assessment from configuration/assessments.json, the Cast's stories, and what earlier steps wrote.
    /// </summary>
    internal sealed class AssessmentPlanner
    {
        /// <summary>
        /// Rock's AssessmentType.MinimumDaysToRetake for all five types.
        /// </summary>
        public const int MinimumDaysToRetake = 365;

        /// <summary>
        /// Rock's Send Assessment Reminders job: every 7 days, for 60 days after the request, at 8 AM each day.
        /// </summary>
        public const int ReminderEveryDays = 7;

        public const int ReminderCutoffDays = 60;

        private static readonly TimeSpan ReminderJobTime = new TimeSpan( 8, 0, 0 );

        private readonly AssessmentsConfiguration _config;
        private readonly Dictionary<string, AssessmentInstrument> _instruments;
        private readonly AssessmentFacts _facts;
        private readonly string _seed;
        private readonly DateTime _adopted;

        private AssessmentPlanner( AssessmentsConfiguration config, Dictionary<string, AssessmentInstrument> instruments, AssessmentFacts facts, string seed, DateTime adopted )
        {
            _config = config;
            _instruments = instruments;
            _facts = facts;
            _seed = seed;
            _adopted = adopted;
        }

        /// <param name="adopted">The day the church adopted assessments, resolved from the configuration.</param>
        /// <param name="resolve">Resolves a Cast relative date.</param>
        public static AssessmentPlan Plan( AssessmentsConfiguration config, Dictionary<string, AssessmentInstrument> instruments, AssessmentFacts facts, string seed, DateTime adopted, Func<RelativeDate, DateTime?> resolve )
        {
            return new AssessmentPlanner( config, instruments, facts, seed, adopted ).Plan( resolve );
        }

        private AssessmentPlan Plan( Func<RelativeDate, DateTime?> resolve )
        {
            CheckLeanings();
            var people = _facts.People.ToDictionary( p => p.PersonId );
            var offers = new List<Offer>();
            offers.AddRange( CastOffers( resolve ) );
            offers.AddRange( StaffOffers( people ) );
            offers.AddRange( ClassOffers( people ) );
            offers.AddRange( LeaderOffers( people ) );
            offers.AddRange( SelfStartedOffers() );

            var plan = new AssessmentPlan();
            var roles = _facts.Leaders.GroupBy( l => l.PersonId ).ToDictionary( g => g.Key, g => g.Select( l => l.GroupKind == "small-group" ? "small-group-leader" : "team-leader" ).Distinct().ToList() );
            foreach ( var personOffers in offers.GroupBy( o => (o.Person.PersonId, o.Type) ).OrderBy( g => g.Key.PersonId ).ThenBy( g => g.Key.Type, StringComparer.Ordinal ) )
            {
                var person = personOffers.First().Person;
                var rows = Accept( personOffers.OrderBy( o => o.Date ).ThenBy( o => o.Source, StringComparer.Ordinal ).ToList() );
                Score( person, personOffers.Key.Type, rows, roles.TryGetValue( person.PersonId, out var personRoles ) ? personRoles : new List<string>() );
                plan.Assessments.AddRange( rows );
                plan.Values.AddRange( Values( person, personOffers.Key.Type, rows ) );
            }

            plan.Assessments.Sort( ( a, b ) =>
            {
                var byCreated = a.Created.CompareTo( b.Created );
                return byCreated != 0 ? byCreated : a.PersonId != b.PersonId ? a.PersonId.CompareTo( b.PersonId ) : string.CompareOrdinal( a.Type, b.Type );
            } );

            foreach ( var personRows in plan.Assessments.GroupBy( a => a.PersonId ) )
            {
                SetReminders( personRows.ToList() );
            }

            return plan;
        }

        #region Offers

        /// <summary>
        /// A request or a self-started take a rule offers, before the state machine accepts or drops it.
        /// </summary>
        private sealed class Offer
        {
            public AssessmentPerson Person { get; set; }

            public string Type { get; set; }

            public string Source { get; set; }

            /// <summary>
            /// When it was requested, or taken without a request.
            /// </summary>
            public DateTime Date { get; set; }

            public bool IsRequest { get; set; }

            public int? RequesterAliasId { get; set; }

            public DateTime? Due { get; set; }

            /// <summary>
            /// When the person finishes it, or null when they never do.
            /// </summary>
            public DateTime? Completed { get; set; }

            public bool OnlyIfNeverTaken { get; set; }

            public CastDiscScores Legacy { get; set; }

            /// <summary>
            /// A Cast take, which the state machine never drops.
            /// </summary>
            public bool IsCastTake { get; set; }
        }

        private IEnumerable<Offer> CastOffers( Func<RelativeDate, DateTime?> resolve )
        {
            foreach ( var person in _facts.People.Where( p => p.IsCast ) )
            {
                foreach ( var (type, story) in person.Cast.OrderBy( c => c.Key, StringComparer.Ordinal ) )
                {
                    var random = new SeededRandom( $"{_seed}:assessments:cast:{person.Guid}:{type}" );
                    foreach ( var take in story.Takes )
                    {
                        var requested = resolve( take.Requested )?.Add( OfficeHours( random ) );
                        var completed = resolve( take.Completed )?.Add( EveningHours( random ) );
                        int? requester = null;
                        if ( take.RequestedBy != null && !_facts.StaffAliases.TryGetValue( take.RequestedBy, out var alias ) )
                        {
                            throw new InvalidDataException( $"{person.Name}'s {type} story is requested by {take.RequestedBy}, who is not loaded." );
                        }
                        else if ( take.RequestedBy != null )
                        {
                            requester = _facts.StaffAliases[take.RequestedBy];
                        }

                        if ( completed.HasValue && completed.Value.Date >= _facts.Anchor || requested > _facts.Anchor )
                        {
                            throw new InvalidDataException( $"{person.Name}'s {type} story is dated after the install day." );
                        }

                        yield return new Offer
                        {
                            Person = person,
                            Type = type,
                            Source = "cast",
                            Date = requested ?? completed.Value,
                            IsRequest = requested.HasValue,
                            RequesterAliasId = requester,
                            Due = requested?.Date.AddDays( 14 ),
                            Completed = completed,
                            Legacy = take.Legacy,
                            IsCastTake = true
                        };
                    }
                }
            }
        }

        private IEnumerable<Offer> StaffOffers( Dictionary<int, AssessmentPerson> people )
        {
            var rule = _config.Staff;
            if ( rule == null )
            {
                yield break;
            }

            foreach ( var staff in _facts.Staff.OrderBy( s => s.PersonId ) )
            {
                var person = people[staff.PersonId];
                var random = new SeededRandom( $"{_seed}:assessments:staff:{person.Guid}" );
                var start = Later( staff.Started, _adopted, person.AdultSince ).AddDays( random.Between( 1, 7 ) );
                var asked = start.Add( OfficeHours( random ) );
                var isNew = ( _facts.Anchor - staff.Started ).TotalDays < rule.PendingIfStartedWithinDays;
                foreach ( var type in rule.Types.Where( t => IsRuled( person, t ) ) )
                {
                    if ( !staff.SupervisorAliasId.HasValue )
                    {
                        // No one asks the person at the top of the org chart, so they take each one on their own.
                        if ( !isNew && asked.Date < _facts.Anchor )
                        {
                            yield return new Offer { Person = person, Type = type, Source = "staff", Date = asked, Completed = asked };
                        }

                        continue;
                    }

                    yield return new Offer
                    {
                        Person = person,
                        Type = type,
                        Source = "staff",
                        Date = asked,
                        IsRequest = true,
                        RequesterAliasId = staff.SupervisorAliasId,
                        Due = asked.Date.AddDays( rule.DueInDays ),
                        Completed = Finishes( person, type, !isNew ) ? Finish( asked, rule.FinishWithinDays, random ) : null
                    };
                }
            }
        }

        private IEnumerable<Offer> ClassOffers( Dictionary<int, AssessmentPerson> people )
        {
            foreach ( var rule in _config.Classes )
            {
                foreach ( var taken in _facts.Classes.Where( c => c.StepType == rule.StepType ).OrderBy( c => c.PersonId ).ThenBy( c => c.FirstNight ) )
                {
                    var person = people[taken.PersonId];
                    var night = taken.FirstNight.Date.AddDays( 7 * ( rule.RequestOnNight - 1 ) );
                    if ( night < _adopted || night < person.AdultSince || night > _facts.Anchor )
                    {
                        continue;
                    }

                    var random = new SeededRandom( $"{_seed}:assessments:{rule.StepType}:{person.Guid}:{taken.FirstNight:yyyyMMdd}" );
                    var asked = night.AddHours( 19 ).AddMinutes( random.Between( 0, 90 ) );
                    foreach ( var type in rule.Types.Where( t => IsRuled( person, t ) ) )
                    {
                        yield return new Offer
                        {
                            Person = person,
                            Type = type,
                            Source = rule.StepType,
                            Date = asked,
                            IsRequest = true,
                            RequesterAliasId = Requester( rule.RequestedBy, person ),
                            Due = taken.FirstNight.Date.AddDays( 7 * ( rule.DueOnNight - 1 ) ),
                            Completed = Finishes( person, type, random.Chance( rule.PercentFinished / 100 ) ) ? Finish( asked, rule.FinishWithinDays, random ) : null
                        };
                    }
                }
            }
        }

        private IEnumerable<Offer> LeaderOffers( Dictionary<int, AssessmentPerson> people )
        {
            var rule = _config.Leaders;
            if ( rule == null )
            {
                yield break;
            }

            foreach ( var leader in _facts.Leaders.OrderBy( l => l.PersonId ).ThenBy( l => l.Since ).ThenBy( l => l.GroupKind, StringComparer.Ordinal ) )
            {
                var person = people[leader.PersonId];
                var random = new SeededRandom( $"{_seed}:assessments:leader:{person.Guid}:{leader.GroupKind}:{leader.Since:yyyyMMdd}" );
                var day = Later( leader.Since.Date.AddDays( -random.Between( rule.RequestDaysBeforeLeading.Min, rule.RequestDaysBeforeLeading.Max ) ), _adopted, person.AdultSince );
                if ( day >= _facts.Anchor.AddDays( -1 ) )
                {
                    continue;
                }

                var asked = day.Add( OfficeHours( random ) );
                foreach ( var type in rule.Types.Where( t => IsRuled( person, t ) ) )
                {
                    yield return new Offer
                    {
                        Person = person,
                        Type = type,
                        Source = "leader",
                        Date = asked,
                        IsRequest = true,
                        RequesterAliasId = Requester( rule.RequestedBy[leader.GroupKind], person ),
                        Due = asked.Date.AddDays( rule.DueInDays ),
                        Completed = Finishes( person, type, true ) ? Finish( asked, rule.FinishWithinDays, random ) : null,
                        OnlyIfNeverTaken = true
                    };
                }
            }
        }

        private IEnumerable<Offer> SelfStartedOffers()
        {
            var rule = _config.SelfStarted;
            if ( rule == null )
            {
                yield break;
            }

            var years = ( int ) Math.Round( ( _facts.Anchor - _facts.HistoryStart ).TotalDays / 365.25 );
            foreach ( var person in _facts.People.Where( p => p.IsActiveAdult ).OrderBy( p => p.PersonId ) )
            {
                var random = new SeededRandom( $"{_seed}:assessments:self:{person.Guid}" );
                for ( var year = 0; year < years; year++ )
                {
                    if ( !random.Chance( rule.PercentOfActiveAdultsPerYear / 100 ) )
                    {
                        continue;
                    }

                    var type = random.Pick( rule.Types );
                    var from = Later( _facts.Anchor.AddYears( -year - 1 ), person.AdultSince, person.FirstVisit ?? _facts.HistoryStart, _adopted );
                    var until = _facts.Anchor.AddYears( -year );
                    if ( from >= until || !IsRuled( person, type ) )
                    {
                        continue;
                    }

                    var taken = from.AddDays( Math.Floor( random.Between( 0.0, ( until - from ).TotalDays ) ) ).Add( EveningHours( random ) );
                    if ( taken.Date >= _facts.Anchor )
                    {
                        continue;
                    }

                    yield return new Offer { Person = person, Type = type, Source = "self", Date = taken, Completed = taken };
                }
            }
        }

        /// <summary>
        /// Whether the rules make this person's assessments of a type: a Cast story with its own takes replaces them.
        /// </summary>
        private static bool IsRuled( AssessmentPerson person, string type )
        {
            return !person.Cast.TryGetValue( type, out var story ) || story.Takes.Count == 0;
        }

        /// <summary>
        /// Whether the person finishes a request: a Cast story's "complete" or "pending", or the rule's draw.
        /// </summary>
        private static bool Finishes( AssessmentPerson person, string type, bool drawn )
        {
            return person.Cast.TryGetValue( type, out var story ) && story.Requests != null ? story.Requests == "complete" : drawn;
        }

        /// <summary>
        /// When a request is finished: within the days given, in the evening, and never on or after the install day,
        /// which leaves it pending on install day, as if the person will finish it later.
        /// </summary>
        private DateTime? Finish( DateTime asked, int withinDays, SeededRandom random )
        {
            // Many finish within a day or two, and the rest spread out to the due date.
            var days = ( int ) Math.Floor( Math.Pow( random.Between( 0.0, 1.0 ), 1.6 ) * ( withinDays + 1 ) );
            var finished = asked.Date.AddDays( days ).Add( EveningHours( random ) );
            if ( finished <= asked )
            {
                finished = asked.AddMinutes( random.Between( 20, 90 ) );
            }

            return finished.Date >= _facts.Anchor ? null : finished;
        }

        private int? Requester( string requestedBy, AssessmentPerson person )
        {
            if ( requestedBy == "campus-pastor" )
            {
                return _facts.CampusPastorAliases.TryGetValue( person.CampusKey, out var pastor ) && pastor != person.AliasId ? pastor : null;
            }

            return _facts.StaffAliases.TryGetValue( requestedBy, out var alias ) && alias != person.AliasId ? alias : null;
        }

        private static TimeSpan OfficeHours( SeededRandom random ) => TimeSpan.FromMinutes( random.Between( 9 * 60, 16 * 60 + 30 ) );

        private static TimeSpan EveningHours( SeededRandom random ) => TimeSpan.FromMinutes( random.Between( 7 * 60, 22 * 60 + 30 ) );

        private static DateTime Later( params DateTime[] dates ) => dates.Max().Date;

        #endregion

        #region The state machine

        /// <summary>
        /// Accepts or drops one person's offers of one type, in date order. Every accepted offer becomes a row.
        /// </summary>
        private List<PlannedAssessment> Accept( List<Offer> offers )
        {
            var rows = new List<PlannedAssessment>();
            foreach ( var offer in offers )
            {
                var lastCompleted = rows.Where( r => r.Completed <= offer.Date ).Select( r => r.Completed ).Max();
                var waiting = rows.Any( r => r.Created <= offer.Date && ( r.IsPending || r.Completed > offer.Date )
                    && ( !offer.IsRequest || ( offer.Date - r.Created ).TotalDays <= ReminderCutoffDays ) );
                var tooSoon = lastCompleted.HasValue && ( offer.Date - lastCompleted.Value ).TotalDays < MinimumDaysToRetake;
                var alreadyTaken = offer.OnlyIfNeverTaken && rows.Any( r => r.Completed <= offer.Date );
                if ( !offer.IsCastTake && ( waiting || tooSoon || alreadyTaken ) )
                {
                    continue;
                }

                rows.Add( new PlannedAssessment
                {
                    PersonId = offer.Person.PersonId,
                    AliasId = offer.Person.AliasId,
                    Type = offer.Type,
                    Source = offer.Source,
                    RequesterAliasId = offer.IsRequest ? offer.RequesterAliasId : null,
                    Requested = offer.IsRequest ? offer.Date : null,
                    Due = offer.Due,
                    Completed = offer.Completed,
                    Score = offer.Legacy == null ? null : _instruments[offer.Type].Legacy( offer.Legacy )
                } );
            }

            CheckRetakes( offers.First().Person, offers.First().Type, rows );
            return rows;
        }

        private static void CheckRetakes( AssessmentPerson person, string type, List<PlannedAssessment> rows )
        {
            var completed = rows.Where( r => r.Completed.HasValue ).Select( r => r.Completed.Value ).OrderBy( d => d ).ToList();
            for ( var i = 1; i < completed.Count; i++ )
            {
                if ( ( completed[i] - completed[i - 1] ).TotalDays < MinimumDaysToRetake )
                {
                    throw new InvalidDataException( $"{person.Name} takes {type} again {( completed[i] - completed[i - 1] ).TotalDays:N0} days after the last time, sooner than Rock's {MinimumDaysToRetake}." );
                }
            }
        }

        #endregion

        #region Answers and scores

        /// <summary>
        /// Every leaning a configuration names must be a dimension of its type.
        /// </summary>
        private void CheckLeanings()
        {
            foreach ( var (role, types) in _config.Leanings )
            {
                foreach ( var (type, dimensions) in types )
                {
                    if ( !_instruments.TryGetValue( type, out var instrument ) )
                    {
                        throw new InvalidDataException( $"configuration/assessments.json: the {role} leaning names {type}, which is not an assessment type." );
                    }

                    var unknown = dimensions.Keys.Where( d => !instrument.Dimensions.Contains( d ) ).ToList();
                    if ( unknown.Count > 0 )
                    {
                        throw new InvalidDataException( $"configuration/assessments.json: the {role} leaning for {type} names {string.Join( ", ", unknown )}, which {type} does not score. It scores {string.Join( ", ", instrument.Dimensions )}." );
                    }
                }
            }
        }

        /*
            11/16/26 - CLAUDE

            A person's lean on each dimension is drawn once per type, so a retake comes out close to their first result,
            as a real retake does. A leader leans a little toward what the role draws on. A Cast story that names gifts
            or letters leans hard toward them, and its answers are drawn again until those come out on top.

            Reason: Results look like real people with real strengths, and Cast stories always hold.
        */
        private void Score( AssessmentPerson person, string type, List<PlannedAssessment> rows, List<string> roles )
        {
            var instrument = _instruments[type];
            var minutes = _config.Types.Single( t => t.Key == type ).Minutes;
            var leanRandom = new SeededRandom( $"{_seed}:assessments:lean:{person.Guid}:{type}" );
            var leaning = instrument.Dimensions.ToDictionary( d => d, d => Normal( leanRandom ) );
            foreach ( var role in roles )
            {
                if ( _config.Leanings.TryGetValue( role, out var byType ) && byType.TryGetValue( type, out var leans ) )
                {
                    foreach ( var (dimension, lean) in leans )
                    {
                        leaning[dimension] += lean;
                    }
                }
            }

            var named = person.Cast.TryGetValue( type, out var story ) ? story.Leaning : new List<string>();
            foreach ( var dimension in named )
            {
                if ( !leaning.ContainsKey( dimension ) )
                {
                    throw new InvalidDataException( $"{person.Name}'s {type} story leans toward {dimension}, which {type} does not score. It scores {string.Join( ", ", instrument.Dimensions )}." );
                }

                leaning[dimension] = Math.Max( leaning[dimension], 0 ) + 2;
            }

            for ( var i = 0; i < rows.Count; i++ )
            {
                var row = rows[i];
                row.Guid = SeededRandom.StableGuid( $"{_seed}:assessments:row:{person.Guid}:{type}:{i}" );
                if ( row.IsPending || row.Score != null )
                {
                    continue;
                }

                var random = new SeededRandom( $"{_seed}:assessments:answers:{person.Guid}:{type}:{i}" );
                for ( var attempt = 0; ; attempt++ )
                {
                    row.Answers = instrument.Answer( leaning, random );
                    row.Score = instrument.Score( row.Answers );
                    if ( named.All( d => row.Score.Ranking.Take( instrument.TopCount ).Contains( d ) ) )
                    {
                        break;
                    }

                    if ( attempt == 200 )
                    {
                        throw new InvalidDataException( $"{person.Name}'s {type} answers never put {string.Join( ", ", named )} in the top {instrument.TopCount}." );
                    }
                }

                row.SecondsToTake = Math.Round( random.Between( minutes.Min * 60.0, minutes.Max * 60.0 ), 3 );
            }
        }

        private static double Normal( SeededRandom random )
        {
            return Math.Sqrt( -2 * Math.Log( 1 - random.Between( 0.0, 1.0 ) ) ) * Math.Cos( 2 * Math.PI * random.Between( 0.0, 1.0 ) );
        }

        /// <summary>
        /// The person attribute values Rock's service leaves after the person's results, in order: the latest result's
        /// values, plus its save date. A value that was never set is not saved when it is empty, as Rock's
        /// SaveAttributeValues adds no empty value, but a value that was set before is cleared.
        /// </summary>
        private IEnumerable<PlannedAssessmentValue> Values( AssessmentPerson person, string type, List<PlannedAssessment> rows )
        {
            var saveDateKey = _instruments[type].LastSaveDateKey;
            var current = new Dictionary<string, (string Value, DateTime Created, DateTime Modified)>();
            foreach ( var row in rows.Where( r => r.Completed.HasValue ).OrderBy( r => r.Completed ) )
            {
                var values = row.Score.Values.ToList();
                if ( saveDateKey != null )
                {
                    values.Add( new KeyValuePair<string, string>( saveDateKey, row.Completed.Value.ToString( "o" ) ) );
                }

                foreach ( var (key, value) in values )
                {
                    if ( current.TryGetValue( key, out var existing ) )
                    {
                        current[key] = (value, existing.Created, row.Completed.Value);
                    }
                    else if ( !string.IsNullOrWhiteSpace( value ) )
                    {
                        current[key] = (value, row.Completed.Value, row.Completed.Value);
                    }
                }
            }

            return current.Select( c => new PlannedAssessmentValue( person.PersonId, c.Key, c.Value.Value, c.Value.Created, c.Value.Modified ) );
        }

        #endregion

        #region Reminders

        /*
            11/16/26 - CLAUDE

            Rock's Send Assessment Reminders job runs every morning at 8. For each person, it looks at the latest row of
            each type, and when one is pending, was requested no more than 60 days ago, and was last reminded (or
            requested) at least 7 days ago, it emails the person once and stamps LastReminderDate on the latest pending
            row of every type they have. The loader replays the job for every day before the install day, so its first
            run after install sends exactly the reminders a church's would.

            Reason: decision D-assessments-3.
        */
        private void SetReminders( List<PlannedAssessment> rows )
        {
            var requested = rows.Where( r => r.Requested.HasValue ).ToList();
            if ( requested.Count == 0 )
            {
                return;
            }

            var days = requested.SelectMany( r => Enumerable.Range( ReminderEveryDays, ReminderCutoffDays - ReminderEveryDays + 1 ).Select( d => r.Requested.Value.Date.AddDays( d ) ) )
                .Where( d => d < _facts.Anchor )
                .Distinct()
                .OrderBy( d => d );
            foreach ( var day in days )
            {
                var run = day.Add( ReminderJobTime );
                bool PendingAt( PlannedAssessment r ) => r.Created <= run && ( r.IsPending || r.Completed > run );
                var latest = rows.Where( r => r.Created <= run ).GroupBy( r => r.Type ).Select( g => g.Last() ).ToList();
                var due = latest.Any( r => PendingAt( r ) && r.Requested.HasValue && day <= r.Requested.Value.AddDays( ReminderCutoffDays )
                    && ( r.LastReminder == null ? day.AddDays( -ReminderEveryDays ) >= r.Requested.Value.Date : day.AddDays( -ReminderEveryDays ) >= r.LastReminder.Value.Date ) );
                if ( !due )
                {
                    continue;
                }

                foreach ( var typeRows in rows.Where( PendingAt ).GroupBy( r => r.Type ) )
                {
                    typeRows.Last().LastReminder = run;
                }
            }
        }

        #endregion
    }
}
