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

        Bulk email follows Rock's own send path (docs/communications.md, decisions 131 and 132). Every
        send is scheduled first, then the sends are walked in date order, because what one send does to
        a person (a bounce, an unsubscribe) changes who the later sends reach and how they fare:

        - A recipient Rock would refuse is Failed with Rock's own status note and no send time: an
          inactive email, "Do not email", or "No mass emails" on a bulk send.
        - Everyone else is sent through Mailgun and confirmed delivered. A few addresses bounce once in
          the window: that recipient fails with the bounce, and Rock deactivates the email from then on.
        - Opens and clicks follow the measured rates and delays. Each person reads at their own rate,
          higher for Leading and Engaged people, and each open or click is an interaction with its own
          session, as Rock's Mailgun webhook writes it.
        - Church-wide emails go to each campus's involved adults (those who attended, gave, or were in a
          group in the last year), so the involvement that sets list membership also sets who hears the
          most (decision 133).
        - Unsubscribes follow Rock's email preference page. A list email makes the person Inactive in
          that list. A church-wide email steps a person on no active list down to "No mass emails", so
          nobody with that preference is ever left on a list.

        The Cast never bounces or unsubscribes at random. Their stories script the few that happen.

        Reason: The communication screens, the person records, and the lists always agree.
    */

    /// <summary>
    /// Plans every bulk email send, its recipients, and what happened to each.
    /// </summary>
    internal sealed class EmailPlanner
    {
        public const int Pending = 0;
        public const int Delivered = 1;
        public const int Failed = 2;
        public const int Opened = 4;

        public const int UnsubscribeBulk = 2;
        public const int UnsubscribeCommunicationList = 3;

        /// <summary>
        /// The audiences this slice sends to. Classes, flows, and one-to-one messages come later.
        /// </summary>
        private static readonly HashSet<string> MassAudiences = new HashSet<string> { "newsletter", "everyone", "students-and-parents" };

        private static readonly string[] DelayBins = { "1-under 1 hour", "2-1 to 6 hours", "3-6 to 24 hours", "4-1 to 3 days", "5-3 to 7 days", "6-7 days or more" };
        private static readonly (double From, double To)[] DelayHours = { (0.02, 1), (1, 6), (6, 24), (24, 72), (72, 168), (168, 336) };

        private readonly EmailPlanInputs _inputs;
        private readonly Design _design;
        private readonly Dictionary<int, EmailPerson> _people;
        private readonly Dictionary<(string ListKey, string CampusKey), List<PlannedListMember>> _lists;
        private readonly Dictionary<int, int> _activeLists;

        public EmailPlanner( EmailPlanInputs inputs )
        {
            _inputs = inputs;
            _design = new Design( inputs.Profile );
            _people = inputs.People.ToDictionary( p => p.PersonId );
            _lists = inputs.Members.GroupBy( m => (m.ListKey, m.CampusKey) ).ToDictionary( g => g.Key, g => g.OrderBy( m => m.PersonId ).ToList() );
            _activeLists = inputs.Members.GroupBy( m => m.PersonId ).ToDictionary( g => g.Key, g => g.Count() );
            PlanBounces();
        }

        /// <summary>
        /// The people whose email bounced, by person Id, with when.
        /// </summary>
        public Dictionary<int, DateTime> Bounces { get; } = new Dictionary<int, DateTime>();

        /// <summary>
        /// The people who chose "No mass emails", by person Id, with when.
        /// </summary>
        public Dictionary<int, DateTime> BulkUnsubscribes { get; } = new Dictionary<int, DateTime>();

        /// <summary>
        /// Every send, oldest first, with its recipients and their outcomes. Sends are planned one at a time,
        /// so a caller can write and drop each before the next.
        /// </summary>
        public IEnumerable<PlannedEmail> Plan()
        {
            var sends = Schedule();
            var castTargets = CastTargets( sends );
            foreach ( var send in sends )
            {
                PlanRecipients( send, castTargets );
                yield return send;

                // The caller has written the send. Its recipients would otherwise stay in memory for the whole history.
                send.Recipients.Clear();
                send.Recipients.TrimExcess();
                send.Html = null;
            }
        }

        #region Schedule

        /// <summary>
        /// Every send in the window, oldest first: each campus's weekly newsletter and each library email's occasions.
        /// </summary>
        private List<PlannedEmail> Schedule()
        {
            var sends = new List<PlannedEmail>();
            var newsletter = _inputs.Lists.FirstOrDefault( l => l.Members == CommunicationListPlanner.Newsletter );
            foreach ( var campus in _inputs.Campuses )
            {
                if ( newsletter != null )
                {
                    foreach ( var thursday in _inputs.Calendar.Dates( "newsletter" ).Where( InWindow ) )
                    {
                        sends.Add( Newsletter( newsletter, campus, thursday ) );
                    }
                }

                foreach ( var email in _inputs.Emails.Where( e => IsMass( e ) ) )
                {
                    // A list that is not a campus list goes out once for the church, from the main campus's send.
                    var listKey = email.Audience.StartsWith( "list:", StringComparison.Ordinal ) ? email.Audience.Substring( 5 ) : null;
                    var list = listKey == null ? null : _inputs.Lists.FirstOrDefault( l => l.Key == listKey );
                    if ( listKey != null && ( list == null || ( !list.Campus && campus.Key != _inputs.MainCampusKey ) ) )
                    {
                        continue;
                    }

                    foreach ( var occasion in _inputs.Calendar.Dates( email.Occasion ) )
                    {
                        var sendDay = occasion.AddDays( -email.DaysBefore );
                        if ( InWindow( sendDay ) )
                        {
                            sends.Add( LibrarySend( email, list, campus, occasion, sendDay ) );
                        }
                    }
                }
            }

            sends = sends.OrderBy( s => s.Sent ).ThenBy( s => s.Key, StringComparer.Ordinal ).ThenBy( s => s.CampusKey, StringComparer.Ordinal ).ToList();
            for ( var i = 0; i < sends.Count; i++ )
            {
                sends[i].Index = i;
            }

            return sends;
        }

        private bool IsMass( LibraryEmail email ) => MassAudiences.Contains( email.Audience ) || email.Audience.StartsWith( "list:", StringComparison.Ordinal );

        private bool InWindow( DateTime day ) => day.Date >= _inputs.From.Date && day.Date < _inputs.Anchor.Date;

        /// <summary>
        /// One campus's newsletter: an opener, this Sunday's series, the pieces for coming occasions, evergreen
        /// pieces to make four, and a closing.
        /// </summary>
        private PlannedEmail Newsletter( CommunicationListDefinition list, EmailCampus campus, DateTime thursday )
        {
            var library = _inputs.Newsletter;
            var random = new SeededRandom( $"{_inputs.Seed}:email:newsletter:{campus.Key}:{thursday:yyyyMMdd}" );
            var sunday = thursday.AddDays( 3 );
            var series = _inputs.Calendar.SeriesOn( sunday );
            var baseTokens = MessageRenderer.Tokens( sunday, thursday, campus.Name, campus.Key, campus.ServiceTimes, series );

            var blocks = new List<EmailBlock>
            {
                new EmailBlock { H = $"Rock Solid {campus.Name} Weekly" },
                new EmailBlock { P = random.Pick( library.Openers ) }
            };
            if ( series.HasValue && library.Series.Count > 0 )
            {
                blocks.Add( new EmailBlock { P = random.Pick( library.Series ) } );
            }

            var pieces = new List<(NewsletterPiece Piece, DateTime Date)>();
            foreach ( var piece in library.Pieces )
            {
                var next = _inputs.Calendar.Dates( piece.Occasion ).Where( d => ( d - thursday ).TotalDays >= piece.Until && ( d - thursday ).TotalDays <= piece.From ).Cast<DateTime?>().FirstOrDefault();
                if ( next.HasValue )
                {
                    pieces.Add( (piece, next.Value) );
                }
            }

            var chosen = pieces.OrderBy( p => p.Date ).Take( 3 ).ToList();
            var evergreen = library.Evergreen.OrderBy( _ => random.Between( 0, 1000000 ) ).Take( Math.Max( 0, 4 - chosen.Count ) ).Select( p => (p, sunday) );
            foreach ( var (piece, date) in chosen.Concat( evergreen ) )
            {
                var tokens = MessageRenderer.Tokens( date, thursday, campus.Name, campus.Key, campus.ServiceTimes, series );
                blocks.Add( new EmailBlock { H = MessageRenderer.Fill( piece.Heading, tokens ) } );
                blocks.Add( new EmailBlock { P = MessageRenderer.Fill( piece.Text, tokens ) } );
                if ( piece.Button != null )
                {
                    blocks.Add( new EmailBlock { Button = piece.Button, Href = piece.Href } );
                }
            }

            blocks.Add( new EmailBlock { P = random.Pick( library.Closings ) } );
            var subject = MessageRenderer.Fill( random.Pick( library.Subjects ), baseTokens );
            var (html, links) = _inputs.Renderer.Render( blocks, $"This Sunday at Rock Solid {campus.Name}, and what is coming up.", baseTokens );
            var sent = thursday.AddHours( 6 ).AddMinutes( random.Between( 0, 20 ) );

            return new PlannedEmail
            {
                Key = "newsletter",
                Name = $"{campus.Name} Weekly - {thursday:M/d/yyyy}",
                Subject = subject,
                Html = html,
                Links = links,
                Audience = "newsletter",
                ListKey = list.Key,
                ListCampusKey = campus.Key,
                CampusKey = campus.Key,
                IsBulk = true,
                Topic = "general",
                Sender = "church",
                Created = sent.AddDays( -random.Between( 1, 2 ) ).Date.AddHours( random.Between( 9, 16 ) ),
                IsScheduled = true,
                Sent = sent
            };
        }

        /// <summary>
        /// One library email on one occasion, for one campus.
        /// </summary>
        private PlannedEmail LibrarySend( LibraryEmail email, CommunicationListDefinition list, EmailCampus campus, DateTime occasion, DateTime sendDay )
        {
            var random = new SeededRandom( $"{_inputs.Seed}:email:{email.Key}:{campus.Key}:{occasion:yyyyMMdd}" );
            var series = _inputs.Calendar.SeriesOn( occasion.AddDays( -( int ) occasion.DayOfWeek ).AddDays( occasion.DayOfWeek == DayOfWeek.Sunday ? 0 : 7 ) );
            var tokens = MessageRenderer.Tokens( occasion, sendDay, campus.Name, campus.Key, campus.ServiceTimes, series );
            var (html, links) = _inputs.Renderer.Render( email.Body, email.Preheader, tokens );
            var hour = random.Pick( _design.SendHourBands ).Split( '-' );
            var sent = sendDay.AddHours( random.Between( int.Parse( hour[0] ), int.Parse( hour[1] ) ) ).AddMinutes( random.Between( 0, 59 ) );
            var listCampus = list != null && list.Campus ? campus.Key : null;
            var subject = MessageRenderer.Fill( email.Subject, tokens );

            return new PlannedEmail
            {
                Key = email.Key,
                Name = list == null || list.Campus ? $"{subject} ({campus.Name})" : subject,
                Subject = subject,
                Html = html,
                Links = links,
                Audience = email.Audience,
                ListKey = list?.Key ?? ( email.Audience == "newsletter" ? CommunicationListPlanner.Newsletter : null ),
                CampusKey = list != null && !list.Campus ? null : campus.Key,
                ListCampusKey = email.Audience == "newsletter" ? campus.Key : listCampus,
                IsBulk = email.Bulk,
                Topic = email.Topic,
                Sender = email.Sender,
                Created = sent.AddMinutes( -random.Between( 15, 240 ) ),
                Sent = sent
            };
        }

        #endregion Schedule

        #region One-to-one

        /// <summary>
        /// One person's copy of a one-to-one email, planned the way a bulk send's recipient is, but with no unsubscribe and
        /// no bulk refusal: Rock sends a personal email to someone on "No mass emails". Call it after <see cref="Plan"/>,
        /// so bounces before the send are known.
        /// </summary>
        public void PlanOneToOne( PlannedEmail send, EmailPerson person, string seed )
        {
            var random = new SeededRandom( seed );
            var recipient = new PlannedRecipient { Person = person, Seed = seed, Guid = SeededRandom.StableGuid( $"{seed}:recipient" ), HistoryGuid = SeededRandom.StableGuid( $"{seed}:history" ) };
            send.Recipients.Add( recipient );
            var refusal = !person.HasEmail ? "No Email Address"
                : !person.EmailActive || ( person.HasBounced && Bounces[person.PersonId] < send.Sent ) ? "Recipient Email Address is not active"
                : person.Preference == 2 ? "Communication Preference of 'Do Not Send Communication'"
                : null;
            if ( refusal != null )
            {
                recipient.Status = Failed;
                recipient.StatusNote = refusal;
                return;
            }

            recipient.SendDateTime = send.Sent;
            recipient.Status = Delivered;
            recipient.DeliveredDateTime = send.Sent.AddSeconds( random.Between( 2, 40 ) );
            recipient.StatusNote = $"Confirmed delivered by Mailgun at {recipient.DeliveredDateTime.Value.ToString( "M/d/yyyy h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture )}";
            PlanEngagement( send, recipient, random, false, OneToOneOpenFactor );
        }

        /// <summary>
        /// Whether Rock can email the person at a time: they have an email that has not bounced, and they did not choose "Do not email".
        /// </summary>
        public bool CanEmail( EmailPerson person, DateTime at ) => person.HasEmail && Refusal( person, new PlannedEmail { Sent = at } ) == null;

        /// <summary>
        /// A personal email gets opened more often than a mass email does.
        /// </summary>
        private const double OneToOneOpenFactor = 1.1;

        #endregion One-to-one

        #region Recipients

        /// <summary>
        /// The send each scripted Cast unsubscribe happens on: the newest fitting send at least its days before install day.
        /// </summary>
        private Dictionary<int, (int PersonId, int Level)> CastTargets( List<PlannedEmail> sends )
        {
            var targets = new Dictionary<int, (int, int)>();
            foreach ( var cast in _inputs.CastUnsubscribes )
            {
                if ( !_inputs.PersonIdsByGuid.TryGetValue( cast.Person, out var personId ) || !_people.TryGetValue( personId, out var person ) )
                {
                    continue;
                }

                var latest = _inputs.Anchor.AddDays( -cast.DaysAgo );
                var target = sends.Where( s => s.Sent <= latest )
                    .Where( s => cast.List != null
                        ? s.ListKey == cast.List && ( s.ListCampusKey == null || s.ListCampusKey == person.CampusKey )
                        : s.ListKey == null && s.IsBulk && s.CampusKey == person.CampusKey && s.Audience == "everyone" )
                    .LastOrDefault();
                if ( target == null )
                {
                    throw new InvalidOperationException( $"configuration/communications.json: no send fits the scripted unsubscribe of {cast.Person}." );
                }

                targets[target.Index] = (personId, cast.List != null ? UnsubscribeCommunicationList : UnsubscribeBulk);
            }

            return targets;
        }

        /// <summary>
        /// Who the send reaches at its send time, and what happened to each.
        /// </summary>
        private void PlanRecipients( PlannedEmail send, Dictionary<int, (int PersonId, int Level)> castTargets )
        {
            var audience = Audience( send );
            var scripted = castTargets.TryGetValue( send.Index, out var target ) ? target : (PersonId: 0, Level: 0);
            var position = 0;
            foreach ( var (person, member) in audience )
            {
                var seed = $"{_inputs.Seed}:email:{send.Index}:{send.Key}:{person.Guid}";
                var random = new SeededRandom( seed );
                var recipient = new PlannedRecipient { Person = person, Seed = seed, Guid = SeededRandom.StableGuid( $"{seed}:recipient" ), HistoryGuid = SeededRandom.StableGuid( $"{seed}:history" ) };
                send.Recipients.Add( recipient );
                var at = send.Sent.AddSeconds( 0.15 * position++ );

                var refusal = Refusal( person, send );
                if ( refusal != null )
                {
                    recipient.Status = Failed;
                    recipient.StatusNote = refusal;
                    continue;
                }

                recipient.SendDateTime = at;
                if ( Bounces.TryGetValue( person.PersonId, out var bounceAt ) && !person.HasBounced && at >= bounceAt )
                {
                    person.HasBounced = true;
                    Bounces[person.PersonId] = at;
                    recipient.Status = Failed;
                    recipient.StatusNote = random.Pick( BounceNotes );
                    continue;
                }

                recipient.Status = Delivered;
                recipient.DeliveredDateTime = at.AddSeconds( random.Between( 2, 40 ) );
                recipient.StatusNote = $"Confirmed delivered by Mailgun at {recipient.DeliveredDateTime.Value.ToString( "M/d/yyyy h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture )}";

                var isScripted = scripted.PersonId == person.PersonId;
                var unsubscribe = isScripted ? scripted.Level : person.IsCast ? 0 : RandomUnsubscribe( send, person, random );
                if ( unsubscribe == UnsubscribeCommunicationList && member == null )
                {
                    unsubscribe = 0;
                }
                PlanEngagement( send, recipient, random, unsubscribe > 0 );

                if ( unsubscribe > 0 && recipient.OpenedDateTime.HasValue )
                {
                    var when = recipient.OpenedDateTime.Value.AddMinutes( random.Between( 1, 20 ) );
                    if ( when < _inputs.Anchor )
                    {
                        recipient.UnsubscribeLevel = unsubscribe;
                        recipient.UnsubscribeDateTime = when;
                        if ( unsubscribe == UnsubscribeCommunicationList )
                        {
                            member.UnsubscribedAt = when;
                            _activeLists[person.PersonId]--;
                        }
                        else
                        {
                            BulkUnsubscribes[person.PersonId] = when;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Rock's status note when it refuses to send, or null.
        /// </summary>
        private string Refusal( EmailPerson person, PlannedEmail send )
        {
            if ( !person.EmailActive || ( person.HasBounced && Bounces[person.PersonId] < send.Sent ) )
            {
                return "Recipient Email Address is not active";
            }

            if ( person.Preference == 2 )
            {
                return "Communication Preference of 'Do Not Send Communication'";
            }

            var isNoMass = person.Preference == 1 || ( BulkUnsubscribes.TryGetValue( person.PersonId, out var unsubscribed ) && unsubscribed < send.Sent );
            return isNoMass && send.IsBulk ? "Communication Preference of 'No Bulk Communication'" : null;
        }

        /// <summary>
        /// The people the send reaches: list members who joined before it and have not left, or for a church-wide send,
        /// the campus's involved adults with an email, the segment a church's "active adults" data view gives. A list member comes with their membership, which an unsubscribe makes Inactive.
        /// </summary>
        private List<(EmailPerson Person, PlannedListMember Member)> Audience( PlannedEmail send )
        {
            if ( send.Audience == "everyone" )
            {
                return _inputs.People.Where( p => p.IsAdult && p.IsInvolved && p.HasEmail && p.CampusKey == send.CampusKey && p.Since <= send.Sent )
                    .Select( p => (p, ( PlannedListMember ) null) ).ToList();
            }

            if ( send.Audience == "students-and-parents" )
            {
                var students = Members( CommunicationListPlanner.Students, null, send ).Select( m => m.Person ).Where( p => p.HasEmail && p.CampusKey == send.CampusKey ).ToList();
                var parents = students.SelectMany( s => s.ParentIds ).Distinct().Where( _people.ContainsKey ).Select( id => _people[id] ).Where( p => p.HasEmail && p.Since <= send.Sent );
                return students.Concat( parents ).GroupBy( p => p.PersonId ).Select( g => (g.First(), ( PlannedListMember ) null) ).OrderBy( r => r.Item1.PersonId ).ToList();
            }

            return Members( send.ListKey, send.ListCampusKey, send ).Where( r => r.Person.HasEmail ).ToList();
        }

        private IEnumerable<(EmailPerson Person, PlannedListMember Member)> Members( string listKey, string campusKey, PlannedEmail send )
        {
            if ( !_lists.TryGetValue( (listKey, campusKey), out var members ) )
            {
                return Enumerable.Empty<(EmailPerson, PlannedListMember)>();
            }

            return members.Where( m => m.Joined < send.Sent && ( m.UnsubscribedAt == null || m.UnsubscribedAt > send.Sent ) && _people.ContainsKey( m.PersonId ) )
                .Select( m => (_people[m.PersonId], m) );
        }

        /// <summary>
        /// A designed unsubscribe: from a list email, the person leaves that list. From a church-wide email, a person on
        /// no list chooses "No mass emails". Returns the level, or 0.
        /// </summary>
        private int RandomUnsubscribe( PlannedEmail send, EmailPerson person, SeededRandom random )
        {
            if ( send.ListKey != null && send.Audience != "students-and-parents" )
            {
                var rate = send.Audience == "newsletter" || send.ListKey == CommunicationListPlanner.Newsletter ? _design.NewsletterUnsubscribes : _design.ListUnsubscribes;
                return random.Chance( rate / 1000 ) ? UnsubscribeCommunicationList : 0;
            }

            return send.IsBulk && _activeLists.GetValueOrDefault( person.PersonId ) == 0 && random.Chance( _design.BulkUnsubscribes / 1000 ) ? UnsubscribeBulk : 0;
        }

        #endregion Recipients

        #region Engagement

        /// <summary>
        /// Opens and clicks: a reader opens at their own rate, mostly soon after the send, and some click a link.
        /// An unsubscriber always opened first.
        /// </summary>
        private void PlanEngagement( PlannedEmail send, PlannedRecipient recipient, SeededRandom random, bool mustOpen, double openFactor = 1 )
        {
            var person = recipient.Person;
            if ( !mustOpen && !random.Chance( Math.Min( 0.98, person.OpenRate * _design.SendOpenFactor( send ) * openFactor ) ) )
            {
                return;
            }

            var sent = recipient.SendDateTime.Value;
            var firstOpen = sent.AddHours( Delay( random, _design.OpenDelay ) );
            if ( firstOpen >= _inputs.Anchor && mustOpen )
            {
                firstOpen = sent.AddMinutes( random.Between( 5, 30 ) );
            }

            if ( firstOpen >= _inputs.Anchor )
            {
                return;
            }

            recipient.Status = Opened;
            recipient.OpenedDateTime = firstOpen;
            var client = person.ClientIndex;
            recipient.OpenedClient = _inputs.Clients[client].OpenedClient;

            var opens = 1 + Geometric( random, _design.OpensPerOpener - 1 );
            var last = firstOpen;
            for ( var i = 0; i < opens; i++ )
            {
                var when = i == 0 ? firstOpen : last.AddHours( random.Between( 0.1, 60 ) );
                if ( when >= _inputs.Anchor )
                {
                    break;
                }

                var device = i == 0 || random.Chance( 0.75 ) ? client : person.SecondClientIndex;
                recipient.Interactions.Add( new PlannedInteraction { Operation = "Opened", When = when, Data = string.Empty, ClientIndex = device, Guid = SeededRandom.StableGuid( $"{recipient.Seed}:open:{i}" ), SessionGuid = SeededRandom.StableGuid( $"{recipient.Seed}:open-session:{i}" ) } );
                last = when;
            }

            if ( send.Links.Count == 0 || !random.Chance( Math.Min( 0.95, _design.ClickersOfOpeners * person.ClickFactor ) ) )
            {
                return;
            }

            var clicks = 1 + Geometric( random, _design.ClicksPerClicker - 1 );
            for ( var i = 0; i < clicks; i++ )
            {
                var after = sent.AddHours( Delay( random, _design.ClickDelay ) );
                var when = after <= firstOpen ? firstOpen.AddMinutes( random.Between( 0.2, 8 ) ) : after;
                if ( when >= _inputs.Anchor )
                {
                    break;
                }

                var link = random.Chance( _design.TopLinkShare ) || send.Links.Count == 1 ? send.Links[0] : send.Links[random.Between( 1, send.Links.Count - 1 )];
                recipient.Interactions.Add( new PlannedInteraction { Operation = "Click", When = when, Data = link, ClientIndex = client, Guid = SeededRandom.StableGuid( $"{recipient.Seed}:click:{i}" ), SessionGuid = SeededRandom.StableGuid( $"{recipient.Seed}:click-session:{i}" ) } );
            }

            recipient.Interactions.Sort( ( a, b ) => a.When.CompareTo( b.When ) );
        }

        /// <summary>
        /// Hours after the send, drawn from a measured delay mix.
        /// </summary>
        private static double Delay( SeededRandom random, IReadOnlyDictionary<string, double> mix )
        {
            var bin = Array.IndexOf( DelayBins, random.Pick( mix ) );
            var (from, to) = DelayHours[bin < 0 ? 0 : bin];
            return random.Between( from, to );
        }

        /// <summary>
        /// A count with the given average, from 0 up.
        /// </summary>
        private static int Geometric( SeededRandom random, double mean )
        {
            var keepGoing = mean / ( 1 + mean );
            var count = 0;
            while ( count < 20 && random.Chance( keepGoing ) )
            {
                count++;
            }

            return count;
        }

        private static readonly string[] BounceNotes =
        {
            "550 5.1.1 The email account that you tried to reach does not exist.",
            "550 5.1.1 <recipient>: Recipient address rejected: User unknown in virtual mailbox table",
            "552 5.2.2 The email account that you tried to reach is over quota.",
            "550 5.7.1 Mailbox unavailable."
        };

        /// <summary>
        /// A few addresses go bad during the window. Each bounces on the first send that reaches it after the day it goes bad.
        /// </summary>
        private void PlanBounces()
        {
            foreach ( var person in _inputs.People.Where( p => !p.IsCast && p.HasEmail && p.EmailActive ) )
            {
                var random = new SeededRandom( $"{_inputs.Seed}:email:bounce:{person.Guid}" );
                if ( random.Chance( _design.PctBounced / 100 ) )
                {
                    Bounces[person.PersonId] = _inputs.From.AddDays( random.Between( 0, Math.Max( 1, ( int ) ( _inputs.Anchor - _inputs.From ).TotalDays - 1 ) ) );
                }
            }
        }

        #endregion Engagement

        /// <summary>
        /// The profile's measured shapes and the designed rates.
        /// </summary>
        private sealed class Design
        {
            public Design( ProfileTargets profile )
            {
                OpenDelay = Require( profile.Map( "communicationDetail.responseDelay.Email.Opened" ), "communicationDetail.responseDelay.Email.Opened" );
                ClickDelay = Require( profile.Map( "communicationDetail.responseDelay.Email.Click" ), "communicationDetail.responseDelay.Email.Click" );
                var hours = profile.Map( "communicationDetail.massSends.hourBandMix.Email" );
                SendHourBands = hours.Where( h => h.Key != "00-05" && h.Key != "21-23" ).ToDictionary( h => h.Key, h => h.Value );
                if ( SendHourBands.Count == 0 )
                {
                    SendHourBands = new Dictionary<string, double> { ["09-11"] = 1 };
                }

                OpensPerOpener = profile.Number( "communicationDetail.engagement.Email.opensPerOpener", 1.8 );
                ClickersOfOpeners = profile.Number( "communicationDetail.engagement.Email.pctUniqueClickersOfOpeners", 13 ) / 100;
                ClicksPerClicker = profile.Number( "communicationDetail.engagement.Email.clicksPerClicker", 1.6 );
                TopLinkShare = profile.Number( "communicationDetail.design.pctClicksOnTopLinkOfMultiLinkSends", profile.Number( "communicationDetail.links.pctClicksOnTopLinkAvg", 80 ) ) / 100;
                NewsletterUnsubscribes = profile.Number( "communicationDetail.design.unsubscribesPerThousandRecipients.newsletter", 0.15 );
                ListUnsubscribes = profile.Number( "communicationDetail.design.unsubscribesPerThousandRecipients.list", 0.5 );
                BulkUnsubscribes = profile.Number( "communicationDetail.design.unsubscribesPerThousandRecipients.churchWide", 0.12 );
                PctBounced = profile.Number( "communicationDetail.design.pctEmailsBouncingIn24Months", 1.8 );
                _newsletterOpenFactor = profile.Number( "communicationDetail.design.openFactorBySend.newsletter", 0.95 );
                _otherOpenFactor = profile.Number( "communicationDetail.design.openFactorBySend.other", 1.05 );
            }

            private readonly double _newsletterOpenFactor;
            private readonly double _otherOpenFactor;

            public Dictionary<string, double> OpenDelay { get; }

            public Dictionary<string, double> ClickDelay { get; }

            public Dictionary<string, double> SendHourBands { get; }

            public double OpensPerOpener { get; }

            public double ClickersOfOpeners { get; }

            public double ClicksPerClicker { get; }

            public double TopLinkShare { get; }

            public double NewsletterUnsubscribes { get; }

            public double ListUnsubscribes { get; }

            public double BulkUnsubscribes { get; }

            public double PctBounced { get; }

            public double SendOpenFactor( PlannedEmail send ) => send.Key == "newsletter" ? _newsletterOpenFactor : _otherOpenFactor;

            private static Dictionary<string, double> Require( Dictionary<string, double> map, string path )
            {
                return map.Count > 0 ? map : throw new InvalidOperationException( $"The profile has no {path}." );
            }
        }
    }

    /// <summary>
    /// What the email planner reads.
    /// </summary>
    internal sealed class EmailPlanInputs
    {
        public ProfileTargets Profile { get; set; }

        public string Seed { get; set; }

        public DateTime Anchor { get; set; }

        /// <summary>
        /// The first day of the communication history window.
        /// </summary>
        public DateTime From { get; set; }

        public CommunicationCalendar Calendar { get; set; }

        public MessageRenderer Renderer { get; set; }

        public NewsletterLibrary Newsletter { get; set; }

        public List<LibraryEmail> Emails { get; set; } = new List<LibraryEmail>();

        public List<CommunicationListDefinition> Lists { get; set; } = new List<CommunicationListDefinition>();

        public List<PlannedListMember> Members { get; set; } = new List<PlannedListMember>();

        public List<EmailPerson> People { get; set; } = new List<EmailPerson>();

        public Dictionary<Guid, int> PersonIdsByGuid { get; set; } = new Dictionary<Guid, int>();

        public List<EmailCampus> Campuses { get; set; } = new List<EmailCampus>();

        public string MainCampusKey { get; set; }

        public List<CastUnsubscribe> CastUnsubscribes { get; set; } = new List<CastUnsubscribe>();

        /// <summary>
        /// The email clients people read on, by index.
        /// </summary>
        public List<EmailClient> Clients { get; set; } = new List<EmailClient>();
    }

    internal sealed class EmailCampus
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string ServiceTimes { get; set; }
    }

    /// <summary>
    /// What the email planner knows about one person.
    /// </summary>
    internal sealed class EmailPerson
    {
        public int PersonId { get; set; }

        public int AliasId { get; set; }

        public Guid Guid { get; set; }

        public string CampusKey { get; set; }

        public bool IsCast { get; set; }

        public bool IsAdult { get; set; }

        /// <summary>
        /// The person attended, gave, or was in a small group in the last year. Church-wide emails reach them.
        /// </summary>
        public bool IsInvolved { get; set; }

        public bool HasEmail { get; set; }

        /// <summary>
        /// The email was active when the window began.
        /// </summary>
        public bool EmailActive { get; set; }

        /// <summary>
        /// Rock's EmailPreference when the window began: 0 email allowed, 1 no mass emails, 2 do not email.
        /// </summary>
        public int Preference { get; set; }

        /// <summary>
        /// When the church first knew the person: the household's first Sunday, or the window's start.
        /// </summary>
        public DateTime Since { get; set; }

        /// <summary>
        /// For a student, the adults in their households.
        /// </summary>
        public List<int> ParentIds { get; set; } = new List<int>();

        /// <summary>
        /// The share of emails the person opens.
        /// </summary>
        public double OpenRate { get; set; }

        /// <summary>
        /// How much more or less likely than others the person is to click once they open.
        /// </summary>
        public double ClickFactor { get; set; } = 1;

        public int ClientIndex { get; set; }

        public int SecondClientIndex { get; set; }

        /// <summary>
        /// Set while planning, once the person's email has bounced.
        /// </summary>
        public bool HasBounced { get; set; }
    }

    /// <summary>
    /// An email client as Mailgun reports it.
    /// </summary>
    internal sealed class EmailClient
    {
        public string Name { get; set; }

        public string OperatingSystem { get; set; }

        public string ClientType { get; set; }

        public string DeviceType { get; set; }

        public double Weight { get; set; }

        public int DeviceTypeId { get; set; }

        public string OpenedClient => $"{OperatingSystem} {Name} ({DeviceType})";
    }

    /// <summary>
    /// One planned send.
    /// </summary>
    internal sealed class PlannedEmail
    {
        public int Index { get; set; }

        public string Key { get; set; }

        public string Name { get; set; }

        public string Subject { get; set; }

        public string Html { get; set; }

        /// <summary>
        /// The message's links, in order. The first is its main call to action.
        /// </summary>
        public List<string> Links { get; set; } = new List<string>();

        public string Audience { get; set; }

        /// <summary>
        /// The communication list it went to, or null for a church-wide or student send.
        /// </summary>
        public string ListKey { get; set; }

        /// <summary>
        /// The campus of the list it went to, for a campus list.
        /// </summary>
        public string ListCampusKey { get; set; }

        /// <summary>
        /// The campus the send is for, or null for a church-wide list.
        /// </summary>
        public string CampusKey { get; set; }

        public bool IsBulk { get; set; }

        public string Topic { get; set; }

        /// <summary>
        /// church, pastor, or students.
        /// </summary>
        public string Sender { get; set; }

        public DateTime Created { get; set; }

        /// <summary>
        /// Scheduled ahead of time, as the weekly newsletters are.
        /// </summary>
        public bool IsScheduled { get; set; }

        public DateTime Sent { get; set; }

        public List<PlannedRecipient> Recipients { get; } = new List<PlannedRecipient>();
    }

    internal sealed class PlannedRecipient
    {
        public EmailPerson Person { get; set; }

        /// <summary>
        /// The text the recipient's random stream and row Guids come from.
        /// </summary>
        public string Seed { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The Guid of the "Sent" line on the person's History.
        /// </summary>
        public Guid HistoryGuid { get; set; }

        public int Status { get; set; }

        public string StatusNote { get; set; }

        public DateTime? SendDateTime { get; set; }

        public DateTime? DeliveredDateTime { get; set; }

        public DateTime? OpenedDateTime { get; set; }

        public string OpenedClient { get; set; }

        public int? UnsubscribeLevel { get; set; }

        public DateTime? UnsubscribeDateTime { get; set; }

        public List<PlannedInteraction> Interactions { get; } = new List<PlannedInteraction>();
    }

    internal sealed class PlannedInteraction
    {
        public string Operation { get; set; }

        public DateTime When { get; set; }

        public string Data { get; set; }

        public int ClientIndex { get; set; }

        public Guid Guid { get; set; }

        public Guid SessionGuid { get; set; }
    }
}
