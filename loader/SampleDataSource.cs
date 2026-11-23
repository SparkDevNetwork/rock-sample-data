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

using System.Text.Json;

using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;
using RockSampleData.Loader.Packages;

namespace RockSampleData.Loader
{
    /// <summary>
    /// Reads the data files a load needs: from a package when loading, or from the
    /// rock-sample-data repository when the builder makes a package. Paths are the same in both.
    /// </summary>
    internal sealed class SampleDataSource
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// Where the files come from.
        /// </summary>
        public IPackageFiles Files { get; }

        public ScheduleConfiguration Schedules { get; }

        public CampusConfiguration Campuses { get; }

        public AccountConfiguration Accounts { get; }

        public CheckInConfiguration CheckIn { get; }

        public ConnectionStatusConfiguration ConnectionStatuses { get; }

        public SmallGroupRules SmallGroups { get; }

        public GivingAlertTypeConfiguration GivingAlertTypes { get; }

        public AchievementConfiguration Achievements { get; }

        public ServingConfiguration Serving { get; }

        public ConnectionsConfiguration Connections { get; }

        /// <summary>
        /// The note types and note authors, or null in a package built before notes existed.
        /// </summary>
        public NotesConfiguration Notes { get; }

        /// <summary>
        /// How Cast role names map to Rock's security roles, or null in a package built before roles loaded.
        /// </summary>
        public SecurityRoleConfiguration SecurityRoles { get; }

        /// <summary>
        /// The step programs, or null in a package built before steps loaded.
        /// </summary>
        public StepsConfiguration Steps { get; }

        /// <summary>
        /// The building campaign, or null in a package built before the campaign loaded.
        /// </summary>
        public CampaignConfiguration Campaign { get; }

        /// <summary>
        /// The phone numbers, senders, template, and communication lists, or null in a package built before communications loaded.
        /// </summary>
        public CommunicationsConfiguration Communications { get; }

        /// <summary>
        /// The church's mobile app, its devices, and its pushes, or null in a package built before push loaded.
        /// </summary>
        public ChurchAppConfiguration ChurchApp { get; }

        /// <summary>
        /// The church's calendar, or null for a package built before calendar events existed.
        /// </summary>
        public CalendarConfiguration Calendar { get; }

        /// <summary>
        /// Who among generated adults has a login, or null in a package built before logins loaded.
        /// </summary>
        public LoginsConfiguration Logins { get; }

        /// <summary>
        /// The event registrations, or null in a package built before registrations loaded.
        /// </summary>
        public RegistrationsConfiguration Registrations { get; }

        /// <summary>
        /// The categories the church adds, or null in a package built before they existed.
        /// </summary>
        public CategoryConfiguration Categories { get; }

        /// <summary>
        /// The church's data views and group syncs, or null in a package built before group sync loaded.
        /// </summary>
        public GroupSyncConfiguration GroupSyncs { get; }

        /// <summary>
        /// The church's layered data views, or null in a package built before they loaded.
        /// </summary>
        public ReportingConfiguration Reporting { get; }

        /// <summary>
        /// Every data view in every configuration file, by key: the reporting views, then the group sync and registration views.
        /// </summary>
        public Dictionary<string, DataViewDefinition> DataViews()
        {
            return ( Reporting?.DataViews ?? new List<DataViewDefinition>() )
                .Concat( GroupSyncs?.DataViews ?? new List<DataViewDefinition>() )
                .Concat( Registrations?.DataViews ?? new List<DataViewDefinition>() )
                .ToDictionary( v => v.Key );
        }

        /// <summary>
        /// The church's Communication Flows, or null in a package built before flows were configured, which loads the
        /// After Starting Point flow as it was built then.
        /// </summary>
        public FlowsConfiguration Flows { get; }

        /// <summary>
        /// The area codes of generated phone numbers, or null in a package built before every generated number was its
        /// own (decision 259), whose generated numbers are drawn as they were then.
        /// </summary>
        public PhoneConfiguration Phones { get; }

