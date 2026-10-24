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
using System.Text;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/23/26 - CLAUDE

        One-to-one emails come from data already loaded: serving requests, connection activities,
        answered prayer requests, and first visits, plus the Cast staff's hand-written emails. Each is
        its own communication with one recipient, the way Rock writes an email a job, a workflow, or a
        staff member sends to one person. They go through the bulk email planner, so a person whose
        email bounced, or who chose "Do not email", gets the note Rock writes instead.

        Serving emails are the ones Rock's Send Group Schedule Notifications job sends from the serving
        team type's system communications. Rock stores those fully rendered, global header and footer
        included, at about 26 KB each, and Full 7 has over 120,000 of them. The stored message here is the
        library's short version of each, without the header and footer, so the database stays small.

        Reason: most messages a real church sends go to one person (decision D-commsoneone-1).
    */

    /// <summary>
    /// Plans and writes one-to-one emails: serving confirmations and reminders, connection follow-ups,
    /// prayer celebrations, first-visit welcomes, and the Cast staff's personal emails.
    /// </summary>
    internal sealed class OneToOneLoader
    {
        private static readonly Guid ScheduleConfirmationGuid = new Guid( "BA1716E0-6B31-4E93-ABA1-42B3C81FDBDC" );
        private static readonly Guid ScheduleReminderGuid = new Guid( "8A20FE79-B73C-447A-82B1-416F9B50C038" );
        private const string ContactedActivity = "Contacted Waiting for Response";
        private const int RsvpNo = 0;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public OneToOneLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Plans and writes every one-to-one email over the same 2 years as bulk email.
        /// </summary>
        /// <param name="emails">The bulk email load, already run, with its planner and people.</param>
        /// <param name="welcomers">Who welcomes a first-time guest, by campus key: each campus pastor. A campus with none uses the welcome sender.</param>
        public void Load( CommunicationsConfiguration configuration, PeopleLoader people, BulkEmailLoader emails, IReadOnlyDictionary<string, int> campusIds,
            IReadOnlyDictionary<string, LoadedPerson> welcomers )
        {
            var library = _source.ReadEmailLibrary().Emails.ToDictionary( e => e.Key );
            var from = _dates.Anchor.AddYears( -2 );
            var byAlias = emails.EmailPeople.GroupBy( p => p.AliasId ).ToDictionary( g => g.Key, g => g.First() );
            var byGuid = emails.EmailPeople.ToDictionary( p => p.Guid );
            var loaded = people.People.Values.GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var campusKeys = campusIds.ToDictionary( c => c.Value, c => c.Key );
            var campuses = emails.CampusList.ToDictionary( c => c.Key );
            var church = configuration.Senders.Church;
            var welcome = configuration.Senders.Welcome ?? church;
            var staff = _source.Notes?.Staff.ToDictionary( s => s.Key, s => people.People[s.Guid] ) ?? new Dictionary<string, LoadedPerson>();

            var sends = new List<(PlannedEmail Send, EmailPerson Person, OneToOneSender Sender)>();

            // A person can get two of the same email in one minute, such as two connectors writing at once, so each key gets a number when it repeats.
            var keys = new Dictionary<string, int>();
            string Unique( string key ) => ( keys[key] = keys.GetValueOrDefault( key ) + 1 ) == 1 ? key : $"{key}:{keys[key]}";
            void Add( string kind, LibraryEmail message, EmailPerson person, DateTime sent, string campusKey, DateTime eventDate, OneToOneSender sender )
            {
                if ( sent < from || sent >= _dates.Anchor || !loaded.TryGetValue( person.PersonId, out var who ) )
                {
                    return;
                }

                var campus = campuses.GetValueOrDefault( campusKey ?? person.CampusKey ?? string.Empty ) ?? campuses.Values.First();
                var tokens = MessageRenderer.Tokens( eventDate, sent, campus.Name, campus.Key, campus.ServiceTimes, null );
                tokens["{{ Person.NickName }}"] = who.Cast.NickName ?? who.Cast.FirstName;
                var (html, links) = MessageRenderer.Compact( message.Body, tokens );
                sends.Add( (new PlannedEmail
                {
                    Key = Unique( $"{kind}:{person.Guid}:{sent:yyyyMMddHHmm}" ),
                    Name = MessageRenderer.Fill( message.Subject, tokens ),
                    Subject = MessageRenderer.Fill( message.Subject, tokens ),
                    Html = html,
                    Links = links,
                    Audience = "person",
                    CampusKey = campus.Key,
                    Topic = message.Topic,
                    Created = sent,
                    Sent = sent
                }, person, sender) );
            }

            var churchSender = new OneToOneSender { FromName = church.FromName, FromEmail = church.FromEmail };
            OneToOneSender Personal( LoadedPerson person ) => new OneToOneSender
            {
                AliasId = person.AliasId,
                FromName = $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}",
                FromEmail = string.IsNullOrWhiteSpace( person.Cast.Email ) ? church.FromEmail : person.Cast.Email
            };

            // Serving: one confirmation per volunteer each day the scheduler asks them, and one reminder two days before each Sunday they did not decline.
            // Rock's job only picks volunteers it can email, so a volunteer with no working email gets no serving email at all.
            var confirmationId = _db.RequireIdByGuid( "SystemCommunication", ScheduleConfirmationGuid );
            var reminderId = _db.RequireIdByGuid( "SystemCommunication", ScheduleReminderGuid );
            var requests = _db.Query( "SELECT [PersonAliasId], [CampusId], [StartDateTime], [RSVP], [CreatedDateTime] FROM [Attendance] WHERE [ForeignKey] = @ForeignKey AND [RequestedToAttend] = 1 AND [ScheduleConfirmationSent] = 1",
                P( "@ForeignKey", _foreignKey ) )
                .Where( r => byAlias.ContainsKey( ( int ) r["PersonAliasId"] ) )
                .Select( r => (Person: byAlias[( int ) r["PersonAliasId"]], CampusKey: r["CampusId"] is int c ? campusKeys.GetValueOrDefault( c ) : null, Start: ( DateTime ) r["StartDateTime"],
                    Rsvp: Convert.ToInt32( r["RSVP"] ), Requested: ( DateTime ) r["CreatedDateTime"]) )
                .ToList();
            foreach ( var day in requests.GroupBy( r => (r.Person.PersonId, r.Requested.Date) ).OrderBy( g => g.Key.Date ).ThenBy( g => g.Key.PersonId ) )
            {
                var first = day.OrderBy( r => r.Start ).First();
                var sent = day.Key.Date.AddHours( 16 ).AddMinutes( new SeededRandom( $"{_seed}:oneone:confirm:{first.Person.Guid}:{day.Key.Date:yyyyMMdd}" ).Between( 1, 20 ) );
                if ( !emails.Planner.CanEmail( first.Person, sent ) )
                {
                    continue;
                }

                Add( "confirm", library["serving-scheduled"], first.Person, sent, first.CampusKey, first.Start, new OneToOneSender { FromName = church.FromName, FromEmail = church.FromEmail, SystemCommunicationId = confirmationId } );
            }

            foreach ( var day in requests.Where( r => r.Rsvp != RsvpNo ).GroupBy( r => (r.Person.PersonId, r.Start.Date) ).OrderBy( g => g.Key.Date ).ThenBy( g => g.Key.PersonId ) )
            {
                var first = day.OrderBy( r => r.Start ).First();
                var sent = day.Key.Date.AddDays( -2 ).AddHours( 16 ).AddMinutes( new SeededRandom( $"{_seed}:oneone:remind:{first.Person.Guid}:{day.Key.Date:yyyyMMdd}" ).Between( 1, 20 ) );
                if ( !emails.Planner.CanEmail( first.Person, sent ) )
                {
                    continue;
                }

                Add( "remind", library["serving-reminder"], first.Person, sent, first.CampusKey, first.Start, new OneToOneSender { FromName = church.FromName, FromEmail = church.FromEmail, SystemCommunicationId = reminderId } );
            }

            // Connections: the connector's email when they log that they reached out and are waiting to hear back.
            var contacted = _db.Query( @"
SELECT [r].[PersonAliasId], [r].[CampusId], [a].[ConnectorPersonAliasId], [a].[CreatedDateTime]
FROM [ConnectionRequestActivity] AS [a]
INNER JOIN [ConnectionRequest] AS [r] ON [r].[Id] = [a].[ConnectionRequestId]
INNER JOIN [ConnectionActivityType] AS [t] ON [t].[Id] = [a].[ConnectionActivityTypeId]
WHERE [a].[ForeignKey] = @ForeignKey AND [t].[Name] = @Activity", P( "@ForeignKey", _foreignKey ), P( "@Activity", ContactedActivity ) );
            var aliases = people.People.Values.GroupBy( p => p.AliasId ).ToDictionary( g => g.Key, g => g.First() );
            foreach ( var row in contacted.OrderBy( r => ( DateTime ) r["CreatedDateTime"] ) )
            {
                if ( !byAlias.TryGetValue( ( int ) row["PersonAliasId"], out var person ) )
                {
                    continue;
                }

                var connector = row["ConnectorPersonAliasId"] is int c && aliases.TryGetValue( c, out var who ) ? Personal( who ) : churchSender;
                var when = ( DateTime ) row["CreatedDateTime"];
                Add( "connect", library["connection-follow-up"], person, when.AddMinutes( 3 ), row["CampusId"] is int campus ? campusKeys.GetValueOrDefault( campus ) : null, when, connector );
            }

            // Prayer: a workflow emails the person who asked when their request is marked answered.
            var answered = _db.Query( "SELECT [RequestedByPersonAliasId], [CampusId], [ModifiedDateTime] FROM [PrayerRequest] WHERE [ForeignKey] = @ForeignKey AND [Answer] IS NOT NULL AND [RequestedByPersonAliasId] IS NOT NULL",
                P( "@ForeignKey", _foreignKey ) );
            foreach ( var row in answered.OrderBy( r => ( DateTime ) r["ModifiedDateTime"] ) )
            {
                if ( byAlias.TryGetValue( ( int ) row["RequestedByPersonAliasId"], out var person ) )
                {
                    var when = ( DateTime ) row["ModifiedDateTime"];
                    Add( "prayer", library["prayer-answered"], person, when.AddMinutes( 2 ), row["CampusId"] is int campus ? campusKeys.GetValueOrDefault( campus ) : null, when, churchSender );
                }
            }

            // Baptisms: the next morning, the church writes to invite the person to Foundations.
            var baptism = _source.Steps?.StepTypes.FirstOrDefault( t => t.Key == "baptism" );
            if ( baptism != null )
            {
                var baptized = _db.Query( @"
SELECT [s].[PersonAliasId], [s].[CampusId], [s].[CompletedDateTime]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
WHERE [st].[Guid] = @Type AND [s].[CompletedDateTime] IS NOT NULL", P( "@Type", baptism.Guid ) );
                foreach ( var row in baptized.OrderBy( r => ( DateTime ) r["CompletedDateTime"] ) )
                {
                    if ( byAlias.TryGetValue( ( int ) row["PersonAliasId"], out var person ) )
                    {
                        var day = (( DateTime ) row["CompletedDateTime"]).Date;
                        var sent = day.AddDays( 1 ).AddHours( 10 ).AddMinutes( new SeededRandom( $"{_seed}:oneone:baptized:{person.Guid}" ).Between( 0, 90 ) );
                        Add( "baptized", library["baptized-follow-up"], person, sent, row["CampusId"] is int campus ? campusKeys.GetValueOrDefault( campus ) : null, day, churchSender );
                    }
                }
            }

            // First visits: the Monday after a generated adult's first Sunday, from their campus pastor or the welcome sender.
            // The Cast's welcomes are their own hand-written emails.
            foreach ( var person in emails.EmailPeople.Where( p => p.IsAdult && !p.IsCast && p.Since > from ).OrderBy( p => p.Since ).ThenBy( p => p.PersonId ) )
            {
                var welcomer = welcomers.TryGetValue( person.CampusKey ?? string.Empty, out var pastor ) ? Personal( pastor )
                    : new OneToOneSender { AliasId = people.People.TryGetValue( welcome.Sender, out var sender ) ? sender.AliasId : null, FromName = welcome.FromName, FromEmail = welcome.FromEmail };
                var sent = person.Since.Date.AddDays( 1 ).AddHours( 9 ).AddMinutes( new SeededRandom( $"{_seed}:oneone:welcome:{person.Guid}" ).Between( 0, 150 ) );
                Add( "welcome", library["first-visit-welcome"], person, sent, person.CampusKey, person.Since, welcomer );
            }

            // The Cast staff's personal emails.
            foreach ( var email in _source.ReadCastStaffEmails() )
            {
                if ( !staff.TryGetValue( email.From, out var writer ) || !byGuid.TryGetValue( email.To, out var person ) )
                {
                    throw new InvalidOperationException( $"cast/staff-emails.json: {email.Key} needs a staff key in configuration/notes.json and a loaded Cast person." );
                }

                var sent = _dates.Anchor.AddDays( -email.DaysAgo ).Add( TimeSpan.Parse( email.Time, CultureInfo.InvariantCulture ) );
                var html = new StringBuilder();
                foreach ( var paragraph in email.Body )
                {
                    html.Append( $"<p>{MessageRenderer.Escape( paragraph )}</p>" );
                }

                sends.Add( (new PlannedEmail
                {
                    Key = $"staff:{email.Key}",
                    Name = email.Subject,
                    Subject = email.Subject,
                    Html = html.ToString(),
                    Audience = "person",
                    CampusKey = person.CampusKey,
                    Created = sent,
                    Sent = sent
                }, person, Personal( writer )) );
            }

            Write( sends, emails );
        }

        private void Write( List<(PlannedEmail Send, EmailPerson Person, OneToOneSender Sender)> sends, BulkEmailLoader emails )
        {
            var topicIds = _source.Communications.Topics.ToDictionary( t => t.Key, t => _db.RequireIdByGuid( "DefinedValue", t.Value ) );
            var writer = new EmailWriter( _db, _foreignKey );
            var index = 0;
            var counts = new Dictionary<string, int>();
            foreach ( var (send, person, sender) in sends.OrderBy( s => s.Send.Sent ).ThenBy( s => s.Send.Key, StringComparer.Ordinal ) )
            {
                send.Index = index++;
                emails.Planner.PlanOneToOne( send, person, $"{_seed}:oneone:{send.Key}" );
                writer.Add( send, new EmailSendContext
                {
                    SenderAliasId = sender.AliasId,
                    ReviewerAliasId = sender.AliasId,
                    SystemCommunicationId = sender.SystemCommunicationId,
                    TopicValueId = topicIds.TryGetValue( send.Topic ?? string.Empty, out var topicId ) ? topicId : null,
                    FromName = sender.FromName,
                    FromEmail = sender.FromEmail,
                    Guid = SeededRandom.StableGuid( $"{_seed}:oneone:{send.Key}:communication" ),
                    ComponentGuid = SeededRandom.StableGuid( $"{_seed}:oneone:{send.Key}:component" ),
                    IpAddress = BulkEmailLoader.IpAddress
                }, emails.Clients );

                var kind = send.Key.Substring( 0, send.Key.IndexOf( ':' ) );
                counts[kind] = counts.GetValueOrDefault( kind ) + 1;
            }

            writer.Finish();
            var delivered = sends.Count( s => s.Send.Recipients[0].DeliveredDateTime.HasValue );
            var opened = sends.Count( s => s.Send.Recipients[0].Status == EmailPlanner.Opened );
            _db.Log.Write( $"  One-to-one email: {sends.Count:N0} ({string.Join( ", ", counts.Select( c => $"{c.Value:N0} {c.Key}" ) )}), {delivered:N0} delivered and {opened:N0} opened" );
        }

        private sealed class OneToOneSender
        {
            public int? AliasId { get; set; }

            public string FromName { get; set; }

            public string FromEmail { get; set; }

            public int? SystemCommunicationId { get; set; }
        }
    }
}
