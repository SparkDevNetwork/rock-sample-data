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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/23/26 - CLAUDE

        The rows are the ones Rock writes for its mobile app. The Site, its Homepage and Full layouts, and
        its homepage are what Rock's Mobile Application Detail block seeds for a new app. Each device is
        the row the app's launch packet writes, with the registration token it sends next. Each push goes
        through Rock Mobile Push: every device with notifications on gets it, a person with none fails with
        Rock's note, and each sent push has its "Push Notification" History line. An open is the one the
        app reports: the recipient Opened, and an "Opened" interaction on the communication.

        Every registration token starts with "sample-data-". Firebase rejects such a token, so no push sent
        from a sample database can ever reach a real phone.

        Reason: decisions 94 and 161, and nothing a load writes may ever send (decision 95).
    */

    /// <summary>
    /// Loads the church's app, the devices it runs on, and two years of pushes with their opens.
    /// </summary>
    internal sealed class PushLoader
    {
        private static readonly Guid MobileDeviceTypeGuid = new Guid( "5A8F264F-3BE6-4F15-912A-3CE93A98E8F6" );
        private static readonly Guid PushMediumGuid = new Guid( "3638C6DF-4FF3-4A52-B4B8-AFB754991597" );
        private static readonly Guid CommunicationChannelGuid = new Guid( "C88A187F-0343-4E7C-AF3F-79A8989DFA65" );
        private static readonly Guid CommunicationEntityTypeGuid = new Guid( "C4CCBD91-1264-48BF-BC33-92751C8948B5" );
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid CommunicationHistoryCategoryGuid = new Guid( "F291034B-7581-48F3-B522-E31B8534D529" );
        private const string RockMobilePushTransport = "Rock.Communication.Transport.RockMobilePush";
        private const string NoDevices = "No Personal Devices with Messaging Enabled";
        private const int PushType = 3;
        private const int ApprovedStatus = 3;
        private const int Delivered = 1;
        private const int Failed = 2;
        private const int Opened = 4;

        private const string HomepageXaml = @"<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
    xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
    xmlns:Rock=""clr-namespace:Rock.Mobile.Cms;assembly=Rock.Mobile"">
    <ScrollView VerticalScrollBarVisibility=""Never"">
        <Rock:Zone ZoneName=""Main"" />
    </ScrollView>
</ContentPage>";

        private const string FullXaml = @"<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
    xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
    xmlns:Rock=""clr-namespace:Rock.Mobile.Cms;assembly=Rock.Mobile"">
    <Rock:Zone ZoneName=""Main"" />
</ContentPage>";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public PushLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes the app, its devices, and its pushes.
        /// </summary>
        /// <param name="emails">The bulk email load, already run, for its people, calendar, and campuses.</param>
        public void Load( CommunicationsConfiguration configuration, PeopleLoader people, BulkEmailLoader emails )
        {
            var app = _source.ChurchApp;
            if ( app == null || !app.IsComplete )
            {
                _db.Log.Write( app == null ? "  Skipped the app (this package has no configuration/church-app.json)" : "  Skipped the app (this package's configuration/church-app.json is the stub from before push)" );
                return;
            }

            var siteId = AddSite( app );
            var staff = _source.Notes?.Staff.Select( s => s.Guid ).ToHashSet() ?? new HashSet<Guid>();
            var devices = AddDevices( app, people, emails.EmailPeople, siteId, staff );
            var sends = Plan( app, configuration, people, emails, devices );
            Write( app, sends, siteId, people.People[configuration.Senders.Texts.Sender].AliasId, people.People[configuration.Senders.Approver].AliasId, configuration );

            var recipients = sends.Sum( s => s.Recipients.Count );
            var delivered = sends.Sum( s => s.Recipients.Count( r => r.Status != Failed ) );
            var opened = sends.Sum( s => s.Recipients.Count( r => r.Status == Opened ) );
            _db.Log.Write( $"  App: {app.Site.Name}, {devices.Count:N0} devices ({devices.Count( d => d.IsIos ):N0} iPhone, {devices.Count( d => d.NotificationsOn ):N0} with notifications on)" );
            _db.Log.Write( $"  Push: {sends.Count:N0} pushes to {recipients:N0} recipients, {delivered:N0} delivered and {opened:N0} opened" );
        }

        #region App

        /// <summary>
        /// Adds the Site, the two layouts, and the homepage Rock seeds for a new mobile application. Returns the site Id.
        /// </summary>
        private int AddSite( ChurchAppConfiguration app )
        {
            var site = app.Site;
            var launched = _dates.Anchor.AddYears( -site.LaunchedYearsAgo );
            var deployed = _dates.Anchor.AddDays( -18 ).Date.AddHours( 14 );
            var settings = new
            {
                LastDeploymentDate = deployed,
                LastDeploymentVersionId = 0,
                ShellType = 2,
                TabLocation = 1,
                EnableNotificationsAutomatically = true,
                IsDeepLinkingEnabled = false,
                BundleIdentifier = site.BundleIdentifier,
                PackageName = site.PackageName,
                DeepLinkDomains = site.DeepLinkDomains,
                IsPackageCompressionEnabled = true,
                DownhillSettings = new { Platform = 1 }
            };

            var siteId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Site] ( [IsSystem], [Name], [Description], [IsActive], [SiteType], [AdditionalSettings], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @Name, @Description, 1, 1, @Settings, @Guid, @Launched, @Deployed, @ForeignKey )",
                P( "@Name", site.Name ), P( "@Description", site.Description ), P( "@Settings", JsonSerializer.Serialize( settings ) ),
                P( "@Guid", site.Guid ), P( "@Launched", launched ), P( "@Deployed", deployed ), P( "@ForeignKey", _foreignKey ) ) );

            var layoutIds = new Dictionary<string, int>();
            foreach ( var layout in app.Layouts )
            {
                var xaml = layout.Name == "Full" ? FullXaml : HomepageXaml;
                layoutIds[layout.Name] = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Layout] ( [IsSystem], [SiteId], [FileName], [Name], [Description], [LayoutMobilePhone], [LayoutMobileTablet], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @SiteId, @FileName, @Name, '', @Xaml, @Xaml, @Guid, @Launched, @Launched, @ForeignKey )",
                    P( "@SiteId", siteId ), P( "@FileName", layout.FileName ), P( "@Name", layout.Name ), P( "@Xaml", xaml ), P( "@Guid", layout.Guid ),
                    P( "@Launched", launched ), P( "@ForeignKey", _foreignKey ) ) );
            }

            // The Page defaults are Rock's Page model defaults.
            var pageName = $"{site.Name} Homepage";
            var pageId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Page] ( [InternalName], [PageTitle], [BrowserTitle], [Description], [IsSystem], [LayoutId], [SiteId], [RequiresEncryption], [EnableViewState], [PageDisplayTitle],
    [PageDisplayBreadCrumb], [PageDisplayIcon], [PageDisplayDescription], [DisplayInNavWhen], [MenuDisplayDescription], [MenuDisplayIcon], [MenuDisplayChildPages],
    [BreadCrumbDisplayName], [BreadCrumbDisplayIcon], [Order], [OutputCacheDuration], [IncludeAdminFooter], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Name, @Name, '', 0, @LayoutId, @SiteId, 0, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, @Guid, @Launched, @Launched, @ForeignKey )",
                P( "@Name", pageName ), P( "@LayoutId", layoutIds["Homepage"] ), P( "@SiteId", siteId ), P( "@Guid", app.Homepage.Guid ),
                P( "@Launched", launched ), P( "@ForeignKey", _foreignKey ) ) );
            _db.Execute( "UPDATE [Site] SET [DefaultPageId] = @PageId WHERE [Id] = @SiteId", P( "@PageId", pageId ), P( "@SiteId", siteId ) );
            return siteId;
        }

        #endregion App

        #region Devices

        /// <summary>
        /// About a quarter of active adults have the app, and every Cast staff member does. Each device was first seen
        /// on a day between the app's launch, or the person's first Sunday, and install day.
        /// </summary>
        private List<AppDevice> AddDevices( ChurchAppConfiguration app, PeopleLoader people, List<EmailPerson> candidates, int siteId, HashSet<Guid> staff )
        {
            var design = app.Devices;
            var launched = _dates.Anchor.AddYears( -app.Site.LaunchedYearsAgo );
            var typeId = _db.RequireIdByGuid( "DefinedValue", MobileDeviceTypeGuid );
            var iosId = _db.RequireIdByGuid( "DefinedValue", design.Platforms["ios"] );
            var androidId = _db.RequireIdByGuid( "DefinedValue", design.Platforms["android"] );
            var names = people.People.Values.GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First().Cast.NickName ?? g.First().Cast.FirstName );

            var devices = new List<AppDevice>();
            foreach ( var person in candidates.Where( p => p.IsAdult ).OrderBy( p => p.PersonId ) )
            {
                var random = new SeededRandom( $"{_seed}:app:device:{person.Guid}" );
                var hasApp = staff.Contains( person.Guid ) || ( person.IsInvolved && random.Chance( design.PctOfActiveAdults.Value / 100 ) );
                if ( !hasApp )
                {
                    continue;
                }

                var start = person.Since > launched ? person.Since : launched;
                var days = Math.Max( 1, ( int ) ( _dates.Anchor - start ).TotalDays );
                var first = start.Date.AddDays( Math.Min( days - 1, ( int ) ( days * Math.Pow( random.Between( 0.0, 1.0 ), 1.6 ) ) ) ).AddHours( random.Between( 8, 21 ) ).AddMinutes( random.Between( 0, 59 ) );
                var lastSeen = random.Chance( design.PctSeenInLast30Days.Value / 100 ) || first > _dates.Anchor.AddDays( -30 )
                    ? _dates.Anchor.AddDays( -random.Between( 0.0, 30.0 ) )
                    : first.AddDays( random.Between( 0.0, Math.Max( 0.0, ( _dates.Anchor.AddDays( -30 ) - first ).TotalDays ) ) );
                if ( lastSeen < first )
                {
                    lastSeen = first;
                }

                var isIos = random.Chance( design.PctIos.Value / 100 );
                var nickName = names.GetValueOrDefault( person.PersonId, "My" );
                string manufacturer, model, name, identifier;
                if ( isIos )
                {
                    manufacturer = "Apple";
                    model = random.Pick( design.IosModels );
                    name = $"{nickName}'s iPhone";
                    identifier = SeededRandom.StableGuid( $"{_seed}:app:device-id:{person.Guid}" ).ToString().ToUpperInvariant();
                }
                else
                {
                    var android = random.Pick( design.AndroidModels );
                    manufacturer = android.Manufacturer;
                    model = android.Model;
                    name = android.Model;
                    identifier = SeededRandom.StableGuid( $"{_seed}:app:device-id:{person.Guid}" ).ToString( "N" ).Substring( 0, 16 );
                }

                devices.Add( new AppDevice
                {
                    Person = person,
                    IsIos = isIos,
                    NotificationsOn = random.Chance( design.PctNotificationsOn.Value / 100 ),
                    FirstSeen = first,
                    LastSeen = lastSeen,
                    Manufacturer = manufacturer,
                    Model = model,
                    Name = name,
                    Identifier = identifier,
                    Token = Token( person.Guid ),
                    Guid = SeededRandom.StableGuid( $"{_seed}:app:device:{person.Guid}" )
                } );
            }

            var nextId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [PersonalDevice]" ) );
            var rows = TableFactory.Create( "Id:int", "PersonAliasId:int", "DeviceRegistrationId:string", "PersonalDeviceTypeValueId:int", "PlatformValueId:int", "DeviceUniqueIdentifier:string",
                "NotificationsEnabled:bool", "IsActive:bool", "SiteId:int", "Manufacturer:string", "Model:string", "Name:string", "LastSeenDateTime:date", "Guid:guid",
                "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            foreach ( var device in devices )
            {
                device.Id = nextId++;
                rows.Rows.Add( device.Id, device.Person.AliasId, device.Token, typeId, device.IsIos ? iosId : androidId, device.Identifier, device.NotificationsOn, true, siteId,
                    device.Manufacturer, device.Model, device.Name, device.LastSeen, device.Guid, device.FirstSeen, device.LastSeen, _foreignKey );
            }

            _db.BulkInsert( "PersonalDevice", rows, keepIdentity: true );
            return devices;
        }

        /// <summary>
        /// A registration token shaped like a Firebase one, but with a prefix Firebase never issues.
        /// </summary>
        private string Token( Guid person )
        {
            using ( var sha = SHA256.Create() )
            {
                var bytes = sha.ComputeHash( Encoding.UTF8.GetBytes( $"{_seed}:app:token:{person}" ) );
                var body = Convert.ToBase64String( bytes.Concat( sha.ComputeHash( bytes ) ).Concat( sha.ComputeHash( Enumerable.Reverse( bytes ).ToArray() ) ).ToArray() )
                    .Replace( '+', '-' ).Replace( '/', '_' ).TrimEnd( '=' );
                return $"sample-data-{body.Substring( 0, 11 )}:APA91b{body.Substring( 11 )}";
            }
        }

        #endregion Devices

        #region Pushes

        /// <summary>
        /// Each push in the library, once per campus and occasion, to the campus's active adults who had the app by then.
        /// </summary>
        private List<PlannedPush> Plan( ChurchAppConfiguration app, CommunicationsConfiguration configuration, PeopleLoader people, BulkEmailLoader emails, List<AppDevice> devices )
        {
            var from = _dates.Anchor.AddYears( -2 );
            var library = _source.ReadTextLibrary().Texts.Where( t => t.Medium == "push" ).ToList();
            var byCampus = devices.Where( d => d.Person.IsInvolved ).ToLookup( d => d.Person.CampusKey );
            var sends = new List<PlannedPush>();
            foreach ( var campus in emails.CampusList )
            {
                foreach ( var push in library )
                {
                    foreach ( var occasion in emails.Calendar.Dates( push.Occasion ) )
                    {
                        var day = occasion.AddDays( -push.DaysBefore );
                        if ( day < from.Date || day >= _dates.Anchor.Date )
                        {
                            continue;
                        }

                        var random = new SeededRandom( $"{_seed}:push:{push.Key}:{campus.Key}:{occasion:yyyyMMdd}" );
                        var sent = day.AddHours( push.Hour ).AddMinutes( random.Between( 0, 20 ) );
                        if ( sent >= _dates.Anchor )
                        {
                            continue;
                        }

                        var series = emails.Calendar.SeriesOn( occasion.AddDays( occasion.DayOfWeek == DayOfWeek.Sunday ? 0 : 7 - ( int ) occasion.DayOfWeek ) );
                        var tokens = MessageRenderer.Tokens( occasion, day, campus.Name, campus.Key, campus.ServiceTimes, series );
                        var send = new PlannedPush
                        {
                            Key = $"{push.Key}:{campus.Key}:{occasion:yyyyMMdd}",
                            Name = $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase( push.Key.Replace( '-', ' ' ) )} ({campus.Name})",
                            Title = MessageRenderer.Fill( push.Title ?? string.Empty, tokens ),
                            Message = MessageRenderer.Fill( push.Text, tokens ),
                            Topic = push.Topic,
                            Created = sent.AddMinutes( -random.Between( 10, 90 ) ),
                            Sent = sent
                        };
                        PlanRecipients( app, send, byCampus[campus.Key] );
                        sends.Add( send );
                    }
                }
            }

            return sends.OrderBy( s => s.Sent ).ThenBy( s => s.Key, StringComparer.Ordinal ).ToList();
        }

        private void PlanRecipients( ChurchAppConfiguration app, PlannedPush send, IEnumerable<AppDevice> devices )
        {
            var position = 0;
            foreach ( var device in devices.Where( d => d.FirstSeen < send.Sent ).OrderBy( d => d.Person.PersonId ) )
            {
                var seed = $"{_seed}:push:{send.Key}:{device.Person.Guid}";
                var random = new SeededRandom( seed );
                var recipient = new PlannedPushRecipient { Device = device, Guid = SeededRandom.StableGuid( $"{seed}:recipient" ), HistoryGuid = SeededRandom.StableGuid( $"{seed}:history" ) };
                send.Recipients.Add( recipient );
                if ( !device.NotificationsOn )
                {
                    recipient.Status = Failed;
                    recipient.StatusNote = NoDevices;
                    continue;
                }

                recipient.Status = Delivered;
                recipient.SendDateTime = send.Sent.AddSeconds( 0.05 * position++ );
                recipient.UniqueMessageId = $"projects/rock-solid-church-app/messages/0:{SeededRandom.StableGuid( $"{seed}:message" ):N}";
                if ( random.Chance( app.Push.PctOpened / 100 ) )
                {
                    var hours = random.Chance( app.Push.PctOpenedWithinHour / 100 ) ? random.Between( 0.01, 1.0 ) : random.Between( 1.0, 30.0 );
                    var opened = recipient.SendDateTime.Value.AddHours( hours );
                    if ( opened < _dates.Anchor )
                    {
                        recipient.Status = Opened;
                        recipient.OpenedDateTime = opened;
                        recipient.InteractionGuid = SeededRandom.StableGuid( $"{seed}:open" );
                        recipient.SessionGuid = SeededRandom.StableGuid( $"{seed}:open-session" );
                    }
                }
            }
        }

        #endregion Pushes

        #region Write

        private void Write( ChurchAppConfiguration app, List<PlannedPush> sends, int siteId, int senderAliasId, int approverAliasId, CommunicationsConfiguration configuration )
        {
            var mediumId = _db.RequireIdByGuid( "EntityType", PushMediumGuid );
            var channelId = _db.RequireIdByGuid( "InteractionChannel", CommunicationChannelGuid );
            var communicationTypeId = _db.RequireIdByGuid( "EntityType", CommunicationEntityTypeGuid );
            var personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var historyCategoryId = _db.RequireIdByGuid( "Category", CommunicationHistoryCategoryGuid );
            var topicIds = configuration.Topics.ToDictionary( t => t.Key, t => _db.RequireIdByGuid( "DefinedValue", t.Value ) );
            var deviceTypes = new Dictionary<bool, int> { [true] = DeviceType( "iOS", app.Site.Name ), [false] = DeviceType( "Android", app.Site.Name ) };
            var pushData = JsonSerializer.Serialize( new { MobilePageId = ( int? ) null, MobilePageQueryString = ( object ) null, MobileApplicationId = siteId, Url = ( string ) null, ApplicationBadgeCount = ( int? ) null, CustomData = ( object ) null } );

            var nextCommunicationId = NextId( "Communication" );
            var nextComponentId = NextId( "InteractionComponent" );
            var nextRecipientId = NextId( "CommunicationRecipient" );
            var nextSessionId = NextId( "InteractionSession" );

            var communications = TableFactory.Create( "Id:int", "Name:string", "CommunicationType:int", "Status:int", "IsBulkCommunication:bool", "SenderPersonAliasId:int", "ReviewerPersonAliasId:int",
                "ReviewedDateTime:date", "SegmentCriteria:int", "PushTitle:string", "PushMessage:string", "PushData:string", "PushOpenAction:int", "SendDateTime:date", "SendDateKey:int",
                "ExcludeDuplicateRecipientAddress:bool", "CommunicationTopicValueId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var components = TableFactory.Create( "Id:int", "Name:string", "EntityId:int", "InteractionChannelId:int", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );
            var recipients = TableFactory.Create( "Id:int", "CommunicationId:int", "PersonAliasId:int", "Status:int", "StatusNote:string", "MediumEntityTypeId:int", "TransportEntityTypeName:string",
                "UniqueMessageId:string", "SendDateTime:date", "FirstSendAttemptDateTime:date", "DeliveredDateTime:date", "OpenedDateTime:date", "OpenedClient:string",
                "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var sessions = TableFactory.Create( "Id:int", "DeviceTypeId:int", "IpAddress:string", "SessionStartDateKey:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var interactions = TableFactory.Create( "InteractionDateTime:date", "Operation:string", "InteractionComponentId:int", "EntityId:int", "PersonAliasId:int", "InteractionSessionId:int",
                "PersonalDeviceId:int", "InteractionSummary:string", "InteractionData:string", "InteractionDateKey:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
                "Verb:string", "ChangeType:string", "ValueName:string", "IsSensitive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int" );

            foreach ( var send in sends )
            {
                var id = nextCommunicationId++;
                var reviewed = senderAliasId == approverAliasId ? send.Created : send.Created.AddMinutes( 15 );
                communications.Rows.Add( id, send.Name, PushType, ApprovedStatus, true, senderAliasId, approverAliasId, reviewed, 0, send.Title, send.Message, pushData, 0,
                    send.Sent, DateKey( send.Sent ), false, topicIds.TryGetValue( send.Topic ?? string.Empty, out var topic ) ? topic : ( object ) DBNull.Value,
                    SeededRandom.StableGuid( $"{_seed}:push:{send.Key}:communication" ), send.Created, send.Sent, senderAliasId, senderAliasId, _foreignKey );

                var componentId = ( int? ) null;
                foreach ( var recipient in send.Recipients )
                {
                    var recipientId = nextRecipientId++;
                    var person = recipient.Device.Person;
                    var client = recipient.Device.IsIos ? "iOS" : "Android";
                    recipients.Rows.Add( recipientId, id, person.AliasId, recipient.Status, recipient.StatusNote ?? ( object ) DBNull.Value, mediumId, RockMobilePushTransport,
                        recipient.UniqueMessageId ?? ( object ) DBNull.Value, recipient.SendDateTime ?? ( object ) DBNull.Value, send.Sent, recipient.SendDateTime ?? ( object ) DBNull.Value,
                        recipient.OpenedDateTime ?? ( object ) DBNull.Value, recipient.OpenedDateTime.HasValue ? $"{client} {app.Site.Name} (Mobile)" : ( object ) DBNull.Value,
                        recipient.Guid, send.Sent, recipient.OpenedDateTime ?? recipient.SendDateTime ?? send.Sent, _foreignKey );

                    // Rock Mobile Push writes a "Push Notification" line on the person after each push it sends, by the sender.
                    if ( recipient.SendDateTime.HasValue )
                    {
                        history.Rows.Add( false, historyCategoryId, personTypeId, person.PersonId, send.Message.Length > 200 ? send.Message.Substring( 0, 200 ) : send.Message, communicationTypeId, id,
                            "SENT", "Record", "Push Notification", false, recipient.HistoryGuid, _foreignKey, recipient.SendDateTime.Value, recipient.SendDateTime.Value, senderAliasId );
                    }

                    if ( recipient.OpenedDateTime.HasValue )
                    {
                        if ( !componentId.HasValue )
                        {
                            componentId = nextComponentId++;
                            components.Rows.Add( componentId.Value, send.Title, id, channelId, send.Sent, send.Sent, SeededRandom.StableGuid( $"{_seed}:push:{send.Key}:component" ), _foreignKey );
                        }

                        var opened = recipient.OpenedDateTime.Value;
                        var sessionId = nextSessionId++;
                        sessions.Rows.Add( sessionId, deviceTypes[recipient.Device.IsIos], BulkEmailLoader.IpAddress( person ), DateKey( opened ), recipient.SessionGuid, opened, opened, _foreignKey );
                        interactions.Rows.Add( opened, "Opened", componentId.Value, recipientId, person.AliasId, sessionId, recipient.Device.Id, string.Empty, string.Empty, DateKey( opened ),
                            recipient.InteractionGuid, opened, opened, _foreignKey );
                    }
                }
            }

            _db.BulkInsert( "Communication", communications, keepIdentity: true );
            _db.BulkInsert( "InteractionComponent", components, keepIdentity: true );
            _db.BulkInsert( "CommunicationRecipient", recipients, keepIdentity: true );
            _db.BulkInsert( "InteractionSession", sessions, keepIdentity: true );
            _db.BulkInsert( "Interaction", interactions, keepIdentity: false );
            _db.BulkInsert( "History", history, keepIdentity: false );
        }

        /// <summary>
        /// Adds the interaction device type Rock names for the app on a platform, the first time it sees it, and returns its Id.
        /// </summary>
        private int DeviceType( string operatingSystem, string application )
        {
            return Convert.ToInt32( _db.Scalar( @"
INSERT INTO [InteractionDeviceType] ( [Name], [DeviceTypeData], [ClientType], [OperatingSystem], [Application], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Data, 'Mobile', @Os, @Application, NEWID(), GETDATE(), GETDATE(), @ForeignKey )",
                P( "@Name", $"{operatingSystem} - {application}" ), P( "@Data", $"{operatingSystem} {application}" ), P( "@Os", operatingSystem ), P( "@Application", application ), P( "@ForeignKey", _foreignKey ) ) );
        }

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );

        private static int DateKey( DateTime date ) => ( date.Year * 10000 ) + ( date.Month * 100 ) + date.Day;

        #endregion Write

        private sealed class AppDevice
        {
            public int Id { get; set; }

            public EmailPerson Person { get; set; }

            public bool IsIos { get; set; }

            public bool NotificationsOn { get; set; }

            public DateTime FirstSeen { get; set; }

            public DateTime LastSeen { get; set; }

            public string Manufacturer { get; set; }

            public string Model { get; set; }

            public string Name { get; set; }

            public string Identifier { get; set; }

            public string Token { get; set; }

            public Guid Guid { get; set; }
        }

        private sealed class PlannedPush
        {
            public string Key { get; set; }

            public string Name { get; set; }

            public string Title { get; set; }

            public string Message { get; set; }

            public string Topic { get; set; }

            public DateTime Created { get; set; }

            public DateTime Sent { get; set; }

            public List<PlannedPushRecipient> Recipients { get; } = new List<PlannedPushRecipient>();
        }

        private sealed class PlannedPushRecipient
        {
            public AppDevice Device { get; set; }

            public Guid Guid { get; set; }

            public Guid HistoryGuid { get; set; }

            public int Status { get; set; }

            public string StatusNote { get; set; }

            public DateTime? SendDateTime { get; set; }

            public string UniqueMessageId { get; set; }

            public DateTime? OpenedDateTime { get; set; }

            public Guid InteractionGuid { get; set; }

            public Guid SessionGuid { get; set; }
        }
    }
}