        /// <summary>
        /// Reads every configuration file.
        /// </summary>
        public SampleDataSource( IPackageFiles files )
        {
            Files = files;
            Schedules = Read<ScheduleConfiguration>( "configuration/schedules.json" );
            Campuses = Read<CampusConfiguration>( "configuration/campuses.json" );
            Accounts = Read<AccountConfiguration>( "configuration/accounts.json" );
            CheckIn = Read<CheckInConfiguration>( "configuration/checkin.json" );
            ConnectionStatuses = Read<ConnectionStatusConfiguration>( "configuration/connection-statuses.json" );
            SmallGroups = Read<SmallGroupRules>( "configuration/small-groups.json" );
            GivingAlertTypes = Read<GivingAlertTypeConfiguration>( "configuration/giving-alert-types.json" );
            Achievements = Read<AchievementConfiguration>( "configuration/achievements.json" );
            Serving = Read<ServingConfiguration>( "configuration/serving.json" );
            Connections = Read<ConnectionsConfiguration>( "configuration/connections.json" );
            Notes = Files.Exists( NotesConfigurationPath ) ? Read<NotesConfiguration>( NotesConfigurationPath ) : null;
            SecurityRoles = Files.Exists( SecurityRolesPath ) ? Read<SecurityRoleConfiguration>( SecurityRolesPath ) : null;
            Steps = Files.Exists( StepsConfigurationPath ) ? Read<StepsConfiguration>( StepsConfigurationPath ) : null;
            Campaign = Files.Exists( CampaignConfigurationPath ) ? Read<CampaignConfiguration>( CampaignConfigurationPath ) : null;
            Communications = Files.Exists( CommunicationsConfigurationPath ) ? Read<CommunicationsConfiguration>( CommunicationsConfigurationPath ) : null;
            ChurchApp = Files.Exists( ChurchAppConfigurationPath ) ? Read<ChurchAppConfiguration>( ChurchAppConfigurationPath ) : null;
            Calendar = Files.Exists( CalendarConfigurationPath ) ? Read<CalendarConfiguration>( CalendarConfigurationPath ) : null;
            Logins = Files.Exists( LoginsConfigurationPath ) ? Read<LoginsConfiguration>( LoginsConfigurationPath ) : null;
            Staff = Files.Exists( StaffConfigurationPath ) ? Read<StaffConfiguration>( StaffConfigurationPath ) : null;
            Registrations = Files.Exists( RegistrationsConfigurationPath ) ? Read<RegistrationsConfiguration>( RegistrationsConfigurationPath ) : null;
            Categories = Files.Exists( CategoriesConfigurationPath ) ? Read<CategoryConfiguration>( CategoriesConfigurationPath ) : null;
            GroupSyncs = Files.Exists( GroupSyncsConfigurationPath ) ? Read<GroupSyncConfiguration>( GroupSyncsConfigurationPath ) : null;
            Reporting = Files.Exists( ReportingConfigurationPath ) ? Read<ReportingConfiguration>( ReportingConfigurationPath ) : null;
            Flows = Files.Exists( FlowsConfigurationPath ) ? Read<FlowsConfiguration>( FlowsConfigurationPath ) : null;
            PersonAttributes = Files.Exists( PersonAttributesConfigurationPath ) ? Read<PersonAttributesConfiguration>( PersonAttributesConfigurationPath ) : null;
            Phones = Files.Exists( PhonesConfigurationPath ) ? Read<PhoneConfiguration>( PhonesConfigurationPath ) : null;
            Assessments = Files.Exists( AssessmentsConfigurationPath ) ? Read<AssessmentsConfiguration>( AssessmentsConfigurationPath ) : null;
            Refunds = Files.Exists( RefundsConfigurationPath ) ? Read<RefundsConfiguration>( RefundsConfigurationPath ) : null;
            InKindGifts = Files.Exists( InKindGiftsConfigurationPath ) ? Read<InKindGiftsConfiguration>( InKindGiftsConfigurationPath ) : null;
            AdaptiveMessages = Files.Exists( AdaptiveMessagesConfigurationPath ) ? Read<AdaptiveMessagesConfiguration>( AdaptiveMessagesConfigurationPath ) : null;
            Signals = Files.Exists( SignalsConfigurationPath ) ? Read<SignalsConfiguration>( SignalsConfigurationPath ) : null;
            PersonalLinks = Files.Exists( PersonalLinksConfigurationPath ) ? Read<PersonalLinksConfiguration>( PersonalLinksConfigurationPath ) : null;
            Tags = Files.Exists( TagsConfigurationPath ) ? Read<TagsConfiguration>( TagsConfigurationPath ) : null;
            Reminders = Files.Exists( RemindersConfigurationPath ) ? Read<RemindersConfiguration>( RemindersConfigurationPath ) : null;
            StepWorkflows = Files.Exists( StepWorkflowsConfigurationPath ) ? Read<StepWorkflowsConfiguration>( StepWorkflowsConfigurationPath ) : null;
            PeerNetwork = Files.Exists( PeerNetworkConfigurationPath ) ? Read<PeerNetworkConfiguration>( PeerNetworkConfigurationPath ) : null;
            BackgroundChecks = Files.Exists( BackgroundChecksConfigurationPath ) ? Read<BackgroundChecksConfiguration>( BackgroundChecksConfigurationPath ) : null;
            Benevolence = Files.Exists( BenevolenceConfigurationPath ) ? Read<BenevolenceConfiguration>( BenevolenceConfigurationPath ) : null;
            DataIntegrity = Files.Exists( DataIntegrityConfigurationPath ) ? Read<DataIntegrityConfiguration>( DataIntegrityConfigurationPath ) : null;
        }

