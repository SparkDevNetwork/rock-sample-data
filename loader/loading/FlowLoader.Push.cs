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
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/30/26 - CLAUDE

        A flow push goes to everyone still Active in the run, as Rock's job sends it. Rock Mobile Push delivers it
        to a person with an app device that has notifications on, and fails it for anyone else with the note Rock
        writes, so most of an audience shows Failed. A delivered push opens at the church app's designed rate, with
        the same interaction, session, and History line the app's other pushes have.

        Reason: Christmas at Rock Solid sends a push (decision D-comm-flows-14).
    */

    internal sealed partial class FlowLoader
    {
        private const int PushType = 3;
        private const int OpenedStatus = 4;
        private const string NoDevices = "No Personal Devices with Messaging Enabled";
        private const string RockMobilePushTransport = "Rock.Communication.Transport.RockMobilePush";
        private static readonly Guid PushMediumGuid = new Guid( "3638C6DF-4FF3-4A52-B4B8-AFB754991597" );
        private static readonly Guid CommunicationChannelGuid = new Guid( "C88A187F-0343-4E7C-AF3F-79A8989DFA65" );

        private readonly DataTable _pushes = TableFactory.Create( "Id:int", "Name:string", "CommunicationType:int", "Status:int", "IsBulkCommunication:bool", "SenderPersonAliasId:int", "ReviewerPersonAliasId:int",
            "ReviewedDateTime:date", "SegmentCriteria:int", "CommunicationTemplateId:int", "PushTitle:string", "PushMessage:string", "PushData:string", "PushOpenAction:int", "SendDateTime:date", "SendDateKey:int",
            "ExcludeDuplicateRecipientAddress:bool", "CommunicationTopicValueId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
        private readonly DataTable _pushRecipients = TableFactory.Create( "Id:int", "CommunicationId:int", "PersonAliasId:int", "Status:int", "StatusNote:string", "MediumEntityTypeId:int", "TransportEntityTypeName:string",
            "UniqueMessageId:string", "SendDateTime:date", "FirstSendAttemptDateTime:date", "DeliveredDateTime:date", "OpenedDateTime:date", "OpenedClient:string",
            "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
        private readonly List<PushOpen> _pushOpens = new List<PushOpen>();

        /// <summary>
        /// A flow push: delivered to each person with an app device that takes notifications, failed for the rest.
        /// </summary>
        private (int CommunicationId, Dictionary<int, int> RecipientIds) WritePush( FlowDefinition flow, MessageBlueprint message, FlowContext context, string key, DateTime at, List<EmailPerson> to )
        {
            var app = context.App;
            var (communicationId, firstRecipientId) = context.Writer.Reserve( to.Count );
            var mediumId = _db.RequireIdByGuid( "EntityType", PushMediumGuid );
            var communicationTypeId = _db.RequireIdByGuid( "EntityType", CommunicationEntityTypeGuid );
            var personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var historyCategoryId = _db.RequireIdByGuid( "Category", CommunicationHistoryCategoryGuid );
            _pushes.Rows.Add( communicationId, $"{flow.Name} – {message.Name}", PushType, ApprovedStatus, true, context.SenderAliasId, context.SenderAliasId, at.AddMinutes( -5 ), 0, message.TemplateId,
                message.Push.Title, message.Push.Text, context.PushData, 0, at, DateKey( at ), false,
                context.TopicIds.TryGetValue( message.Push.Topic ?? string.Empty, out var topic ) ? topic : DBNull.Value,
                SeededRandom.StableGuid( $"{_seed}:{key}:communication" ), at.AddMinutes( -5 ), at, context.SenderAliasId, context.SenderAliasId, _foreignKey );

            var ids = new Dictionary<int, int>();
            var position = 0;
            for ( var i = 0; i < to.Count; i++ )
            {
                var person = to[i];
                var recipientId = firstRecipientId + i;
                var seed = $"{_seed}:flow:{key}:{person.Guid}";
                var guid = SeededRandom.StableGuid( $"{seed}:recipient" );
                var device = context.Devices.TryGetValue( person.PersonId, out var found ) && found.Created < at ? found : null;
                if ( device == null || !device.NotificationsOn )
                {
                    _pushRecipients.Rows.Add( recipientId, communicationId, person.AliasId, FailedStatus, NoDevices, mediumId, DBNull.Value, DBNull.Value, DBNull.Value, at, DBNull.Value,
                        DBNull.Value, DBNull.Value, guid, at, at, _foreignKey );
                    continue;
                }

                var random = new SeededRandom( seed );
                var sent = at.AddSeconds( 0.05 * position++ );
                DateTime? opened = null;
                if ( app != null && random.Chance( app.Push.PctOpened / 100 ) )
                {
                    var hours = random.Chance( app.Push.PctOpenedWithinHour / 100 ) ? random.Between( 0.01, 1.0 ) : random.Between( 1.0, 30.0 );
                    opened = sent.AddHours( hours ) < _dates.Anchor ? sent.AddHours( hours ) : ( DateTime? ) null;
                }

                var client = device.IsIos ? "iOS" : "Android";
                _pushRecipients.Rows.Add( recipientId, communicationId, person.AliasId, opened.HasValue ? OpenedStatus : DeliveredStatus, DBNull.Value, mediumId, RockMobilePushTransport,
                    $"projects/rock-solid-church-app/messages/0:{SeededRandom.StableGuid( $"{seed}:message" ):N}", sent, at, sent,
                    ( object ) opened ?? DBNull.Value, opened.HasValue ? $"{client} {app.Site.Name} (Mobile)" : ( object ) DBNull.Value, guid, at, opened ?? sent, _foreignKey );
                _textHistory.Rows.Add( false, historyCategoryId, personTypeId, person.PersonId, message.Push.Text.Length > 200 ? message.Push.Text.Substring( 0, 200 ) : message.Push.Text, communicationTypeId, communicationId,
                    "SENT", "Record", "Push Notification", false, SeededRandom.StableGuid( $"{seed}:history" ), _foreignKey, sent, sent, context.SenderAliasId );
                if ( opened.HasValue )
                {
                    _pushOpens.Add( new PushOpen { CommunicationId = communicationId, Title = message.Push.Title, Sent = at, RecipientId = recipientId, Person = person, Device = device, When = opened.Value, Seed = seed, Key = key } );
                }

                ids[person.PersonId] = recipientId;
            }

            return (communicationId, ids);
        }

        /// <summary>
        /// Writes the flow pushes, and each open as Rock Mobile records it: one interaction component per push, and a session
        /// and an Opened interaction per open.
        /// </summary>
        private void FinishPushes( FlowContext context )
        {
            _db.BulkInsert( "Communication", _pushes, keepIdentity: true );
            _db.BulkInsert( "CommunicationRecipient", _pushRecipients, keepIdentity: true );
            if ( _pushOpens.Count == 0 )
            {
                return;
            }

            var channelId = _db.RequireIdByGuid( "InteractionChannel", CommunicationChannelGuid );
            var deviceTypes = new Dictionary<bool, object>
            {
                [true] = _db.Scalar( "SELECT TOP 1 [Id] FROM [InteractionDeviceType] WHERE [Name] = @Name", P( "@Name", $"iOS - {context.App.Site.Name}" ) ) ?? DBNull.Value,
                [false] = _db.Scalar( "SELECT TOP 1 [Id] FROM [InteractionDeviceType] WHERE [Name] = @Name", P( "@Name", $"Android - {context.App.Site.Name}" ) ) ?? DBNull.Value
            };
            var nextComponentId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [InteractionComponent]" ) );
            var nextSessionId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [InteractionSession]" ) );
            var components = TableFactory.Create( "Id:int", "Name:string", "EntityId:int", "InteractionChannelId:int", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );
            var sessions = TableFactory.Create( "Id:int", "DeviceTypeId:int", "IpAddress:string", "SessionStartDateKey:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var interactions = TableFactory.Create( "InteractionDateTime:date", "Operation:string", "InteractionComponentId:int", "EntityId:int", "PersonAliasId:int", "InteractionSessionId:int",
                "PersonalDeviceId:int", "InteractionSummary:string", "InteractionData:string", "InteractionDateKey:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            foreach ( var push in _pushOpens.GroupBy( o => o.CommunicationId ) )
            {
                var first = push.First();
                var componentId = nextComponentId++;
                components.Rows.Add( componentId, first.Title, push.Key, channelId, first.Sent, first.Sent, SeededRandom.StableGuid( $"{_seed}:{first.Key}:component" ), _foreignKey );
                foreach ( var open in push )
                {
                    var sessionId = nextSessionId++;
                    sessions.Rows.Add( sessionId, deviceTypes[open.Device.IsIos], BulkEmailLoader.IpAddress( open.Person ), DateKey( open.When ), SeededRandom.StableGuid( $"{open.Seed}:open-session" ), open.When, open.When, _foreignKey );
                    interactions.Rows.Add( open.When, "Opened", componentId, open.RecipientId, open.Person.AliasId, sessionId, open.Device.Id, string.Empty, string.Empty, DateKey( open.When ),
                        SeededRandom.StableGuid( $"{open.Seed}:open" ), open.When, open.When, _foreignKey );
                }
            }

            _db.BulkInsert( "InteractionComponent", components, keepIdentity: true );
            _db.BulkInsert( "InteractionSession", sessions, keepIdentity: true );
            _db.BulkInsert( "Interaction", interactions, keepIdentity: false );
        }

        /// <summary>
        /// Each person's newest app device, from the rows the app part wrote.
        /// </summary>
        private Dictionary<int, FlowDevice> Devices( FlowContext context )
        {
            var ios = context.App?.Devices?.Platforms.TryGetValue( "ios", out var iosGuid ) == true ? _db.IdByGuid( "DefinedValue", iosGuid ) : null;
            return _db.Query( @"
SELECT [d].[Id], [pa].[PersonId], [d].[NotificationsEnabled], [d].[PlatformValueId], [d].[CreatedDateTime]
FROM [PersonalDevice] AS [d]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [d].[PersonAliasId]
WHERE [d].[IsActive] = 1 AND [d].[ForeignKey] = @ForeignKey", P( "@ForeignKey", _foreignKey ) )
                .GroupBy( r => ( int ) r["PersonId"] )
                .ToDictionary( g => g.Key, g => g.OrderByDescending( r => ( DateTime ) r["CreatedDateTime"] ).Select( r => new FlowDevice
                {
                    Id = ( int ) r["Id"],
                    NotificationsOn = ( bool ) r["NotificationsEnabled"],
                    IsIos = ios.HasValue && r["PlatformValueId"] is int platform && platform == ios.Value,
                    Created = ( DateTime ) r["CreatedDateTime"]
                } ).First() );
        }

        /// <summary>
        /// The push data Rock's flow editor saves for the church app.
        /// </summary>
        private string PushData( FlowContext context )
        {
            var siteId = context.App?.Site == null ? null : _db.IdByGuid( "Site", context.App.Site.Guid );
            return JsonSerializer.Serialize( new { MobilePageId = ( int? ) null, MobilePageQueryString = ( object ) null, MobileApplicationId = siteId, Url = ( string ) null, ApplicationBadgeCount = ( int? ) null, CustomData = ( object ) null } );
        }

        private sealed class FlowDevice
        {
            public int Id { get; set; }

            public bool NotificationsOn { get; set; }

            public bool IsIos { get; set; }

            public DateTime Created { get; set; }
        }

        private sealed class PushOpen
        {
            public int CommunicationId { get; set; }

            public string Title { get; set; }

            public DateTime Sent { get; set; }

            public int RecipientId { get; set; }

            public EmailPerson Person { get; set; }

            public FlowDevice Device { get; set; }

            public DateTime When { get; set; }

            public string Seed { get; set; }

            public string Key { get; set; }
        }
    }
}
