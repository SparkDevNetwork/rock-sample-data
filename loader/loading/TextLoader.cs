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

using System.Data;
using System.Globalization;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/23/26 - CLAUDE

        The rows are the ones Rock writes for texts sent through Twilio and texts people send back
        (Rock/Communication/Transport/Twilio.cs and Rock/Communication/Medium/Sms.cs): a communication
        per mass text or per staff text in a conversation, a Delivered recipient with the text as the
        person got it in SentMessage, a "SMS message" line on the person's History, and a
        CommunicationResponse for each text a person sent, read or unread. SMS Conversations orders a
        thread by CreatedDateTime, so every row is created when it was sent. A STOP turns off texting on
        the person's mobile, with the History line Rock's phone number save hook writes.

        Reason: SMS Conversations and each person's record show texts the way Rock writes them (decision 93).
    */

    /// <summary>
    /// Plans and writes every text: mass texts, replies, STOPs, the Cast's threads, and generated conversations.
    /// </summary>
    internal sealed class TextLoader
    {
        private static readonly Guid SmsMediumGuid = new Guid( "4BC02764-512A-4A10-ACDE-586F71D8A8BD" );
        private static readonly Guid TwilioGuid = new Guid( "CF9FD146-8623-4D9A-98E6-4BD710F071A4" );
        private static readonly Guid CommunicationEntityTypeGuid = new Guid( "C4CCBD91-1264-48BF-BC33-92751C8948B5" );
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid CommunicationHistoryCategoryGuid = new Guid( "F291034B-7581-48F3-B522-E31B8534D529" );
        private static readonly Guid DemographicHistoryCategoryGuid = new Guid( "51D3EC5A-D079-45ED-909E-B0AB2FD06835" );
        private static readonly Guid MobilePhoneTypeGuid = new Guid( "407E7E45-7B2E-4FCD-9605-ECB1339F2453" );
        private const string TwilioTransport = "Rock.Communication.Transport.Twilio";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly ProfileTargets _profile;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private HashSet<Guid> _castGuids = new HashSet<Guid>();

        public TextLoader( RockDatabase db, SampleDataSource source, ProfileTargets profile, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _profile = profile;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        public TextLoader WithCast( IEnumerable<Guid> castPeople )
        {
            _castGuids = castPeople.ToHashSet();
            return this;
        }

        /// <summary>
        /// Plans and writes every text over the same 2 years as email.
        /// </summary>
        public void Load( CommunicationsConfiguration configuration, PeopleLoader people, List<ListCandidate> candidates, List<PlannedListMember> members,
            Dictionary<(string ListKey, string CampusKey), int> listIds, string mainCampusKey, IReadOnlyDictionary<string, int> campusIds, CommunicationCalendar calendar, List<EmailCampus> campuses )
        {
            var from = _dates.Anchor.AddYears( -2 );
            var textPeople = People( people, candidates, from );
            var numbers = Numbers( configuration, people );
            var planner = new TextPlanner( new TextPlanInputs
            {
                Seed = _seed,
                Anchor = _dates.Anchor,
                From = from,
                Calendar = calendar,
                Library = _source.ReadTextLibrary(),
                Conversations = _source.ReadConversationLibrary(),
                CastThreads = _source.ReadCastThreads(),
                Lists = configuration.Lists.Lists,
                Members = members,
                People = textPeople,
                PersonIdsByGuid = people.People.ToDictionary( p => p.Key, p => p.Value.PersonId ),
                Campuses = campuses,
                MainCampusKey = mainCampusKey,
                Numbers = numbers,
                TextSenderAliasId = people.People[configuration.Senders.Texts.Sender].AliasId,
                StudentSenderAliasId = people.People[configuration.Senders.Students.Sender].AliasId,
                Design = new TextDesign( _profile )
            } );
            planner.Plan();

            var approverAlias = people.People[configuration.Senders.Approver].AliasId;
            var topicIds = configuration.Topics.ToDictionary( t => t.Key, t => _db.RequireIdByGuid( "DefinedValue", t.Value ) );
            Write( planner, numbers, listIds, topicIds, approverAlias );

            var mass = planner.Texts.Where( t => !t.IsConversation ).ToList();
            var threads = planner.Responses.Select( r => (r.Person.PersonId, r.NumberKey) ).Concat( planner.Texts.Where( t => t.IsConversation ).Select( t => (t.Recipients[0].Person.PersonId, t.NumberKey) ) ).Distinct().Count();
            _db.Log.Write( $"  Texts: {mass.Count:N0} mass texts to {mass.Sum( t => t.Recipients.Count ):N0} recipients ({mass.Sum( t => t.Recipients.Count( r => r.Status == TextPlanner.Delivered ) ):N0} delivered), {planner.Texts.Count - mass.Count:N0} staff texts in conversations" );
            _db.Log.Write( $"  SMS conversations: {threads:N0} threads, {planner.Responses.Count:N0} texts from people ({planner.Responses.Count( r => !r.IsRead ):N0} unread), {planner.OptOuts.Count:N0} texted STOP" );
        }

        /// <summary>
        /// Everyone a text could reach: adults, students, and volunteers, with their mobile number when it can text.
        /// </summary>
        private List<TextPerson> People( PeopleLoader people, List<ListCandidate> candidates, DateTime from )
        {
            var mobileTypeId = _db.RequireIdByGuid( "DefinedValue", MobilePhoneTypeGuid );
            var mobiles = _db.Query( "SELECT [Id], [PersonId], [Number] FROM [PhoneNumber] WHERE [NumberTypeValueId] = @Type AND [IsMessagingEnabled] = 1", P( "@Type", mobileTypeId ) )
                .GroupBy( r => ( int ) r["PersonId"] ).ToDictionary( g => g.Key, g => (Id: ( int ) g.First()["Id"], Number: ( string ) g.First()["Number"]) );
            var byId = people.People.Values.GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var parents = new Dictionary<int, List<int>>();
            var hasStudent = new HashSet<int>();
            var students = candidates.Where( c => !c.IsAdult && c.Grade.HasValue ).Select( c => c.PersonId ).ToHashSet();
            foreach ( var household in people.Households.Where( h => h.Cast.Kind != "business" ) )
            {
                var adults = household.Members.Where( m => m.IsAdult && m.CanAttend ).Select( m => m.PersonId ).ToList();
                foreach ( var child in household.Members.Where( m => students.Contains( m.PersonId ) ) )
                {
                    if ( !parents.TryGetValue( child.PersonId, out var list ) )
                    {
                        list = parents[child.PersonId] = new List<int>();
                    }

                    list.AddRange( adults.Where( a => !list.Contains( a ) ) );
                    hasStudent.UnionWith( adults );
                }
            }

            return candidates.Select( c =>
            {
                var person = byId[c.PersonId];
                var mobile = mobiles.TryGetValue( c.PersonId, out var m ) ? m : ((int Id, string Number)?) null;
                return new TextPerson
                {
                    PersonId = c.PersonId,
                    AliasId = person.AliasId,
                    Guid = person.Cast.Guid,
                    NickName = person.Cast.NickName ?? person.Cast.FirstName,
                    CampusKey = c.CampusKey,
                    ConnectionStatus = person.Cast.ConnectionStatus,
                    IsCast = _castGuids.Contains( person.Cast.Guid ),
                    IsAdult = c.IsAdult,
                    IsStudent = students.Contains( c.PersonId ),
                    HasStudent = hasStudent.Contains( c.PersonId ),
                    IsInvolved = c.IsInvolved,
                    PrefersText = person.Cast.PrefersText,
                    Mobile = mobile?.Number,
                    MobilePhoneId = mobile?.Id,
                    Since = c.FirstSunday.HasValue && c.FirstSunday.Value > from ? c.FirstSunday.Value : from,
                    ParentIds = parents.GetValueOrDefault( c.PersonId ) ?? new List<int>()
                };
            } ).ToList();
        }

        /// <summary>
        /// The church's numbers as loaded, with who answers on each: the owner of a personal number, Alisha Marble on
        /// the main line, and the student pastor on the student line.
        /// </summary>
        private Dictionary<string, TextNumber> Numbers( CommunicationsConfiguration configuration, PeopleLoader people )
        {
            var keysByGuid = configuration.PhoneNumbers.Numbers.ToDictionary( n => n.Guid, n => n.Key );
            var campusByGuid = configuration.PhoneNumbers.CampusPastors.Numbers.ToDictionary( n => n.Value.Guid, n => n.Key );
            var byAlias = people.People.Values.GroupBy( p => p.AliasId ).ToDictionary( g => g.Key, g => g.First() );
            var shared = new Dictionary<string, Guid>
            {
                ["main"] = configuration.PhoneNumbers.Numbers.First( n => n.AssignedTo.HasValue && n.Key == "alisha" ).AssignedTo.Value,
                ["students"] = configuration.Senders.Students.Sender
            };

            var numbers = new Dictionary<string, TextNumber>();
            foreach ( var row in _db.Query( "SELECT [Id], [Guid], [AssignedToPersonAliasId] FROM [SystemPhoneNumber] WHERE [ForeignKey] = @ForeignKey", P( "@ForeignKey", _foreignKey ) ) )
            {
                var guid = ( Guid ) row["Guid"];
                var assigned = row["AssignedToPersonAliasId"] is int alias ? alias : ( int? ) null;
                var key = keysByGuid.TryGetValue( guid, out var configured ) ? configured : campusByGuid.TryGetValue( guid, out var campus ) ? $"pastor-{campus}" : null;
                if ( key == null )
                {
                    continue;
                }

                var staff = assigned.HasValue ? byAlias[assigned.Value] : people.People[shared[key]];
                numbers[key] = new TextNumber
                {
                    Key = key,
                    Id = ( int ) row["Id"],
                    StaffAliasId = staff.AliasId,
                    StaffFirstName = staff.Cast.NickName ?? staff.Cast.FirstName,
                    StaffFullName = $"{staff.Cast.NickName ?? staff.Cast.FirstName} {staff.Cast.LastName}",
                    AssignedAliasId = assigned,
                    CampusKey = campusByGuid.GetValueOrDefault( guid )
                };
            }

            return numbers;
        }

        private void Write( TextPlanner planner, Dictionary<string, TextNumber> numbers, Dictionary<(string ListKey, string CampusKey), int> listIds, Dictionary<string, int> topicIds, int approverAlias )
        {
            var smsMediumId = _db.RequireIdByGuid( "EntityType", SmsMediumGuid );
            var twilioId = _db.RequireIdByGuid( "EntityType", TwilioGuid );
            var communicationTypeId = _db.RequireIdByGuid( "EntityType", CommunicationEntityTypeGuid );
            var personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var historyCategoryId = _db.RequireIdByGuid( "Category", CommunicationHistoryCategoryGuid );
            var nextCommunicationId = NextId( "Communication" );
            var nextRecipientId = NextId( "CommunicationRecipient" );

            var communications = TableFactory.Create( "Id:int", "Name:string", "CommunicationType:int", "Status:int", "IsBulkCommunication:bool", "SenderPersonAliasId:int", "ReviewerPersonAliasId:int",
                "ReviewedDateTime:date", "ListGroupId:int", "SegmentCriteria:int", "SMSMessage:string", "SmsFromSystemPhoneNumberId:int", "SendDateTime:date", "SendDateKey:int",
                "ExcludeDuplicateRecipientAddress:bool", "CommunicationTopicValueId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var recipients = TableFactory.Create( "Id:int", "CommunicationId:int", "PersonAliasId:int", "Status:int", "StatusNote:string", "MediumEntityTypeId:int", "TransportEntityTypeName:string",
                "UniqueMessageId:string", "SendDateTime:date", "FirstSendAttemptDateTime:date", "DeliveredDateTime:date", "SentMessage:string", "ResponseCode:string",
                "UnsubscribeDateTime:date", "UnsubscribeLevel:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
                "Verb:string", "ChangeType:string", "ValueName:string", "OldValue:string", "NewValue:string", "IsSensitive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int" );
            var responses = TableFactory.Create( "MessageKey:string", "FromPersonAliasId:int", "ToPersonAliasId:int", "IsRead:bool", "RelatedSmsFromSystemPhoneNumberId:int", "RelatedTransportEntityTypeId:int",
                "RelatedMediumEntityTypeId:int", "Response:string", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );

            object Value( object value ) => value ?? DBNull.Value;
            var guids = new SeededRandom( $"{_seed}:text:guids" );

            foreach ( var text in planner.Texts )
            {
                var id = nextCommunicationId++;
                var number = numbers[text.NumberKey];
                var reviewer = text.IsConversation ? text.SenderAliasId : approverAlias;
                var listId = text.ListKey != null && listIds.TryGetValue( (text.ListKey, text.ListCampusKey), out var l ) ? l : ( int? ) null;
                communications.Rows.Add( id, text.Name, 2, 3, text.IsBulk, text.SenderAliasId, reviewer, text.Created, Value( listId ), 0, text.Message, number.Id,
                    text.Sent, DateKey( text.Sent ), false, Value( text.Topic != null && topicIds.TryGetValue( text.Topic, out var topic ) ? topic : ( int? ) null ),
                    guids.NextGuid(), text.Created, text.Sent, text.SenderAliasId, text.SenderAliasId, _foreignKey );

                foreach ( var recipient in text.Recipients )
                {
                    var created = recipient.SendDateTime ?? text.Sent;
                    recipients.Rows.Add( nextRecipientId++, id, recipient.Person.AliasId, recipient.Status, Value( recipient.StatusNote ), smsMediumId,
                        recipient.SendDateTime.HasValue ? TwilioTransport : DBNull.Value, recipient.SendDateTime.HasValue ? recipient.UniqueMessageId : DBNull.Value,
                        Value( recipient.SendDateTime ), text.Sent, Value( recipient.SendDateTime ), Value( recipient.SentMessage ), Value( recipient.ResponseCode ),
                        Value( recipient.UnsubscribeAt ), recipient.UnsubscribeAt.HasValue ? TextPlanner.UnsubscribeAll : DBNull.Value,
                        recipient.Guid, created, recipient.UnsubscribeAt ?? created, _foreignKey );

                    // Rock's Twilio transport writes a "SMS message" line on the person after each text it sends, by the sender.
                    if ( recipient.SendDateTime.HasValue )
                    {
                        var caption = recipient.SentMessage.Length > 200 ? recipient.SentMessage.Substring( 0, 200 ) : recipient.SentMessage;
                        history.Rows.Add( false, historyCategoryId, personTypeId, recipient.Person.PersonId, caption, communicationTypeId, id, "SENT", "Record", "SMS message",
                            DBNull.Value, DBNull.Value, false, recipient.HistoryGuid, _foreignKey, recipient.SendDateTime.Value, recipient.SendDateTime.Value, text.SenderAliasId );
                    }
                }
            }

            foreach ( var response in planner.Responses )
            {
                var number = numbers[response.NumberKey];
                responses.Rows.Add( $"1{response.Person.Mobile}", response.Person.AliasId, Value( number.AssignedAliasId ), response.IsRead, number.Id, twilioId, smsMediumId,
                    response.Text, response.Guid, response.When, response.When, _foreignKey );
            }

            _db.BulkInsert( "Communication", communications, keepIdentity: true );
            _db.BulkInsert( "CommunicationRecipient", recipients, keepIdentity: true );
            _db.BulkInsert( "CommunicationResponse", responses, keepIdentity: false );
            WriteOptOuts( planner, history, personTypeId );
            _db.BulkInsert( "History", history, keepIdentity: false );
        }

        /// <summary>
        /// Turns off texting on the mobile of everyone who texted STOP, the way Rock's opt-out tracking does, with the
        /// History line Rock's phone number save hook writes.
        /// </summary>
        private void WriteOptOuts( TextPlanner planner, DataTable history, int personTypeId )
        {
            if ( planner.OptOuts.Count == 0 )
            {
                return;
            }

            var categoryId = _db.RequireIdByGuid( "Category", DemographicHistoryCategoryGuid );
            var updates = TableFactory.Create( "Id:int", "MessagingOptedOutDateTime:date" );
            var people = planner.Responses.Select( r => r.Person ).GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var names = _db.Query( "SELECT [Id], [NickName], [LastName] FROM [Person] WHERE [ForeignKey] = @ForeignKey", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["Id"], r => $"{r["NickName"]} {r["LastName"]}" );
            foreach ( var (personId, when) in planner.OptOuts.OrderBy( o => o.Key ) )
            {
                if ( !people.TryGetValue( personId, out var person ) || person.MobilePhoneId == null )
                {
                    continue;
                }

                updates.Rows.Add( person.MobilePhoneId.Value, when );
                history.Rows.Add( false, categoryId, personTypeId, personId, names.GetValueOrDefault( personId, string.Empty ), DBNull.Value, DBNull.Value, "MODIFY", "Property",
                    "Mobile Phone Messaging Enabled", "True", "False", false, SeededRandom.StableGuid( $"{_seed}:text:opt-out:{personId}" ), _foreignKey, when, when, DBNull.Value );
            }

            _db.Execute( "CREATE TABLE #OptOut ( [Id] INT PRIMARY KEY, [MessagingOptedOutDateTime] DATETIME )" );
            _db.BulkInsert( "#OptOut", updates, keepIdentity: false );
            _db.Execute( @"
UPDATE [pn]
SET [pn].[IsMessagingEnabled] = 0, [pn].[IsMessagingOptedOut] = 1, [pn].[MessagingOptedOutDateTime] = [o].[MessagingOptedOutDateTime], [pn].[ModifiedDateTime] = [o].[MessagingOptedOutDateTime]
FROM [PhoneNumber] AS [pn]
INNER JOIN #OptOut AS [o] ON [o].[Id] = [pn].[Id];
DROP TABLE #OptOut;" );
        }

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );

        private static int DateKey( DateTime date ) => ( date.Year * 10000 ) + ( date.Month * 100 ) + date.Day;
    }
}