        /// <summary>
        /// The bookmarks in Rock's bookmark menu, or null in a package built before personal links.
        /// </summary>
        public PersonalLinksConfiguration PersonalLinks { get; }

        public const string PersonalLinksConfigurationPath = "configuration/personal-links.json";

        /// <summary>
        /// The church's person signal types and the signals on people, or null in a package built before signals.
        /// </summary>
        public SignalsConfiguration Signals { get; }

        public const string SignalsConfigurationPath = "configuration/signals.json";

        /// <summary>
        /// The workflows completed steps start, or null in a package built before step workflows loaded.
        /// </summary>
        public StepWorkflowsConfiguration StepWorkflows { get; }

        public const string StepWorkflowsConfigurationPath = "configuration/step-workflows.json";

        /// <summary>
        /// The reminders staff and leaders set, or null in a package built before reminders loaded.
        /// </summary>
        public RemindersConfiguration Reminders { get; }

        public const string RemindersConfigurationPath = "configuration/reminders.json";

        /// <summary>
        /// The group types with Rock's Peer Network on, and who follows whom, or null in a package built before peer
        /// networks loaded, which leaves Peer Network off as Rock ships it.
        /// </summary>
        public PeerNetworkConfiguration PeerNetwork { get; }

        public const string PeerNetworkConfigurationPath = "configuration/peer-network.json";

        /// <summary>
        /// The person attributes the sample data fills and the Date Engaged attribute it adds, or null in a package built
        /// before them, which fills only the Cast's allergies.
        /// </summary>
        public PersonAttributesConfiguration PersonAttributes { get; }

        public const string PersonAttributesConfigurationPath = "configuration/person-attributes.json";

        /// <summary>
        /// Who takes Rock's assessments, or null in a package built before assessments.
        /// </summary>
        public AssessmentsConfiguration Assessments { get; }

        public const string AssessmentsConfigurationPath = "configuration/assessments.json";

        /// <summary>
        /// The refunds staff make on generated households' gifts, or null in a package built before refunds, which has none.
        /// </summary>
        public RefundsConfiguration Refunds { get; }

        public const string RefundsConfigurationPath = "configuration/refunds.json";

        /// <summary>
        /// The kinds of in-kind (non-cash) gifts, or null in a package built before them, whose non-cash gifts have no
        /// asset type or summary.
        /// </summary>
        public InKindGiftsConfiguration InKindGifts { get; }

        public const string InKindGiftsConfigurationPath = "configuration/in-kind-gifts.json";
        /// Who has a background check and the requirements that read it, or null in a package built before background
        /// check records, which loads none.
        /// </summary>
        public BackgroundChecksConfiguration BackgroundChecks { get; }

        public const string BackgroundChecksConfigurationPath = "configuration/background-checks.json";

        /// <summary>
        /// The benevolence types, the case worker, and who asks for help, or null in a package built before benevolence,
        /// which loads none.
        /// </summary>
        public BenevolenceConfiguration Benevolence { get; }

        public const string BenevolenceConfigurationPath = "configuration/benevolence.json";

