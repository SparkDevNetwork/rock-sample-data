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
        /// Who among generated adults has a login, or null in a package built before logins loaded.
        /// </summary>
        public LoginsConfiguration Logins { get; }

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
            Logins = Files.Exists( LoginsConfigurationPath ) ? Read<LoginsConfiguration>( LoginsConfigurationPath ) : null;
        }

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

        public const string LoginsConfigurationPath = "configuration/logins.json";

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
