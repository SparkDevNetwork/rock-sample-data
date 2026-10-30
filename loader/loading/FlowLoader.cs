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
        10/23/26 - CLAUDE

        A flow is an On-Demand Communication Flow, the kind a workflow's Activate Communication Flow
        action fills: everyone who completes the flow's entry step joins the run for the next day. Each
        run has the rows Rock's Process Communication Flows job writes: one bulk email or text per message
        from that message's flow template, its recipients, the link from the run to each send, and
        conversions credited the way the job credits them.

        Reason: decision 96, and the flows in configuration/flows.json.

        10/30/26 - CLAUDE

        The flows come from configuration/flows.json, and a run that started before install day keeps the
        messages that are still to come. Rock's job sends them after install (decision 220), so
        an open run holds only people whose email is on the reserved domain or empty. Rock's job prunes
        recipients only while a run is still sending: with "last message sent" every recipient stays Active,
        and with "conversion achieved" a person who converted before the run's last message leaves with the
        reason "goal met" and gets no later message. A package from before flows.json loads the finished
        After Starting Point flow as it did.

        Reason: Jon wants people mid-flow on install day (decision 220).
    */

    /// <summary>
    /// Loads the church's On-Demand Communication Flows, each with a run for every day people entered it.
    /// </summary>
    internal sealed class FlowLoader
    {
        private const int OnDemand = 2;
        private const int EmailType = 1;
        private const int SmsType = 2;
        private const int ActiveRecipient = 1;
        private const int InactiveRecipient = 2;
        private const int ConversionGoalMet = 2;
        private const int ApprovedStatus = 3;
        private const int DeliveredStatus = 1;
        private const int FailedStatus = 2;

        private static readonly Dictionary<string, int> ExitTypes = new Dictionary<string, int>
        {
            ["lastMessageSent"] = 0,
            ["anyEmailOpened"] = 1,
            ["anyEmailClicked"] = 2,
            ["conversionAchieved"] = 3
        };

        private static readonly Dictionary<string, int> GoalTypes = new Dictionary<string, int>
        {
            ["joinedGroupType"] = 3,
            ["tookStep"] = 6
        };

        private static readonly Guid SmsMediumGuid = new Guid( "4BC02764-512A-4A10-ACDE-586F71D8A8BD" );
        private static readonly Guid CommunicationEntityTypeGuid = new Guid( "C4CCBD91-1264-48BF-BC33-92751C8948B5" );
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid CommunicationHistoryCategoryGuid = new Guid( "F291034B-7581-48F3-B522-E31B8534D529" );
        private static readonly Guid MobilePhoneTypeGuid = new Guid( "407E7E45-7B2E-4FCD-9605-ECB1339F2453" );
        private const string TwilioTransport = "Rock.Communication.Transport.Twilio";

        /// <summary>
        /// The After Starting Point flow as a package from before configuration/flows.json defines it.
        /// </summary>
        private static readonly FlowDefinition LegacyFlow = new FlowDefinition
        {
            Key = "after-starting-point",
            Name = "After Starting Point",
            Description = "Follows up with everyone who finishes the Starting Point Class: a thank-you the next day, a small group invitation a week later, and a serving invitation three weeks later.",
            Trigger = "onDemand",
            Entry = new FlowEntry { StepType = "starting-point-class" },
            HistoryMonths = 24,
            Messages = new List<FlowMessage>
            {
                new FlowMessage { Key = "after-starting-point-thanks", Name = "Thank You", DaysToWait = 0, Time = "10:00" },
                new FlowMessage { Key = "after-starting-point-groups", Name = "Small Groups", DaysToWait = 6, Time = "10:00" },
                new FlowMessage { Key = "after-starting-point-serve", Name = "Serving", DaysToWait = 14, Time = "10:00" }
            },
            Goal = new FlowGoal { Type = "joinedGroupType", GroupType = "small-groups", Days = 30, TargetPercent = 30 },
            Exit = "lastMessageSent"
        };

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        private readonly DataTable _links = TableFactory.Create( "CommunicationFlowInstanceId:int", "CommunicationFlowCommunicationId:int", "CommunicationId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
        private readonly DataTable _recipients = TableFactory.Create( "CommunicationFlowInstanceId:int", "RecipientPersonAliasId:int", "Status:int", "InactiveReason:int", "WasConversionGoalPreMet:bool", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
        private readonly DataTable _conversions = TableFactory.Create( "CommunicationFlowInstanceCommunicationId:int", "Date:date", "PersonAliasId:int", "CommunicationRecipientId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
        private readonly DataTable _texts = TableFactory.Create( "Id:int", "Name:string", "CommunicationType:int", "Status:int", "IsBulkCommunication:bool", "SenderPersonAliasId:int", "ReviewerPersonAliasId:int",
            "ReviewedDateTime:date", "SegmentCriteria:int", "CommunicationTemplateId:int", "SMSMessage:string", "SmsFromSystemPhoneNumberId:int", "SendDateTime:date", "SendDateKey:int",
            "ExcludeDuplicateRecipientAddress:bool", "CommunicationTopicValueId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
        private readonly DataTable _textRecipients = TableFactory.Create( "Id:int", "CommunicationId:int", "PersonAliasId:int", "Status:int", "StatusNote:string", "MediumEntityTypeId:int", "TransportEntityTypeName:string",
            "UniqueMessageId:string", "SendDateTime:date", "FirstSendAttemptDateTime:date", "DeliveredDateTime:date", "SentMessage:string", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
        private readonly DataTable _textHistory = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
            "Verb:string", "ChangeType:string", "ValueName:string", "IsSensitive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int" );

        private int _nextLinkId;

        public FlowLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes each flow, its templates and messages, and a run for each day people entered it.
        /// </summary>
        /// <param name="emails">The bulk email load, already run, for its planner, people, and campuses.</param>
        /// <param name="templateGuid">The church's branded template, whose logo, category, and colors the flow templates share.</param>
        /// <param name="groupTypeIds">The group types a goal can name: small-groups and serving-teams.</param>
        public void Load( CommunicationsConfiguration configuration, PeopleLoader people, BulkEmailLoader emails, Guid templateGuid, IReadOnlyDictionary<string, int> groupTypeIds )
        {
            if ( _source.Steps == null )
            {
                return;
            }

            var flows = _source.Flows?.Flows ?? new List<FlowDefinition> { LegacyFlow };
            var context = new FlowContext
            {
                Configuration = configuration,
                People = people,
                Emails = emails,
                AllowOpenRuns = _source.Flows != null,
                ReservedEmailSuffix = "@" + ( _source.Flows?.ReservedEmailDomain ?? "example.org" ),
                EmailLibrary = _source.ReadEmailLibrary().Emails.ToDictionary( e => e.Key ),
                TextLibrary = _source.ReadTextLibrary().Texts.Where( t => t.Medium == "sms" ).ToDictionary( t => t.Key ),
                SenderAliasId = people.People[configuration.Senders.Church.Sender].AliasId,
                ByAlias = emails.EmailPeople.GroupBy( p => p.AliasId ).ToDictionary( g => g.Key, g => g.First() ),
                Template = _db.Query( "SELECT [LogoBinaryFileId], [CategoryId], [LavaFieldsJson] FROM [CommunicationTemplate] WHERE [Guid] = @Guid", P( "@Guid", templateGuid ) ).First(),
                TopicIds = configuration.Topics.ToDictionary( t => t.Key, t => _db.RequireIdByGuid( "DefinedValue", t.Value ) ),
                GroupTypeIds = groupTypeIds,
                Writer = new EmailWriter( _db, _foreignKey )
            };
            context.EmailAddresses = _db.Query( "SELECT [Id], [Email] FROM [Person] WHERE [ForeignKey] = @ForeignKey OR [Id] = 1", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["Id"], r => r["Email"] as string );
            context.Mobiles = Mobiles();
            context.NickNames = people.People.Values.GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First().Cast.NickName ?? g.First().Cast.FirstName );
            var mainNumber = configuration.PhoneNumbers.Numbers.FirstOrDefault( n => n.Key == "main" );
            context.MainNumberId = mainNumber == null ? null : _db.IdByGuid( "SystemPhoneNumber", mainNumber.Guid );
            _nextLinkId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [CommunicationFlowInstanceCommunication]" ) );

            foreach ( var flow in flows )
            {
                LoadFlow( flow, context );
            }

            context.Writer.Finish();
            _db.BulkInsert( "Communication", _texts, keepIdentity: true );
            _db.BulkInsert( "CommunicationRecipient", _textRecipients, keepIdentity: true );
            _db.BulkInsert( "History", _textHistory, keepIdentity: false );
            _db.BulkInsert( "CommunicationFlowInstanceCommunication", _links, keepIdentity: false );
            _db.BulkInsert( "CommunicationFlowInstanceRecipient", _recipients, keepIdentity: false );
            _db.BulkInsert( "CommunicationFlowInstanceCommunicationConversion", _conversions, keepIdentity: false );
        }

        private void LoadFlow( FlowDefinition flow, FlowContext context )
        {
            var messages = flow.Messages.Select( ( m, order ) => Blueprint( flow, m, order, context ) ).ToList();
            var goal = Goal( flow, context );
            var exitType = ExitTypes.TryGetValue( flow.Exit ?? string.Empty, out var exit ) ? exit
                : throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} has exit '{flow.Exit}', which is not one this slice writes." );
            var firstDay = _dates.Anchor.AddMonths( -flow.HistoryMonths ).Date;

            // A person joins the run for the day they entered: the day after their entry step, or the day they filled out the form.
            var workflow = flow.Entry?.Workflow == null ? null : _source.Flows.Workflows.FirstOrDefault( w => w.Key == flow.Entry.Workflow )
                ?? throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} enters from workflow '{flow.Entry.Workflow}', which the file does not define." );
            var formEntries = workflow == null ? null : FormEntries( flow, context, goal, firstDay );
            var entries = formEntries?.Select( e => (Day: e.At.Date, e.Person) ).ToList() ?? StepEntries( flow, context );
            var entrants = entries
                .Where( e => e.Day >= firstDay && ( context.AllowOpenRuns ? e.Day < _dates.Anchor.Date : LastSend( flow, e.Day ) < _dates.Anchor ) )
                .GroupBy( e => e.Day )
                .OrderBy( g => g.Key )
                .Select( g => (Start: g.Key, People: g.Select( e => e.Person ).GroupBy( p => p.PersonId ).Select( p => p.First() ).OrderBy( p => p.PersonId ).ToList()) )
                .ToList();
            if ( entrants.Count == 0 )
            {
                return;
            }

            var launched = entrants[0].Start.AddDays( -14 ).AddHours( 11 );
            var trackingClosed = entrants.All( e => TrackingDone( flow, e.Start ) );
            var categoryId = flow.Category == null ? ( int? ) null : _db.RequireIdByGuid( "Category", _source.Categories.Get( flow.Category ).Guid );
            var flowId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [CommunicationFlow] ( [Name], [Description], [CategoryId], [PublicName], [IsActive], [TriggerType], [ConversionGoalType], [ConversionGoalTargetPercent], [ConversionGoalTimeframeInDays],
    [ExitConditionType], [AdditionalSettingsJson], [IsMessagingClosed], [IsConversionGoalTrackingClosed], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @CategoryId, @PublicName, 1, @Trigger, @Goal, @Target, @Days, @Exit, @Settings, 0, @TrackingClosed, @Guid, @Launched, @Launched, @Sender, @Sender, @ForeignKey )",
                P( "@Name", flow.Name ), P( "@Description", flow.Description ), P( "@CategoryId", ( object ) categoryId ?? DBNull.Value ), P( "@PublicName", ( object ) flow.PublicName ?? DBNull.Value ),
                P( "@Trigger", OnDemand ), P( "@Goal", goal.TypeValue ), P( "@Target", flow.Goal.TargetPercent ), P( "@Days", flow.Goal.Days ), P( "@Exit", exitType ),
                P( "@Settings", goal.SettingsJson ), P( "@TrackingClosed", trackingClosed ), P( "@Guid", SeededRandom.StableGuid( $"{_seed}:flow:{flow.Key}" ) ), P( "@Launched", launched ),
                P( "@Sender", context.SenderAliasId ), P( "@ForeignKey", _foreignKey ) ) );

            foreach ( var message in messages )
            {
                message.Write( flowId, launched );
            }

            // Each person who filled out the form has the finished workflow that put them in the flow.
            if ( workflow != null )
            {
                var writer = new FlowWorkflowWriter( _db, _source, _foreignKey, _seed );
                var rows = writer.WriteType( workflow, SeededRandom.StableGuid( $"{_seed}:flow:{flow.Key}" ), context.SenderAliasId, launched.AddDays( -1 ) );
                writer.WriteRuns( rows, formEntries.Where( e => e.At.Date >= firstDay && e.At.Date < _dates.Anchor.Date ).Select( e => (e.Person.PersonId, e.Person.AliasId, e.Person.Guid, e.At) ) );
            }

            var stats = new FlowStats();
            foreach ( var (start, joined) in entrants )
            {
                var lastSend = LastSend( flow, start );
                var isOpen = lastSend >= _dates.Anchor;

                // A flow can leave out people who already met its goal, as the workflow that adds them would check.
                var candidates = flow.Entry.SkipIfGoalMet
                    ? joined.Where( p => !( goal.Dates.TryGetValue( p.PersonId, out var met ) && met.Any( d => d < start ) ) ).ToList()
                    : joined;

                // A run Rock still sends after install holds only people it can never reach for real.
                var members = isOpen ? candidates.Where( p => IsReserved( p.PersonId, context ) ).ToList() : candidates;
                stats.LeftOutOfOpenRuns += candidates.Count - members.Count;
                if ( members.Count == 0 )
                {
                    continue;
                }

                WriteRun( flow, context, messages, goal, exitType, flowId, start, isOpen, members, stats );
            }

            _db.Log.Write( $"  Communication flow: {flow.Name}, {stats.Runs:N0} runs ({stats.OpenRuns:N0} still sending) for {stats.People:N0} people, {stats.Sends:N0} sends, {stats.Conversions:N0} reached the goal"
                + ( stats.LeftOutOfOpenRuns > 0 ? $", {stats.LeftOutOfOpenRuns:N0} left out of open runs for a real email address" : string.Empty ) );
        }

        /// <summary>
        /// Everyone who completed the flow's entry step, on the day after.
        /// </summary>
        private List<(DateTime Day, EmailPerson Person)> StepEntries( FlowDefinition flow, FlowContext context )
        {
            var entryType = _source.Steps.StepTypes.FirstOrDefault( t => t.Key == flow.Entry?.StepType )
                ?? throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} enters from step type '{flow.Entry?.StepType}', which configuration/steps.json does not have." );
            return _db.Query( @"
SELECT [s].[PersonAliasId], [s].[CompletedDateTime]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
WHERE [st].[Guid] = @Type AND [s].[CompletedDateTime] IS NOT NULL", P( "@Type", entryType.Guid ) )
                .Where( r => context.ByAlias.ContainsKey( ( int ) r["PersonAliasId"] ) )
                .Select( r => ((( DateTime ) r["CompletedDateTime"]).Date.AddDays( 1 ), context.ByAlias[( int ) r["PersonAliasId"]]) )
                .ToList();
        }

        /*
            10/30/26 - CLAUDE

            Who filled out a form that puts people in a flow comes from the Baptism steps the steps part already
            wrote, never the other way round. Most people baptized here in the window filled it out 1 to 16
            weeks before, so their flow ends on its own with the goal met. As many people again who are not
            baptized filled it out at any time, and their runs end after the last message, or are still Pending.
            The pins are Cast stories. A person whose run would still be sending on install day must have
            reserved contact points, so the plan leaves out anyone else in those weeks.

            Reason: Jon's Baptism flow, and decision 220.
        */

        /// <summary>
        /// Who filled out the flow's form, and when: in the morning, before the first message goes out.
        /// </summary>
        private List<(DateTime At, EmailPerson Person)> FormEntries( FlowDefinition flow, FlowContext context, GoalPlan goal, DateTime firstDay )
        {
            var entry = flow.Entry;
            var baptismType = _source.Steps.StepTypes.Single( t => t.Source?.Kind == "baptism" );
            var here = _db.Query( @"
SELECT [av].[EntityId] AS [PersonId]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
WHERE [a].[Guid] = @Attribute AND [av].[Value] = 'True'", P( "@Attribute", baptismType.Source.BaptizedHerePersonAttributeGuid ) )
                .Select( r => ( int ) r["PersonId"] ).ToHashSet();
            var byPerson = context.ByAlias.Values.GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var byGuid = context.ByAlias.Values.GroupBy( p => p.Guid ).ToDictionary( g => g.Key, g => g.First() );
            var entries = new Dictionary<int, (DateTime At, EmailPerson Person)>();
            DateTime Morning( DateTime day, string seed ) => day.Date.AddMinutes( new SeededRandom( seed ).Between( 6 * 60, 9 * 60 + 45 ) );

            // The Cast's stories come first, so the plan never moves them.
            foreach ( var pin in entry.Pins )
            {
                var person = pin.Person.HasValue
                    ? byGuid.GetValueOrDefault( pin.Person.Value )
                    : byPerson.Values.Where( p => p.IsCast && p.IsAdult && p.IsInvolved && p.HasEmail && IsReserved( p.PersonId, context ) && !goal.Dates.ContainsKey( p.PersonId ) && !entries.ContainsKey( p.PersonId ) )
                        .OrderBy( p => p.PersonId ).FirstOrDefault();
                if ( person != null )
                {
                    var at = _dates.Anchor.Date.AddDays( -pin.DaysAgo ).Add( TimeSpan.Parse( pin.Time ?? "08:00", System.Globalization.CultureInfo.InvariantCulture ) );
                    entries[person.PersonId] = (at, person);
                    _db.Log.Write( $"  {flow.Name}: {context.NickNames.GetValueOrDefault( person.PersonId )} (person {person.PersonId}) filled out the form {pin.DaysAgo} days before install" );
                }
            }

            // People baptized here in the window, most of whom filled out the form 1 to 16 weeks before.
            var baptized = 0;
            foreach ( var (personId, dates) in goal.Dates.OrderBy( d => d.Key ) )
            {
                var baptism = dates.FirstOrDefault( d => d >= firstDay && d < _dates.Anchor );
                if ( baptism == default || !here.Contains( personId ) || !byPerson.TryGetValue( personId, out var person ) || entries.ContainsKey( personId ) )
                {
                    continue;
                }

                var seed = $"{_seed}:flow:{flow.Key}:entry:{person.Guid}";
                var random = new SeededRandom( seed );
                if ( !random.Chance( entry.BaptizedShare / 100.0 ) )
                {
                    continue;
                }

                var day = baptism.Date.AddDays( -random.Between( entry.WeeksBeforeBaptismMin * 7, entry.WeeksBeforeBaptismMax * 7 ) );
                if ( day < firstDay || day < person.Since.Date )
                {
                    continue;
                }

                entries[personId] = (Morning( day, seed ), person);
                baptized++;
            }

            // As many again who are not baptized, at any time in the window after they started coming.
            var unbaptized = byPerson.Values
                .Where( p => p.IsAdult && p.IsInvolved && !goal.Dates.ContainsKey( p.PersonId ) && !entries.ContainsKey( p.PersonId ) )
                .OrderBy( p => SeededRandom.StableGuid( $"{_seed}:flow:{flow.Key}:pick:{p.Guid}" ) )
                .Take( ( int ) Math.Round( baptized * entry.NotBaptizedPerBaptized ) );
            foreach ( var person in unbaptized )
            {
                var seed = $"{_seed}:flow:{flow.Key}:entry:{person.Guid}";
                var from = person.Since.Date > firstDay ? person.Since.Date : firstDay;
                var days = ( int ) ( _dates.Anchor.Date - from ).TotalDays;
                if ( days > 0 )
                {
                    entries[person.PersonId] = (Morning( from.AddDays( new SeededRandom( seed ).Between( 0, days - 1 ) ), seed ), person);
                }
            }

            // Rock's job sends the rest of a run that is still going on install day, so only reserved contact points are in one.
            return entries.Values
                .Where( e => LastSend( flow, e.At.Date ) < _dates.Anchor || IsReserved( e.Person.PersonId, context ) )
                .OrderBy( e => e.At )
                .ToList();
        }

        private void WriteRun( FlowDefinition flow, FlowContext context, List<MessageBlueprint> messages, GoalPlan goal, int exitType, int flowId, DateTime start, bool isOpen, List<EmailPerson> members, FlowStats stats )
        {
            stats.Runs++;
            stats.People += members.Count;
            if ( isOpen )
            {
                stats.OpenRuns++;
            }

            // Each person's first conversion in the goal window, from the records already loaded.
            var windowEnd = goal.WindowEnd( start, flow.Goal.Days );
            var converted = new Dictionary<int, DateTime>();
            foreach ( var person in members )
            {
                if ( goal.Dates.TryGetValue( person.PersonId, out var dates ) )
                {
                    var first = dates.Where( d => d >= start && d < windowEnd && d < _dates.Anchor ).Cast<DateTime?>().FirstOrDefault();
                    if ( first.HasValue )
                    {
                        converted[person.PersonId] = first.Value;
                    }
                }
            }

            // The messages due before install day went out. Each waits from the day the one before it went out, as Rock's job dates them.
            var due = new List<(MessageBlueprint Message, DateTime At)>();
            var day = start;
            foreach ( var message in messages )
            {
                day = day.AddDays( message.DaysToWait );
                var at = day.Add( message.TimeToSend );
                if ( at.Date >= _dates.Anchor.Date )
                {
                    break;
                }

                due.Add( (message, at) );
            }

            var instanceId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [CommunicationFlowInstance] ( [CommunicationFlowId], [StartDate], [IsMessagingCompleted], [IsConversionGoalTrackingCompleted], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @FlowId, @Start, @Done, @TrackingDone, @Guid, @Created, @Modified, @ForeignKey )",
                P( "@FlowId", flowId ), P( "@Start", start ), P( "@Done", !isOpen ), P( "@TrackingDone", TrackingDone( flow, start ) ),
                P( "@Guid", SeededRandom.StableGuid( $"{_seed}:flow:{flow.Key}:instance:{start:yyyyMMdd}" ) ), P( "@Created", start.AddHours( 6 ) ),
                P( "@Modified", due.Count > 0 ? due[due.Count - 1].At : start.AddHours( 6 ) ), P( "@ForeignKey", _foreignKey ) ) );

            var leavesOnConversion = exitType == ExitTypes["conversionAchieved"];
            var sent = new List<(int LinkId, int Order, DateTime Sent, Dictionary<int, int> RecipientIds)>();
            foreach ( var (message, at) in due )
            {
                // Rock makes each send early on its day, from the people still Active then.
                var to = leavesOnConversion ? members.Where( p => !converted.TryGetValue( p.PersonId, out var c ) || c.Date >= at.Date ).ToList() : members;
                var key = $"flow:{flow.Key}:{message.Key}:{start:yyyyMMdd}";
                var (communicationId, recipientIds) = message.IsText ? WriteText( flow, message, context, key, at, to ) : WriteEmail( flow, message, context, key, at, to );
                stats.Sends++;

                var linkId = _nextLinkId++;
                _links.Rows.Add( instanceId, message.Id, communicationId, SeededRandom.StableGuid( $"{_seed}:{key}:link" ), at.AddMinutes( -5 ), at.AddMinutes( -5 ), _foreignKey );
                sent.Add( (linkId, message.Order, at, recipientIds) );
            }

            // A conversion is credited to the newest message the person got on or before it, or on its day, as Rock's job does.
            var lastMessageDay = sent.Count > 0 ? sent[sent.Count - 1].Sent.Date : start;
            foreach ( var person in members )
            {
                var preMet = goal.Dates.TryGetValue( person.PersonId, out var dates ) && dates.Any( d => d < start );
                var leftOnGoal = false;
                if ( converted.TryGetValue( person.PersonId, out var when ) )
                {
                    var credited = sent.Where( s => s.RecipientIds.ContainsKey( person.PersonId ) && ( s.Sent <= when || s.Sent.Date == when.Date ) )
                        .OrderByDescending( s => s.Order ).ThenByDescending( s => s.Sent ).FirstOrDefault();
                    if ( credited.LinkId != 0 )
                    {
                        _conversions.Rows.Add( credited.LinkId, when, person.AliasId, credited.RecipientIds[person.PersonId], SeededRandom.StableGuid( $"{_seed}:flow:{flow.Key}:conversion:{start:yyyyMMdd}:{person.Guid}" ),
                            when, when, _foreignKey );
                        stats.Conversions++;

                        // Rock's job prunes on each run while the run is still sending, so only a goal met before the last send takes the person out.
                        leftOnGoal = leavesOnConversion && ( isOpen || when.Date < lastMessageDay );
                    }
                }

                _recipients.Rows.Add( instanceId, person.AliasId, leftOnGoal ? InactiveRecipient : ActiveRecipient, leftOnGoal ? ConversionGoalMet : ( object ) DBNull.Value, preMet,
                    SeededRandom.StableGuid( $"{_seed}:flow:{flow.Key}:recipient:{start:yyyyMMdd}:{person.Guid}" ), start.AddHours( 6 ), leftOnGoal ? when : start.AddHours( 6 ), _foreignKey );
            }
        }

        private (int CommunicationId, Dictionary<int, int> RecipientIds) WriteEmail( FlowDefinition flow, MessageBlueprint message, FlowContext context, string key, DateTime at, List<EmailPerson> to )
        {
            var main = context.Emails.CampusList.First();
            var tokens = MessageRenderer.Tokens( at, at, main.Name, main.Key, main.ServiceTimes, null );
            var send = new PlannedEmail
            {
                Key = key,
                Name = $"{flow.Name} – {message.Name}",
                Subject = MessageRenderer.Fill( message.Email.Subject, tokens ),
                Html = message.Html,
                Links = context.Emails.Renderer.Render( message.Email.Body, message.Email.Preheader, tokens ).Links,
                Audience = "flow",
                IsBulk = true,
                Topic = message.Email.Topic,
                Created = at.AddMinutes( -5 ),
                Sent = at
            };
            foreach ( var person in to )
            {
                context.Emails.Planner.PlanOneToOne( send, person, $"{_seed}:flow:{send.Key}:{person.Guid}" );
            }

            var (communicationId, firstRecipientId) = context.Writer.Add( send, new EmailSendContext
            {
                SenderAliasId = context.SenderAliasId,
                ReviewerAliasId = context.SenderAliasId,
                TemplateId = message.TemplateId,
                TopicValueId = context.TopicIds.TryGetValue( send.Topic ?? string.Empty, out var topic ) ? topic : null,
                FromName = context.Configuration.Senders.Church.FromName,
                FromEmail = context.Configuration.Senders.Church.FromEmail,
                Guid = SeededRandom.StableGuid( $"{_seed}:{send.Key}:communication" ),
                ComponentGuid = SeededRandom.StableGuid( $"{_seed}:{send.Key}:component" ),
                IpAddress = BulkEmailLoader.IpAddress
            }, context.Emails.Clients );

            var ids = new Dictionary<int, int>();
            for ( var i = 0; i < send.Recipients.Count; i++ )
            {
                if ( send.Recipients[i].DeliveredDateTime.HasValue )
                {
                    ids[send.Recipients[i].Person.PersonId] = firstRecipientId + i;
                }
            }

            return (communicationId, ids);
        }

        /// <summary>
        /// A flow text, as Rock's Twilio transport records it: delivered with the words the person got, and a "SMS
        /// message" line on their History, or failed when they have no mobile that takes texts.
        /// </summary>
        private (int CommunicationId, Dictionary<int, int> RecipientIds) WriteText( FlowDefinition flow, MessageBlueprint message, FlowContext context, string key, DateTime at, List<EmailPerson> to )
        {
            var (communicationId, firstRecipientId) = context.Writer.Reserve( to.Count );
            var smsMediumId = _db.RequireIdByGuid( "EntityType", SmsMediumGuid );
            var communicationTypeId = _db.RequireIdByGuid( "EntityType", CommunicationEntityTypeGuid );
            var personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var historyCategoryId = _db.RequireIdByGuid( "Category", CommunicationHistoryCategoryGuid );
            _texts.Rows.Add( communicationId, $"{flow.Name} – {message.Name}", SmsType, ApprovedStatus, true, context.SenderAliasId, context.SenderAliasId, at.AddMinutes( -5 ), 0, message.TemplateId,
                message.Text.Text, context.MainNumberId, at, DateKey( at ), false,
                context.TopicIds.TryGetValue( message.Text.Topic ?? string.Empty, out var topic ) ? topic : DBNull.Value,
                SeededRandom.StableGuid( $"{_seed}:{key}:communication" ), at.AddMinutes( -5 ), at, context.SenderAliasId, context.SenderAliasId, _foreignKey );

            var ids = new Dictionary<int, int>();
            for ( var i = 0; i < to.Count; i++ )
            {
                var person = to[i];
                var recipientId = firstRecipientId + i;
                var seed = $"{_seed}:flow:{key}:{person.Guid}";
                var guid = SeededRandom.StableGuid( $"{seed}:recipient" );
                var sentAt = at.AddSeconds( 0.4 * i );
                if ( !context.Mobiles.TryGetValue( person.PersonId, out var mobile ) || !( mobile.Enabled || ( mobile.OptedOut.HasValue && mobile.OptedOut.Value > at ) ) )
                {
                    _textRecipients.Rows.Add( recipientId, communicationId, person.AliasId, FailedStatus, TextPlanner.NoPhone, smsMediumId, DBNull.Value, DBNull.Value,
                        DBNull.Value, at, DBNull.Value, DBNull.Value, guid, at, at, _foreignKey );
                    continue;
                }

                var words = message.Text.Text.Replace( "{{ Person.NickName }}", context.NickNames.GetValueOrDefault( person.PersonId ) ?? string.Empty );
                _textRecipients.Rows.Add( recipientId, communicationId, person.AliasId, DeliveredStatus, DBNull.Value, smsMediumId, TwilioTransport,
                    "SM" + SeededRandom.StableGuid( $"{seed}:sid" ).ToString( "N" ), sentAt, at, sentAt, words, guid, at, sentAt, _foreignKey );
                _textHistory.Rows.Add( false, historyCategoryId, personTypeId, person.PersonId, words.Length > 200 ? words.Substring( 0, 200 ) : words, communicationTypeId, communicationId, "SENT", "Record", "SMS message",
                    false, SeededRandom.StableGuid( $"{seed}:history" ), _foreignKey, sentAt, sentAt, context.SenderAliasId );
                ids[person.PersonId] = recipientId;
            }

            return (communicationId, ids);
        }

        /// <summary>
        /// A message's library entry, and the flow template Rock's flow editor saves for it.
        /// </summary>
        private MessageBlueprint Blueprint( FlowDefinition flow, FlowMessage message, int order, FlowContext context )
        {
            var blueprint = new MessageBlueprint
            {
                Loader = this,
                Context = context,
                Flow = flow,
                Key = message.Key,
                Name = message.Name,
                Order = order,
                DaysToWait = message.DaysToWait,
                TimeToSend = TimeSpan.Parse( message.Time ?? "10:00", System.Globalization.CultureInfo.InvariantCulture )
            };
            if ( context.EmailLibrary.TryGetValue( message.Key, out var email ) )
            {
                blueprint.Email = email;
            }
            else if ( context.TextLibrary.TryGetValue( message.Key, out var text ) && context.MainNumberId.HasValue )
            {
                blueprint.Text = text;
            }
            else
            {
                throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} sends '{message.Key}', which is neither an email in emails.json nor a text in texts.json." );
            }

            return blueprint;
        }

        /// <summary>
        /// The goal's type, its settings as Rock's flow editor stores them, and each person's dates that meet it.
        /// </summary>
        private GoalPlan Goal( FlowDefinition flow, FlowContext context )
        {
            if ( !GoalTypes.TryGetValue( flow.Goal?.Type ?? string.Empty, out var type ) )
            {
                throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} has goal '{flow.Goal?.Type}', which is not one this slice writes." );
            }

            if ( flow.Goal.Type == "joinedGroupType" )
            {
                var groupTypeId = context.GroupTypeIds.TryGetValue( flow.Goal.GroupType ?? string.Empty, out var id ) ? id
                    : throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} names group type '{flow.Goal.GroupType}', which is not small-groups or serving-teams." );
                var groupTypeGuid = ( Guid ) _db.Scalar( "SELECT [Guid] FROM [GroupType] WHERE [Id] = @Id", P( "@Id", groupTypeId ) );
                var joins = _db.Query( "SELECT [PersonId], [DateTimeAdded] FROM [GroupMember] WHERE [GroupTypeId] = @Type AND [DateTimeAdded] IS NOT NULL", P( "@Type", groupTypeId ) )
                    .GroupBy( r => ( int ) r["PersonId"] )
                    .ToDictionary( g => g.Key, g => g.Select( r => ( DateTime ) r["DateTimeAdded"] ).OrderBy( d => d ).ToList() );
                return new GoalPlan
                {
                    TypeValue = type,
                    SettingsJson = JsonSerializer.Serialize( new { ConversionGoalSettings = new { JoinedGroupTypeSettings = new { GroupTypeGuid = groupTypeGuid } } } ),
                    Dates = joins,
                    WindowEnd = ( start, days ) => start.AddDays( days )
                };
            }

            var stepType = _source.Steps.StepTypes.FirstOrDefault( t => t.Key == flow.Goal.StepType )
                ?? throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} names step type '{flow.Goal.StepType}', which configuration/steps.json does not have." );
            var programGuid = _source.Steps.Programs.First( p => p.StepTypes.Contains( stepType ) ).Guid;

            // Rock counts a step only once its status is a complete one, by its completed date, through the whole last day of the window.
            var steps = _db.Query( @"
SELECT [pa].[PersonId], [s].[CompletedDateTime]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
WHERE [st].[Guid] = @Type AND [s].[CompletedDateTime] IS NOT NULL AND [ss].[IsCompleteStatus] = 1", P( "@Type", stepType.Guid ) )
                .GroupBy( r => ( int ) r["PersonId"] )
                .ToDictionary( g => g.Key, g => g.Select( r => ( DateTime ) r["CompletedDateTime"] ).OrderBy( d => d ).ToList() );
            return new GoalPlan
            {
                TypeValue = type,
                SettingsJson = JsonSerializer.Serialize( new { ConversionGoalSettings = new { TookStepSettings = new { StepProgramGuid = programGuid, StepTypeGuid = stepType.Guid } } } ),
                Dates = steps,
                WindowEnd = ( start, days ) => start.AddDays( days + 1 )
            };
        }

        /// <summary>
        /// Each person's mobile number that can take texts, and when they texted STOP, if they did.
        /// </summary>
        private Dictionary<int, (bool Enabled, DateTime? OptedOut)> Mobiles()
        {
            var mobileTypeId = _db.RequireIdByGuid( "DefinedValue", MobilePhoneTypeGuid );
            return _db.Query( "SELECT [PersonId], [IsMessagingEnabled], [MessagingOptedOutDateTime] FROM [PhoneNumber] WHERE [NumberTypeValueId] = @Type", P( "@Type", mobileTypeId ) )
                .GroupBy( r => ( int ) r["PersonId"] )
                .ToDictionary( g => g.Key, g => (( bool ) g.First()["IsMessagingEnabled"], g.First()["MessagingOptedOutDateTime"] as DateTime?) );
        }

        private static bool IsReserved( int personId, FlowContext context )
        {
            var email = context.EmailAddresses.GetValueOrDefault( personId );
            return string.IsNullOrWhiteSpace( email ) || email.EndsWith( context.ReservedEmailSuffix, StringComparison.OrdinalIgnoreCase );
        }

        /// <summary>
        /// When a run's last message goes out, counting each wait from the message before it.
        /// </summary>
        private static DateTime LastSend( FlowDefinition flow, DateTime start )
        {
            var day = start;
            foreach ( var message in flow.Messages )
            {
                day = day.AddDays( message.DaysToWait );
            }

            return day.Add( TimeSpan.Parse( flow.Messages.Last().Time ?? "10:00", System.Globalization.CultureInfo.InvariantCulture ) );
        }

        private bool TrackingDone( FlowDefinition flow, DateTime start ) => start.AddDays( flow.Goal.Days ) < _dates.Anchor.Date;

        private static int DateKey( DateTime date ) => ( date.Year * 10000 ) + ( date.Month * 100 ) + date.Day;

        private sealed class FlowContext
        {
            public CommunicationsConfiguration Configuration { get; set; }

            public PeopleLoader People { get; set; }

            public BulkEmailLoader Emails { get; set; }

            public bool AllowOpenRuns { get; set; }

            public string ReservedEmailSuffix { get; set; }

            public Dictionary<string, LibraryEmail> EmailLibrary { get; set; }

            public Dictionary<string, LibraryText> TextLibrary { get; set; }

            public int SenderAliasId { get; set; }

            public Dictionary<int, EmailPerson> ByAlias { get; set; }

            public Dictionary<string, object> Template { get; set; }

            public Dictionary<string, int> TopicIds { get; set; }

            public IReadOnlyDictionary<string, int> GroupTypeIds { get; set; }

            public EmailWriter Writer { get; set; }

            public Dictionary<int, string> EmailAddresses { get; set; }

            public Dictionary<int, (bool Enabled, DateTime? OptedOut)> Mobiles { get; set; }

            public Dictionary<int, string> NickNames { get; set; }

            public int? MainNumberId { get; set; }
        }

        private sealed class GoalPlan
        {
            public int TypeValue { get; set; }

            public string SettingsJson { get; set; }

            /// <summary>
            /// Each person's dates that meet the goal, in order.
            /// </summary>
            public Dictionary<int, List<DateTime>> Dates { get; set; }

            /// <summary>
            /// The first moment after a run's goal window, from its start date and length in days.
            /// </summary>
            public Func<DateTime, int, DateTime> WindowEnd { get; set; }
        }

        private sealed class FlowStats
        {
            public int Runs { get; set; }

            public int OpenRuns { get; set; }

            public int People { get; set; }

            public int Sends { get; set; }

            public int Conversions { get; set; }

            public int LeftOutOfOpenRuns { get; set; }
        }

        /// <summary>
        /// One message in a flow: its library entry, and its row and template once written.
        /// </summary>
        private sealed class MessageBlueprint
        {
            public FlowLoader Loader { get; set; }

            public FlowContext Context { get; set; }

            public FlowDefinition Flow { get; set; }

            public string Key { get; set; }

            public string Name { get; set; }

            public int Order { get; set; }

            public int DaysToWait { get; set; }

            public TimeSpan TimeToSend { get; set; }

            public LibraryEmail Email { get; set; }

            public LibraryText Text { get; set; }

            public bool IsText => Text != null;

            public int Id { get; private set; }

            public int TemplateId { get; private set; }

            public string Html { get; private set; }

            /// <summary>
            /// Writes the message's flow template and its row in the flow, as Rock's flow editor saves them.
            /// </summary>
            public void Write( int flowId, DateTime launched )
            {
                var db = Loader._db;
                var church = Context.Configuration.Senders.Church;
                var templateGuid = SeededRandom.StableGuid( $"{Loader._seed}:flow:template:{Key}" );
                if ( IsText )
                {
                    TemplateId = Convert.ToInt32( db.Scalar( @"
INSERT INTO [CommunicationTemplate] ( [Name], [Description], [IsSystem], [SenderPersonAliasId], [SMSMessage], [SmsFromSystemPhoneNumberId], [IsActive], [CategoryId], [CssInliningEnabled], [IsStarter], [Version], [UsageType],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, '', 0, @Sender, @Message, @Number, 1, @CategoryId, 1, 0, 1, 1, @Guid, @Launched, @Launched, @Sender, @Sender, @ForeignKey )",
                        P( "@Name", $"{Flow.Name} - {Name}" ), P( "@Sender", Context.SenderAliasId ), P( "@Message", Text.Text ), P( "@Number", Context.MainNumberId.Value ),
                        P( "@CategoryId", Context.Template["CategoryId"] ), P( "@Guid", templateGuid ), P( "@Launched", launched ), P( "@ForeignKey", Loader._foreignKey ) ) );
                }
                else
                {
                    var main = Context.Emails.CampusList.First();
                    var tokens = MessageRenderer.Tokens( launched, launched, main.Name, main.Key, main.ServiceTimes, null );
                    (Html, _) = Context.Emails.Renderer.Render( Email.Body, Email.Preheader, tokens );
                    TemplateId = Convert.ToInt32( db.Scalar( @"
INSERT INTO [CommunicationTemplate] ( [Name], [Description], [IsSystem], [Subject], [FromName], [FromEmail], [SenderPersonAliasId], [Message], [IsActive], [LogoBinaryFileId], [CategoryId],
    [LavaFieldsJson], [CssInliningEnabled], [IsStarter], [Version], [UsageType], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, '', 0, @Subject, @FromName, @FromEmail, @Sender, @Message, 1, @LogoId, @CategoryId, @LavaFields, 1, 0, 1, 1, @Guid, @Launched, @Launched, @Sender, @Sender, @ForeignKey )",
                        P( "@Name", $"{Flow.Name} - {Name}" ), P( "@Subject", MessageRenderer.Fill( Email.Subject, tokens ) ), P( "@FromName", church.FromName ), P( "@FromEmail", church.FromEmail ),
                        P( "@Sender", Context.SenderAliasId ), P( "@Message", Html ), P( "@LogoId", Context.Template["LogoBinaryFileId"] ), P( "@CategoryId", Context.Template["CategoryId"] ),
                        P( "@LavaFields", Context.Template["LavaFieldsJson"] ), P( "@Guid", templateGuid ), P( "@Launched", launched ), P( "@ForeignKey", Loader._foreignKey ) ) );
                }

                Id = Convert.ToInt32( db.Scalar( @"
INSERT INTO [CommunicationFlowCommunication] ( [Name], [DaysToWait], [TimeToSend], [CommunicationType], [CommunicationFlowId], [CommunicationTemplateId], [Order], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Days, @Time, @Type, @FlowId, @TemplateId, @Order, @Guid, @Launched, @Launched, @ForeignKey )",
                    P( "@Name", Name ), P( "@Days", DaysToWait ), P( "@Time", TimeToSend ), P( "@Type", IsText ? SmsType : EmailType ), P( "@FlowId", flowId ), P( "@TemplateId", TemplateId ), P( "@Order", Order ),
                    P( "@Guid", SeededRandom.StableGuid( $"{Loader._seed}:flow:message:{Key}" ) ), P( "@Launched", launched ), P( "@ForeignKey", Loader._foreignKey ) ) );
            }
        }
    }
}