        /// <summary>
        /// The designed problems on Rock's Data Integrity pages and the Data Integrity Settings, or null in a package built
        /// before them, which loads none.
        /// </summary>
        public DataIntegrityConfiguration DataIntegrity { get; }

        public const string DataIntegrityConfigurationPath = "configuration/data-integrity.json";

        /// <summary>
        /// The church's personalization segments and adaptive messages, or null in a package built before they loaded.
        /// </summary>
        public AdaptiveMessagesConfiguration AdaptiveMessages { get; }

        /// <summary>
        /// The church's tags on people, or null in a package built before tags loaded.
        /// </summary>
        public TagsConfiguration Tags { get; }

        public const string TagsConfigurationPath = "configuration/tags.json";

        /// <summary>
        /// Reads each adaptation's text, from content/adaptive-messages/messages.json.
        /// </summary>
        public AdaptiveMessageText ReadAdaptiveMessageText() => Read<AdaptiveMessageText>( AdaptiveMessageTextPath );

        /// <summary>
        /// The church staff and each campus's named campus pastor, or null in a package built before staff were
        /// named, whose campus pastors come from generated households.
        /// </summary>
        public StaffConfiguration Staff { get; }

        /// <summary>
        /// Reads photos/staff/manifest.json, the staff photos. A package built before staff photos existed has none.
        /// </summary>
        public StaffPhotoManifest ReadStaffPhotoManifest()
        {
            return Files.Exists( StaffPhotoManifestPath ) ? Read<StaffPhotoManifest>( StaffPhotoManifestPath ) : new StaffPhotoManifest();
        }

        public const string StaffPhotoManifestPath = "photos/staff/manifest.json";

        public const string StaffConfigurationPath = "configuration/staff.json";

        /// <summary>
        /// Reads from a folder, such as the rock-sample-data repository.
        /// </summary>
        public static SampleDataSource FromFolder( string root ) => new SampleDataSource( new FolderFiles( root ) );

        /// <summary>
        /// The dataset's manifest from datasets/{key}.json, or null when there is none.
        /// </summary>
        public DatasetManifest ReadDataset( string key )
        {
            return Files.Exists( $"datasets/{key}.json" ) ? Read<DatasetManifest>( $"datasets/{key}.json" ) : null;
        }

        /// <summary>
        /// Reads a package's resolved calibration profile (profile.json). The builder writes it
        /// from calibration/profiles, so a load never blends sources itself.
        /// </summary>
        public ProfileTargets ReadProfile()
        {
            return ProfileTargets.FromJson( Files.ReadText( SampleDataPackage.ProfilePath ) );
        }

        /// <summary>
        /// Reads photos/manifest.json, the list of delivered Cast and pool photos.
        /// </summary>
        public PhotoManifest ReadPhotoManifest()
        {
            return Read<PhotoManifest>( "photos/manifest.json" );
        }

        /// <summary>
        /// Reads the Cast's serving team rosters (cast/groups/serving-*.json), by team key.
        /// </summary>
        public Dictionary<string, CastServingTeam> ReadCastServingTeams()
        {
            return Files.List( "cast/groups" )
                .Where( f => Path.GetFileName( f ).StartsWith( "serving-", StringComparison.Ordinal ) && f.EndsWith( ".json", StringComparison.Ordinal ) )
                .Select( Read<CastServingTeam> )
                .ToDictionary( t => t.Team );
        }

        /// <summary>
        /// Reads photos/groups/manifest.json, the Cast small group photos. A package built before
        /// group photos existed has none.
        /// </summary>
        public GroupPhotoManifest ReadGroupPhotoManifest()
        {
            return Files.Exists( GroupPhotoManifestPath ) ? Read<GroupPhotoManifest>( GroupPhotoManifestPath ) : new GroupPhotoManifest();
        }

        public const string GroupPhotoManifestPath = "photos/groups/manifest.json";

        public const string NotesConfigurationPath = "configuration/notes.json";

        public const string SecurityRolesPath = "configuration/security-roles.json";

        public const string StepsConfigurationPath = "configuration/steps.json";

        public const string CampaignConfigurationPath = "configuration/campaign.json";

        public const string CommunicationsConfigurationPath = "configuration/communications.json";

        public const string ChurchAppConfigurationPath = "configuration/church-app.json";

