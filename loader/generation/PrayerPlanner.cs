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
    /// Plans every prayer request in the history window, and what the prayer team did with each one:
    /// approval, prayers, flags, comments, and answers (decision 67). Plans only; <see cref="Loading.PrayerLoader"/> writes.
    /// </summary>
    /// <remarks>
    /// Rock's prayer screens follow a request's life: it is entered, a prayer administrator approves it
    /// (which sets it to expire 14 days later), the prayer team prays through it while it is approved and
    /// not expired, and a few get comments, flags, or an answer. Every number comes from the profile's
    /// prayer section.
    /// </remarks>
    internal sealed class PrayerPlanner
    {
        private readonly ProfileTargets _profile;
        private readonly string _seed;
        private readonly DateTime _start;
        private readonly DateTime _now;

        /// <param name="start">The first moment requests can be entered.</param>
        /// <param name="now">The install moment: nothing happens at or after it.</param>
        public PrayerPlanner( ProfileTargets profile, string seed, DateTime start, DateTime now )
        {
            _profile = profile;
            _seed = seed;
            _start = start;
            _now = now;
        }

        /// <summary>
        /// Plans the requests, oldest first.
        /// </summary>
        public PrayerPlan Plan( IReadOnlyList<PrayerPerson> people, IReadOnlyList<PrayerCategory> categories, PrayerText text )
        {
            var active = people.Where( p => p.IsActive ).OrderBy( p => p.PersonId ).ToList();
            var plan = new PrayerPlan { ActiveAdults = active.Count };
            if ( active.Count == 0 )
            {
                return plan;
            }

            plan.Team = PickTeam( people, active );
            plan.Admins = people.Where( p => p.IsAdmin ).OrderBy( p => p.PersonId ).ToList();
            if ( plan.Admins.Count == 0 )
            {
                plan.Admins = plan.Team.Take( 1 ).Select( t => t.Person ).ToList();
            }

            var years = ( _now - _start ).TotalDays / 365.25;
            var total = ( int ) Math.Round( Number( "prayer.requestsPerYearPerThousandActiveAdults", 250 ) * active.Count / 1000 * years );
            var linked = ( int ) Math.Round( total * Number( "prayer.pctLinkedToPerson", 94 ) / 100 );

            var slots = AssignRequesters( active, plan.Team, linked, years );
            var guestCampuses = active.Select( p => p.CampusId ).ToList();
            var guests = new SeededRandom( $"{_seed}:prayer:guests" );
            for ( var i = slots.Count; i < total; i++ )
            {
                slots.Add( null );
            }

            var days = new DayPicker( _start, _now, Map( "prayer.monthIndex" ), Map( "prayer.weekdayMix" ) );
            var timing = new SeededRandom( $"{_seed}:prayer:entered" );
            var hourBands = Map( "prayer.hourBandMix" );
            var requests = slots
                .Select( person => (Person: person, Entered: days.Pick( timing ).AddHours( HourIn( timing, hourBands ) ).AddMinutes( timing.Between( 0, 59 ) )) )
                .Where( s => s.Entered < _now )
                .OrderBy( s => s.Entered )
                .ThenBy( s => s.Person?.PersonId ?? 0 )
                .Select( s => s.Person == null
                    ? Guest( guests, guestCampuses, s.Entered )
                    : new PlannedPrayerRequest { Person = s.Person, FirstName = s.Person.FirstName, LastName = s.Person.LastName, Email = s.Person.Email, CampusId = s.Person.CampusId, Entered = s.Entered } )
                .ToList();

            var categoryWeights = categories.Where( c => c.Weight > 0 ).ToDictionary( c => c.Key, c => c.Weight );
            var categoriesByKey = categories.ToDictionary( c => c.Key );
            for ( var i = 0; i < requests.Count; i++ )
            {
                var random = new SeededRandom( $"{_seed}:prayer:request:{i}" );
                var request = requests[i];
                var category = categoriesByKey[random.Pick( categoryWeights )];
                request.CategoryId = category.Id;
                request.Text = Compose( random, text.Categories[category.Key], text.Shared.Closings );
                request.IsUrgent = random.Chance( Number( "prayer.pctUrgent", 5 ) / 100 );
                request.IsPublic = random.Chance( Number( "prayer.pctPublic", 78 ) / 100 );
                request.AllowComments = random.Chance( Number( "prayer.pctAllowComments", 40 ) / 100 );
                Approve( random, request, plan.Admins );
                Pray( random, request, plan.Team );
                request.AnswerPool = text.Categories[category.Key].Answers;
            }

            ShareWithGroups( requests );
            CloseAndAnswer( requests );
            Comment( requests, text.Comments );
            plan.Requests = requests;
            return plan;
        }

        #region Who

        /*
            10/20/26 - CLAUDE

            The prayer team is the Cast's Prayer Team plus enough other active adults to reach the profile's
            share. Older adults and small group members are more likely to serve on it. Each member has a
            "busyness": a few pray far more than the rest, so the busiest tenth make about a third of the
            prayers. The Cast's members are among the busiest, so their profiles show prayer activity.

            Reason: Real prayer teams lean on a few faithful members, and the Cast should show it.
        */
        private List<PrayerTeamMember> PickTeam( IReadOnlyList<PrayerPerson> people, List<PrayerPerson> active )
        {
            var team = people.Where( p => p.IsCastPrayerTeam ).OrderBy( p => p.PersonId ).Select( p => new PrayerTeamMember( p, 3.0 ) ).ToList();
            var size = ( int ) Math.Ceiling( active.Count * Number( "prayer.team.pctOfActiveAdultsWhoPrayedIn24Months", 3 ) / 100 );
            var random = new SeededRandom( $"{_seed}:prayer:team" );
            var candidates = active
                .Where( p => !p.IsCastPrayerTeam )
                .Select( p => (Person: p, Weight: ( p.Age >= 40 ? 2.0 : 1.0 ) * ( p.GroupIds.Count > 0 ? 2.0 : 1.0 )) )
                .ToList();

            foreach ( var candidate in WeightedOrder( random, candidates, c => c.Weight ).Take( Math.Max( 0, size - team.Count ) ) )
            {
                team.Add( new PrayerTeamMember( candidate.Person, random.LogNormal( 0.95 ) ) );
            }

            return team;
        }

        /*
            10/20/26 - CLAUDE

            Each age band and gender gets its quota of requests from the profile's mix of people who ask,
            filled by people in that group in a random order. A weighted draw alone flattens toward the
            church's own mix, because about a third of active adults ask. A person's band is their age
            halfway through the window, close to their age when they ask. Prayer team members come first
            more often, five times as likely as others.

            Repeat counts run a little above the profile's 24-month mix, because the window is longer and
            the profile measures only part of it.

            Reason: The requester mix matches the profile at any volume.
        */
        private List<PrayerPerson> AssignRequesters( List<PrayerPerson> active, List<PrayerTeamMember> team, int count, double years )
        {
            var random = new SeededRandom( $"{_seed}:prayer:requesters" );
            var ageTargets = Map( "prayer.requesters.ageMixAmongKnown" );
            var pctFemale = Number( "prayer.requesters.pctFemaleAmongKnown", 60 ) / 100;
            var askMix = Map( "prayer.requesters.requestsPerRequesterMix" );
            var teamIds = team.Select( t => t.Person.PersonId ).ToHashSet();

            double Share( (string Band, bool IsFemale) cell ) => ageTargets.GetValueOrDefault( cell.Band ) / 100 * ( cell.IsFemale ? pctFemale : 1 - pctFemale );

            var cells = active
                .GroupBy( p => (Band: AgeBand( ( int ) Math.Round( p.Age - years / 2 ) ), p.IsFemale) )
                .OrderBy( g => g.Key.Band, StringComparer.Ordinal )
                .ThenBy( g => g.Key.IsFemale )
                .ToList();
            var totalShare = cells.Sum( c => Share( c.Key ) );
            var slots = new List<PrayerPerson>();
            var carried = 0.0;
            foreach ( var cell in cells )
            {
                var quota = ( totalShare > 0 ? count * Share( cell.Key ) / totalShare : 0 ) + carried;
                var filled = 0;
                var askers = new List<PrayerPerson>();
                foreach ( var person in WeightedOrder( random, cell, p => teamIds.Contains( p.PersonId ) ? 5.0 : 1.0 ) )
                {
                    if ( filled >= Math.Round( quota ) )
                    {
                        break;
                    }

                    var times = random.Pick( askMix ) switch
                    {
                        "1" => 1,
                        "2-3" => random.Between( 2, 4 ),
                        _ => random.Between( 5, 8 )
                    };
                    askers.Add( person );
                    for ( var i = 0; i < times && filled < Math.Round( quota ); i++ )
                    {
                        slots.Add( person );
                        filled++;
                    }
                }

                // When everyone in a group has asked and its quota is not met, people who asked ask once more.
                foreach ( var person in askers.Take( ( int ) Math.Max( 0, Math.Round( quota ) - filled ) ) )
                {
                    slots.Add( person );
                    filled++;
                }

                // What a group still cannot fill passes to the next one.
                carried = quota - filled;
            }

            foreach ( var person in WeightedOrder( random, active, _ => 1.0 ).Take( Math.Max( 0, count - slots.Count ) ) )
            {
                slots.Add( person );
            }

            return slots;
        }

        /// <summary>
        /// A request from someone with no person record: Rock keeps only the name and email they typed.
        /// </summary>
        private static PlannedPrayerRequest Guest( SeededRandom random, List<int> campuses, DateTime entered )
        {
            var isFemale = random.Chance( 0.6 );
            var first = random.Pick( isFemale ? CrowdNames.FemaleFirstNames : CrowdNames.MaleFirstNames );
            var last = random.Pick( CrowdNames.LastNames );
            return new PlannedPrayerRequest
            {
                FirstName = first,
                LastName = last,
                Email = random.Chance( 0.8 ) ? $"{first}.{last}@example.org".ToLowerInvariant() : null,
                CampusId = random.Pick( campuses ),
                Entered = entered
            };
        }

        #endregion

        #region What

        /*
            10/20/26 - CLAUDE

            A request's text is one request from its category's pool, then some details, then sometimes a
            closing. The detail mix was tuned against the pools so lengths come out near the profile:
            about 100 characters at the 10th percentile, 240 at the median, and 500 at the 90th. Almost half
            of the short requests stay as they are, because short prayer cards are real.

            Reason: Lengths match the profile without writing one long story per request.
        */
        private static readonly (int Details, double Weight)[] DetailMix = { (0, 0.35), (1, 0.25), (2, 0.15), (3, 0.10), (4, 0.07), (6, 0.05), (8, 0.03) };

        private static string Compose( SeededRandom random, PrayerCategoryText pool, List<string> closings )
        {
            var text = random.Pick( pool.Requests );
            if ( text.Length < 100 && random.Chance( 0.45 ) )
            {
                return text;
            }

            var roll = random.Between( 0.0, 1.0 );
            var count = DetailMix.Last().Details;
            foreach ( var (details, weight) in DetailMix )
            {
                roll -= weight;
                if ( roll < 0 )
                {
                    count = details;
                    break;
                }
            }

            var detailOrder = Enumerable.Range( 0, pool.Details.Count ).OrderBy( _ => random.Between( 0.0, 1.0 ) ).Take( count );
            var parts = new List<string> { text };
            parts.AddRange( detailOrder.Select( d => pool.Details[d] ) );
            if ( random.Chance( 0.3 ) )
            {
                parts.Add( random.Pick( closings ) );
            }

            return string.Join( " ", parts );
        }

        #endregion

        #region The prayer team

        /*
            10/20/26 - CLAUDE

            A prayer administrator approves most requests by hand within hours (Rock's approval stamps who
            and when, and sets the request to expire 14 days later). A few were approved automatically when
            entered. A request whose approval time falls after install day is still waiting, so the approval
            queue always holds the newest few.

            Reason: Mirrors Rock's PrayerRequestList approve action and its default of 14 days.
        */
        private void Approve( SeededRandom random, PlannedPrayerRequest request, List<PrayerPerson> admins )
        {
            var isByHand = random.Chance( Number( "prayer.detail.pctApprovedByAPerson", 95 ) / 100 );
            var approvedOn = isByHand ? request.Entered.AddHours( FromMedianAndP90( random, Number( "prayer.detail.hoursToApproveP50", 6 ), Number( "prayer.detail.hoursToApproveP90", 36 ) ) ) : request.Entered;
            if ( approvedOn >= _now )
            {
                return;
            }

            var roll = random.Between( 0.0, 1.0 );
            var days = roll < 0.85 ? 14 : roll < 0.95 ? 21 : 30;
            request.IsApproved = true;
            request.ApprovedOn = approvedOn;
            request.ApprovedBy = isByHand ? random.Pick( admins ) : null;
            request.Expiration = approvedOn.AddDays( days ).Date;
        }

        /*
            10/20/26 - CLAUDE

            The team prays through a request while it is approved and not expired. The number of prayers
            comes from the profile's prayer count mix. The first prayer usually comes within a day, and
            later ones lean early in the window. A few requests are flagged partway through; Rock's flag
            limit then unapproves them, so they stop getting prayers and go back to the approval queue.
            Each prayer is one "Prayed" interaction, and PrayerCount is the number of them, as Rock keeps it.

            Reason: Rock's prayer screens and counts agree with its interactions.
        */
        private void Pray( SeededRandom random, PlannedPrayerRequest request, List<PrayerTeamMember> team )
        {
            if ( !request.IsApproved )
            {
                return;
            }

            var end = ExpiresAt( request ) < _now ? ExpiresAt( request ) : _now;
            var flagShare = Number( "prayer.detail.pctFlagged", 2 ) / 100 / Math.Max( 0.01, Number( "prayer.pctApproved", 97 ) / 100 );
            if ( random.Chance( flagShare ) )
            {
                var flaggedAt = request.ApprovedOn.Value.AddHours( random.Between( 1.0, Math.Max( 1.0, ( end - request.ApprovedOn.Value ).TotalHours ) ) );
                if ( flaggedAt < _now )
                {
                    request.FlagCount = 1;
                    request.IsApproved = false;
                    end = flaggedAt;
                }
            }

            /*
                10/20/26 - CLAUDE

                The profile's hours to the first prayer count from entry, so they include the wait for
                approval. The sum of two skewed waits has a larger median than the two medians added, so the
                wait after approval is scaled down: 0.6 of the gap between the two medians, and 0.55 of the
                profile's 90th percentile. Simulated against the approval wait, that gives the profile's
                18 and 72 hours from entry.

                Reason: Hours to the first prayer match the profile when measured from entry, as part 5 does.
            */
            var afterApproval = FromMedianAndP90( random,
                Math.Max( 1, ( Number( "prayer.team.hoursToFirstPrayerP50", 18 ) - Number( "prayer.detail.hoursToApproveP50", 6 ) ) * 0.6 ),
                Math.Max( 2, Number( "prayer.team.hoursToFirstPrayerP90", 72 ) * 0.55 ) );
            var first = request.ApprovedOn.Value.AddHours( afterApproval ).AddMinutes( 10 );

            var count = PrayerCount( random );
            if ( count == 0 || first >= end )
            {
                return;
            }

            var times = new List<DateTime> { first };
            for ( var i = 1; i < count; i++ )
            {
                times.Add( first.AddHours( ( end - first ).TotalHours * Math.Pow( random.Between( 0.0, 1.0 ), 1.6 ) ) );
            }

            var prayed = new HashSet<int>();
            foreach ( var when in times.OrderBy( t => t ) )
            {
                var member = PickPrayer( random, team, request, prayed );
                if ( member != null )
                {
                    prayed.Add( member.Person.PersonId );
                    request.Prayers.Add( (when, member.Person) );
                }
            }
        }

        /// <summary>
        /// Picks a team member who has not prayed for this request yet and did not ask it.
        /// </summary>
        private static PrayerTeamMember PickPrayer( SeededRandom random, List<PrayerTeamMember> team, PlannedPrayerRequest request, HashSet<int> prayed )
        {
            var available = team.Where( t => !prayed.Contains( t.Person.PersonId ) && t.Person.PersonId != request.Person?.PersonId ).ToList();
            if ( available.Count == 0 )
            {
                return null;
            }

            var roll = random.Between( 0.0, available.Sum( t => t.Busyness ) );
            foreach ( var member in available )
            {
                roll -= member.Busyness;
                if ( roll <= 0 )
                {
                    return member;
                }
            }

            return available.Last();
        }

        /// <summary>
        /// Picks a number of prayers from the profile's prayer count mix ("0", "1", "2-4", "5-9", "10-19", "20+").
        /// </summary>
        private int PrayerCount( SeededRandom random )
        {
            var range = random.Pick( Map( "prayer.detail.prayerCountMix" ) );
            if ( range.EndsWith( "+" ) )
            {
                var low = int.Parse( range.TrimEnd( '+' ) );
                return random.Between( low, low + 10 );
            }

            var bounds = range.Split( '-' );
            return bounds.Length == 1 ? int.Parse( bounds[0] ) : random.Between( int.Parse( bounds[0] ), int.Parse( bounds[1] ) );
        }

        /// <summary>
        /// Shares the profile's share of requests with a small group the requester belongs to.
        /// </summary>
        private void ShareWithGroups( List<PlannedPrayerRequest> requests )
        {
            var inGroups = requests.Where( r => r.Person != null && r.Person.GroupIds.Count > 0 ).ToList();
            if ( inGroups.Count == 0 )
            {
                return;
            }

            var random = new SeededRandom( $"{_seed}:prayer:groups" );
            var share = Math.Min( 1, requests.Count * Number( "prayer.detail.pctWithGroup", 8 ) / 100 / inGroups.Count );
            foreach ( var request in inGroups.Where( _ => random.Chance( share ) ) )
            {
                request.GroupId = random.Pick( request.Person.GroupIds );
            }
        }

        /*
            10/20/26 - CLAUDE

            Answers and closed requests come only from requests whose prayer window has ended, because a
            requester shares an answer after the fact. Rock keeps a closed request but marks it inactive.

            Reason: An answer on a request still being prayed for reads wrong in Rock's prayer screens.
        */
        private void CloseAndAnswer( List<PlannedPrayerRequest> requests )
        {
            var random = new SeededRandom( $"{_seed}:prayer:answers" );
            var ended = requests.Where( r => r.Expiration.HasValue && ExpiresAt( r ) <= _now ).ToList();
            if ( ended.Count == 0 )
            {
                return;
            }

            var answerShare = Math.Min( 1, requests.Count * Number( "prayer.pctAnswered", 15 ) / 100 / ended.Count );
            var closeShare = Math.Min( 1, requests.Count * ( 100 - Number( "prayer.detail.pctActive", 95 ) ) / 100 / ended.Count );
            foreach ( var request in ended )
            {
                if ( random.Chance( answerShare ) )
                {
                    request.Answer = random.Pick( request.AnswerPool );
                }

                request.IsActive = !random.Chance( closeShare );
            }
        }

        /*
            10/20/26 - CLAUDE

            Comments come from team members who prayed for the request, usually within a few hours of praying. Only
            requests that allow comments get them. A few requesters reply to the first comment, which Rock
            shows as a reply under it.

            Reason: Comments in Rock come from the prayer session, where people pray and then encourage.
        */
        private void Comment( List<PlannedPrayerRequest> requests, PrayerCommentText text )
        {
            var random = new SeededRandom( $"{_seed}:prayer:comments" );
            var eligible = requests.Where( r => r.AllowComments && r.Prayers.Count > 0 ).ToList();
            if ( eligible.Count == 0 )
            {
                return;
            }

            var share = Math.Min( 1, requests.Count * Number( "prayer.comments.pctRequestsWithComment", 15 ) / 100 / eligible.Count );
            var replyShare = Number( "prayer.comments.pctCommentsThatAreReplies", 10 ) / 100;
            foreach ( var request in eligible.Where( _ => random.Chance( share ) ) )
            {
                var roll = random.Between( 0.0, 1.0 );
                var count = Math.Min( request.Prayers.Count, roll < 0.7 ? 1 : roll < 0.9 ? 2 : 3 );
                foreach ( var (when, by) in request.Prayers.Take( count ) )
                {
                    var at = when.AddHours( FromMedianAndP90( random, 5, 24 ) );
                    if ( at < _now )
                    {
                        request.Comments.Add( new PlannedPrayerComment { When = at, By = by, Text = random.Pick( text.Team ) } );
                    }
                }

                // Replies are a share of all comments, and each reply follows a request's team comments.
                var first = request.Comments.FirstOrDefault();
                var reply = first?.When.AddHours( random.Between( 1, 48 ) );
                if ( first != null && request.Person != null && reply < _now && random.Chance( replyShare / ( 1 - replyShare ) * request.Comments.Count ) )
                {
                    request.Comments.Add( new PlannedPrayerComment { When = reply.Value, By = request.Person, Text = random.Pick( text.RequesterReplies ), IsReply = true } );
                }
            }
        }

        #endregion

        #region Helpers

        private double Number( string path, double fallback ) => _profile.Number( path, fallback );

        /// <summary>
        /// When a request leaves the prayer session. Rock stores the expiration as a date and shows the request
        /// while today is on or before it (PrayerRequestService), so it stays through the end of that day.
        /// </summary>
        private static DateTime ExpiresAt( PlannedPrayerRequest request ) => request.Expiration.Value.Date.AddDays( 1 );

        private Dictionary<string, double> Map( string path ) => _profile.Map( path );

        /// <summary>
        /// The age band the profile's requester mix uses.
        /// </summary>
        private static string AgeBand( int age ) => age < 18 ? "under18" : age < 30 ? "18-29" : age < 45 ? "30-44" : age < 65 ? "45-64" : "65+";

        /// <summary>
        /// A random hour, from the profile's hour band mix ("06-11" is 6am to just before noon).
        /// </summary>
        private static double HourIn( SeededRandom random, Dictionary<string, double> bands )
        {
            var band = random.Pick( bands ).Split( '-' );
            return random.Between( int.Parse( band[0] ), int.Parse( band[1] ) );
        }

        /// <summary>
        /// A log-normal amount with the given median and 90th percentile.
        /// </summary>
        private static double FromMedianAndP90( SeededRandom random, double median, double p90 )
        {
            var spread = Math.Log( p90 / median ) / 1.2816;

            // LogNormal has a mean of 1; scaling by exp(spread^2 / 2) gives it a median of 1.
            return median * random.LogNormal( spread ) * Math.Exp( spread * spread / 2 );
        }

        /// <summary>
        /// Orders items by a weighted random draw without replacement (Efraimidis and Spirakis).
        /// </summary>
        private static IEnumerable<T> WeightedOrder<T>( SeededRandom random, IEnumerable<T> items, Func<T, double> weight )
        {
            return items
                .Select( item => (Item: item, Key: Math.Pow( random.Between( 0.0, 1.0 ), 1 / Math.Max( 1e-9, weight( item ) ) )) )
                .OrderByDescending( x => x.Key )
                .Select( x => x.Item )
                .ToList();
        }

        /// <summary>
        /// Picks a day in the history window, weighted by the profile's month and weekday patterns.
        /// </summary>
        private sealed class DayPicker
        {
            private readonly List<DateTime> _days = new List<DateTime>();
            private readonly List<double> _cumulative = new List<double>();

            public DayPicker( DateTime start, DateTime end, Dictionary<string, double> monthIndex, Dictionary<string, double> weekdayMix )
            {
                var total = 0.0;
                for ( var day = start.Date; day < end; day = day.AddDays( 1 ) )
                {
                    var month = monthIndex.TryGetValue( day.Month.ToString( "00" ), out var m ) ? m : 1.0;
                    var weekday = weekdayMix.TryGetValue( day.DayOfWeek.ToString().Substring( 0, 3 ), out var w ) ? w : 100.0 / 7;
                    total += month / DateTime.DaysInMonth( day.Year, day.Month ) * weekday;
                    _days.Add( day );
                    _cumulative.Add( total );
                }
            }

            public DateTime Pick( SeededRandom random )
            {
                var roll = random.Between( 0.0, _cumulative[^1] );
                var index = _cumulative.BinarySearch( roll );
                return _days[Math.Min( _days.Count - 1, index < 0 ? ~index : index )];
            }
        }

        #endregion
    }

    /// <summary>
    /// What the planner needs to know about one person.
    /// </summary>
    internal sealed class PrayerPerson
    {
        public int PersonId { get; set; }

        public int AliasId { get; set; }

        public int CampusId { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public int Age { get; set; }

        public bool IsFemale { get; set; }

        /// <summary>
        /// An adult who gave, attended, or was in a small group in the last 12 months.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// The Cast's story puts this person on the Prayer Team.
        /// </summary>
        public bool IsCastPrayerTeam { get; set; }

        /// <summary>
        /// The Cast's story makes this person a Prayer Administrator, who approves requests.
        /// </summary>
        public bool IsAdmin { get; set; }

        /// <summary>
        /// The active small groups this person is in.
        /// </summary>
        public List<int> GroupIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// A prayer category Rock ships, with its Id in this database.
    /// </summary>
    internal sealed record PrayerCategory( string Key, int Id, double Weight );

    /// <summary>
    /// A prayer team member and how much they pray compared with others.
    /// </summary>
    internal sealed record PrayerTeamMember( PrayerPerson Person, double Busyness );

    /// <summary>
    /// Everything the planner decided.
    /// </summary>
    internal sealed class PrayerPlan
    {
        public int ActiveAdults { get; set; }

        public List<PrayerTeamMember> Team { get; set; } = new List<PrayerTeamMember>();

        public List<PrayerPerson> Admins { get; set; } = new List<PrayerPerson>();

        public List<PlannedPrayerRequest> Requests { get; set; } = new List<PlannedPrayerRequest>();
    }

    /// <summary>
    /// One planned prayer request and what happened to it.
    /// </summary>
    internal sealed class PlannedPrayerRequest
    {
        /// <summary>
        /// The requester, or null for a guest with no person record.
        /// </summary>
        public PrayerPerson Person { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public int CampusId { get; set; }

        public DateTime Entered { get; set; }

        public int CategoryId { get; set; }

        public string Text { get; set; }

        public string Answer { get; set; }

        public bool IsUrgent { get; set; }

        public bool IsPublic { get; set; }

        public bool AllowComments { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsApproved { get; set; }

        public DateTime? ApprovedOn { get; set; }

        /// <summary>
        /// The prayer administrator who approved it, or null when it was approved automatically or is waiting.
        /// </summary>
        public PrayerPerson ApprovedBy { get; set; }

        public DateTime? Expiration { get; set; }

        public int FlagCount { get; set; }

        public int? GroupId { get; set; }

        public List<(DateTime When, PrayerPerson By)> Prayers { get; } = new List<(DateTime, PrayerPerson)>();

        public List<PlannedPrayerComment> Comments { get; } = new List<PlannedPrayerComment>();

        /// <summary>
        /// The category's answers, for the answer pass.
        /// </summary>
        public List<string> AnswerPool { get; set; }
    }

    /// <summary>
    /// One comment on a prayer request.
    /// </summary>
    internal sealed class PlannedPrayerComment
    {
        public DateTime When { get; set; }

        public PrayerPerson By { get; set; }

        public string Text { get; set; }

        /// <summary>
        /// A reply from the requester to the first comment.
        /// </summary>
        public bool IsReply { get; set; }
    }
}
