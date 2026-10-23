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

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        10/23/26 - CLAUDE

        Texts follow Rock's Twilio path (docs/communications.md, decision 93):

        - Mass texts go out on the main line or the student line. Church-wide texts reach each campus's
          involved adults who chose text as their communication preference, and list texts reach the
          list's members. A member with no phone that can text fails the way Rock fails them.
        - Some people reply. Texts that ask for an answer get many more replies. Rock 21 looks for the
          text a reply answers by comparing a communication Id with a recipient Id, so it almost never
          links one, and the replies here are not linked either.
        - A few people text STOP. Rock marks their newest text in the last 60 days as a level All
          unsubscribe and turns off texting on their phone, so later texts fail.
        - The Cast's threads are hand-written, and generated people get short conversations from the
          pools, mostly on staff members' personal numbers, at the measured lengths and reply delays.
          A staff text in a conversation is its own one-person communication, as SMS Conversations sends it.

        Reason: SMS Conversations, Communication Detail, and each person's phone agree.
    */

    /// <summary>
    /// Plans every text: mass texts with their replies and STOPs, the Cast's threads, and generated conversations.
    /// </summary>
    internal sealed class TextPlanner
    {
        public const int Delivered = 1;
        public const int Failed = 2;
        public const int UnsubscribeAll = 1;
        public const string NoPhone = "No Phone Number with Messaging Enabled";

        private static readonly HashSet<string> MassAudiences = new HashSet<string> { "everyone", "students-and-parents" };
        private static readonly string[] DelayBins = { "pctUnder15Minutes", "pct15To60Minutes", "pct1To4Hours", "pct4To24Hours", "pct1To7Days" };
        private static readonly (double From, double To)[] DelayMinutes = { (1, 15), (15, 60), (60, 240), (240, 1440), (1440, 10080) };

        private readonly TextPlanInputs _inputs;
        private readonly Dictionary<int, TextPerson> _people;
        private readonly Dictionary<(string ListKey, string CampusKey), List<PlannedListMember>> _lists;
        private readonly Dictionary<int, DateTime> _optOuts = new Dictionary<int, DateTime>();

        public TextPlanner( TextPlanInputs inputs )
        {
            _inputs = inputs;
            _people = inputs.People.ToDictionary( p => p.PersonId );
            _lists = inputs.Members.GroupBy( m => (m.ListKey, m.CampusKey) ).ToDictionary( g => g.Key, g => g.OrderBy( m => m.PersonId ).ToList() );
        }

        /// <summary>
        /// Every text sent, oldest first.
        /// </summary>
        public List<PlannedText> Texts { get; } = new List<PlannedText>();

        /// <summary>
        /// Every text people sent.
        /// </summary>
        public List<PlannedResponse> Responses { get; } = new List<PlannedResponse>();

        /// <summary>
        /// The people who texted STOP, with when.
        /// </summary>
        public IReadOnlyDictionary<int, DateTime> OptOuts => _optOuts;

        /// <summary>
        /// Plans everything. The Cast's threads come first, because a scripted STOP changes the mass texts after it.
        /// </summary>
        public void Plan()
        {
            PlanCastThreads();
            PlanMassTexts();
            PlanConversations();
            Texts.Sort( ( a, b ) => a.Sent != b.Sent ? a.Sent.CompareTo( b.Sent ) : string.CompareOrdinal( a.Key, b.Key ) );
            Responses.Sort( ( a, b ) => a.When.CompareTo( b.When ) );
            MarkOptOuts();
        }

        #region Mass texts

        private void PlanMassTexts()
        {
            var sends = new List<PlannedText>();
            foreach ( var campus in _inputs.Campuses )
            {
                foreach ( var text in _inputs.Library.Texts.Where( t => t.Medium == "sms" && IsMass( t ) ) )
                {
                    var listKey = text.Audience.StartsWith( "list:", StringComparison.Ordinal ) ? text.Audience.Substring( 5 ) : null;
                    var list = listKey == null ? null : _inputs.Lists.FirstOrDefault( l => l.Key == listKey );
                    if ( listKey != null && ( list == null || ( !list.Campus && campus.Key != _inputs.MainCampusKey ) ) )
                    {
                        continue;
                    }

                    foreach ( var occasion in _inputs.Calendar.Dates( text.Occasion ) )
                    {
                        var day = occasion.AddDays( -text.DaysBefore );
                        if ( day >= _inputs.From.Date && day < _inputs.Anchor.Date )
                        {
                            sends.Add( MassText( text, list, campus, occasion, day ) );
                        }
                    }
                }
            }

            foreach ( var send in sends.OrderBy( s => s.Sent ).ThenBy( s => s.Key, StringComparer.Ordinal ).ThenBy( s => s.CampusKey, StringComparer.Ordinal ) )
            {
                PlanMassRecipients( send );
                Texts.Add( send );
            }
        }

        private static bool IsMass( LibraryText text ) => MassAudiences.Contains( text.Audience ) || text.Audience.StartsWith( "list:", StringComparison.Ordinal );

        private PlannedText MassText( LibraryText text, CommunicationListDefinition list, EmailCampus campus, DateTime occasion, DateTime day )
        {
            var random = new SeededRandom( $"{_inputs.Seed}:text:{text.Key}:{campus.Key}:{occasion:yyyyMMdd}" );
            var series = _inputs.Calendar.SeriesOn( occasion.AddDays( occasion.DayOfWeek == DayOfWeek.Sunday ? 0 : 7 - ( int ) occasion.DayOfWeek ) );
            var tokens = MessageRenderer.Tokens( occasion, day, campus.Name, campus.Key, campus.ServiceTimes, series );
            var sent = day.AddHours( text.Hour ).AddMinutes( random.Between( 0, 25 ) );
            var number = text.Line ?? "main";
            var message = MessageRenderer.Fill( text.Text, tokens );

            return new PlannedText
            {
                Key = text.Key,
                Name = list == null || list.Campus ? $"{Name( text.Key )} ({campus.Name})" : Name( text.Key ),
                Message = message,
                NumberKey = number,
                SenderAliasId = number == "students" ? _inputs.StudentSenderAliasId : _inputs.TextSenderAliasId,
                IsBulk = true,
                Topic = text.Topic,
                Audience = text.Audience,
                ListKey = list?.Key,
                ListCampusKey = list != null && list.Campus ? campus.Key : null,
                CampusKey = list != null && !list.Campus ? null : campus.Key,
                AsksReply = text.AsksReply,
                Created = sent.AddMinutes( -random.Between( 10, 120 ) ),
                Sent = sent
            };
        }

        private static string Name( string key ) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase( key.Replace( '-', ' ' ) );

        private void PlanMassRecipients( PlannedText send )
        {
            var position = 0;
            foreach ( var person in Audience( send ) )
            {
                var seed = $"{_inputs.Seed}:text:{send.Key}:{send.Sent:yyyyMMddHHmm}:{person.Guid}";
                var recipient = NewRecipient( person, seed );
                send.Recipients.Add( recipient );
                if ( person.Mobile == null || ( _optOuts.TryGetValue( person.PersonId, out var stopped ) && stopped <= send.Sent ) )
                {
                    recipient.Status = Failed;
                    recipient.StatusNote = NoPhone;
                    continue;
                }

                Deliver( recipient, send, send.Sent.AddSeconds( 0.4 * position++ ) );
                if ( person.IsCast )
                {
                    continue;
                }

                var random = new SeededRandom( seed );
                if ( random.Chance( _inputs.Design.StopsPerThousand / 1000 ) )
                {
                    var when = recipient.SendDateTime.Value.AddMinutes( StopDelayMinutes( random ) );
                    if ( when < _inputs.Anchor )
                    {
                        Respond( person, send.NumberKey, when, "STOP", random.Chance( 0.5 ), seed );
                        _optOuts[person.PersonId] = when;
                    }

                    continue;
                }

                if ( random.Chance( ( send.AsksReply ? _inputs.Design.AskReplyPercent : _inputs.Design.OtherReplyPercent ) / 100 ) )
                {
                    var kind = send.AsksReply ? random.Pick( _inputs.Design.ReplyMix ) : "thanks";
                    var pool = _inputs.Library.Replies.GetValueOrDefault( kind );
                    var when = recipient.SendDateTime.Value.AddMinutes( random.Chance( 0.7 ) ? random.Between( 1, 60 ) : random.Between( 60, 720 ) );
                    if ( pool != null && pool.Count > 0 && when < _inputs.Anchor )
                    {
                        Respond( person, send.NumberKey, when, random.Pick( pool ), IsRead( random, when, false ), seed );
                    }
                }
            }
        }

        /// <summary>
        /// The people a mass text reaches at its send time.
        /// </summary>
        private IEnumerable<TextPerson> Audience( PlannedText send )
        {
            if ( send.Audience == "everyone" )
            {
                return _inputs.People.Where( p => p.IsAdult && p.IsInvolved && p.PrefersText && p.Mobile != null && p.CampusKey == send.CampusKey && p.Since <= send.Sent );
            }

            if ( send.Audience == "students-and-parents" )
            {
                var students = Members( CommunicationListPlanner.Students, null, send.Sent ).Where( p => p.CampusKey == send.CampusKey ).ToList();
                var parents = students.SelectMany( s => s.ParentIds ).Distinct().Where( _people.ContainsKey ).Select( id => _people[id] ).Where( p => p.Mobile != null && p.Since <= send.Sent );
                return students.Concat( parents ).GroupBy( p => p.PersonId ).Select( g => g.First() ).OrderBy( p => p.PersonId );
            }

            return Members( send.ListKey, send.ListCampusKey, send.Sent );
        }

        private IEnumerable<TextPerson> Members( string listKey, string campusKey, DateTime at )
        {
            return _lists.TryGetValue( (listKey, campusKey), out var members )
                ? members.Where( m => m.Joined < at && ( m.UnsubscribedAt == null || m.UnsubscribedAt > at ) && _people.ContainsKey( m.PersonId ) ).Select( m => _people[m.PersonId] )
                : Enumerable.Empty<TextPerson>();
        }

        private double StopDelayMinutes( SeededRandom random )
        {
            var bin = random.Pick( _inputs.Design.StopDelayMix );
            return bin switch
            {
                "1-same day" => random.Between( 1, 300 ),
                "2-1 to 2 days" => random.Between( 1440, 2880 ),
                "3-3 to 7 days" => random.Between( 4320, 10080 ),
                _ => random.Between( 10080, 40000 )
            };
        }

        #endregion Mass texts

        #region Conversations

        /// <summary>
        /// The Cast's hand-written threads, exactly as their files say.
        /// </summary>
        private void PlanCastThreads()
        {
            foreach ( var thread in _inputs.CastThreads )
            {
                if ( !_inputs.PersonIdsByGuid.TryGetValue( thread.Person, out var personId ) || !_people.TryGetValue( personId, out var person ) )
                {
                    throw new InvalidOperationException( $"cast/sms-threads.json: thread {thread.Key} is for {thread.Person}, who is not a loaded person who can text." );
                }

                var number = Number( thread.Number ) ?? throw new InvalidOperationException( $"cast/sms-threads.json: thread {thread.Key} uses number {thread.Number}, which is not loaded." );
                var times = thread.Messages.Select( m => _inputs.Anchor.Date.AddDays( -m.DaysAgo ).Add( TimeSpan.Parse( m.Time, CultureInfo.InvariantCulture ) ) ).ToList();
                var lastStaff = thread.Messages.FindLastIndex( m => m.From == "staff" );
                for ( var i = 0; i < thread.Messages.Count; i++ )
                {
                    var message = thread.Messages[i];
                    var seed = $"{_inputs.Seed}:sms-thread:cast:{thread.Key}:{i}";
                    if ( message.From == "staff" )
                    {
                        AddStaffText( person, number, times[i], message.Text, seed );
                    }
                    else
                    {
                        var isRead = thread.State != "unread" || i < lastStaff;
                        Respond( person, number.Key, times[i], message.Text, isRead, seed );
                    }
                }

                if ( thread.State == "stopped" )
                {
                    _optOuts[person.PersonId] = times[times.Count - 1];
                }
            }
        }

        /// <summary>
        /// Short conversations for generated people, mostly on staff members' personal numbers.
        /// </summary>
        private void PlanConversations()
        {
            var design = _inputs.Design;
            var adults = _inputs.People.Where( p => !p.IsCast && p.Mobile != null && ( p.IsAdult || p.IsStudent ) ).ToList();
            var activeAdults = _inputs.People.Count( p => p.IsAdult );
            var days = ( _inputs.Anchor.Date - _inputs.From.Date ).TotalDays;
            var count = ( int ) Math.Round( design.ConversationsPerYearPerThousand * activeAdults / 1000 * days / 365 );
            if ( adults.Count == 0 || count == 0 )
            {
                return;
            }

            var weights = adults.Select( ( p, i ) => (i.ToString( CultureInfo.InvariantCulture ), Weight( p )) ).ToDictionary( x => x.Item1, x => x.Item2 );
            for ( var n = 0; n < count; n++ )
            {
                var seed = $"{_inputs.Seed}:sms-thread:{n}";
                var random = new SeededRandom( seed );
                var person = adults[int.Parse( random.Pick( weights ), CultureInfo.InvariantCulture )];
                var start = StartTime( random );
                if ( start < person.Since || ( _optOuts.TryGetValue( person.PersonId, out var stopped ) && stopped <= start ) )
                {
                    continue;
                }

                var (number, topic) = PickConversation( random, person );
                if ( number == null || topic == null )
                {
                    continue;
                }

                PlanConversation( random, seed, person, number, topic, start );
            }
        }

        private static double Weight( TextPerson person )
        {
            return person.IsStudent ? 1.2 : person.ConnectionStatus switch
            {
                "Leading" => 3,
                "Engaged" => 2.5,
                "Connected" => 1.5,
                _ => 0.6
            };
        }

        /// <summary>
        /// A conversation's start, on the measured weekdays and hours.
        /// </summary>
        private DateTime StartTime( SeededRandom random )
        {
            var days = ( int ) ( _inputs.Anchor.Date - _inputs.From.Date ).TotalDays;
            var day = _inputs.From.Date.AddDays( random.Between( 0, days - 1 ) );
            var weekday = int.Parse( random.Pick( _inputs.Design.InboundWeekdayMix ).Substring( 0, 1 ), CultureInfo.InvariantCulture ) % 7;
            day = day.AddDays( weekday - ( int ) day.DayOfWeek );
            if ( day >= _inputs.Anchor.Date )
            {
                day = day.AddDays( -7 );
            }

            if ( day < _inputs.From.Date )
            {
                day = day.AddDays( 7 );
            }

            var band = random.Pick( _inputs.Design.InboundHourBandMix ).Split( '-' );
            return day.AddHours( random.Between( int.Parse( band[0], CultureInfo.InvariantCulture ), int.Parse( band[1], CultureInfo.InvariantCulture ) ) ).AddMinutes( random.Between( 0, 59 ) );
        }

        /// <summary>
        /// Where the conversation happens and what it is about: a personal number for most, the student line for students
        /// and their parents now and then, and the main line otherwise.
        /// </summary>
        private (TextNumber Number, ConversationTopic Topic) PickConversation( SeededRandom random, TextPerson person )
        {
            var isParent = person.IsAdult && person.HasStudent;
            var place = random.Pick( _inputs.Design.ConversationNumberMix );
            if ( place == "students" && !person.IsStudent && !isParent )
            {
                place = "personal";
            }

            TextNumber number;
            if ( place == "students" )
            {
                number = Number( "students" );
            }
            else if ( place == "personal" )
            {
                number = person.IsStudent ? Number( "isaac" )
                    : _inputs.Numbers.Values.FirstOrDefault( n => n.CampusKey != null && n.CampusKey == person.CampusKey )
                    ?? ( random.Chance( 0.6 ) ? Number( "ted" ) : Number( "alisha" ) );
            }
            else
            {
                number = Number( "main" );
            }

            if ( number == null )
            {
                return (null, null);
            }

            var audience = person.IsStudent ? "student" : place == "students" ? "parent" : "adult";
            var topics = _inputs.Conversations.Topics.Where( t => t.Audience == audience && t.Numbers.Contains( place ) ).ToList();
            return (number, topics.Count == 0 ? null : random.Pick( topics ));
        }

        private void PlanConversation( SeededRandom random, string seed, TextPerson person, TextNumber number, ConversationTopic topic, DateTime start )
        {
            var design = _inputs.Design;
            var length = random.Pick( design.ThreadLengthMix ) switch
            {
                "pctThreadsWith1Message" => 1,
                "pctThreadsWith2To3Messages" => random.Between( 2, 3 ),
                "pctThreadsWith4To9Messages" => random.Between( 4, 9 ),
                _ => random.Between( 10, 16 )
            };

            var tokens = new Dictionary<string, string> { ["{name}"] = person.NickName, ["{staff}"] = number.StaffFirstName, ["{campus}"] = _inputs.Campuses.FirstOrDefault( c => c.Key == person.CampusKey )?.Name ?? string.Empty };
            var messages = new List<(bool IsStaff, DateTime When, string Text)>();
            var when = start;
            for ( var i = 0; i < length; i++ )
            {
                var isStaff = i % 2 == 1;
                if ( i > 0 )
                {
                    when = when.AddMinutes( Delay( random, isStaff ? design.StaffReplyDelay : design.PersonReplyDelay ) );
                }

                if ( when >= _inputs.Anchor || ( _optOuts.TryGetValue( person.PersonId, out var stopped ) && stopped <= when ) )
                {
                    break;
                }

                var pool = i == 0 ? topic.Openers
                    : isStaff ? ( i == 1 ? topic.StaffReplies : topic.StaffFollowUps )
                    : i == length - 1 && length >= 4 && topic.Closings.Count > 0 && random.Chance( 0.6 ) ? topic.Closings : topic.PersonFollowUps;
                messages.Add( (isStaff, when, MessageRenderer.Fill( random.Pick( pool ), tokens )) );
            }

            var lastStaff = messages.FindLastIndex( m => m.IsStaff );
            var end = messages.Count == 0 ? start : messages[messages.Count - 1].When;
            var unreadTail = random.Chance( ( _inputs.Anchor - end ).TotalDays <= 14 ? design.RecentUnreadShare : design.OlderUnreadShare );
            for ( var i = 0; i < messages.Count; i++ )
            {
                var (isStaff, at, text) = messages[i];
                if ( isStaff )
                {
                    AddStaffText( person, number, at, text, $"{seed}:{i}" );
                }
                else
                {
                    Respond( person, number.Key, at, text, i < lastStaff || !unreadTail, $"{seed}:{i}" );
                }
            }
        }

        private static double Delay( SeededRandom random, IReadOnlyDictionary<string, double> mix )
        {
            var bin = Array.IndexOf( DelayBins, random.Pick( mix ) );
            var (from, to) = DelayMinutes[bin < 0 ? 0 : bin];
            return random.Between( from, to );
        }

        private bool IsRead( SeededRandom random, DateTime when, bool answered )
        {
            if ( answered )
            {
                return true;
            }

            return !random.Chance( ( _inputs.Anchor - when ).TotalDays <= 14 ? _inputs.Design.RecentUnreadShare : _inputs.Design.OlderUnreadShare );
        }

        #endregion Conversations

        #region Rows

        private TextNumber Number( string key ) => _inputs.Numbers.TryGetValue( key, out var number ) ? number : null;

        private static PlannedTextRecipient NewRecipient( TextPerson person, string seed )
        {
            return new PlannedTextRecipient
            {
                Person = person,
                Guid = SeededRandom.StableGuid( $"{seed}:recipient" ),
                HistoryGuid = SeededRandom.StableGuid( $"{seed}:history" ),
                UniqueMessageId = "SM" + SeededRandom.StableGuid( $"{seed}:sid" ).ToString( "N" )
            };
        }

        private static void Deliver( PlannedTextRecipient recipient, PlannedText send, DateTime at )
        {
            recipient.Status = Delivered;
            recipient.SendDateTime = at;
            recipient.SentMessage = send.Message.Replace( "{{ Person.NickName }}", recipient.Person.NickName );
        }

        /// <summary>
        /// One staff text in a conversation: its own communication to one person, as SMS Conversations sends it.
        /// </summary>
        private void AddStaffText( TextPerson person, TextNumber number, DateTime at, string text, string seed )
        {
            var send = new PlannedText
            {
                Key = "conversation",
                Name = $"From: {number.StaffFullName}",
                Message = text,
                NumberKey = number.Key,
                SenderAliasId = number.StaffAliasId,
                IsBulk = false,
                Audience = "person",
                IsConversation = true,
                Created = at,
                Sent = at
            };
            var recipient = NewRecipient( person, seed );
            recipient.ResponseCode = $"@{1000 + ( Math.Abs( StableNumber( seed ) ) % 9000 )}";
            Deliver( recipient, send, at );
            send.Recipients.Add( recipient );
            Texts.Add( send );
        }

        private void Respond( TextPerson person, string numberKey, DateTime when, string text, bool isRead, string seed )
        {
            Responses.Add( new PlannedResponse { Person = person, NumberKey = numberKey, When = when, Text = text, IsRead = isRead, Guid = SeededRandom.StableGuid( $"{seed}:response" ) } );
        }

        /// <summary>
        /// Rock's STOP handling: the person's newest delivered text in the 60 days before marks the unsubscribe.
        /// </summary>
        private void MarkOptOuts()
        {
            var delivered = Texts.SelectMany( t => t.Recipients ).Where( r => r.SendDateTime.HasValue && _optOuts.ContainsKey( r.Person.PersonId ) )
                .GroupBy( r => r.Person.PersonId ).ToDictionary( g => g.Key, g => g.ToList() );
            foreach ( var (personId, when) in _optOuts )
            {
                var target = delivered.GetValueOrDefault( personId )?
                    .Where( r => r.SendDateTime <= when && r.SendDateTime.Value.Date >= when.AddDays( -60 ).Date )
                    .OrderByDescending( r => r.SendDateTime ).FirstOrDefault();
                if ( target != null )
                {
                    target.UnsubscribeAt = when;
                }
            }
        }

        private static int StableNumber( string text )
        {
            unchecked
            {
                var hash = 17;
                foreach ( var c in text )
                {
                    hash = ( hash * 31 ) + c;
                }

                return hash == int.MinValue ? 0 : hash;
            }
        }

        #endregion Rows
    }

    internal sealed class TextPlanInputs
    {
        public string Seed { get; set; }

        public DateTime Anchor { get; set; }

        public DateTime From { get; set; }

        public CommunicationCalendar Calendar { get; set; }

        public TextLibrary Library { get; set; }

        public ConversationLibrary Conversations { get; set; }

        public List<CastThread> CastThreads { get; set; } = new List<CastThread>();

        public List<CommunicationListDefinition> Lists { get; set; } = new List<CommunicationListDefinition>();

        public List<PlannedListMember> Members { get; set; } = new List<PlannedListMember>();

        public List<TextPerson> People { get; set; } = new List<TextPerson>();

        public Dictionary<Guid, int> PersonIdsByGuid { get; set; } = new Dictionary<Guid, int>();

        public List<EmailCampus> Campuses { get; set; } = new List<EmailCampus>();

        public string MainCampusKey { get; set; }

        /// <summary>
        /// The church's texting numbers, by key. Campus pastors' numbers have their campus.
        /// </summary>
        public Dictionary<string, TextNumber> Numbers { get; set; } = new Dictionary<string, TextNumber>();

        public int TextSenderAliasId { get; set; }

        public int StudentSenderAliasId { get; set; }

        public TextDesign Design { get; set; }
    }

    /// <summary>
    /// The measured texting shapes and the designed rates, from the profile.
    /// </summary>
    internal sealed class TextDesign
    {
        public TextDesign( ProfileTargets profile )
        {
            StopsPerThousand = profile.Number( "communicationDetail.unsubscribesYear.perThousandMassRecipients.SMS.All", 0.34 );
            StopDelayMix = Fallback( profile.Map( "communicationDetail.unsubscribesYear.delayMix" ), "1-same day" );
            AskReplyPercent = profile.Number( "communicationDetail.design.textReplies.pctOfAskingTextRecipients", 12 );
            OtherReplyPercent = profile.Number( "communicationDetail.design.textReplies.pctOfOtherTextRecipients", 0.8 );
            ReplyMix = Fallback( profile.Map( "communicationDetail.design.textReplies.kindMix" ), "yes" );
            ConversationsPerYearPerThousand = profile.Number( "communicationDetail.design.conversationsPerYearPerThousandActiveAdults", 50 );
            ConversationNumberMix = Fallback( profile.Map( "communicationDetail.design.conversationNumberMix" ), "personal" );
            RecentUnreadShare = profile.Number( "communicationDetail.design.unreadShareOfUnansweredTexts.recent", 80 ) / 100;
            OlderUnreadShare = profile.Number( "communicationDetail.design.unreadShareOfUnansweredTexts.older", 35 ) / 100;
            ThreadLengthMix = Fallback( profile.Map( "communicationDetail.sms.threads" ).Where( k => k.Key.StartsWith( "pctThreadsWith", StringComparison.Ordinal ) && k.Key.Contains( "Message" ) ).ToDictionary( k => k.Key, k => k.Value ), "pctThreadsWith2To3Messages" );
            StaffReplyDelay = Fallback( Answered( profile.Map( "communicationDetail.sms.replyDelay.churchToPerson" ) ), "pctUnder15Minutes" );
            PersonReplyDelay = Fallback( Answered( profile.Map( "communicationDetail.sms.replyDelay.personToChurch" ) ), "pctUnder15Minutes" );
            InboundWeekdayMix = Fallback( profile.Map( "communicationDetail.sms.inboundWeekdayMix" ), "4-Thu" );
            InboundHourBandMix = Fallback( profile.Map( "communicationDetail.sms.inboundHourBandMix" ).Where( h => h.Key != "00-05" ).ToDictionary( h => h.Key, h => h.Value ), "12-14" );
        }

        public double StopsPerThousand { get; }

        public Dictionary<string, double> StopDelayMix { get; }

        public double AskReplyPercent { get; }

        public double OtherReplyPercent { get; }

        public Dictionary<string, double> ReplyMix { get; }

        public double ConversationsPerYearPerThousand { get; }

        public Dictionary<string, double> ConversationNumberMix { get; }

        public double RecentUnreadShare { get; }

        public double OlderUnreadShare { get; }

        public Dictionary<string, double> ThreadLengthMix { get; }

        public Dictionary<string, double> StaffReplyDelay { get; }

        public Dictionary<string, double> PersonReplyDelay { get; }

        public Dictionary<string, double> InboundWeekdayMix { get; }

        public Dictionary<string, double> InboundHourBandMix { get; }

        /// <summary>
        /// The delay bins of the texts that were answered, without the answered share itself.
        /// </summary>
        private static Dictionary<string, double> Answered( Dictionary<string, double> map ) => map.Where( m => m.Key != "pctAnswered" ).ToDictionary( m => m.Key, m => m.Value );

        private static Dictionary<string, double> Fallback( Dictionary<string, double> map, string key ) => map.Count > 0 ? map : new Dictionary<string, double> { [key] = 1 };
    }

    /// <summary>
    /// What the text planner knows about one person.
    /// </summary>
    internal sealed class TextPerson
    {
        public int PersonId { get; set; }

        public int AliasId { get; set; }

        public Guid Guid { get; set; }

        public string NickName { get; set; }

        public string CampusKey { get; set; }

        public string ConnectionStatus { get; set; }

        public bool IsCast { get; set; }

        public bool IsAdult { get; set; }

        public bool IsStudent { get; set; }

        /// <summary>
        /// For an adult, a student lives in one of their households.
        /// </summary>
        public bool HasStudent { get; set; }

        public bool IsInvolved { get; set; }

        public bool PrefersText { get; set; }

        /// <summary>
        /// The mobile number that can text, ten digits, or null.
        /// </summary>
        public string Mobile { get; set; }

        /// <summary>
        /// The Id of that phone number row, or null.
        /// </summary>
        public int? MobilePhoneId { get; set; }

        public DateTime Since { get; set; }

        public List<int> ParentIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// One of the church's texting numbers and who texts from it.
    /// </summary>
    internal sealed class TextNumber
    {
        public string Key { get; set; }

        public int Id { get; set; }

        /// <summary>
        /// The staff member who answers on it.
        /// </summary>
        public int StaffAliasId { get; set; }

        public string StaffFirstName { get; set; }

        public string StaffFullName { get; set; }

        /// <summary>
        /// The person it is assigned to, for a personal number.
        /// </summary>
        public int? AssignedAliasId { get; set; }

        /// <summary>
        /// The campus of a campus pastor's number, or null.
        /// </summary>
        public string CampusKey { get; set; }
    }

    internal sealed class PlannedText
    {
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The text as Rock stores it on the communication, with Rock's Lava for each recipient.
        /// </summary>
        public string Message { get; set; }

        public string NumberKey { get; set; }

        public int SenderAliasId { get; set; }

        public bool IsBulk { get; set; }

        public string Topic { get; set; }

        public string Audience { get; set; }

        public string ListKey { get; set; }

        public string ListCampusKey { get; set; }

        public string CampusKey { get; set; }

        public bool AsksReply { get; set; }

        /// <summary>
        /// A staff text in a conversation, sent from SMS Conversations.
        /// </summary>
        public bool IsConversation { get; set; }

        public DateTime Created { get; set; }

        public DateTime Sent { get; set; }

        public List<PlannedTextRecipient> Recipients { get; } = new List<PlannedTextRecipient>();
    }

    internal sealed class PlannedTextRecipient
    {
        public TextPerson Person { get; set; }

        public Guid Guid { get; set; }

        public Guid HistoryGuid { get; set; }

        public string UniqueMessageId { get; set; }

        public int Status { get; set; }

        public string StatusNote { get; set; }

        public DateTime? SendDateTime { get; set; }

        /// <summary>
        /// The text as the person got it, which SMS Conversations shows.
        /// </summary>
        public string SentMessage { get; set; }

        public string ResponseCode { get; set; }

        /// <summary>
        /// When the person texted STOP, for the text Rock marks as unsubscribed.
        /// </summary>
        public DateTime? UnsubscribeAt { get; set; }
    }

    /// <summary>
    /// A text a person sent to one of the church's numbers.
    /// </summary>
    internal sealed class PlannedResponse
    {
        public TextPerson Person { get; set; }

        public string NumberKey { get; set; }

        public DateTime When { get; set; }

        public string Text { get; set; }

        public bool IsRead { get; set; }

        public Guid Guid { get; set; }
    }
}