        public const string CalendarConfigurationPath = "configuration/calendar.json";

        /// <summary>
        /// Reads photos/events/manifest.json, the calendar event photos. A package built before event photos existed has none.
        /// </summary>
        public EventPhotoManifest ReadEventPhotoManifest()
        {
            return Files.Exists( EventPhotoManifestPath ) ? Read<EventPhotoManifest>( EventPhotoManifestPath ) : new EventPhotoManifest();
        }

        public const string EventPhotoManifestPath = "photos/events/manifest.json";

        /// <summary>
        /// Reads photos/messages/manifest.json, the adaptive message images. Until the image agent delivers them, and in
        /// a package built before they existed, there is none, and no adaptation gets one.
        /// </summary>
        public MessagePhotoManifest ReadMessagePhotoManifest()
        {
            return Files.Exists( MessagePhotoManifestPath ) ? Read<MessagePhotoManifest>( MessagePhotoManifestPath ) : new MessagePhotoManifest();
        }

        public const string MessagePhotoManifestPath = "photos/messages/manifest.json";

        /// <summary>
        /// Reads photos/campuses/manifest.json, the campus photos. A package built before campus photos loaded has none.
        /// </summary>
        public CampusPhotoManifest ReadCampusPhotoManifest()
        {
            return Files.Exists( CampusPhotoManifestPath ) ? Read<CampusPhotoManifest>( CampusPhotoManifestPath ) : new CampusPhotoManifest();
        }

        public const string CampusPhotoManifestPath = "photos/campuses/manifest.json";

        /// <summary>
        /// Reads configuration/staff-homepage.json, Rock's staff homepage. A package built before it existed has none.
        /// </summary>
        public StaffHomepageConfiguration ReadStaffHomepage()
        {
            return Files.Exists( StaffHomepageConfigurationPath ) ? Read<StaffHomepageConfiguration>( StaffHomepageConfigurationPath ) : null;
        }

        public const string StaffHomepageConfigurationPath = "configuration/staff-homepage.json";

        /// <summary>
        /// Reads content/internal-news/updates.json, the text of the staff homepage's weekly updates.
        /// </summary>
        public InternalNewsLibrary ReadInternalNews() => Read<InternalNewsLibrary>( InternalNewsPath );

        public const string InternalNewsFolder = "content/internal-news";

        public const string InternalNewsPath = InternalNewsFolder + "/updates.json";

        public const string LoginsConfigurationPath = "configuration/logins.json";

        public const string RegistrationsConfigurationPath = "configuration/registrations.json";

        public const string CategoriesConfigurationPath = "configuration/categories.json";

        public const string GroupSyncsConfigurationPath = "configuration/group-syncs.json";

        public const string ReportingConfigurationPath = "configuration/reporting.json";
        public const string FlowsConfigurationPath = "configuration/flows.json";

        public const string PhonesConfigurationPath = "configuration/phones.json";

        public const string AdaptiveMessagesConfigurationPath = "configuration/adaptive-messages.json";

        /// <summary>
        /// The text of each adaptation, and the Lava of the block that shows them.
        /// </summary>
        public const string AdaptiveMessagesContentFolder = "content/adaptive-messages";

        public const string AdaptiveMessageTextPath = AdaptiveMessagesContentFolder + "/messages.json";

        /// <summary>
        /// The message library, the email template, and its logo.
        /// </summary>
        public const string CommunicationsContentFolder = "content/communications";

        /// <summary>
        /// Reads the sermon series from the message library.
        /// </summary>
        public SeriesLibrary ReadSeriesLibrary() => Read<SeriesLibrary>( $"{CommunicationsContentFolder}/series.json" );

        /// <summary>
        /// Reads the weekly newsletter's pieces from the message library.
        /// </summary>
        public NewsletterLibrary ReadNewsletterLibrary() => Read<NewsletterLibrary>( $"{CommunicationsContentFolder}/newsletter.json" );

        /// <summary>
        /// Reads the library's emails.
        /// </summary>
        public EmailLibrary ReadEmailLibrary() => Read<EmailLibrary>( $"{CommunicationsContentFolder}/emails.json" );

