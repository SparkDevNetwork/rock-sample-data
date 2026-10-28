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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using SixLabors.ImageSharp;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Attaches profile photos to people, stored as Rock person images in database storage. Every Cast
    /// person with a delivered photo gets it. Generated households get faces from the shared pool when
    /// the dataset asks for it (Core 3), one photo ethnicity per family, never reusing a face on a campus.
    /// </summary>
    internal sealed class PhotoLoader
    {
        private static readonly Guid PersonImageFileTypeGuid = new Guid( "03BD8476-8A9F-4078-B628-5B538F967AFC" );

        /// <summary>
        /// Rock's Default file type, which its group detail page uses for a group's photo.
        /// </summary>
        private static readonly Guid DefaultFileTypeGuid = new Guid( "C1142570-8CD6-4A20-83B1-ACB47C1CD377" );
        private const string PoolSet = "core-3-pool";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;
        private readonly string _seed;

        public PhotoLoader( RockDatabase db, SampleDataSource source, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Picks a photo for each person and writes the files and each person's PhotoId.
        /// </summary>
        /// <param name="households">Every loaded household.</param>
        /// <param name="castHouseholdKeys">The keys of the Cast households; every other household was generated.</param>
        /// <param name="generatedHouseholdPercent">Percent of generated households per campus that get pool photos.</param>
        public void Load( List<LoadedHousehold> households, HashSet<string> castHouseholdKeys, int generatedHouseholdPercent )
        {
            var manifest = _source.ReadPhotoManifest();
            var picks = new Dictionary<int, PhotoEntry>();

            var castPhotos = manifest.Images.Where( i => i.Set == "cast" ).ToDictionary( i => (i.Household, i.Member) );
            foreach ( var household in households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) ) )
            {
                foreach ( var person in household.Members )
                {
                    if ( castPhotos.TryGetValue( (household.Cast.Key, person.Cast.Key), out var photo ) )
                    {
                        picks[person.PersonId] = photo;
                    }
                }
            }

            var castCount = picks.Count;
            var poolSummary = new List<string>();
            if ( generatedHouseholdPercent > 0 )
            {
                var pool = manifest.Images.Where( i => i.Set == PoolSet ).OrderBy( i => i.File, StringComparer.Ordinal ).ToList();
                var generated = households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) );
                foreach ( var campus in generated.GroupBy( h => h.CampusKey ).OrderBy( g => g.Key, StringComparer.Ordinal ) )
                {
                    var (photographed, people) = PickPoolPhotos( campus.Key, campus.OrderBy( h => h.Cast.Key, StringComparer.Ordinal ).ToList(), pool, generatedHouseholdPercent, picks );
                    poolSummary.Add( $"{campus.Key} {people} people in {photographed} of {campus.Count()} households" );
                }
            }

            Write( picks );
            _db.Log.Write( $"  Photos: {castCount:N0} Cast people{( poolSummary.Count > 0 ? $"; from the pool, {string.Join( ", ", poolSummary )}" : string.Empty )}" );
        }

        /// <summary>
        /// Gives pool photos to about the given percent of one campus's generated households. A household
        /// gets photos only when one photo ethnicity has an unused face for every member's gender and age
        /// band, so a family always looks like one family. Returns the households and people photographed.
        /// </summary>
        private (int Households, int People) PickPoolPhotos( string campusKey, List<LoadedHousehold> households, List<PhotoEntry> pool, int percent, Dictionary<int, PhotoEntry> picks )
        {
            var random = new SeededRandom( $"{_seed}:photos:{campusKey}" );
            var target = ( int ) Math.Round( households.Count * percent / 100.0 );
            var used = new HashSet<string>();
            var ethnicities = pool.Select( p => p.Ethnicity ).Distinct().OrderBy( e => e, StringComparer.Ordinal ).ToList();
            var photographed = 0;
            var people = 0;

            foreach ( var household in Shuffle( households, random ) )
            {
                if ( photographed >= target )
                {
                    break;
                }

                var members = household.Members.Where( m => !m.Cast.IsDeceased && m.Cast.RecordType != "business" && m.Cast.Birth != null ).ToList();
                foreach ( var ethnicity in Shuffle( ethnicities, random ) )
                {
                    var faces = MatchFamily( members, pool, ethnicity, used, random );
                    if ( faces == null )
                    {
                        continue;
                    }

                    foreach ( var (person, photo) in faces )
                    {
                        picks[person.PersonId] = photo;
                        used.Add( photo.File );
                    }

                    photographed++;
                    people += faces.Count;
                    break;
                }
            }

            return (photographed, people);
        }

        /// <summary>
        /// One unused face of the given photo ethnicity for every member, or null when the pool runs out.
        /// </summary>
        private static List<(LoadedPerson Person, PhotoEntry Photo)> MatchFamily( List<LoadedPerson> members, List<PhotoEntry> pool, string ethnicity, HashSet<string> used, SeededRandom random )
        {
            var taken = new HashSet<string>( used );
            var faces = new List<(LoadedPerson, PhotoEntry)>();
            foreach ( var member in members )
            {
                var band = AgeBand( member.Cast.Birth.Age );
                var candidates = pool.Where( p => p.Ethnicity == ethnicity && p.Gender == member.Cast.Gender && p.AgeBand == band && !taken.Contains( p.File ) ).ToList();
                if ( candidates.Count == 0 )
                {
                    return null;
                }

                var photo = candidates[random.Between( 0, candidates.Count - 1 )];
                taken.Add( photo.File );
                faces.Add( (member, photo) );
            }

            return faces;
        }

        /// <summary>
        /// The pool's age band for an age, matching photos/README.md.
        /// </summary>
        private static string AgeBand( int age )
        {
            return age switch
            {
                <= 2 => "0-2",
                <= 5 => "3-5",
                <= 11 => "6-11",
                <= 17 => "12-17",
                <= 29 => "18-29",
                <= 44 => "30-44",
                <= 64 => "45-64",
                _ => "65+"
            };
        }

        private static List<T> Shuffle<T>( List<T> items, SeededRandom random )
        {
            var shuffled = new List<T>( items );
            for ( var i = shuffled.Count - 1; i > 0; i-- )
            {
                var j = random.Between( 0, i );
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            return shuffled;
        }

        /// <summary>
        /// Writes one person image file per person, then points each person's PhotoId at it. Files are not
        /// temporary, the same as Rock leaves a photo once it is saved to a person.
        /// </summary>
        private void Write( Dictionary<int, PhotoEntry> picks )
        {
            if ( picks.Count == 0 )
            {
                return;
            }

            var ordered = picks.OrderBy( p => p.Key ).ToList();
            var fileIds = WriteFiles( PersonImageFileTypeGuid, ordered.Select( p => p.Value.File ).ToList(), "photos/manifest.json" );

            // A VALUES list holds at most 1,000 rows.
            for ( var start = 0; start < ordered.Count; start += 900 )
            {
                var values = string.Join( ", ", Enumerable.Range( start, Math.Min( 900, ordered.Count - start ) ).Select( i => $"( {ordered[i].Key}, {fileIds[i]} )" ) );
                _db.Execute( $"UPDATE [p] SET [p].[PhotoId] = [v].[FileId] FROM [Person] AS [p] INNER JOIN ( VALUES {values} ) AS [v] ( [PersonId], [FileId] ) ON [v].[PersonId] = [p].[Id]" );
            }
        }

        /// <summary>
        /// Gives each Cast small group in photos/groups/manifest.json its photo, the way Rock stores a
        /// photo uploaded on the group's page: a Default file, not temporary, set as the group's PhotoId.
        /// Run after the small groups load. Returns how many groups got a photo.
        /// </summary>
        /// <param name="plans">The loaded small group plans; photos match them by group key.</param>
        public int LoadGroupPhotos( IReadOnlyList<SmallGroupPlan> plans )
        {
            var guids = plans.ToDictionary( p => p.Key, p => p.Guid );
            var photos = _source.ReadGroupPhotoManifest().Images
                .Where( p => guids.ContainsKey( p.Group ) )
                .OrderBy( p => p.Group, StringComparer.Ordinal )
                .ToList();
            if ( photos.Count == 0 )
            {
                return 0;
            }

            var fileIds = WriteFiles( DefaultFileTypeGuid, photos.Select( p => p.File ).ToList(), SampleDataSource.GroupPhotoManifestPath );
            var values = string.Join( ", ", photos.Select( ( p, i ) => $"( '{guids[p.Group]}', {fileIds[i]} )" ) );
            _db.Execute( $"UPDATE [g] SET [g].[PhotoId] = [v].[FileId] FROM [Group] AS [g] INNER JOIN ( VALUES {values} ) AS [v] ( [Guid], [FileId] ) ON [v].[Guid] = [g].[Guid]" );
            return photos.Count;
        }

        /// <summary>
        /// Stores each serving opportunity's photo the way Rock stores one uploaded on the opportunity's page,
        /// and sets it as the opportunity's photo, replacing the photos Rock ships for its first three.
        /// Returns how many opportunities got a photo.
        /// </summary>
        /// <param name="opportunityIds">Opportunity Ids by team key.</param>
        public int LoadOpportunityPhotos( IReadOnlyDictionary<string, int> opportunityIds )
        {
            var photos = _source.ReadOpportunityPhotoManifest().Images
                .Where( p => opportunityIds.ContainsKey( p.Opportunity ) )
                .OrderBy( p => p.Opportunity, StringComparer.Ordinal )
                .ToList();
            if ( photos.Count == 0 )
            {
                return 0;
            }

            var fileIds = WriteFiles( DefaultFileTypeGuid, photos.Select( p => p.File ).ToList(), SampleDataSource.OpportunityPhotoManifestPath );
            var values = string.Join( ", ", photos.Select( ( p, i ) => $"( {opportunityIds[p.Opportunity]}, {fileIds[i]} )" ) );
            var replaced = string.Join( ", ", photos.Select( p => opportunityIds[p.Opportunity] ) );
            var oldPhotoIds = _db.Query( $"SELECT [PhotoId] FROM [ConnectionOpportunity] WHERE [Id] IN ( {replaced} ) AND [PhotoId] IS NOT NULL" )
                .Select( r => Convert.ToInt32( r["PhotoId"] ) )
                .ToList();
            _db.Execute( $"UPDATE [o] SET [o].[PhotoId] = [v].[FileId] FROM [ConnectionOpportunity] AS [o] INNER JOIN ( VALUES {values} ) AS [v] ( [Id], [FileId] ) ON [v].[Id] = [o].[Id]" );

            // Rock's photos for its three shipped opportunities are unused once replaced, and Rock Cleanup deletes
            // them. The loader deletes them itself, so the data is already as Rock Cleanup would leave it.
            if ( oldPhotoIds.Count > 0 )
            {
                var unused = string.Join( ", ", oldPhotoIds );
                _db.Execute( $"DELETE FROM [BinaryFileData] WHERE [Id] IN ( {unused} )" );
                _db.Execute( $"DELETE FROM [BinaryFile] WHERE [Id] IN ( {unused} )" );
            }

            return photos.Count;
        }

        /// <summary>
        /// Stores each calendar event's photo the way Rock stores one uploaded on the event's page (a Default file, not
        /// temporary) and sets it as the event item's photo. Events with no photo in photos/events/manifest.json get
        /// none. Returns how many events got a photo.
        /// </summary>
        /// <param name="eventItemIds">Event item Ids by event key.</param>
        public int LoadEventPhotos( IReadOnlyDictionary<string, int> eventItemIds )
        {
            var photos = _source.ReadEventPhotoManifest().Images
                .Where( p => eventItemIds.ContainsKey( p.Event ) )
                .OrderBy( p => p.Event, StringComparer.Ordinal )
                .ToList();
            if ( photos.Count == 0 )
            {
                return 0;
            }

            var fileIds = WriteFiles( DefaultFileTypeGuid, photos.Select( p => p.File ).ToList(), SampleDataSource.EventPhotoManifestPath );
            var values = string.Join( ", ", photos.Select( ( p, i ) => $"( {eventItemIds[p.Event]}, {fileIds[i]} )" ) );
            _db.Execute( $"UPDATE [e] SET [e].[PhotoId] = [v].[FileId] FROM [EventItem] AS [e] INNER JOIN ( VALUES {values} ) AS [v] ( [Id], [FileId] ) ON [v].[Id] = [e].[Id]" );
            return photos.Count;
        }

        /// <summary>
        /// Writes one JPEG file per item, in order, stored in the database, and returns their Ids in the same
        /// order. Files are not temporary, the same as Rock leaves an image once it is saved to a record.
        /// </summary>
        /// <param name="fileTypeGuid">The Rock file type.</param>
        /// <param name="photoFiles">Each item's file, relative to the photos folder. One file can be listed more than once.</param>
        /// <param name="manifestName">The manifest that lists them, for the error when one is missing.</param>
        private List<int> WriteFiles( Guid fileTypeGuid, IReadOnlyList<string> photoFiles, string manifestName )
        {
            var fileTypeId = _db.RequireIdByGuid( "BinaryFileType", fileTypeGuid );
            var storageTypeId = Convert.ToInt32( _db.Scalar( "SELECT [StorageEntityTypeId] FROM [BinaryFileType] WHERE [Id] = @Id", RockDatabase.P( "@Id", fileTypeId ) ) );
            var now = DateTime.Now;
            var nextFileId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [BinaryFile]" ) );

            var files = TableFactory.Create( "Id:int", "IsTemporary:bool", "IsSystem:bool", "BinaryFileTypeId:int", "FileName:string", "MimeType:string", "StorageEntityTypeId:int", "FileSize:int", "Width:int", "Height:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "ContentLastModified:date" );
            var data = TableFactory.Create( "Id:int", "Content:bytes", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var fileIds = new List<int>( photoFiles.Count );
            var contents = new Dictionary<string, (byte[] Bytes, int Width, int Height)>();

            foreach ( var photoFile in photoFiles )
            {
                if ( !contents.TryGetValue( photoFile, out var content ) )
                {
                    var path = $"photos/{photoFile}";
                    if ( !_source.Files.Exists( path ) )
                    {
                        throw new FileNotFoundException( $"{manifestName} lists {photoFile}, but the file is missing.", path );
                    }

                    var bytes = _source.Files.ReadBytes( path );
                    var info = Image.Identify( bytes );
                    content = (bytes, info.Width, info.Height);
                    contents[photoFile] = content;
                }

                var fileId = nextFileId++;
                files.Rows.Add( fileId, false, false, fileTypeId, Path.GetFileName( photoFile ), "image/jpeg", storageTypeId, content.Bytes.Length, content.Width, content.Height, Guid.NewGuid(), _foreignKey, now, now, now );
                data.Rows.Add( fileId, content.Bytes, Guid.NewGuid(), _foreignKey, now, now );
                fileIds.Add( fileId );
            }

            _db.BulkInsert( "BinaryFile", files, keepIdentity: true );
            _db.BulkInsert( "BinaryFileData", data, keepIdentity: false );
            return fileIds;
        }
    }
}
