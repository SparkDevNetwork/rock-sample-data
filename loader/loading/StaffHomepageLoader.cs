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

using System.Text;
using System.Text.RegularExpressions;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/19/26 - CLAUDE

        Rock's staff homepage shows its Internal Communication View block: the newest approved item in the Internal
        Communications - Homepage channel whose start date has passed, its feature, its one to three articles, and the
        homepage metrics. A new Rock shows only its 2018 item and tiles from 2020 (issue 9). The weekly updates are the
        Content part. The metric history, the Quick Links, and the path metrics on the block are church setup: they read
        whatever rows the load wrote, as the path metrics do, so they always run.

        Each update is written as Rock's item editor saves one: Approved by its author when saved, a start date with no
        time (the channel's type stores none), a global key and a slug from its title by Rock's own rules, its feature
        attributes, and an Internal Article List matrix with an item per article. Rock's 2018 item becomes the oldest
        update in place, and its old images are deleted, as Rock deletes an image file no value names any longer.
        Images are shared: one file per approved event image or staff photo, named by every value that shows it
        (decisions 345 to 347).

        Reason: Staff land on this page every day, and it should look like a church that uses it.
    */

    /// <summary>
    /// Writes Rock's staff homepage: the weekly staff updates, a year of the homepage metrics, the church's Quick Links,
    /// and the Ministry Path metrics on the block.
    /// </summary>
    internal sealed class StaffHomepageLoader
    {
        private static readonly Guid FeatureImageGuid = new Guid( "6B0A2023-A3F9-4208-A688-10A67483A435" );
        private static readonly Guid FeatureTitleGuid = new Guid( "00692241-04F5-4B08-85A5-DC671294E372" );
        private static readonly Guid FeatureTextGuid = new Guid( "8D057A30-9BA4-4E56-977A-50870580016B" );
        private static readonly Guid FeatureLinkGuid = new Guid( "453F8D20-C65E-4F44-A5AC-32CDD0493331" );
        private static readonly Guid ArticlesGuid = new Guid( "C9C7B33F-DE67-470D-9EFD-39275E00CE81" );
        private static readonly Guid ArticleImageGuid = new Guid( "BA1D667D-080D-4A17-9392-F3E953969C7A" );
        private static readonly Guid ArticleTitleGuid = new Guid( "DBC9E868-D509-4436-8B11-1343A02DB433" );
        private static readonly Guid ArticleContentGuid = new Guid( "95FE772F-D799-41DD-9802-F5FD487933FC" );

        /// <summary>
        /// Rock's Internal Article List matrix template, which holds one to three articles.
        /// </summary>
        private static readonly Guid ArticleListTemplateGuid = new Guid( "1D24694E-445C-4852-B5BC-64CDEA6F7175" );

        private static readonly Guid FamilyGroupTypeGuid = new Guid( "790E3215-3B10-442B-AF69-616C0DCB998E" );

        /// <summary>
        /// Rock's Known Relationships and Peer Network group types, which hold every person and say nothing about when a
        /// family came.
        /// </summary>
        private static readonly Guid KnownRelationshipsGroupTypeGuid = new Guid( "E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF" );
        private static readonly Guid PeerNetworkGroupTypeGuid = new Guid( "8C0E5852-F08F-4327-9AA5-87800A6AB53E" );

        /// <summary>
        /// Rock's file field types, the ones its DeleteBinaryFileAttribute task checks: Audio File, Background Check,
        /// Binary File, File, Image, and Video File.
        /// </summary>
        private const string FileFieldTypes = "'9772CB1F-3BC4-432E-80DD-D635CDB2DA32', 'D05B3808-803A-4531-9680-DD4AAB8ADF1A', 'C403E219-A56B-439E-9D50-9302DFE760CF', '6F9E2DD0-E39E-4602-ADF9-EB710A75304A', '97F8157D-A8C8-4AB3-96A2-9CB2A9049E6D', 'F1F5B59D-F086-4627-A94A-DFA7E67950F3'";

        /// <summary>
        /// Rock's Approved content channel item status.
        /// </summary>
        private const int Approved = 2;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly StaffHomepageConfiguration _configuration;
        private readonly AnchorDates _dates;
        private readonly DateTime _installed;
        private readonly string _foreignKey;
        private readonly string _seed;

        /// <param name="installed">The moment of install: the anchor day at the time the load runs.</param>
        public StaffHomepageLoader( RockDatabase db, SampleDataSource source, StaffHomepageConfiguration configuration, AnchorDates dates, DateTime installed, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _configuration = configuration;
            _dates = dates;
            _installed = installed;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        #region The weekly updates

        /// <summary>
        /// Writes one staff update for each of the last weeks' Mondays whose post time has passed. Returns how many.
        /// </summary>
        /// <param name="people">The loaded people, by Guid, for the author.</param>
        /// <param name="campusPlans">The loaded campuses, which the calendar's event dates are planned for.</param>
        /// <param name="historyStart">The first day of the history window.</param>
        public int LoadUpdates( IReadOnlyDictionary<Guid, LoadedPerson> people, IReadOnlyDictionary<string, CampusPlan> campusPlans, DateTime historyStart )
        {
            var settings = _configuration.Updates;
            var channel = _db.Query( "SELECT [Id], [ContentChannelTypeId] FROM [ContentChannel] WHERE [Guid] = @Guid", P( "@Guid", settings.ContentChannel ) ).SingleOrDefault()
                ?? throw new InvalidDataException( $"{SampleDataSource.StaffHomepageConfigurationPath}: this Rock has no content channel {settings.ContentChannel}." );
            var channelId = ( int ) channel["Id"];
            var channelTypeId = ( int ) channel["ContentChannelTypeId"];
            var authorAliasId = people.TryGetValue( settings.Author, out var author ) ? author.AliasId : ( int? ) null;

            var mondays = Mondays( settings );
            var library = _source.ReadInternalNews();
            var occasions = OccasionDates( campusPlans, historyStart, mondays[0] );
            var updates = StaffUpdatePlanner.Plan( library, settings.ArticleCounts, mondays, occasions, _seed );

            // One shared file per image (decision 346).
            var photoLoader = new PhotoLoader( _db, _source, _foreignKey, _seed );
            var eventFiles = _source.ReadEventPhotoManifest().Images.ToDictionary( i => i.Event, i => i.File, StringComparer.Ordinal );
            var staffFiles = _source.ReadStaffPhotoManifest().Images.ToDictionary( i => $"{i.Household}/{i.Member}", i => i.File, StringComparer.Ordinal );
            var featureImages = photoLoader.LoadSharedImages( updates.Select( u => eventFiles.TryGetValue( u.Feature.Image, out var file ) ? file
                : throw new InvalidDataException( $"{SampleDataSource.InternalNewsPath}: feature {u.Feature.Key} shows event image {u.Feature.Image}, which is not in {SampleDataSource.EventPhotoManifestPath}." ) ), SampleDataSource.EventPhotoManifestPath );
            var articleImages = photoLoader.LoadSharedImages( updates.SelectMany( u => u.Articles ).Where( a => staffFiles.ContainsKey( a.Author ) ).Select( a => staffFiles[a.Author] ), SampleDataSource.StaffPhotoManifestPath );
            string ArticleImage( NewsArticle article ) => staffFiles.TryGetValue( article.Author, out var file ) ? articleImages[file].ToString() : null;

            var attributeIds = new Dictionary<Guid, int>();
            int AttributeId( Guid guid ) => attributeIds.TryGetValue( guid, out var id ) ? id : attributeIds[guid] = _db.RequireIdByGuid( "Attribute", guid );
            var templateId = _db.RequireIdByGuid( "AttributeMatrixTemplate", ArticleListTemplateGuid );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var items = TableFactory.Create( "Id:int", "ContentChannelId:int", "ContentChannelTypeId:int", "Title:string", "Content:string", "Priority:int", "Status:int", "ApprovedByPersonAliasId:int",
                "ApprovedDateTime:date", "StartDateTime:date", "Order:int", "ItemGlobalKey:string", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "Guid:guid", "ForeignKey:string",
                "CreatedDateTime:date", "ModifiedDateTime:date" );
            var slugs = TableFactory.Create( "ContentChannelItemId:int", "Slug:string", "IsPrimary:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var matrices = TableFactory.Create( "Id:int", "AttributeMatrixTemplateId:int", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var matrixItems = TableFactory.Create( "Id:int", "AttributeMatrixId:int", "AttributeMatrixTemplateId:int", "Order:int", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var nextItemId = NextId( "ContentChannelItem" );
            var nextMatrixId = NextId( "AttributeMatrix" );
            var nextMatrixItemId = NextId( "AttributeMatrixItem" );
            var keys = new SlugKeys( _db, channelId );
            var random = new SeededRandom( $"{_seed}:staff-update-rows" );

            // Rock's 2018 item becomes the oldest update in place (issue 9 question 1).
            var rockItem = _db.Query( "SELECT [Id] FROM [ContentChannelItem] WHERE [Guid] = @Guid AND [ContentChannelId] = @ChannelId", P( "@Guid", settings.RockItem ), P( "@ChannelId", channelId ) ).SingleOrDefault();
            for ( var i = 0; i < updates.Count; i++ )
            {
                var update = updates[i];
                var posted = update.Monday.Add( TimeSpan.Parse( settings.PostTime ) ).AddMinutes( random.Between( -6, 9 ) );
                int itemId;
                int matrixId;
                Guid matrixGuid;
                if ( i == 0 && rockItem != null )
                {
                    itemId = ( int ) rockItem["Id"];
                    matrixGuid = ReplaceRockItem( itemId, update, posted, authorAliasId, AttributeId, featureImages[eventFiles[update.Feature.Image]] );
                    matrixId = _db.RequireIdByGuid( "AttributeMatrix", matrixGuid );
                }
                else
                {
                    itemId = nextItemId++;
                    var key = keys.Next( update.Feature.Title );
                    items.Rows.Add( itemId, channelId, channelTypeId, update.Feature.Title, string.Empty, 0, Approved, Value( authorAliasId ), posted, update.Monday, 0, Value( key.GlobalKey ),
                        Value( authorAliasId ), Value( authorAliasId ), random.NextGuid(), _foreignKey, posted, posted );
                    if ( key.Slug != null )
                    {
                        slugs.Rows.Add( itemId, key.Slug, false, random.NextGuid(), _foreignKey, posted, posted );
                    }

                    matrixId = nextMatrixId++;
                    matrixGuid = random.NextGuid();
                    matrices.Rows.Add( matrixId, templateId, Value( authorAliasId ), Value( authorAliasId ), matrixGuid, _foreignKey, posted, posted );
                    AddValue( FeatureImageGuid, itemId, featureImages[eventFiles[update.Feature.Image]].ToString() );
                    AddValue( FeatureTitleGuid, itemId, update.Feature.Title );
                    AddValue( FeatureTextGuid, itemId, Html( update.Feature.Text ) );
                    AddValue( FeatureLinkGuid, itemId, settings.Link );
                    AddValue( ArticlesGuid, itemId, matrixGuid.ToString() );
                }

                for ( var order = 0; order < update.Articles.Count; order++ )
                {
                    var article = update.Articles[order];
                    var matrixItemId = nextMatrixItemId++;
                    matrixItems.Rows.Add( matrixItemId, matrixId, templateId, order, Value( authorAliasId ), Value( authorAliasId ), random.NextGuid(), _foreignKey, posted, posted );
                    AddValue( ArticleImageGuid, matrixItemId, ArticleImage( article ) );
                    AddValue( ArticleTitleGuid, matrixItemId, article.Title );
                    AddValue( ArticleContentGuid, matrixItemId, Html( new[] { article.Text } ) );
                }

                void AddValue( Guid attribute, int entityId, string value )
                {
                    if ( !string.IsNullOrEmpty( value ) )
                    {
                        values.Rows.Add( false, AttributeId( attribute ), entityId, value, true, random.NextGuid(), _foreignKey, posted, posted );
                    }
                }
            }

            _db.BulkInsert( "ContentChannelItem", items, keepIdentity: true );
            _db.BulkInsert( "ContentChannelItemSlug", slugs, keepIdentity: false );
            _db.BulkInsert( "AttributeMatrix", matrices, keepIdentity: true );
            _db.BulkInsert( "AttributeMatrixItem", matrixItems, keepIdentity: true );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
            _db.Log.Write( $"  Staff updates: {updates.Count} Mondays from {updates[0].Monday:yyyy-MM-dd}, {matrixItems.Rows.Count} articles, {featureImages.Count + articleImages.Count} shared images" );
            return updates.Count;
        }

        /// <summary>
        /// The Monday of each update, oldest first: the last Mondays whose post time has passed by install.
        /// </summary>
        private List<DateTime> Mondays( StaffUpdatesSettings settings )
        {
            var newest = StaffUpdatePlanner.MondayOf( _installed );
            if ( newest.Add( TimeSpan.Parse( settings.PostTime ) ) > _installed )
            {
                newest = newest.AddDays( -7 );
            }

            return Enumerable.Range( 0, settings.Weeks ).Select( w => newest.AddDays( -7 * ( settings.Weeks - 1 - w ) ) ).ToList();
        }

        /// <summary>
        /// The dates of each occasion an update can follow: the message library's occasions, and every calendar event's
        /// dates as event:key. The calendar is planned here on its own, so the updates do not need the Calendar part.
        /// </summary>
        private Func<string, IReadOnlyList<DateTime>> OccasionDates( IReadOnlyDictionary<string, CampusPlan> campusPlans, DateTime historyStart, DateTime firstMonday )
        {
            var communications = new CommunicationCalendar( _dates.Anchor, firstMonday.AddDays( -7 ), _source.Steps, _source.Campaign, _source.Communications?.Events, null, _seed );
            var events = new Dictionary<string, List<DateTime>>( StringComparer.Ordinal );
            if ( _source.Calendar != null )
            {
                var campusServices = _source.Campuses.Campuses
                    .Where( c => campusPlans.ContainsKey( c.Key ) )
                    .Select( c => (c.Key, ( IReadOnlyList<TimeSpan> ) campusPlans[c.Key].Services.Select( s => s.Start ).ToList()) )
                    .ToList();
                var until = _installed.AddDays( 120 );
                foreach ( var planned in new CalendarPlanner( _source.Calendar, _source.Steps, _source.Communications?.Events, _source.Campaign, _dates.Anchor, historyStart ).Plan( campusServices ) )
                {
                    events[planned.Event.Key] = planned.Occurrences.SelectMany( o => o.Schedule.Starts( until ) ).Select( s => s.Date ).Distinct().OrderBy( d => d ).ToList();
                }
            }

            return occasion => occasion == null ? new List<DateTime>()
                : occasion.StartsWith( "event:", StringComparison.Ordinal ) ? ( events.TryGetValue( occasion.Substring( "event:".Length ), out var dates ) ? dates : new List<DateTime>() )
                : communications.Dates( occasion );
        }

        /// <summary>
        /// Turns Rock's 2018 item into the oldest update, as Rock's item editor would save the change, and returns its
        /// article matrix's Guid. Its old images are deleted once nothing names them, as Rock deletes a replaced image.
        /// </summary>
        private Guid ReplaceRockItem( int itemId, PlannedStaffUpdate update, DateTime posted, int? authorAliasId, Func<Guid, int> attributeId, Guid featureImage )
        {
            var oldImages = _db.Query( $@"
SELECT [v].[Value]
FROM [AttributeValue] AS [v]
WHERE ( [v].[AttributeId] = {attributeId( FeatureImageGuid )} AND [v].[EntityId] = {itemId} )
    OR ( [v].[AttributeId] = {attributeId( ArticleImageGuid )} AND [v].[EntityId] IN (
        SELECT [mi].[Id] FROM [AttributeMatrixItem] AS [mi] INNER JOIN [AttributeMatrix] AS [m] ON [m].[Id] = [mi].[AttributeMatrixId]
        INNER JOIN [AttributeValue] AS [a] ON [a].[AttributeId] = {attributeId( ArticlesGuid )} AND [a].[EntityId] = {itemId} AND TRY_CAST( [a].[Value] AS UNIQUEIDENTIFIER ) = [m].[Guid] ) )" )
                .Select( r => r["Value"] as string ).Where( v => Guid.TryParse( v, out _ ) ).Select( Guid.Parse ).ToList();

            _db.Execute( @"
UPDATE [ContentChannelItem]
SET [Title] = @Title, [Status] = @Approved, [StartDateTime] = @Start, [ApprovedByPersonAliasId] = @Author, [ApprovedDateTime] = @Posted,
    [ModifiedByPersonAliasId] = @Author, [ModifiedDateTime] = @Posted
WHERE [Id] = @Id",
                P( "@Title", update.Feature.Title ), P( "@Approved", Approved ), P( "@Start", update.Monday ), P( "@Author", authorAliasId ), P( "@Posted", posted ), P( "@Id", itemId ) );

            SetValue( attributeId( FeatureImageGuid ), itemId, featureImage.ToString(), posted );
            SetValue( attributeId( FeatureTitleGuid ), itemId, update.Feature.Title, posted );
            SetValue( attributeId( FeatureTextGuid ), itemId, Html( update.Feature.Text ), posted );
            SetValue( attributeId( FeatureLinkGuid ), itemId, _configuration.Updates.Link, posted );

            // The matrix keeps its row, and its articles are replaced, as Rock's matrix editor saves a changed list.
            var matrix = _db.Scalar( $"SELECT TRY_CAST( [Value] AS UNIQUEIDENTIFIER ) FROM [AttributeValue] WHERE [AttributeId] = {attributeId( ArticlesGuid )} AND [EntityId] = {itemId}" ) as Guid?;
            if ( matrix == null )
            {
                throw new InvalidDataException( $"Rock's item {_configuration.Updates.RockItem} has no article matrix." );
            }

            var matrixId = _db.RequireIdByGuid( "AttributeMatrix", matrix.Value );
            _db.Execute( $@"
DELETE [v] FROM [AttributeValue] AS [v]
INNER JOIN [AttributeMatrixItem] AS [mi] ON [mi].[Id] = [v].[EntityId] AND [mi].[AttributeMatrixId] = {matrixId}
WHERE [v].[AttributeId] IN ( SELECT [Id] FROM [Attribute] WHERE [EntityTypeQualifierColumn] = 'AttributeMatrixTemplateId' AND [EntityTypeQualifierValue] = CAST( [mi].[AttributeMatrixTemplateId] AS NVARCHAR( 200 ) ) );
DELETE FROM [AttributeMatrixItem] WHERE [AttributeMatrixId] = {matrixId};
UPDATE [AttributeMatrix] SET [ModifiedByPersonAliasId] = @Author, [ModifiedDateTime] = @Posted WHERE [Id] = {matrixId};", P( "@Author", authorAliasId ), P( "@Posted", posted ) );

            // Rock's DeleteBinaryFileAttribute task: a replaced image goes only when no file attribute's value or default names it.
            // The file attributes are read inline, not into a table variable, so the contract recorder can read every write.
            foreach ( var old in oldImages.Distinct() )
            {
                _db.Execute( $@"
IF NOT EXISTS ( SELECT 1 FROM [AttributeValue] AS [v]
        INNER JOIN [Attribute] AS [a] ON [a].[Id] = [v].[AttributeId]
        INNER JOIN [FieldType] AS [f] ON [f].[Id] = [a].[FieldTypeId]
        WHERE [f].[Guid] IN ( {FileFieldTypes} ) AND [v].[Value] = @Guid )
    AND NOT EXISTS ( SELECT 1 FROM [Attribute] AS [a]
        INNER JOIN [FieldType] AS [f] ON [f].[Id] = [a].[FieldTypeId]
        WHERE [f].[Guid] IN ( {FileFieldTypes} ) AND [a].[DefaultValue] = @Guid )
BEGIN
    DELETE FROM [BinaryFileData] WHERE [Id] IN ( SELECT [Id] FROM [BinaryFile] WHERE [Guid] = @Guid );
    DELETE FROM [BinaryFile] WHERE [Guid] = @Guid;
END", P( "@Guid", old.ToString() ) );
            }

            return matrix.Value;
        }

        /// <summary>
        /// Sets an existing attribute value, or adds it, as Rock's attribute editor saves one.
        /// </summary>
        private void SetValue( int attributeId, int entityId, string value, DateTime when )
        {
            var changed = _db.Execute( "UPDATE [AttributeValue] SET [Value] = @Value, [IsPersistedValueDirty] = 1, [ModifiedDateTime] = @When WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId",
                P( "@Value", value ), P( "@When", when ), P( "@AttributeId", attributeId ), P( "@EntityId", entityId ) );
            if ( changed == 0 )
            {
                _db.Execute( @"
INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @AttributeId, @EntityId, @Value, 1, NEWID(), @ForeignKey, @When, @When )",
                    P( "@AttributeId", attributeId ), P( "@EntityId", entityId ), P( "@Value", value ), P( "@ForeignKey", _foreignKey ), P( "@When", when ) );
            }
        }

        /// <summary>
        /// Paragraphs as the HTML Rock's HTML editor saves.
        /// </summary>
        private static string Html( IEnumerable<string> paragraphs ) => string.Concat( paragraphs.Select( p => $"<p>{System.Net.WebUtility.HtmlEncode( p )}</p>" ) );

        /// <summary>
        /// Gives each new item the global key and slug Rock's save hook gives it: its title made into a slug, unique among
        /// every item's slugs for the global key, and among its channel's slugs for the slug.
        /// </summary>
        private sealed class SlugKeys
        {
            private readonly HashSet<string> _all;
            private readonly HashSet<string> _channel;

            public SlugKeys( RockDatabase db, int channelId )
            {
                var slugs = db.Query( "SELECT [s].[Slug], [i].[ContentChannelId] FROM [ContentChannelItemSlug] AS [s] INNER JOIN [ContentChannelItem] AS [i] ON [i].[Id] = [s].[ContentChannelItemId] WHERE [s].[Slug] IS NOT NULL" );
                _all = new HashSet<string>( slugs.Select( s => ( string ) s["Slug"] ), StringComparer.OrdinalIgnoreCase );
                _channel = new HashSet<string>( slugs.Where( s => ( int ) s["ContentChannelId"] == channelId ).Select( s => ( string ) s["Slug"] ), StringComparer.OrdinalIgnoreCase );
            }

            public (string GlobalKey, string Slug) Next( string title )
            {
                var slug = MakeSlugValid( title );
                if ( string.IsNullOrWhiteSpace( slug ) )
                {
                    return (null, null);
                }

                var globalKey = Unique( slug, _all );
                var channelSlug = Unique( slug, _channel );
                _all.Add( channelSlug );
                _channel.Add( channelSlug );
                return (globalKey, channelSlug);
            }

            /// <summary>
            /// Rock's GetUniqueSlug: the slug, or the slug with -1, -2, and so on.
            /// </summary>
            private static string Unique( string slug, HashSet<string> taken )
            {
                for ( var number = 0; ; number++ )
                {
                    var candidate = number == 0 ? slug : slug.Length + $"-{number}".Length > 200 ? slug.Substring( 0, 200 - $"-{number}".Length ) + $"-{number}" : $"{slug}-{number}";
                    if ( !taken.Contains( candidate ) )
                    {
                        return candidate;
                    }
                }
            }

            /// <summary>
            /// Rock's ContentChannelItemSlugService.MakeSlugValid.
            /// </summary>
            private static string MakeSlugValid( string slug )
            {
                slug = slug.Trim().ToLower()
                    .Replace( "&nbsp;", "-" ).Replace( "&#160;", "-" ).Replace( "&ndash;", "-" ).Replace( "&#8211;", "-" ).Replace( "&mdash;", "-" ).Replace( "&#8212;", "-" )
                    .Replace( "_", "-" ).Replace( " ", "-" );
                slug = Regex.Replace( slug, @"-+", "-" );
                slug = Regex.Replace( slug, @"[^a-zA-Z0-9 -]", string.Empty );
                return ( slug.Length > 200 ? slug.Substring( 0, 200 ) : slug ).TrimEnd( '-' );
            }
        }

        #endregion

        #region The homepage metrics

        /*
            11/19/26 - CLAUDE

            Rock's three homepage metrics count with SQL on Rock's own schedules: Active Records and Active Connection
            Requests daily at 1 AM, and Active Families each Monday at 4 AM, by campus. The loader writes the value Rock's
            Calculate Metrics job would have saved at each run in the last year. Rock keeps no history of who was a record
            when, so a run counts today's active people whose family had come by that day: a family arrives with its first
            check-in, gift, request, or group membership, and a family with none has always been there. A request counts
            while it was open: from the day it was made until the day it was connected or set inactive. The newest daily
            values equal Rock's own SQL on install day, and LastRunDateTime is the newest run, so the job's next run is the
            next scheduled one (decision 347).

            Reason: The tiles show real numbers on install day, and Rock's metric charts show a year.
        */

        /// <summary>
        /// Writes a year of values for each homepage metric this Rock has. Returns how many metrics and values.
        /// </summary>
        public (int Metrics, int Values) LoadMetrics()
        {
            var values = TableFactory.Create( "Id:int", "MetricValueType:int", "YValue:decimal", "MetricId:int", "MetricValueDateTime:date", "MetricValueDateKey:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var partitions = TableFactory.Create( "Id:int", "MetricPartitionId:int", "EntityId:int", "MetricValueId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var nextValueId = NextId( "MetricValue" );
            var nextPartitionId = NextId( "MetricValuePartition" );
            var written = 0;
            Dictionary<int, DateTime?> familyArrivals = null;

            foreach ( var metric in _configuration.Metrics )
            {
                var row = _db.Query( "SELECT [Id] FROM [Metric] WHERE [Guid] = @Guid", P( "@Guid", metric.Guid ) ).SingleOrDefault();
                if ( row == null )
                {
                    _db.Log.Write( $"    Skipped metric {metric.Key}: this Rock does not have it" );
                    continue;
                }

                var metricId = ( int ) row["Id"];
                var partitionId = Convert.ToInt32( _db.Scalar( "SELECT TOP 1 [Id] FROM [MetricPartition] WHERE [MetricId] = @MetricId ORDER BY [Order], [Id]", P( "@MetricId", metricId ) ) );
                var runs = Runs( metric.Schedule );
                familyArrivals ??= FamilyArrivals();
                var counts = metric.Key switch
                {
                    "active-records" => ActiveRecords( runs, familyArrivals ),
                    "active-families" => ActiveFamilies( runs, familyArrivals ),
                    "active-connection-requests" => ActiveConnectionRequests( runs ),
                    _ => throw new InvalidDataException( $"{SampleDataSource.StaffHomepageConfigurationPath}: the loader does not know how metric {metric.Key} counts." )
                };

                var random = new SeededRandom( $"{_seed}:homepage-metric:{metric.Key}" );
                foreach ( var run in runs )
                {
                    var saved = run.AddSeconds( random.Between( 2, 40 ) );
                    foreach ( var (entityId, count) in counts[run] )
                    {
                        values.Rows.Add( nextValueId, 0, ( decimal ) count, metricId, run, int.Parse( run.ToString( "yyyyMMdd" ) ), random.NextGuid(), _foreignKey, saved, saved );
                        partitions.Rows.Add( nextPartitionId++, partitionId, Value( entityId ), nextValueId++, random.NextGuid(), _foreignKey, saved, saved );
                    }
                }

                _db.Execute( "UPDATE [Metric] SET [LastRunDateTime] = @LastRun WHERE [Id] = @Id", P( "@LastRun", runs.Count > 0 ? runs[runs.Count - 1] : null ), P( "@Id", metricId ) );
                written++;
            }

            _db.BulkInsert( "MetricValue", values, keepIdentity: true );
            _db.BulkInsert( "MetricValuePartition", partitions, keepIdentity: true );
            return (written, values.Rows.Count);
        }

        /// <summary>
        /// Each run of a schedule in the last 52 weeks, up to install, oldest first.
        /// </summary>
        private List<DateTime> Runs( HomepageMetricSchedule schedule )
        {
            var time = TimeSpan.Parse( schedule.Time );
            var weekday = schedule.Day == null ? ( DayOfWeek? ) null : ( DayOfWeek ) Enum.Parse( typeof( DayOfWeek ), schedule.Day );
            var runs = new List<DateTime>();
            for ( var day = _installed.Date.AddDays( -363 ); day <= _installed.Date; day = day.AddDays( 1 ) )
            {
                var run = day.Add( time );
                if ( ( weekday == null || day.DayOfWeek == weekday ) && run <= _installed )
                {
                    runs.Add( run );
                }
            }

            return runs;
        }

        /// <summary>
        /// The day each family first came, by family Id: its first check-in, gift, request to serve, or group membership.
        /// A family with none has always been there, and has no entry. Rock's relationship groups, Known Relationships and
        /// Peer Network, are left out: Rock makes one for every person, and the load adds each on install day, so they
        /// would make every family that never checked in, gave, or asked to serve look new today.
        /// </summary>
        private Dictionary<int, DateTime?> FamilyArrivals()
        {
            return _db.Query( @"
DECLARE @FamilyTypeId INT = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = @FamilyType );
WITH [Firsts] AS (
    SELECT [pa].[PersonId], MIN( [a].[StartDateTime] ) AS [At]
    FROM [Attendance] AS [a] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    WHERE [a].[DidAttend] = 1
    GROUP BY [pa].[PersonId]
    UNION ALL
    SELECT [pa].[PersonId], MIN( [t].[TransactionDateTime] )
    FROM [FinancialTransaction] AS [t] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [t].[AuthorizedPersonAliasId]
    GROUP BY [pa].[PersonId]
    UNION ALL
    SELECT [pa].[PersonId], MIN( [r].[CreatedDateTime] )
    FROM [ConnectionRequest] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
    GROUP BY [pa].[PersonId]
    UNION ALL
    SELECT [gm].[PersonId], MIN( [gm].[DateTimeAdded] )
    FROM [GroupMember] AS [gm]
    WHERE [gm].[GroupTypeId] <> @FamilyTypeId
        AND [gm].[GroupTypeId] NOT IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( @KnownRelationships, @PeerNetwork ) )
    GROUP BY [gm].[PersonId]
)
SELECT [fm].[GroupId], MIN( [f].[At] ) AS [At]
FROM [GroupMember] AS [fm]
INNER JOIN [Firsts] AS [f] ON [f].[PersonId] = [fm].[PersonId]
WHERE [fm].[GroupTypeId] = @FamilyTypeId
GROUP BY [fm].[GroupId]", P( "@FamilyType", FamilyGroupTypeGuid ), P( "@KnownRelationships", KnownRelationshipsGroupTypeGuid ), P( "@PeerNetwork", PeerNetworkGroupTypeGuid ) )
                .ToDictionary( r => ( int ) r["GroupId"], r => r["At"] as DateTime? );
        }

        /// <summary>
        /// Rock's Active Records: active person records, each from the day their primary family came.
        /// </summary>
        private Dictionary<DateTime, List<(int? EntityId, int Count)>> ActiveRecords( List<DateTime> runs, Dictionary<int, DateTime?> arrivals )
        {
            var people = _db.Query( "SELECT [PrimaryFamilyId] FROM [Person] WHERE ( [RecordTypeValueId] = 1 ) AND ( [RecordStatusValueId] = 3 )" )
                .Select( r => r["PrimaryFamilyId"] is int family && arrivals.TryGetValue( family, out var at ) ? at?.Date : null )
                .ToList();
            return runs.ToDictionary( run => run, run => new List<(int?, int)> { (null, people.Count( at => at == null || at <= run.Date )) } );
        }

        /// <summary>
        /// Rock's Active Families: by each active person's primary campus, the distinct families they belong to, each
        /// from the day it came. The campus is the one Rock Cleanup leaves each night, the campus of the person's first
        /// family (PersonService.UpdatePersonsPrimaryFamily). Rock's own Giver Anonymous has no primary campus in a new
        /// Rock until that job gives it its family's, and a church's past runs all came after many such nights.
        /// </summary>
        private Dictionary<DateTime, List<(int? EntityId, int Count)>> ActiveFamilies( List<DateTime> runs, Dictionary<int, DateTime?> arrivals )
        {
            var rows = _db.Query( @"
DECLARE @FamilyTypeId INT = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = @FamilyType );
SELECT DISTINCT
    ( SELECT TOP 1 [pg].[CampusId]
      FROM [GroupMember] AS [pgm]
      INNER JOIN [Group] AS [pg] ON [pg].[Id] = [pgm].[GroupId]
      WHERE [pg].[GroupTypeId] = @FamilyTypeId AND [pgm].[IsArchived] = 0 AND [pgm].[PersonId] = [p].[Id]
      ORDER BY [pgm].[GroupOrder], [pgm].[GroupId] ) AS [PrimaryCampusId],
    [g].[Id] AS [FamilyId]
FROM [Person] AS [p]
INNER JOIN [GroupMember] AS [gm] ON [gm].[PersonId] = [p].[Id]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId] AND [g].[GroupTypeId] = @FamilyTypeId
WHERE ( [p].[RecordTypeValueId] = 1 ) AND ( [p].[RecordStatusValueId] = 3 )", P( "@FamilyType", FamilyGroupTypeGuid ) )
                .Select( r => (Campus: r["PrimaryCampusId"] as int?, Family: ( int ) r["FamilyId"], At: arrivals.TryGetValue( ( int ) r["FamilyId"], out var at ) ? at?.Date : null) )
                .ToList();
            return runs.ToDictionary( run => run, run => rows
                .Where( r => r.At == null || r.At <= run.Date )
                .GroupBy( r => r.Campus )
                .OrderBy( g => g.Key ?? 0 )
                .Select( g => (g.Key, g.Select( r => r.Family ).Distinct().Count()) )
                .ToList() );
        }

        /// <summary>
        /// Rock's Active Connection Requests: requests in the Active state, each from the day it was made until the day it
        /// was connected or set inactive.
        /// </summary>
        private Dictionary<DateTime, List<(int? EntityId, int Count)>> ActiveConnectionRequests( List<DateTime> runs )
        {
            var requests = _db.Query( @"
SELECT [r].[CreatedDateTime] AS [Made], [r].[ConnectionState],
    COALESCE( ( SELECT MIN( [h].[CreatedDateTime] ) FROM [History] AS [h]
        WHERE [h].[RelatedEntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.ConnectionRequest' ) AND [h].[RelatedEntityId] = [r].[Id]
            AND [h].[Verb] IN ( 'CONNECTIONREQUESTCONNECTED', 'CONNECTIONREQUESTSTATEMODIFY' ) ), [r].[ConnectedDateTime], [r].[ModifiedDateTime] ) AS [Closed]
FROM [ConnectionRequest] AS [r]" )
                .Select( r => (Made: ( ( DateTime? ) r["Made"] )?.Date, IsActive: Convert.ToInt32( r["ConnectionState"] ) == 0, Closed: ( ( DateTime? ) r["Closed"] )?.Date) )
                .ToList();
            return runs.ToDictionary( run => run, run => new List<(int?, int)>
            {
                (null, requests.Count( r => ( r.Made == null || r.Made <= run.Date ) && ( r.IsActive || r.Closed > run.Date ) ))
            } );
        }

        #endregion

        #region The block and Quick Links

        /// <summary>
        /// Adds the Ministry Path metrics the load wrote to the block's Metrics setting, after Rock's own three (issue 9
        /// question 2). Returns how many it added.
        /// </summary>
        public int AddPathMetrics( IReadOnlyList<MetricDefinition> pathMetrics )
        {
            var setting = _db.Query( @"
SELECT [a].[Id] AS [AttributeId], [b].[Id] AS [BlockId], [v].[Value], [a].[DefaultValue]
FROM [Block] AS [b]
INNER JOIN [Attribute] AS [a] ON [a].[EntityTypeQualifierColumn] = 'BlockTypeId' AND [a].[EntityTypeQualifierValue] = CAST( [b].[BlockTypeId] AS NVARCHAR( 200 ) ) AND [a].[Key] = 'Metrics'
LEFT JOIN [AttributeValue] AS [v] ON [v].[AttributeId] = [a].[Id] AND [v].[EntityId] = [b].[Id]
WHERE [b].[Guid] = @Block", P( "@Block", _configuration.Block ) ).SingleOrDefault();
            if ( setting == null )
            {
                _db.Log.Write( "    Skipped the path metrics on the staff homepage: this Rock has no Internal Communication View block" );
                return 0;
            }

            var current = ( setting["Value"] as string ) ?? ( setting["DefaultValue"] as string ) ?? string.Empty;
            var entries = current.Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries ).ToList();
            var added = 0;
            foreach ( var metric in pathMetrics.Where( m => _db.IdByGuid( "Metric", m.Guid ) != null ) )
            {
                var entry = $"{metric.Guid.ToString().ToLower()}|{_source.Categories.Get( metric.Category ).Guid.ToString().ToLower()}";
                if ( !entries.Any( e => e.StartsWith( metric.Guid.ToString(), StringComparison.OrdinalIgnoreCase ) ) )
                {
                    entries.Add( entry );
                    added++;
                }
            }

            SetValue( ( int ) setting["AttributeId"], ( int ) setting["BlockId"], string.Join( ",", entries ), _installed );
            return added;
        }

        /// <summary>
        /// Adds the church's links to Rock's Quick Links block, after Rock's own (issue 9 question 3). Returns how many.
        /// </summary>
        public int AddQuickLinks()
        {
            var quickLinks = _configuration.QuickLinks;
            var row = _db.Query( "SELECT [Id], [Content] FROM [HtmlContent] WHERE [Guid] = @Guid", P( "@Guid", quickLinks.Content ) ).SingleOrDefault();
            if ( row == null )
            {
                _db.Log.Write( "    Skipped the Quick Links: this Rock does not have them" );
                return 0;
            }

            var content = ( row["Content"] as string ) ?? string.Empty;
            var newline = content.Contains( "\r\n" ) ? "\r\n" : "\n";
            var added = new StringBuilder();
            var count = 0;
            foreach ( var link in quickLinks.Links.Where( l => !content.Contains( $"href=\"{l.Href}\"" ) ) )
            {
                added.Append( $"    <li class=\"list-group-item\"><a href=\"{link.Href}\">{System.Net.WebUtility.HtmlEncode( link.Label )}</a></li>{newline}" );
                count++;
            }

            var end = content.LastIndexOf( "</ul>", StringComparison.Ordinal );
            content = end < 0 ? content + added : content.Substring( 0, end ) + added + content.Substring( end );
            _db.Execute( "UPDATE [HtmlContent] SET [Content] = @Content, [ModifiedDateTime] = @When WHERE [Id] = @Id", P( "@Content", content ), P( "@When", _installed ), P( "@Id", row["Id"] ) );
            return count;
        }

        #endregion

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );

        private static object Value( object value ) => value ?? DBNull.Value;
    }
}
