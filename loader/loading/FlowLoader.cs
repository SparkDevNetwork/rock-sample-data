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
using System.Linq;
using System.Text.Json;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/23/26 - CLAUDE

        "After Starting Point" is an on-demand Communication Flow, the kind a workflow's Activate
        Communication Flow action fills: everyone who finishes a Starting Point Class joins the flow
        instance for the next day. Each instance has the rows Rock's Process Communication Flows job
        writes: one bulk email per message from that message's flow template, its recipients, the link
        from the instance to each email, and conversions for people who joined a small group in the 30
        days after. With the exit condition "last message sent", Rock leaves each recipient Active.

        Only classes whose last message went out before install day get an instance, and every instance
        is complete, so the job never has a message left to send.

        Reason: decision 96, and nothing a load writes may ever send (decision 95).
    */

    /// <summary>
    /// Loads the finished "After Starting Point" Communication Flow with an instance for each Starting Point Class.
    /// </summary>
    internal sealed class FlowLoader
    {
        private const string FlowName = "After Starting Point";
        private const int OnDemand = 2;
        private const int LastMessageSent = 0;
        private const int JoinedGroupType = 3;
        private const int ConversionDays = 30;
        private const int EmailType = 1;
        private const int ActiveRecipient = 1;

        /// <summary>
        /// The flow's messages: the library email, its name in the flow, and the days after the previous message.
        /// </summary>
        private static readonly (string Key, string Name, int DaysToWait)[] Messages =
        {
            ("after-starting-point-thanks", "Thank You", 0),
            ("after-starting-point-groups", "Small Groups", 6),
            ("after-starting-point-serve", "Serving", 14)
        };

        private static readonly TimeSpan TimeToSend = new TimeSpan( 10, 0, 0 );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public FlowLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes the flow, its templates and messages, and an instance for each finished class.
        /// </summary>
        /// <param name="emails">The bulk email load, already run, for its planner, people, and campuses.</param>
        /// <param name="templateGuid">The church's branded template, whose logo, category, and colors the flow templates share.</param>
        public void Load( CommunicationsConfiguration configuration, PeopleLoader people, BulkEmailLoader emails, Guid templateGuid, int smallGroupTypeId )
        {
            var classType = _source.Steps?.StepTypes.FirstOrDefault( t => t.Key == "starting-point-class" );
            if ( classType == null )
            {
                return;
            }

            var library = _source.ReadEmailLibrary().Emails.ToDictionary( e => e.Key );
            var church = configuration.Senders.Church;
            var senderAliasId = people.People[church.Sender].AliasId;
            var byAlias = emails.EmailPeople.GroupBy( p => p.AliasId ).ToDictionary( g => g.Key, g => g.First() );
            var firstDay = _dates.Anchor.AddYears( -2 ).Date;

            // Each class's finishers, by the day after the class's last night.
            var instances = _db.Query( @"
SELECT [s].[PersonAliasId], [s].[CompletedDateTime]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
WHERE [st].[Guid] = @Type AND [s].[CompletedDateTime] IS NOT NULL", P( "@Type", classType.Guid ) )
                .Where( r => byAlias.ContainsKey( ( int ) r["PersonAliasId"] ) )
                .GroupBy( r => (( DateTime ) r["CompletedDateTime"]).Date.AddDays( 1 ) )
                .Where( g => g.Key >= firstDay && LastSend( g.Key ) < _dates.Anchor )
                .OrderBy( g => g.Key )
                .Select( g => (Start: g.Key, People: g.Select( r => byAlias[( int ) r["PersonAliasId"]] ).GroupBy( p => p.PersonId ).Select( p => p.First() ).OrderBy( p => p.PersonId ).ToList()) )
                .ToList();
            if ( instances.Count == 0 )
            {
                return;
            }

            var template = _db.Query( "SELECT [LogoBinaryFileId], [CategoryId], [LavaFieldsJson] FROM [CommunicationTemplate] WHERE [Guid] = @Guid", P( "@Guid", templateGuid ) ).First();
            var groupTypeGuid = ( Guid ) _db.Scalar( "SELECT [Guid] FROM [GroupType] WHERE [Id] = @Id", P( "@Id", smallGroupTypeId ) );
            var launched = instances[0].Start.AddDays( -14 ).AddHours( 11 );
            var trackingClosed = instances.All( i => TrackingDone( i.Start ) );
            var flowId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [CommunicationFlow] ( [Name], [Description], [IsActive], [TriggerType], [ConversionGoalType], [ConversionGoalTargetPercent], [ConversionGoalTimeframeInDays],
    [ExitConditionType], [AdditionalSettingsJson], [IsMessagingClosed], [IsConversionGoalTrackingClosed], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, 1, @Trigger, @Goal, 30, @Days, @Exit, @Settings, 0, @TrackingClosed, @Guid, @Launched, @Launched, @Sender, @Sender, @ForeignKey )",
                P( "@Name", FlowName ),
                P( "@Description", "Follows up with everyone who finishes the Starting Point Class: a thank-you the next day, a small group invitation a week later, and a serving invitation three weeks later." ),
                P( "@Trigger", OnDemand ), P( "@Goal", JoinedGroupType ), P( "@Days", ConversionDays ), P( "@Exit", LastMessageSent ),
                P( "@Settings", JsonSerializer.Serialize( new { ConversionGoalSettings = new { JoinedGroupTypeSettings = new { GroupTypeGuid = groupTypeGuid } } } ) ),
                P( "@TrackingClosed", trackingClosed ), P( "@Guid", SeededRandom.StableGuid( $"{_seed}:flow:after-starting-point" ) ), P( "@Launched", launched ),
                P( "@Sender", senderAliasId ), P( "@ForeignKey", _foreignKey ) ) );

            // Each message has its own flow template, which holds its subject and body, as Rock's flow editor saves it.
            var main = emails.CampusList.First();
            var blueprints = new List<(int Id, int TemplateId, LibraryEmail Email, string Name)>();
            for ( var order = 0; order < Messages.Length; order++ )
            {
                var (key, name, daysToWait) = Messages[order];
                var email = library[key];
                var tokens = MessageRenderer.Tokens( launched, launched, main.Name, main.Key, main.ServiceTimes, null );
                var (html, _) = emails.Renderer.Render( email.Body, email.Preheader, tokens );
                var templateId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [CommunicationTemplate] ( [Name], [Description], [IsSystem], [Subject], [FromName], [FromEmail], [SenderPersonAliasId], [Message], [IsActive], [LogoBinaryFileId], [CategoryId],
    [LavaFieldsJson], [CssInliningEnabled], [IsStarter], [Version], [UsageType], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, '', 0, @Subject, @FromName, @FromEmail, @Sender, @Message, 1, @LogoId, @CategoryId, @LavaFields, 1, 0, 1, 1, @Guid, @Launched, @Launched, @Sender, @Sender, @ForeignKey )",
                    P( "@Name", $"{FlowName} - {name}" ), P( "@Subject", MessageRenderer.Fill( email.Subject, tokens ) ), P( "@FromName", church.FromName ), P( "@FromEmail", church.FromEmail ),
                    P( "@Sender", senderAliasId ), P( "@Message", html ), P( "@LogoId", template["LogoBinaryFileId"] ), P( "@CategoryId", template["CategoryId"] ),
                    P( "@LavaFields", template["LavaFieldsJson"] ), P( "@Guid", SeededRandom.StableGuid( $"{_seed}:flow:template:{key}" ) ), P( "@Launched", launched ), P( "@ForeignKey", _foreignKey ) ) );
                var id = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [CommunicationFlowCommunication] ( [Name], [DaysToWait], [TimeToSend], [CommunicationType], [CommunicationFlowId], [CommunicationTemplateId], [Order], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Days, @Time, @Type, @FlowId, @TemplateId, @Order, @Guid, @Launched, @Launched, @ForeignKey )",
                    P( "@Name", name ), P( "@Days", daysToWait ), P( "@Time", TimeToSend ), P( "@Type", EmailType ), P( "@FlowId", flowId ), P( "@TemplateId", templateId ), P( "@Order", order ),
                    P( "@Guid", SeededRandom.StableGuid( $"{_seed}:flow:message:{key}" ) ), P( "@Launched", launched ), P( "@ForeignKey", _foreignKey ) ) );
                blueprints.Add( (id, templateId, email, name) );
            }

            var topicIds = configuration.Topics.ToDictionary( t => t.Key, t => _db.RequireIdByGuid( "DefinedValue", t.Value ) );
            var joined = SmallGroupJoins( smallGroupTypeId );
            var writer = new EmailWriter( _db, _foreignKey );
            var links = TableFactory.Create( "CommunicationFlowInstanceId:int", "CommunicationFlowCommunicationId:int", "CommunicationId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var recipients = TableFactory.Create( "CommunicationFlowInstanceId:int", "RecipientPersonAliasId:int", "Status:int", "WasConversionGoalPreMet:bool", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var conversions = TableFactory.Create( "CommunicationFlowInstanceCommunicationId:int", "Date:date", "PersonAliasId:int", "CommunicationRecipientId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var nextLinkId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [CommunicationFlowInstanceCommunication]" ) );
            var conversionCount = 0;
            var sent = 0;

            foreach ( var (start, finishers) in instances )
            {
                var instanceId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [CommunicationFlowInstance] ( [CommunicationFlowId], [StartDate], [IsMessagingCompleted], [IsConversionGoalTrackingCompleted], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @FlowId, @Start, 1, @TrackingDone, @Guid, @Created, @Modified, @ForeignKey )",
                    P( "@FlowId", flowId ), P( "@Start", start ), P( "@TrackingDone", TrackingDone( start ) ), P( "@Guid", SeededRandom.StableGuid( $"{_seed}:flow:instance:{start:yyyyMMdd}" ) ),
                    P( "@Created", start.AddHours( 6 ) ), P( "@Modified", LastSend( start ) ), P( "@ForeignKey", _foreignKey ) ) );

                foreach ( var person in finishers )
                {
                    var preMet = joined.TryGetValue( person.PersonId, out var joins ) && joins.Any( j => j < start );
                    recipients.Rows.Add( instanceId, person.AliasId, ActiveRecipient, preMet, SeededRandom.StableGuid( $"{_seed}:flow:recipient:{start:yyyyMMdd}:{person.Guid}" ), start.AddHours( 6 ), start.AddHours( 6 ), _foreignKey );
                }

                // Each message's email, with where its recipients landed, for conversions.
                var delivered = new List<(int LinkId, DateTime Sent, Dictionary<int, int> RecipientIds)>();
                var day = start;
                for ( var order = 0; order < blueprints.Count; order++ )
                {
                    var blueprint = blueprints[order];
                    day = day.AddDays( Messages[order].DaysToWait );
                    var at = day.Add( TimeToSend );
                    var send = new PlannedEmail
                    {
                        Key = $"flow:{Messages[order].Key}:{start:yyyyMMdd}",
                        Name = $"{FlowName} – {blueprint.Name}",
                        Subject = MessageRenderer.Fill( blueprint.Email.Subject, MessageRenderer.Tokens( at, at, main.Name, main.Key, main.ServiceTimes, null ) ),
                        Html = ( string ) _db.Scalar( "SELECT [Message] FROM [CommunicationTemplate] WHERE [Id] = @Id", P( "@Id", blueprint.TemplateId ) ),
                        Links = emails.Renderer.Render( blueprint.Email.Body, blueprint.Email.Preheader, MessageRenderer.Tokens( at, at, main.Name, main.Key, main.ServiceTimes, null ) ).Links,
                        Audience = "flow",
                        IsBulk = true,
                        Topic = blueprint.Email.Topic,
                        Created = at.AddMinutes( -5 ),
                        Sent = at
                    };
                    foreach ( var person in finishers )
                    {
                        emails.Planner.PlanOneToOne( send, person, $"{_seed}:flow:{send.Key}:{person.Guid}" );
                    }

                    var (communicationId, firstRecipientId) = writer.Add( send, new EmailSendContext
                    {
                        SenderAliasId = senderAliasId,
                        ReviewerAliasId = senderAliasId,
                        TemplateId = blueprint.TemplateId,
                        TopicValueId = topicIds.TryGetValue( send.Topic ?? string.Empty, out var topic ) ? topic : null,
                        FromName = church.FromName,
                        FromEmail = church.FromEmail,
                        Guid = SeededRandom.StableGuid( $"{_seed}:{send.Key}:communication" ),
                        ComponentGuid = SeededRandom.StableGuid( $"{_seed}:{send.Key}:component" ),
                        IpAddress = BulkEmailLoader.IpAddress
                    }, emails.Clients );
                    sent++;

                    var linkId = nextLinkId++;
                    links.Rows.Add( instanceId, blueprint.Id, communicationId, SeededRandom.StableGuid( $"{_seed}:{send.Key}:link" ), at.AddMinutes( -5 ), at.AddMinutes( -5 ), _foreignKey );
                    var ids = new Dictionary<int, int>();
                    for ( var i = 0; i < send.Recipients.Count; i++ )
                    {
                        if ( send.Recipients[i].DeliveredDateTime.HasValue )
                        {
                            ids[send.Recipients[i].Person.PersonId] = firstRecipientId + i;
                        }
                    }

                    delivered.Add( (linkId, at, ids) );
                }

                // A conversion is a small group the person joined in the goal window, credited to the newest message they got before it.
                if ( TrackingDone( start ) )
                {
                    foreach ( var person in finishers )
                    {
                        var join = joined.TryGetValue( person.PersonId, out var joins ) ? joins.Where( j => j >= start && j < start.AddDays( ConversionDays ) ).Cast<DateTime?>().FirstOrDefault() : null;
                        var credited = join.HasValue ? delivered.LastOrDefault( d => d.Sent <= join.Value && d.RecipientIds.ContainsKey( person.PersonId ) ) : default;
                        if ( join.HasValue && credited.LinkId != 0 )
                        {
                            conversions.Rows.Add( credited.LinkId, join.Value, person.AliasId, credited.RecipientIds[person.PersonId], SeededRandom.StableGuid( $"{_seed}:flow:conversion:{start:yyyyMMdd}:{person.Guid}" ),
                                start.AddDays( ConversionDays ), start.AddDays( ConversionDays ), _foreignKey );
                            conversionCount++;
                        }
                    }
                }
            }

            writer.Finish();
            _db.BulkInsert( "CommunicationFlowInstanceCommunication", links, keepIdentity: false );
            _db.BulkInsert( "CommunicationFlowInstanceRecipient", recipients, keepIdentity: false );
            _db.BulkInsert( "CommunicationFlowInstanceCommunicationConversion", conversions, keepIdentity: false );
            _db.Log.Write( $"  Communication flow: {FlowName}, {instances.Count:N0} finished instances for {recipients.Rows.Count:N0} people, {sent:N0} emails, {conversionCount:N0} joined a small group" );
        }

        /// <summary>
        /// When an instance's last message goes out.
        /// </summary>
        private static DateTime LastSend( DateTime start ) => start.AddDays( Messages.Sum( m => m.DaysToWait ) ).Add( TimeToSend );

        private bool TrackingDone( DateTime start ) => start.AddDays( ConversionDays ) < _dates.Anchor.Date;

        /// <summary>
        /// When each person joined a small group.
        /// </summary>
        private Dictionary<int, List<DateTime>> SmallGroupJoins( int smallGroupTypeId )
        {
            return _db.Query( "SELECT [PersonId], [DateTimeAdded] FROM [GroupMember] WHERE [GroupTypeId] = @Type AND [DateTimeAdded] IS NOT NULL", P( "@Type", smallGroupTypeId ) )
                .GroupBy( r => ( int ) r["PersonId"] )
                .ToDictionary( g => g.Key, g => g.Select( r => ( DateTime ) r["DateTimeAdded"] ).OrderBy( d => d ).ToList() );
        }
    }
}
