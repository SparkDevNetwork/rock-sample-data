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
    /// <summary>
    /// Plans and writes two years of bulk email (decision 90): every campus's weekly newsletter and the
    /// library's mass emails, with their recipients, opens, clicks, bounces, and unsubscribes, and the
    /// changes those make to people.
    /// </summary>
    internal sealed class BulkEmailLoader
    {
        private static readonly Guid DemographicHistoryCategoryGuid = new Guid( "51D3EC5A-D079-45ED-909E-B0AB2FD06835" );
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly ProfileTargets _profile;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public BulkEmailLoader( RockDatabase db, SampleDataSource source, ProfileTargets profile, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _profile = profile;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// The occasions' dates, once <see cref="Load"/> has run. Texts use the same calendar.
        /// </summary>
        public CommunicationCalendar Calendar { get; private set; }

        /// <summary>
        /// The dataset's campuses with their service times, once <see cref="Load"/> has run.
        /// </summary>
        public List<EmailCampus> CampusList { get; private set; }

        /// <summary>
        /// Plans and writes every send. Unsubscribes mark their list members in <paramref name="members"/>.
        /// </summary>
        public void Load( CommunicationsConfiguration configuration, PeopleLoader people, List<ListCandidate> candidates, List<PlannedListMember> members,
            Dictionary<(string ListKey, string CampusKey), int> listIds, int templateId, string templateHtml, string mainCampusKey, IReadOnlyDictionary<string, int> campusIds )
        {
            var from = _dates.Anchor.AddYears( -2 );
            var series = _source.ReadSeriesLibrary();
            var calendar = Calendar = new CommunicationCalendar( _dates.Anchor, from, _source.Steps, _source.Campaign, configuration.Events, series, _seed );
            var clients = configuration.EmailClients;
            var emailPeople = People( people, candidates, from, clients );

            var planner = new EmailPlanner( new EmailPlanInputs
            {
                Profile = _profile,
                Seed = _seed,
                Anchor = _dates.Anchor,
                From = from,
                Calendar = calendar,
                Renderer = new MessageRenderer( templateHtml ),
                Newsletter = _source.ReadNewsletterLibrary(),
                Emails = _source.ReadEmailLibrary().Emails,
                Lists = configuration.Lists.Lists,
                Members = members,
                People = emailPeople,
                PersonIdsByGuid = people.People.ToDictionary( p => p.Key, p => p.Value.PersonId ),
                Campuses = CampusList = Campuses( campusIds ),
                MainCampusKey = mainCampusKey,
                CastUnsubscribes = configuration.CastUnsubscribes,
                Clients = clients
            } );

            var writer = new EmailWriter( _db, _foreignKey );
            writer.AddDeviceTypes( clients );
            var senders = Senders( configuration, people );
            var approverAlias = people.People[configuration.Senders.Approver].AliasId;
            var topicIds = configuration.Topics.ToDictionary( t => t.Key, t => _db.RequireIdByGuid( "DefinedValue", t.Value ) );
            var guids = new SeededRandom( $"{_seed}:email:guids" );
            var delivered = 0;
            var opened = 0;
            var unsubscribes = 0;

            foreach ( var send in planner.Plan() )
            {
                var sender = senders[send.Sender];
                var listId = send.ListKey == null ? ( int? ) null : listIds.TryGetValue( (send.ListKey, send.ListCampusKey), out var id ) ? id : null;
                writer.Add( send, new EmailSendContext
                {
                    SenderAliasId = sender.AliasId,
                    ReviewerAliasId = approverAlias,
                    NeedsApproval = sender.AliasId != approverAlias,
                    ListGroupId = listId,
                    TemplateId = templateId,
                    TopicValueId = topicIds.TryGetValue( send.Topic ?? string.Empty, out var topicId ) ? topicId : null,
                    FromName = sender.FromName,
                    FromEmail = sender.FromEmail,
                    Guid = guids.NextGuid(),
                    ComponentGuid = guids.NextGuid(),
                    IpAddress = IpAddress
                }, clients );

                delivered += send.Recipients.Count( r => r.Status == EmailPlanner.Delivered || r.Status == EmailPlanner.Opened );
                opened += send.Recipients.Count( r => r.Status == EmailPlanner.Opened );
                unsubscribes += send.Recipients.Count( r => r.UnsubscribeLevel.HasValue );
            }

            writer.Finish();
            var personChanges = WritePersonChanges( planner, emailPeople );

            _db.Log.Write( $"  Bulk email: {writer.Communications:N0} sends to {writer.Recipients:N0} recipients, {delivered:N0} delivered and {opened:N0} opened ({( delivered == 0 ? 0 : 100.0 * opened / delivered ):N1}%), {writer.Interactions:N0} opens and clicks" );
            _db.Log.Write( $"  Unsubscribes: {unsubscribes:N0} ({members.Count( m => m.UnsubscribedAt.HasValue ):N0} left a list, {planner.BulkUnsubscribes.Count:N0} chose No mass emails); bounced emails: {planner.Bounces.Count( b => emailPeople.First( p => p.PersonId == b.Key ).HasBounced ):N0}; person changes: {personChanges:N0}" );
        }

        /// <summary>
        /// Everyone the planner may reach, with their email state when the window began, their reading habits, and
        /// for students, their parents.
        /// </summary>
        private List<EmailPerson> People( PeopleLoader people, List<ListCandidate> candidates, DateTime from, List<EmailClient> clients )
        {
            var openRates = _profile.Map( "communicationDetail.design.openRateByStatus" );
            var clientWeights = clients.Select( ( c, i ) => (i, c.Weight) ).ToDictionary( c => c.i.ToString( CultureInfo.InvariantCulture ), c => c.Weight );
            var byId = people.People.Values.GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var parents = new Dictionary<int, List<int>>();
            foreach ( var household in people.Households.Where( h => h.Cast.Kind != "business" ) )
            {
                var adults = household.Members.Where( m => m.IsAdult && m.CanAttend ).Select( m => m.PersonId ).ToList();
                foreach ( var child in household.Members.Where( m => !m.IsAdult ) )
                {
                    if ( !parents.TryGetValue( child.PersonId, out var list ) )
                    {
                        list = parents[child.PersonId] = new List<int>();
                    }

                    list.AddRange( adults.Where( a => !list.Contains( a ) ) );
                }
            }

            var result = new List<EmailPerson>();
            foreach ( var candidate in candidates )
            {
                var person = byId[candidate.PersonId];
                var cast = person.Cast;
                var random = new SeededRandom( $"{_seed}:email:reader:{cast.Guid}" );
                var rate = openRates.GetValueOrDefault( cast.ConnectionStatus ?? string.Empty, 0.6 ) * random.LogNormal( 0.45 );
                var client = int.Parse( random.Pick( clientWeights ), CultureInfo.InvariantCulture );
                var second = int.Parse( random.Pick( clientWeights ), CultureInfo.InvariantCulture );
                result.Add( new EmailPerson
                {
                    PersonId = person.PersonId,
                    AliasId = person.AliasId,
                    Guid = cast.Guid,
                    CampusKey = candidate.CampusKey,
                    IsCast = _castGuids.Contains( cast.Guid ),
                    IsAdult = candidate.IsAdult,
                    IsInvolved = candidate.IsInvolved,
                    HasEmail = !string.IsNullOrWhiteSpace( cast.Email ),
                    EmailActive = cast.EmailIsActive,
                    Preference = cast.EmailDoNotEmail ? 2 : 0,
                    Since = candidate.FirstSunday.HasValue && candidate.FirstSunday.Value > from ? candidate.FirstSunday.Value : from,
                    ParentIds = parents.GetValueOrDefault( person.PersonId ) ?? new List<int>(),
                    OpenRate = Math.Max( 0.02, Math.Min( 0.98, rate ) ),
                    ClickFactor = random.LogNormal( 0.5 ),
                    ClientIndex = client,
                    SecondClientIndex = second
                } );
            }

            return result;
        }

        private HashSet<Guid> _castGuids = new HashSet<Guid>();

        /// <summary>
        /// Tells the loader which people are the Cast. Only generated people bounce or unsubscribe at random.
        /// </summary>
        public BulkEmailLoader WithCast( IEnumerable<Guid> castPeople )
        {
            _castGuids = castPeople.ToHashSet();
            return this;
        }

        /// <summary>
        /// The dataset's campuses in configuration order, with their Sunday service times.
        /// </summary>
        private List<EmailCampus> Campuses( IReadOnlyDictionary<string, int> campusIds )
        {
            var slots = _source.Schedules.Slots.ToDictionary( s => s.Key, s => TimeSpan.Parse( s.Start, CultureInfo.InvariantCulture ) );
            return _source.Campuses.Campuses.Where( c => campusIds.ContainsKey( c.Key ) ).Select( c => new EmailCampus
            {
                Key = c.Key,
                Name = c.Name,
                ServiceTimes = MessageRenderer.ServiceTimes( c.Schedules.Where( s => s.Type == "weekend" ).Select( s => slots[s.Slot] ).Distinct() )
            } ).ToList();
        }

        /// <summary>
        /// Who sends each kind of email, by sender key.
        /// </summary>
        private static Dictionary<string, (int AliasId, string FromName, string FromEmail)> Senders( CommunicationsConfiguration configuration, PeopleLoader people )
        {
            (int, string, string) Sender( CommunicationSender sender ) => people.People.TryGetValue( sender.Sender, out var person )
                ? (person.AliasId, sender.FromName, sender.FromEmail)
                : throw new InvalidOperationException( $"configuration/communications.json names sender {sender.Sender}, who is not a loaded person." );

            return new Dictionary<string, (int, string, string)>
            {
                ["church"] = Sender( configuration.Senders.Church ),
                ["pastor"] = Sender( configuration.Senders.Pastor ),
                ["students"] = Sender( configuration.Senders.Students )
            };
        }

        /// <summary>
        /// A person's address, from the ranges reserved for documentation, so no real computer is named.
        /// </summary>
        private static string IpAddress( EmailPerson person )
        {
            var nets = new[] { "192.0.2", "198.51.100", "203.0.113" };
            return $"{nets[person.PersonId % 3]}.{( person.PersonId / 3 % 254 ) + 1}";
        }

        /*
            10/23/26 - CLAUDE

            A bounce and a "No mass emails" unsubscribe change the person, and Rock's person save hook
            writes the change to the person's History tab. A bounce comes from Mailgun's webhook, so no
            one is the author, and Rock's bounce note says when it happened. An unsubscribe is made by the
            person on Rock's email preference page, so they are the author.

            Reason: The person record and its history agree with the email the person got (decision 131).
        */

        /// <summary>
        /// Updates the people whose email bounced or who chose "No mass emails", with their History. Returns the number changed.
        /// </summary>
        private int WritePersonChanges( EmailPlanner planner, List<EmailPerson> people )
        {
            var byId = people.ToDictionary( p => p.PersonId );
            var updates = TableFactory.Create( "Id:int", "IsEmailActive:bool", "EmailNote:string", "EmailPreference:int", "ModifiedDateTime:date" );
            var history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "Verb:string", "ChangeType:string", "ValueName:string",
                "OldValue:string", "NewValue:string", "IsSensitive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int" );
            var categoryId = _db.RequireIdByGuid( "Category", DemographicHistoryCategoryGuid );
            var personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var names = _db.Query( "SELECT [Id], [NickName], [LastName] FROM [Person] WHERE [ForeignKey] = @ForeignKey", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["Id"], r => $"{r["NickName"]} {r["LastName"]}" );
            var random = new SeededRandom( $"{_seed}:email:person-changes" );

            void History( int personId, DateTime when, string valueName, string oldValue, string newValue, int? author )
            {
                history.Rows.Add( false, categoryId, personTypeId, personId, names.GetValueOrDefault( personId, string.Empty ), "MODIFY", "Property", valueName,
                    ( object ) oldValue ?? DBNull.Value, newValue, false, SeededRandom.StableGuid( $"{_seed}:email:person-change:{personId}:{valueName}" ), _foreignKey, when, when, ( object ) author ?? DBNull.Value );
            }

            foreach ( var personId in byId.Keys.OrderBy( id => id ) )
            {
                var person = byId[personId];
                var bounced = person.HasBounced ? planner.Bounces[personId] : ( DateTime? ) null;
                var unsubscribed = planner.BulkUnsubscribes.TryGetValue( personId, out var when ) ? when : ( DateTime? ) null;
                if ( bounced == null && unsubscribed == null )
                {
                    continue;
                }

                string note = null;
                if ( bounced.HasValue )
                {
                    var at = bounced.Value.AddSeconds( 30 );
                    note = $"Email experienced a Hard Bounce on {at.ToString( "M/d/yyyy", CultureInfo.InvariantCulture )}. The user's mailbox was unavailable or could not be found.";
                    History( personId, at, "Email Active", "True", "False", null );
                    History( personId, at, "Email Note", null, note, null );
                }

                if ( unsubscribed.HasValue )
                {
                    History( personId, unsubscribed.Value, "Email Preference", "Email Allowed", "No Mass Emails", person.AliasId );
                }

                var modified = new[] { bounced, unsubscribed }.Max().Value;
                updates.Rows.Add( personId, !bounced.HasValue && person.EmailActive, ( object ) note ?? DBNull.Value, unsubscribed.HasValue ? 1 : person.Preference, modified );
            }

            if ( updates.Rows.Count > 0 )
            {
                _db.Execute( "CREATE TABLE #EmailChange ( [Id] INT PRIMARY KEY, [IsEmailActive] BIT, [EmailNote] NVARCHAR(250), [EmailPreference] INT, [ModifiedDateTime] DATETIME )" );
                _db.BulkInsert( "#EmailChange", updates, keepIdentity: false );
                _db.Execute( @"
UPDATE [p]
SET [p].[IsEmailActive] = [c].[IsEmailActive], [p].[EmailNote] = [c].[EmailNote], [p].[EmailPreference] = [c].[EmailPreference], [p].[ModifiedDateTime] = [c].[ModifiedDateTime]
FROM [Person] AS [p]
INNER JOIN #EmailChange AS [c] ON [c].[Id] = [p].[Id];
DROP TABLE #EmailChange;" );
                _db.BulkInsert( "History", history, keepIdentity: false );
            }

            return updates.Rows.Count;
        }
    }
}