        /// <summary>
        /// Reads the library's texts, pushes, and reply pools.
        /// </summary>
        public TextLibrary ReadTextLibrary() => Read<TextLibrary>( $"{CommunicationsContentFolder}/texts.json" );

        /// <summary>
        /// Reads the pools generated text conversations are built from.
        /// </summary>
        public ConversationLibrary ReadConversationLibrary() => Read<ConversationLibrary>( $"{CommunicationsContentFolder}/conversations.json" );

        public const string SnippetsPath = CommunicationsContentFolder + "/snippets.json";

        /// <summary>
        /// Reads the SMS snippets, or null for a package built before they existed.
        /// </summary>
        public SnippetLibrary ReadSnippetLibrary() => Files.Exists( SnippetsPath ) ? Read<SnippetLibrary>( SnippetsPath ) : null;

        public const string EmailSectionsPath = CommunicationsContentFolder + "/email-sections.json";

        /// <summary>
        /// Reads the church's email sections, or null for a package built before they existed.
        /// </summary>
        public EmailSectionLibrary ReadEmailSectionLibrary() => Files.Exists( EmailSectionsPath ) ? Read<EmailSectionLibrary>( EmailSectionsPath ) : null;

        public const string CastThreadsPath = "cast/sms-threads.json";

        /// <summary>
        /// Reads the Cast's hand-written text threads, or none for a package built before they existed.
        /// </summary>
        public List<CastThread> ReadCastThreads() => Files.Exists( CastThreadsPath ) ? Read<CastThreadFile>( CastThreadsPath ).Threads : new List<CastThread>();

        public const string CastStaffEmailsPath = "cast/staff-emails.json";

        /// <summary>
        /// Reads the Cast staff's hand-written personal emails, or none for a package built before they existed.
        /// </summary>
        public List<CastStaffEmail> ReadCastStaffEmails() => Files.Exists( CastStaffEmailsPath ) ? Read<CastStaffEmailFile>( CastStaffEmailsPath ).Emails : new List<CastStaffEmail>();

        public const string CastDuplicatesPath = "cast/duplicates.json";

        /// <summary>
        /// Reads the Cast's duplicate pairs, merge requests, and finished merges, or none for a package built before they existed.
        /// </summary>
        public CastDuplicatesFile ReadCastDuplicates() => Files.Exists( CastDuplicatesPath ) ? Read<CastDuplicatesFile>( CastDuplicatesPath ) : new CastDuplicatesFile();

        /// <summary>
        /// Reads every Cast note file in cast/notes, in file name order so loads are repeatable.
        /// </summary>
        public List<CastNoteFile> ReadCastNotes()
        {
            return Files.List( "cast/notes" )
                .Where( f => f.EndsWith( ".json", StringComparison.Ordinal ) )
                .Select( Read<CastNoteFile> )
                .ToList();
        }

        /// <summary>
        /// Reads one note type's text pool from content/notes/{key}.json, by audience. Keys that start
        /// with $ (such as $comment) are not audiences.
        /// </summary>
        public Dictionary<string, NotePoolSection> ReadNotePool( string typeKey )
        {
            using var document = JsonDocument.Parse( Files.ReadText( $"content/notes/{typeKey}.json" ) );
            return document.RootElement.EnumerateObject()
                .Where( p => !p.Name.StartsWith( "$", StringComparison.Ordinal ) )
                .ToDictionary( p => p.Name, p => Parse( $"content/notes/{typeKey}.json", () => p.Value.Deserialize<NotePoolSection>( JsonOptions ) ) );
        }

        /// <summary>
        /// Reads content/notes/shared.json: staff replies and mention lines.
        /// </summary>
        public SharedNotePool ReadSharedNotePool()
        {
            return Read<SharedNotePool>( "content/notes/shared.json" );
        }

        /// <summary>
        /// Reads photos/opportunities/manifest.json, the serving opportunity photos. A package built before
        /// opportunity photos existed has none.
        /// </summary>
        public OpportunityPhotoManifest ReadOpportunityPhotoManifest()
        {
            return Files.Exists( OpportunityPhotoManifestPath ) ? Read<OpportunityPhotoManifest>( OpportunityPhotoManifestPath ) : new OpportunityPhotoManifest();
        }

        public const string OpportunityPhotoManifestPath = "photos/opportunities/manifest.json";

