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
        /// Reads photos/groups/manifest.json, the Cast small group photos. A package built before
        /// group photos existed has none.
        /// </summary>
        public GroupPhotoManifest ReadGroupPhotoManifest()
        {
            return Files.Exists( GroupPhotoManifestPath ) ? Read<GroupPhotoManifest>( GroupPhotoManifestPath ) : new GroupPhotoManifest();
        }

        public const string GroupPhotoManifestPath = "photos/groups/manifest.json";

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
                    plans.Add( JsonSerializer.Deserialize<SmallGroupPlan>( text, SmallGroupPlanJson.Options ) );
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
            return JsonSerializer.Deserialize<T>( Files.ReadText( path ), JsonOptions );
        }
    }
}
