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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/23/26 - CLAUDE

        The rows are the ones Rock writes when it sends a bulk email through Mailgun and the webhook
        reports back: the communication, approved with its send time; one recipient row per person; a
        "Sent" history line on each person Rock emailed; an interaction component per communication;
        and an interaction with its own session and device type for each open and click. Email
        recipients keep no SentMessage, because Rock's email transport never stores one. Rows go out
        in chunks, so Full 7's history never sits in memory at once.

        Reason: Communication Detail, the person's History tab, and the Communication List read these
        rows as Rock wrote them (decisions 92 and 132).
    */

    /// <summary>
    /// Writes the planned bulk emails and everything Rock records about them.
    /// </summary>
    internal sealed class EmailWriter
    {
        private const int ChunkRows = 300000;
        private const int ApprovedStatus = 3;
        private const int EmailType = 1;

        private static readonly Guid CommunicationChannelGuid = new Guid( "C88A187F-0343-4E7C-AF3F-79A8989DFA65" );
        private static readonly Guid EmailMediumGuid = new Guid( "5A653EBE-6803-44B4-85D2-FB7B8146D55D" );
        private static readonly Guid CommunicationEntityTypeGuid = new Guid( "C4CCBD91-1264-48BF-BC33-92751C8948B5" );
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid CommunicationHistoryCategoryGuid = new Guid( "F291034B-7581-48F3-B522-E31B8534D529" );
        private const string MailgunTransport = "Rock.Communication.Transport.MailgunHttp";

        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly int _channelId;
        private readonly int _mediumId;
        private readonly int _communicationEntityTypeId;
        private readonly int _personEntityTypeId;
        private readonly int _historyCategoryId;

        private readonly DataTable _communications = TableFactory.Create( "Id:int", "Name:string", "Subject:string", "CommunicationType:int", "Status:int", "IsBulkCommunication:bool",
            "SenderPersonAliasId:int", "ReviewerPersonAliasId:int", "ReviewedDateTime:date", "ListGroupId:int", "SegmentCriteria:int", "CommunicationTemplateId:int", "SystemCommunicationId:int", "FromName:string", "FromEmail:string",
            "Message:string", "FutureSendDateTime:date", "SendDateTime:date", "SendDateKey:int", "ExcludeDuplicateRecipientAddress:bool", "CommunicationTopicValueId:int",
            "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );

        private readonly DataTable _components = TableFactory.Create( "Id:int", "Name:string", "EntityId:int", "InteractionChannelId:int", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );

        private readonly DataTable _recipients = TableFactory.Create( "Id:int", "CommunicationId:int", "PersonAliasId:int", "Status:int", "StatusNote:string", "MediumEntityTypeId:int",
            "TransportEntityTypeName:string", "SendDateTime:date", "FirstSendAttemptDateTime:date", "DeliveredDateTime:date", "OpenedDateTime:date", "OpenedClient:string",
            "UnsubscribeDateTime:date", "UnsubscribeLevel:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );

        private readonly DataTable _sessions = TableFactory.Create( "Id:int", "DeviceTypeId:int", "IpAddress:string", "SessionStartDateKey:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );

        private readonly DataTable _interactions = TableFactory.Create( "InteractionDateTime:date", "Operation:string", "InteractionComponentId:int", "EntityId:int", "PersonAliasId:int", "InteractionSessionId:int",
            "InteractionSummary:string", "InteractionData:string", "InteractionDateKey:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );

        private readonly DataTable _history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
            "Verb:string", "ChangeType:string", "ValueName:string", "RelatedData:string", "IsSensitive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date",
            "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );

        private int _nextCommunicationId;
        private int _nextComponentId;
        private int _nextRecipientId;
        private int _nextSessionId;

        public EmailWriter( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
            _channelId = db.RequireIdByGuid( "InteractionChannel", CommunicationChannelGuid );
            _mediumId = db.RequireIdByGuid( "EntityType", EmailMediumGuid );
            _communicationEntityTypeId = db.RequireIdByGuid( "EntityType", CommunicationEntityTypeGuid );
            _personEntityTypeId = db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            _historyCategoryId = db.RequireIdByGuid( "Category", CommunicationHistoryCategoryGuid );
            _nextCommunicationId = NextId( "Communication" );
            _nextComponentId = NextId( "InteractionComponent" );
            _nextRecipientId = NextId( "CommunicationRecipient" );
            _nextSessionId = NextId( "InteractionSession" );

            // Each chunk would otherwise disable and rebuild these tables' indexes, which grows with every chunk.
            // Finish rebuilds them once, after the last chunk, unless the Communications step still defers them.
            foreach ( var table in LargeTables )
            {
                db.DeferIndexRebuild( table );
            }
        }

        /// <summary>
        /// The tables that take millions of rows from communications. Their index rebuilds are deferred while they load.
        /// </summary>
        public static readonly string[] LargeTables = { "CommunicationRecipient", "InteractionSession", "Interaction", "History" };

        public int Communications { get; private set; }

        public int Recipients { get; private set; }

        public int Interactions { get; private set; }

        /// <summary>
        /// Adds each client as a Rock interaction device type, the way Rock names one it first sees, and
        /// sets its Id.
        /// </summary>
        public void AddDeviceTypes( IEnumerable<EmailClient> clients )
        {
            foreach ( var client in clients )
            {
                client.DeviceTypeId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [InteractionDeviceType] ( [Name], [DeviceTypeData], [ClientType], [OperatingSystem], [Application], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Data, @ClientType, @Os, @Application, NEWID(), GETDATE(), GETDATE(), @ForeignKey )",
                    P( "@Name", $"{client.OperatingSystem} - {client.Name}" ), P( "@Data", client.DeviceType ), P( "@ClientType", client.ClientType ), P( "@Os", client.OperatingSystem ),
                    P( "@Application", client.Name ), P( "@ForeignKey", _foreignKey ) ) );
            }
        }

        /// <summary>
        /// Adds one send and everything about it. Returns the communication's Id and its first recipient's Id; the
        /// rest follow in the send's recipient order.
        /// </summary>
        public (int CommunicationId, int FirstRecipientId) Add( PlannedEmail send, EmailSendContext context, IReadOnlyList<EmailClient> clients )
        {
            var communicationId = _nextCommunicationId++;
            var firstRecipientId = _nextRecipientId;
            Communications++;
            var reviewed = context.NeedsApproval ? send.Created.AddMinutes( 20 + ( send.Index % 200 ) ) : send.Created;
            var sendDateTime = send.Sent > reviewed ? send.Sent : reviewed.AddMinutes( 5 );

            _communications.Rows.Add( communicationId, send.Name, send.Subject, EmailType, ApprovedStatus, send.IsBulk,
                Value( context.SenderAliasId ), Value( context.ReviewerAliasId ), reviewed, Value( context.ListGroupId ), 0, Value( context.TemplateId ), Value( context.SystemCommunicationId ), context.FromName, context.FromEmail,
                send.Html, send.IsScheduled ? send.Sent : DBNull.Value, sendDateTime, DateKey( sendDateTime ), false, Value( context.TopicValueId ),
                context.Guid, send.Created, sendDateTime, Value( context.SenderAliasId ), Value( context.SenderAliasId ), _foreignKey );

            var delivered = send.Recipients.Where( r => r.DeliveredDateTime.HasValue ).Select( r => r.DeliveredDateTime.Value ).DefaultIfEmpty( sendDateTime ).Min();
            var componentId = _nextComponentId++;
            _components.Rows.Add( componentId, send.Subject, communicationId, _channelId, delivered, delivered, context.ComponentGuid, _foreignKey );

            foreach ( var recipient in send.Recipients )
            {
                var recipientId = _nextRecipientId++;
                Recipients++;
                var modified = new[] { recipient.SendDateTime, recipient.DeliveredDateTime, recipient.OpenedDateTime, recipient.UnsubscribeDateTime }.Max() ?? sendDateTime;
                _recipients.Rows.Add( recipientId, communicationId, recipient.Person.AliasId, recipient.Status, Value( recipient.StatusNote ), _mediumId,
                    recipient.SendDateTime.HasValue ? MailgunTransport : DBNull.Value, Value( recipient.SendDateTime ), sendDateTime, Value( recipient.DeliveredDateTime ),
                    Value( recipient.OpenedDateTime ), Value( recipient.OpenedClient ), Value( recipient.UnsubscribeDateTime ), Value( recipient.UnsubscribeLevel ),
                    recipient.Guid, sendDateTime, modified, _foreignKey );

                // Rock's email transport writes a "Sent" line on the person after every successful send, by the sender.
                if ( recipient.SendDateTime.HasValue )
                {
                    _history.Rows.Add( false, _historyCategoryId, _personEntityTypeId, recipient.Person.PersonId, Truncate( send.Subject, 200 ), _communicationEntityTypeId, communicationId,
                        "SENT", "Record", "Communication", context.FromName, false, recipient.HistoryGuid, _foreignKey, recipient.SendDateTime.Value, recipient.SendDateTime.Value,
                        Value( context.SenderAliasId ), Value( context.SenderAliasId ) );
                }

                foreach ( var interaction in recipient.Interactions )
                {
                    var client = clients[interaction.ClientIndex];
                    var sessionId = _nextSessionId++;
                    Interactions++;
                    _sessions.Rows.Add( sessionId, client.DeviceTypeId, context.IpAddress( recipient.Person ), DateKey( interaction.When ), interaction.SessionGuid, interaction.When, interaction.When, _foreignKey );
                    _interactions.Rows.Add( interaction.When, interaction.Operation, componentId, recipientId, recipient.Person.AliasId, sessionId,
                        string.Empty, interaction.Data, DateKey( interaction.When ), interaction.Guid, interaction.When, interaction.When, _foreignKey );
                }
            }

            if ( _recipients.Rows.Count + _interactions.Rows.Count + _history.Rows.Count >= ChunkRows )
            {
                Flush();
            }

            return (communicationId, firstRecipientId);
        }

        /// <summary>
        /// Takes a communication Id and a run of recipient Ids for a send another writer adds, such as a flow's text,
        /// so the Ids never collide with the ones this writer gives out.
        /// </summary>
        public (int CommunicationId, int FirstRecipientId) Reserve( int recipients )
        {
            var communicationId = _nextCommunicationId++;
            var firstRecipientId = _nextRecipientId;
            _nextRecipientId += recipients;
            return (communicationId, firstRecipientId);
        }

        /// <summary>
        /// Writes the last chunk and ends this writer's deferral of the index rebuilds. Nothing reads these tables before
        /// the last deferral ends.
        /// </summary>
        public void Finish()
        {
            Flush();
            _db.RebuildDeferredIndexes( LargeTables );
        }

        /// <summary>
        /// Writes what is waiting, parents before the rows that point to them.
        /// </summary>
        public void Flush()
        {
            _db.BulkInsert( "Communication", _communications, keepIdentity: true );
            _db.BulkInsert( "InteractionComponent", _components, keepIdentity: true );
            _db.BulkInsert( "CommunicationRecipient", _recipients, keepIdentity: true );
            _db.BulkInsert( "InteractionSession", _sessions, keepIdentity: true );
            _db.BulkInsert( "Interaction", _interactions, keepIdentity: false );
            _db.BulkInsert( "History", _history, keepIdentity: false );
            foreach ( var table in new[] { _communications, _components, _recipients, _sessions, _interactions, _history } )
            {
                table.Rows.Clear();
            }
        }

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );

        private static int DateKey( DateTime date ) => ( date.Year * 10000 ) + ( date.Month * 100 ) + date.Day;

        private static object Value( object value ) => value ?? DBNull.Value;

        private static string Truncate( string text, int length ) => text.Length <= length ? text : text.Substring( 0, length );
    }

    /// <summary>
    /// What the writer needs to know about a send beyond the plan: who sent and approved it, its list,
    /// template, topic, and from line, and the Guids its rows take.
    /// </summary>
    internal sealed class EmailSendContext
    {
        /// <summary>
        /// The person who sent it, or null for a system email Rock's jobs send.
        /// </summary>
        public int? SenderAliasId { get; set; }

        public int? ReviewerAliasId { get; set; }

        public bool NeedsApproval { get; set; }

        public int? ListGroupId { get; set; }

        public int? TemplateId { get; set; }

        /// <summary>
        /// The Rock system communication a job sent it from, or null.
        /// </summary>
        public int? SystemCommunicationId { get; set; }

        public int? TopicValueId { get; set; }

        public string FromName { get; set; }

        public string FromEmail { get; set; }

        public Guid Guid { get; set; }

        public Guid ComponentGuid { get; set; }

        /// <summary>
        /// The address a person's opens and clicks come from.
        /// </summary>
        public Func<EmailPerson, string> IpAddress { get; set; }
    }
}
