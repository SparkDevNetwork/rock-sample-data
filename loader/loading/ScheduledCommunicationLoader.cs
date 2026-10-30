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
using System.Globalization;
using System.Linq;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/30/26 - CLAUDE

        Everything else a load writes is history, and this is the one communication that waits to send. It
        is saved as Rock's communication wizard saves a scheduled email: Approved by the approver, with a
        future send date and no send date, and a Pending recipient for each person, so Rock's Send
        Communications job sends it when its time comes and the Communication List shows it as scheduled.
        Its people are worked out on install day by the Involved Adults rule with the reserved-email guard,
        on one campus, so it can reach only example.org addresses or none. It names no communication list,
        because Rock can refresh a list's recipients when it sends, and the newsletter lists include staff.

        Reason: decisions D-comm-flows-slice4-2 and 220. Jon chose a scheduled communication to show a
        future send date.
    */

    /// <summary>
    /// Writes the one communication configuration/communications.json schedules to send after install.
    /// </summary>
    internal sealed class ScheduledCommunicationLoader
    {
        private const int EmailType = 1;
        private const int ApprovedStatus = 3;
        private const int PendingRecipient = 0;
        private static readonly Guid EmailMediumGuid = new Guid( "5A653EBE-6803-44B4-85D2-FB7B8146D55D" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public ScheduledCommunicationLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <param name="emails">The bulk email load, already run, for its renderer and campuses.</param>
        /// <param name="templateId">The church's branded template.</param>
        /// <param name="flows">The flow load, already run, whose audience facts give the people.</param>
        public void Load( CommunicationsConfiguration configuration, PeopleLoader people, BulkEmailLoader emails, int templateId, IReadOnlyDictionary<string, int> campusIds, FlowLoader flows )
        {
            var scheduled = configuration.Scheduled;
            if ( scheduled == null )
            {
                return;
            }

            var email = _source.ReadEmailLibrary().Emails.FirstOrDefault( e => e.Key == scheduled.Email )
                ?? throw new InvalidOperationException( $"configuration/communications.json: the scheduled communication sends '{scheduled.Email}', which emails.json does not have." );
            var sender = scheduled.Sender switch
            {
                "church" => configuration.Senders.Church,
                "pastor" => configuration.Senders.Pastor,
                "students" => configuration.Senders.Students,
                _ => throw new InvalidOperationException( $"configuration/communications.json: the scheduled communication's sender '{scheduled.Sender}' is not church, pastor, or students." )
            };
            if ( scheduled.Audience?.Rule != "involvedAdults" )
            {
                throw new InvalidOperationException( $"configuration/communications.json: the scheduled communication's audience rule '{scheduled.Audience?.Rule}' is not one the loader knows." );
            }

            var campus = emails.CampusList.FirstOrDefault( c => c.Key == scheduled.Audience.Campus );
            if ( campus == null || !campusIds.TryGetValue( campus.Key, out var campusId ) )
            {
                _db.Log.Write( $"  Scheduled communication skipped, this load has no {scheduled.Audience.Campus} campus" );
                return;
            }

            // The next send time after the install moment.
            var time = TimeSpan.Parse( scheduled.Time ?? "10:00", CultureInfo.InvariantCulture );
            var sendAt = new DateTime( _dates.Anchor.Year, scheduled.Month, scheduled.Day ).Add( time );
            if ( sendAt <= _dates.Anchor )
            {
                sendAt = sendAt.AddYears( 1 );
            }

            var onCampus = _db.Query( "SELECT [Id] FROM [Person] WHERE [PrimaryCampusId] = @CampusId AND [ForeignKey] = @ForeignKey", P( "@CampusId", campusId ), P( "@ForeignKey", _foreignKey ) )
                .Select( r => ( int ) r["Id"] ).ToHashSet();
            var to = flows.ReservedInvolvedAdultsOnInstallDay().Where( p => onCampus.Contains( p.PersonId ) ).OrderBy( p => p.PersonId ).ToList();
            if ( to.Count == 0 )
            {
                _db.Log.Write( $"  Scheduled communication skipped, no involved adult at {campus.Name} has a reserved email" );
                return;
            }

            // Staff set it up two days before install, at the latest, and approved it the same afternoon.
            var created = _dates.Anchor.Date.AddDays( -2 ).AddHours( 14 );
            var tokens = MessageRenderer.Tokens( sendAt.Date.AddDays( email.DaysBefore ), sendAt, campus.Name, campus.Key, campus.ServiceTimes, null );
            var (html, _) = emails.Renderer.Render( email.Body, email.Preheader, tokens );
            var senderAliasId = people.People[sender.Sender].AliasId;
            var approverAliasId = people.People[configuration.Senders.Approver].AliasId;
            var subject = MessageRenderer.Fill( email.Subject, tokens );
            var topicId = configuration.Topics.TryGetValue( email.Topic ?? string.Empty, out var topic ) ? _db.IdByGuid( "DefinedValue", topic ) : null;

            var communicationId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Communication] ( [Name], [Subject], [CommunicationType], [Status], [IsBulkCommunication], [SenderPersonAliasId], [ReviewerPersonAliasId], [ReviewedDateTime], [SegmentCriteria],
    [CommunicationTemplateId], [FromName], [FromEmail], [Message], [FutureSendDateTime], [ExcludeDuplicateRecipientAddress], [CommunicationTopicValueId],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Subject, @Type, @Status, 1, @Sender, @Approver, @Reviewed, 0, @TemplateId, @FromName, @FromEmail, @Message, @SendAt, 0, @TopicId,
    @Guid, @Created, @Reviewed, @Sender, @Approver, @ForeignKey )",
                P( "@Name", subject ), P( "@Subject", subject ), P( "@Type", EmailType ), P( "@Status", ApprovedStatus ), P( "@Sender", senderAliasId ), P( "@Approver", approverAliasId ),
                P( "@Reviewed", created.AddHours( 1 ) ), P( "@TemplateId", templateId ), P( "@FromName", sender.FromName ), P( "@FromEmail", sender.FromEmail ), P( "@Message", html ),
                P( "@SendAt", sendAt ), P( "@TopicId", ( object ) topicId ?? DBNull.Value ), P( "@Guid", SeededRandom.StableGuid( $"{_seed}:scheduled:{email.Key}:communication" ) ),
                P( "@Created", created ), P( "@ForeignKey", _foreignKey ) ) );

            var mediumId = _db.RequireIdByGuid( "EntityType", EmailMediumGuid );
            var recipients = TableFactory.Create( "CommunicationId:int", "PersonAliasId:int", "Status:int", "MediumEntityTypeId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            foreach ( var person in to )
            {
                recipients.Rows.Add( communicationId, person.AliasId, PendingRecipient, mediumId, SeededRandom.StableGuid( $"{_seed}:scheduled:{email.Key}:recipient:{person.PersonId}" ), created, created, _foreignKey );
            }

            _db.BulkInsert( "CommunicationRecipient", recipients, keepIdentity: false );
            _db.Log.Write( $"  Scheduled communication: \"{subject}\" to {to.Count:N0} involved adults at {campus.Name}, sending {sendAt:yyyy-MM-dd HH:mm}" );
        }
    }
}