        /// <summary>
        /// Reads configuration/prayer.json, or null for a package built before prayer requests existed.
        /// </summary>
        public PrayerConfiguration ReadPrayerConfiguration()
        {
            return Files.Exists( "configuration/prayer.json" ) ? Read<PrayerConfiguration>( "configuration/prayer.json" ) : null;
        }

        public const string CastPrayerRequestsPath = "cast/prayer-requests.json";

        /// <summary>
        /// Reads the Cast's own prayer requests, or none for a package built before they existed.
        /// </summary>
        public List<CastPrayerRequest> ReadCastPrayerRequests()
        {
            return Files.Exists( CastPrayerRequestsPath ) ? Read<CastPrayerRequestFile>( CastPrayerRequestsPath ).Requests : new List<CastPrayerRequest>();
        }

        /// <summary>
        /// Reads the prayer text pools in content/prayer: one file per category with a weight above 0,
        /// plus the comments and closings.
        /// </summary>
        public PrayerText ReadPrayerText( PrayerConfiguration configuration )
        {
            var text = new PrayerText
            {
                Comments = Read<PrayerCommentText>( "content/prayer/comments.json" ),
                Shared = Read<PrayerSharedText>( "content/prayer/shared.json" )
            };

            foreach ( var category in configuration.Categories.Where( c => c.Weight > 0 ) )
            {
                text.Categories[category.Key] = Read<PrayerCategoryText>( $"content/prayer/categories/{category.Key}.json" );
            }

            return text;
        }

        /// <summary>
        /// Reads the Cast's small group plans from cast/groups, in file name order. Groups the
        /// configuration lists as not in the pattern, and other group types, are skipped.
        /// </summary>
        public List<SmallGroupPlan> ReadCastSmallGroupPlans()
        {
            var plans = new List<SmallGroupPlan>();
            foreach ( var file in Files.List( "cast/groups" ).Where( f => f.EndsWith( ".json", StringComparison.Ordinal ) ) )
            {
                var text = Files.ReadText( file );
                using var document = JsonDocument.Parse( text );
                var isSmallGroup = document.RootElement.TryGetProperty( "type", out var type ) && type.GetString() == "small-group";
                if ( isSmallGroup && !SmallGroups.Cast.NotInPattern.Contains( document.RootElement.GetProperty( "key" ).GetString() ) )
                {
                    plans.Add( Parse( file, () => JsonSerializer.Deserialize<SmallGroupPlan>( text, SmallGroupPlanJson.Options ) ) );
                }
            }

            return plans;
        }

        /// <summary>
        /// Reads every Cast household file, in file name order so loads are repeatable.
        /// </summary>
        public List<CastHousehold> ReadHouseholds()
        {
            return Files.List( "cast/households" )
                .Where( f => f.EndsWith( ".json", StringComparison.Ordinal ) )
                .Select( Read<CastHousehold> )
                .ToList();
        }

        /// <summary>
        /// Finds the rock-sample-data repository by walking up from a starting folder.
        /// </summary>
        public static string FindRoot( string start )
        {
            var dir = new DirectoryInfo( start );
            while ( dir != null )
            {
                if ( Directory.Exists( Path.Combine( dir.FullName, "configuration" ) ) && Directory.Exists( Path.Combine( dir.FullName, "cast" ) ) )
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException( "Could not find the rock-sample-data repository. Pass --root." );
        }

        /// <summary>
        /// Reads and deserializes one JSON file.
        /// </summary>
        public T Read<T>( string path )
        {
            return Parse( path, () => JsonSerializer.Deserialize<T>( Files.ReadText( path ), JsonOptions ) );
        }

        /*
            10/26/26 - CLAUDE

            A package is built for the loader of its release. When a newer loader meets a file whose shape
            it does not expect, the person installing sees a plain message instead of a JSON error, and the
            JSON error stays as the inner exception for whoever reads the log.

            Reason: a published package must never stop a newer loader with a raw parse error
            (decision 168).
        */

        private static T Parse<T>( string path, Func<T> parse )
        {
            try
            {
                return parse();
            }
            catch ( JsonException ex )
            {
                throw new InvalidDataException( $"This package can't be read by this loader: {path} is not in the form this loader reads. Use the package for this loader's release.", ex );
            }
        }
    }
}
